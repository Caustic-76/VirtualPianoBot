[CmdletBinding()]
param(
    [switch]$PublishWindowsX64
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'VirtualPianoBot.sln'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET 10 SDK is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun this script.'
}
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdk.StartsWith('10.')) {
    throw "VPB 6 targets .NET 10. Detected SDK '$sdk'. Install the .NET 10 SDK and rerun this script."
}

& (Join-Path $PSScriptRoot 'Validate-Release.ps1')

Write-Host "Restoring VPB 6 with .NET SDK $sdk" -ForegroundColor Cyan
& dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }

Write-Host 'Building the full Release solution...' -ForegroundColor Cyan
& dotnet build $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed. See the compiler output above.' }

$checks = @(
    @{ Name = 'MIDI persistence and no-overwrite'; Path = 'tests\VPB6.FoundationChecks\VPB6.FoundationChecks.csproj' },
    @{ Name = 'MIDI analysis'; Path = 'tests\VPB6.AnalysisChecks\VPB6.AnalysisChecks.csproj' },
    @{ Name = 'Keyboard mapping'; Path = 'tests\VPB6.MigrationChecks\VPB6.MigrationChecks.csproj' }
)
foreach ($check in $checks) {
    Write-Host "Running $($check.Name) regression checks..." -ForegroundColor Cyan
    & dotnet run --project (Join-Path $root $check.Path) --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "$($check.Name) checks failed." }
}

if ($PublishWindowsX64) {
    $publishPath = Join-Path $root 'artifacts\publish-win-x64'
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
    $project = Join-Path $root 'src\VirtualPianoBot\VirtualPianoBot.csproj'
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'Windows x64 publish failed.' }
    Write-Host "Publish complete: $publishPath" -ForegroundColor Green
} else {
    Write-Host 'Build and all regression checks passed. Run scripts\Run-VPB6.bat to launch from source.' -ForegroundColor Green
    Write-Host 'Use -PublishWindowsX64 to create a self-contained Windows x64 publish folder.' -ForegroundColor DarkGray
}
