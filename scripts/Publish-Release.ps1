[CmdletBinding()]
param(
    [string]$UpdateManifestUrl = $env:VPB6_UPDATE_MANIFEST_URL,
    [string]$GitHubRepository = $env:VPB6_GITHUB_REPOSITORY
)

$ErrorActionPreference = 'Stop'
if ($null -eq $GitHubRepository) { $GitHubRepository = '' }
$GitHubRepository = $GitHubRepository.Trim().Trim('/')
if (-not [string]::IsNullOrWhiteSpace($GitHubRepository)) {
    if ($GitHubRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw 'GitHubRepository must use the owner/repository form, for example YourName/VirtualPianoBot.'
    }
    $githubManifestUrl = "https://github.com/$GitHubRepository/releases/latest/download/update-manifest.json"
    if ([string]::IsNullOrWhiteSpace($UpdateManifestUrl)) {
        $UpdateManifestUrl = $githubManifestUrl
    } elseif ($UpdateManifestUrl -ne $githubManifestUrl) {
        throw 'When GitHubRepository is configured, UpdateManifestUrl must be the GitHub latest-release manifest URL.'
    }
}
$version = (Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'VERSION.txt') -Raw).Trim()
$parsedVersion = $null
if (-not [version]::TryParse($version, [ref]$parsedVersion)) {
    throw "VERSION.txt must contain a numeric version such as 6.0.1. Found '$version'."
}
if (-not [string]::IsNullOrWhiteSpace($UpdateManifestUrl)) {
    $parsedUpdateUrl = $null
    if (-not [uri]::TryCreate($UpdateManifestUrl, [UriKind]::Absolute, [ref]$parsedUpdateUrl) -or $parsedUpdateUrl.Scheme -ne 'https' -or $parsedUpdateUrl.UserInfo) {
        throw 'UpdateManifestUrl must be an absolute HTTPS URL without embedded credentials.'
    }
    $UpdateManifestUrl = $parsedUpdateUrl.AbsoluteUri
}
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'VirtualPianoBot.sln'
$checks = @(
    @{ Name = 'MIDI persistence'; Path = 'tests\VPB6.FoundationChecks\VPB6.FoundationChecks.csproj' },
    @{ Name = 'MIDI analysis'; Path = 'tests\VPB6.AnalysisChecks\VPB6.AnalysisChecks.csproj' },
    @{ Name = 'Keyboard mapping'; Path = 'tests\VPB6.MigrationChecks\VPB6.MigrationChecks.csproj' }
)
$releaseRoot = Join-Path $root 'release'
$stageRoot = Join-Path $root ('release.staging-' + [Guid]::NewGuid().ToString('N'))
$stageApp = Join-Path $stageRoot 'app'
$stageRuntime = Join-Path $stageApp 'runtime'
$backupRoot = $null

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET 10 SDK is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun this script.'
}
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdk.StartsWith('10.')) {
    throw "VPB 6 targets .NET 10. Detected SDK '$sdk'. Install the .NET 10 SDK and rerun this script."
}

Write-Host 'Running release source preflight...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'Validate-Release.ps1')

Write-Host "Restoring VPB 6 with .NET SDK $sdk..." -ForegroundColor Cyan
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed. The previous release folder has been left untouched.' }

Write-Host 'Building the complete solution in Release configuration...' -ForegroundColor Cyan
& dotnet build $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed. The previous release folder has been left untouched.' }

foreach ($check in $checks) {
    $project = Join-Path $root $check.Path
    Write-Host "Running $($check.Name) regression checks..." -ForegroundColor Cyan
    & dotnet run --project $project --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "$($check.Name) regression checks failed. The previous release folder has been left untouched." }
}

try {
    New-Item -ItemType Directory -Path $stageRuntime -Force | Out-Null

    Write-Host 'Publishing VPB 6 Studio to a staging folder...' -ForegroundColor Cyan
    & dotnet publish (Join-Path $root 'src\VirtualPianoBot\VirtualPianoBot.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $stageRuntime
    if ($LASTEXITCODE -ne 0) { throw 'Studio publish failed.' }

    Write-Host 'Publishing VPB 6 Launcher to the staging folder...' -ForegroundColor Cyan
    & dotnet publish (Join-Path $root 'src\VirtualPianoBot.Launcher\VirtualPianoBot.Launcher.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $stageApp
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }

    $expectedFiles = @(
        (Join-Path $stageApp 'VPB6.exe'),
        (Join-Path $stageRuntime 'VPB6Studio.exe')
    )
    foreach ($file in $expectedFiles) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Publish output is missing required executable: $file" }
        if ((Get-Item -LiteralPath $file).Length -le 0) { throw "Publish output is empty: $file" }
    }

    # Keep only the two user-facing executables plus the runtime Studio payload.
    Get-ChildItem -LiteralPath $stageApp -File | Where-Object { $_.Name -notin @('VPB6.exe', 'README.txt') } | Remove-Item -Force
    Get-ChildItem -LiteralPath $stageRuntime -File | Where-Object { $_.Name -ne 'VPB6Studio.exe' } | Remove-Item -Force

    # Configure the launcher for one hosted HTTPS manifest. Blank means safe local-only mode.
    $localUpdateConfig = Get-Content -LiteralPath (Join-Path $root 'updates.json') -Raw | ConvertFrom-Json
    $localUpdateConfig.version = $version
    $localUpdateConfig.channel = 'stable'
    if ([string]::IsNullOrWhiteSpace($UpdateManifestUrl)) {
        $localUpdateConfig.manifestUrl = ''
        $localUpdateConfig.status = 'manifest-not-configured'
        $localUpdateConfig.message = 'Online update checks are not configured. Set VPB6_UPDATE_MANIFEST_URL before building to enable them.'
    } else {
        $localUpdateConfig.manifestUrl = $UpdateManifestUrl
        $localUpdateConfig.status = 'update-client-enabled'
        $localUpdateConfig.message = 'Online update checks are configured for the stable channel.'
    }
    $updatesCopy = Join-Path $stageApp 'updates.json'
    $localUpdateConfig | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $updatesCopy -Encoding UTF8
    if (-not (Test-Path -LiteralPath $updatesCopy -PathType Leaf) -or (Get-Item -LiteralPath $updatesCopy).Length -le 0) {
        throw 'The local updates.json configuration is missing or empty from the staged application.'
    }

@"
Virtual Piano Bot 6
Version: $version

Run VPB6.exe to open the launcher.
Your MIDI library remains at:
%APPDATA%\VirtualPianoBot\midi

The launcher and studio are self-contained. No .NET installation is required.
The update client verifies package SHA-256 and preserves the previous application files for rollback.
"@ | Set-Content (Join-Path $stageApp 'README.txt') -Encoding UTF8

    # Create a ready-to-host app-only package and its checksum manifest before replacing any prior release.
    $packageName = "VirtualPianoBot-$version-win-x64.zip"
    $packagePath = Join-Path $stageRoot $packageName
    Write-Host "Creating update package $packageName..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $stageApp '*') -DestinationPath $packagePath -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf) -or (Get-Item -LiteralPath $packagePath).Length -le 0) {
        throw 'The application update package could not be created.'
    }
    $packageInfo = Get-Item -LiteralPath $packagePath
    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $releaseNotes = (Get-Content -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Raw).Trim()
    if ($releaseNotes.Length -gt 12000) { $releaseNotes = $releaseNotes.Substring(0, 12000) }
    $packageUrl = $packageName
    if (-not [string]::IsNullOrWhiteSpace($GitHubRepository)) {
        # Fixed asset names let the GitHub latest-release URLs continue to work after every version bump.
        $packageUrl = "https://github.com/$GitHubRepository/releases/download/v$version/VirtualPianoBot-Update.zip"
        $stableAssetPath = Join-Path $stageRoot 'VirtualPianoBot-Update.zip'
        Copy-Item -LiteralPath $packagePath -Destination $stableAssetPath
        if ((Get-FileHash -LiteralPath $stableAssetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packageHash) {
            throw 'The stable GitHub update asset does not match the versioned update package.'
        }
    }
    $remoteManifest = [ordered]@{
        schemaVersion = 1
        channel = 'stable'
        version = $version
        packageUrl = $packageUrl
        sha256 = $packageHash
        packageSizeBytes = [long]$packageInfo.Length
        releaseNotes = $releaseNotes
        publishedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    $remoteManifestPath = Join-Path $stageRoot 'update-manifest.json'
    $remoteManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $remoteManifestPath -Encoding UTF8
    $manifestCheck = Get-Content -LiteralPath $remoteManifestPath -Raw | ConvertFrom-Json
    if ($manifestCheck.version -ne $version -or $manifestCheck.sha256 -ne $packageHash -or $manifestCheck.packageUrl -ne $packageUrl) {
        throw 'Generated update manifest validation failed.'
    }

    # Do not remove the previous release until the replacement has fully built and validated.
    if (Test-Path -LiteralPath $releaseRoot) {
        $suffix = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
        $backupRoot = Join-Path $root ("release.previous-$suffix-" + [Guid]::NewGuid().ToString('N').Substring(0, 6))
        Move-Item -LiteralPath $releaseRoot -Destination $backupRoot
    }

    try {
        Move-Item -LiteralPath $stageRoot -Destination $releaseRoot
    }
    catch {
        if ($backupRoot -and (Test-Path -LiteralPath $backupRoot) -and -not (Test-Path -LiteralPath $releaseRoot)) {
            Move-Item -LiteralPath $backupRoot -Destination $releaseRoot
        }
        throw
    }

    Write-Host "Release payload created at $releaseRoot\app" -ForegroundColor Green
    Write-Host "Update package: $releaseRoot\$packageName" -ForegroundColor Green
    if ($GitHubRepository) { Write-Host "GitHub Release assets prepared for $GitHubRepository, including VirtualPianoBot-Update.zip and update-manifest.json." -ForegroundColor Green } else { Write-Host "Upload both $packageName and update-manifest.json to the same HTTPS location." -ForegroundColor Green }
    if ([string]::IsNullOrWhiteSpace($UpdateManifestUrl)) {
        Write-Warning 'Online update checks are not configured in this build. Set VPB6_UPDATE_MANIFEST_URL and rebuild after choosing a stable HTTPS hosting URL.'
    } else {
        Write-Host "Online update manifest configured: $UpdateManifestUrl" -ForegroundColor Green
    }
    if ($backupRoot) { Write-Host "Previous release preserved at $backupRoot" -ForegroundColor DarkGray }
    Write-Host "Build installer\VPB6.iss with Inno Setup to produce the $version Setup.exe." -ForegroundColor DarkGray
}
catch {
    if (Test-Path -LiteralPath $stageRoot) {
        # This directory is generated by this run only. The existing release is never cleaned here.
        Remove-Item -LiteralPath $stageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}
