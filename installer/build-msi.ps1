# Build script for the Why Save MSI installer.
# Prerequisites:
#   - .NET 8 SDK
#   - WiX v4 (dotnet tool install --global wix --version 4.*)
#
# Usage: powershell -ExecutionPolicy Bypass -File build-msi.ps1

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\WhySave.App\WhySave.App.csproj"
$publishDir = Join-Path $repoRoot "src\WhySave.App\bin\$Configuration\net8.0-windows10.0.17763\$RuntimeIdentifier\publish"
$wxsFile = Join-Path $PSScriptRoot "WhySave.wxs"
$msiOutput = Join-Path $PSScriptRoot "WhySave.msi"

Write-Host "Publishing WhySave.App ($Configuration, $RuntimeIdentifier)..." -ForegroundColor Cyan
dotnet publish $appProject `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Building MSI with WiX..." -ForegroundColor Cyan
$wixExe = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wixExe) {
    Write-Error "WiX not found. Install with: dotnet tool install --global wix --version 4.*"
    exit 1
}

wix build $wxsFile -o $msiOutput --define "BuildDir=$publishDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "MSI build failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "MSI built: $msiOutput" -ForegroundColor Green
