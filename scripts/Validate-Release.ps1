[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]

function Pass([string]$message) { Write-Host "PASS  $message" -ForegroundColor Green }
function Fail([string]$message) { $script:failures.Add($message) }

Write-Host 'VPB 6 release source preflight' -ForegroundColor Cyan

$xmlExtensions = @('.axaml', '.csproj', '.props', '.manifest')
$xmlFiles = Get-ChildItem -Path $root -Recurse -File | Where-Object {
    $xmlExtensions -contains $_.Extension.ToLowerInvariant() -and
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
}
foreach ($file in $xmlFiles) {
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($file.FullName)
    }
    catch {
        Fail "Malformed XML: $($file.FullName) - $($_.Exception.Message)"
    }
}
if ($failures.Count -eq 0) { Pass "Validated $($xmlFiles.Count) XAML/project/manifest XML files." }

$solutionPath = Join-Path $root 'VirtualPianoBot.sln'
$solutionText = Get-Content -LiteralPath $solutionPath -Raw
$projectMatches = [regex]::Matches($solutionText, 'Project\([^\r\n]+?\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)",')
$missingProjects = @()
foreach ($match in $projectMatches) {
    $relativeProject = $match.Groups[1].Value -replace '\\', [IO.Path]::DirectorySeparatorChar
    $projectPath = Join-Path $root $relativeProject
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { $missingProjects += $relativeProject }
}
if ($missingProjects.Count -gt 0) {
    foreach ($project in $missingProjects) { Fail "Solution references missing project: $project" }
} else {
    Pass "All $($projectMatches.Count) solution project paths resolve."
}

$requiredProjects = @(
    'src/VirtualPianoBot/VirtualPianoBot.csproj',
    'src/VirtualPianoBot.Launcher/VirtualPianoBot.Launcher.csproj',
    'tests/VPB6.FoundationChecks/VPB6.FoundationChecks.csproj',
    'tests/VPB6.AnalysisChecks/VPB6.AnalysisChecks.csproj',
    'tests/VPB6.MigrationChecks/VPB6.MigrationChecks.csproj'
)
foreach ($relative in $requiredProjects) {
    $path = Join-Path $root ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail "Required project is missing: $relative" }
}
if ($failures.Count -eq 0) { Pass 'Application, launcher, and all three regression-check projects are present.' }

# Stable release metadata must agree with VERSION.txt across the projects, launcher, installer and local manifest.
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION.txt') -Raw).Trim()
$versionPattern = [regex]::Escape($version)
$assemblyVersion = "$version.0"
$studioProjectText = Get-Content -LiteralPath (Join-Path $root 'src/VirtualPianoBot/VirtualPianoBot.csproj') -Raw
$launcherProjectText = Get-Content -LiteralPath (Join-Path $root 'src/VirtualPianoBot.Launcher/VirtualPianoBot.Launcher.csproj') -Raw
$launcherSourceText = Get-Content -LiteralPath (Join-Path $root 'src/VirtualPianoBot.Launcher/Program.cs') -Raw
$studioManifestText = Get-Content -LiteralPath (Join-Path $root 'src/VirtualPianoBot/app.manifest') -Raw
$installerText = Get-Content -LiteralPath (Join-Path $root 'installer/VPB6.iss') -Raw
$updatesPath = Join-Path $root 'updates.json'
$metadataValid = $studioProjectText -match "<Version>$versionPattern</Version>" -and
    $studioProjectText -match "<InformationalVersion>$versionPattern</InformationalVersion>" -and
    $studioProjectText -match "<AssemblyVersion>$([regex]::Escape($assemblyVersion))</AssemblyVersion>" -and
    $studioProjectText -match "<FileVersion>$([regex]::Escape($assemblyVersion))</FileVersion>" -and
    $launcherProjectText -match "<Version>$versionPattern</Version>" -and
    $launcherProjectText -match "<InformationalVersion>$versionPattern</InformationalVersion>" -and
    $launcherSourceText -match "$versionPattern\s+•\s+PERFORMANCE STUDIO" -and
    $studioManifestText -match "assemblyIdentity version=`"$([regex]::Escape($assemblyVersion))`"" -and
    $installerText -match "#define MyAppVersion `"$versionPattern`"" -and
    $installerText -match "VirtualPianoBot-$versionPattern-Setup"
try {
    $manifestData = Get-Content -LiteralPath $updatesPath -Raw | ConvertFrom-Json
    $metadataValid = $metadataValid -and $manifestData.version -eq $version -and $manifestData.channel -eq 'stable'
} catch { $metadataValid = $false }
if ($metadataValid -and ($manifestData.PSObject.Properties.Name -contains 'manifestUrl')) {
    Pass "Stable $version version metadata and local update-client configuration agree."
} else {
    Fail 'Stable version metadata, launcher label, installer, assembly manifest and local update-client configuration do not agree.'
}

# Ensure the repeated-note regression test remains in the release-check solution.
$foundationChecks = Get-Content -LiteralPath (Join-Path $root 'tests/VPB6.FoundationChecks/Program.cs') -Raw
if ($foundationChecks -match 'repeated-chord-regression\.mid' -and
    $foundationChecks -match 'Repeated chord plan emits every note-on' -and
    $foundationChecks -match 'retriggers as key-up/key-down') {
    Pass 'Repeated chord retrigger regression coverage is included in foundation checks.'
} else {
    Fail 'Repeated chord retrigger regression coverage is missing.'
}

$githubWorkflowPath = Join-Path $root '.github/workflows/release.yml'
if (Test-Path -LiteralPath $githubWorkflowPath -PathType Leaf) {
    $githubWorkflow = Get-Content -LiteralPath $githubWorkflowPath -Raw
    if ($githubWorkflow -match 'releases/latest/download/update-manifest\.json' -and
        $githubWorkflow -match "@\('release',\s*'create'" -and
        $githubWorkflow -match 'contents:\s*write') {
        Pass 'GitHub Releases workflow publishes a latest manifest and release assets with limited write permission.'
    } else {
        Fail 'GitHub Releases workflow is missing expected manifest, release or permission configuration.'
    }
} else {
    Fail 'GitHub Releases workflow is missing.'
}

$updateServicePath = Join-Path $root 'src/VirtualPianoBot.Launcher/UpdateService.cs'
$updateServiceText = if (Test-Path -LiteralPath $updateServicePath -PathType Leaf) { Get-Content -LiteralPath $updateServicePath -Raw } else { '' }
$publishScriptText = Get-Content -LiteralPath (Join-Path $root 'scripts/Publish-Release.ps1') -Raw
$updateClientValid = $updateServiceText -match 'CryptographicOperations\.FixedTimeEquals' -and
    $updateServiceText -match 'ValidateArchive' -and
    $updateServiceText -match 'ApplyUpdateScript' -and
    $updateServiceText -match 'SHA256' -and
    $updateServiceText -match 'manifestUrl' -and
    $publishScriptText -match 'Compress-Archive' -and
    $publishScriptText -match 'update-manifest\.json' -and
    $publishScriptText -match 'Get-FileHash.*SHA256'
if ($updateClientValid) {
    Pass 'Updater includes HTTPS-only manifest/package validation, SHA-256 verification, a bounded archive allow-list, and a rollback apply script.'
} else {
    Fail 'The safe update client or update-package publishing pipeline is incomplete.'
}

$appPathsPath = Join-Path $root 'src/VirtualPianoBot/Core/AppPaths.cs'
$libraryPath = Join-Path $root 'src/VirtualPianoBot/Core/MidiLibraryService.cs'
$migrationPath = Join-Path $root 'src/VirtualPianoBot/Core/LegacyMidiMigrationService.cs'
$appPaths = Get-Content -LiteralPath $appPathsPath -Raw
$library = Get-Content -LiteralPath $libraryPath -Raw
$migration = Get-Content -LiteralPath $migrationPath -Raw
if ($appPaths -match 'Path\.Combine\(root,\s*"VirtualPianoBot"\)' -and $appPaths -match 'MidiDirectory\s*=\s*Path\.Combine\(DataDirectory,\s*"midi"\)') {
    Pass 'Persistent MIDI library remains rooted at %APPDATA%\VirtualPianoBot\midi.'
} else { Fail 'Persistent version-independent MIDI path contract changed.' }
if ($library -match 'File\.Copy\(file,\s*target,\s*overwrite:\s*false\)' -and $library -match 'PathsOverlap\(source,\s*destination\)') {
    Pass 'MIDI import remains copy-only and rejects source/library overlap.'
} else { Fail 'MIDI import no-overwrite/self-import protection was not detected.' }
if ($migration -match 'File\.Copy\(file,\s*target,\s*overwrite:\s*false\)' -and $migration -notmatch 'File\.Delete\s*\(' -and $migration -notmatch 'Directory\.Delete\s*\(') {
    Pass 'Legacy migration copies without overwriting and does not delete source songs.'
} else { Fail 'Legacy migration no-overwrite/source-preservation guard changed.' }

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "Preflight failed with $($failures.Count) issue(s):" -ForegroundColor Red
    foreach ($failure in $failures) { Write-Host " - $failure" -ForegroundColor Red }
    throw "Release source preflight failed with $($failures.Count) issue(s)."
}

Write-Host 'Source preflight passed. This is not a substitute for the Windows build and runtime acceptance checks.' -ForegroundColor Green
