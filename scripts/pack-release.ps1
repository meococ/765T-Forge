<#
.SYNOPSIS
  Pack release artifacts: always publish Forge.Server; optionally pack plugin when AutoCAD refs exist.
.PARAMETER Year
  AutoCAD year the plugin zip is built for, via scripts/build-plugin.ps1 (ForgeTargetYear stamp).
  AUTOCAD_2026_ROOT stays the manual root override for non-default installs.
.PARAMETER SkipPlugin
  Skip plugin packaging (default for CI without AutoCAD).
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("2025", "2026")]
    [string] $Year = "2026",
    [switch] $SkipPlugin
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$releaseRoot = Join-Path $root "artifacts\release"
$serverOut = Join-Path $releaseRoot "server"
$pluginOut = Join-Path $releaseRoot "plugin"
New-Item -ItemType Directory -Force -Path $serverOut | Out-Null

$serverProj = Join-Path $root "src\Forge.Server\Forge.Server.csproj"
Write-Host "Publishing Forge.Server (win-x64)..."
dotnet publish $serverProj `
    --configuration $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $serverOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Honesty docs must be present next to the exe for forge:// MCP resources.
$matrixInZip = Join-Path $serverOut "docs\capability-matrix.md"
$safetyInZip = Join-Path $serverOut "docs\safety.md"
if (-not (Test-Path $matrixInZip) -or -not (Test-Path $safetyInZip)) {
    Write-Warning "docs missing from publish output - copying from repo."
    $docsOut = Join-Path $serverOut "docs"
    New-Item -ItemType Directory -Force -Path $docsOut | Out-Null
    Copy-Item (Join-Path $root "docs\capability-matrix.md") $docsOut -Force
    Copy-Item (Join-Path $root "docs\safety.md") $docsOut -Force
}

$serverZip = Join-Path $releaseRoot "765T-Forge.Server-win-x64.zip"
if (Test-Path $serverZip) { Remove-Item $serverZip -Force }
Compress-Archive -Path (Join-Path $serverOut "*") -DestinationPath $serverZip
Write-Host "Wrote $serverZip"

$pluginPacked = $false
if (-not $SkipPlugin) {
    New-Item -ItemType Directory -Force -Path $pluginOut | Out-Null

    # Delegate to build-plugin.ps1 so the release zip carries the same ForgeTargetYear
    # stamp (docs/cad-job-tools.md C2); a second build path here used to bypass the year table.
    # Run as a child powershell process so build-plugin's exit codes reach $LASTEXITCODE.
    Write-Host "Building Forge.Plugin (year $Year) via scripts\build-plugin.ps1..."
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build-plugin.ps1") `
        -Configuration $Configuration -Year $Year -OutDir $pluginOut
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $pluginZip = Join-Path $releaseRoot "765T-Forge.Plugin.zip"
    if (Test-Path $pluginZip) { Remove-Item $pluginZip -Force }
    Compress-Archive -Path (Join-Path $pluginOut "*") -DestinationPath $pluginZip

    $tmp = Join-Path $releaseRoot "plugin-filtered"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $tmp | Out-Null
    Expand-Archive -Path $pluginZip -DestinationPath $tmp -Force
    Get-ChildItem -Path $tmp -Recurse -File |
        Where-Object { $_.Name -match '^(AcCoreMgd|AcDbMgd|AcMgd)\.dll$' } |
        ForEach-Object { Remove-Item $_.FullName -Force }
    Remove-Item $pluginZip -Force
    Compress-Archive -Path (Join-Path $tmp "*") -DestinationPath $pluginZip
    Remove-Item $tmp -Recurse -Force
    Write-Host "Wrote $pluginZip (Autodesk Ac*.dll excluded)"
    $pluginPacked = $true
}
else {
    Write-Host "SkipPlugin set - server-only pack (CI default)."
}

$pluginPackedText = if ($pluginPacked) { "true" } else { "false" }
$completeText = if ((-not $SkipPlugin.IsPresent) -and $pluginPacked) { "true" } else { "false" }
Write-Host "Pack complete. Server=yes PluginPacked=$pluginPackedText Output=$releaseRoot"

$statusFile = Join-Path $releaseRoot "RELEASE_STATUS.txt"
@(
    "765T-Forge release pack status"
    "ServerZip=yes"
    "PluginZip=$pluginPackedText"
    "Complete=$completeText"
    "Note=A GitHub Release without 765T-Forge.Plugin.zip is INCOMPLETE."
) | Set-Content -LiteralPath $statusFile -Encoding utf8
Write-Host "Wrote $statusFile"

if ($SkipPlugin.IsPresent -or -not $pluginPacked) {
    Write-Warning "INCOMPLETE RELEASE ARTIFACT SET: attach 765T-Forge.Plugin.zip before announcing a public v* tag."
}
