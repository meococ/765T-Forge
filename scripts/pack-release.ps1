<#
.SYNOPSIS
  Pack release artifacts: always publish Forge.Server; optionally pack the plugin when AutoCAD refs exist.

.DESCRIPTION
  The plugin zip mirrors the Autodesk bundle layout: PackageContents.xml, README.md, Contents\2025\
  (net8.0-windows, AutoCAD 2025-2027) and, with -AllSeries, Contents\2017\ (net462, AutoCAD 2017-2024).
  Autodesk Ac*.dll reference assemblies are never shipped.

  Reference roots follow src/Forge.Plugin/Forge.Plugin.csproj:
    modern -> FORGE_AUTOCAD_MODERN_ROOT, AUTOCAD_2025_ROOT, FORGE_AUTOCAD_ROOT, then AutoCAD 2026/2025
    legacy -> FORGE_AUTOCAD_LEGACY_ROOT, AUTOCAD_2017_ROOT

.PARAMETER Configuration
  Build configuration. Default Release.

.PARAMETER SkipPlugin
  Skip plugin packaging (default for CI without AutoCAD).

.PARAMETER AllSeries
  Also pack the legacy net462 component for AutoCAD 2017-2024. Fails if the legacy reference root is
  missing instead of shipping a bundle with a dead 2017 component.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipPlugin,
    [switch] $AllSeries
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot "autocad-roots.ps1")

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
$pluginPackedSeries = ""
if (-not $SkipPlugin) {
    $modernRoot = Resolve-ForgeAutoCadModernRoot
    if ([string]::IsNullOrWhiteSpace($modernRoot)) {
        Write-Warning "No AutoCAD 2025/2026 reference root found (FORGE_AUTOCAD_MODERN_ROOT / AUTOCAD_2025_ROOT / FORGE_AUTOCAD_ROOT). Skipping plugin pack."
    }
    else {
        if ($AllSeries) {
            $legacyRoot = Resolve-ForgeAutoCadLegacyRoot
            Assert-ForgeAutoCadReferenceRoot `
                -Label "legacy net462 (AutoCAD 2017-2024)" `
                -Root $legacyRoot `
                -Remedy "Set FORGE_AUTOCAD_LEGACY_ROOT (or the deprecated AUTOCAD_2017_ROOT) to the AutoCAD 2017 install directory or the ObjectARX 2017 SDK 'inc' folder, or pack without -AllSeries."
        }

        if (Test-Path $pluginOut) { Remove-Item $pluginOut -Recurse -Force }
        $modernOut = Join-Path $pluginOut "Contents\2025"
        New-Item -ItemType Directory -Force -Path $modernOut | Out-Null

        $pluginProj = Join-Path $root "src\Forge.Plugin\Forge.Plugin.csproj"
        Write-Host "Building Forge.Plugin net8.0-windows against $modernRoot..."
        dotnet build $pluginProj --configuration $Configuration -f net8.0-windows -p:ForgeAutoCadModernRoot="$modernRoot" --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Copy-Item -Path (Join-Path $root "src\Forge.Plugin\bin\$Configuration\net8.0-windows\*") -Destination $modernOut -Recurse -Force

        if ($AllSeries) {
            $legacyOut = Join-Path $pluginOut "Contents\2017"
            New-Item -ItemType Directory -Force -Path $legacyOut | Out-Null
            Write-Host "Building Forge.Plugin net462 against $legacyRoot..."
            dotnet build $pluginProj --configuration $Configuration -f net462 -p:ForgeBuildLegacy=true --nologo
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
            Copy-Item -Path (Join-Path $root "src\Forge.Plugin\bin\$Configuration\net462\*") -Destination $legacyOut -Recurse -Force
            $pluginPackedSeries = "2017;2025"
        }
        else {
            Write-Warning "Packing without -AllSeries: Contents\2017 is absent, so AutoCAD 2017-2024 will not load this bundle."
            $pluginPackedSeries = "2025"
        }

        # Bundle manifest and README travel with the payload.
        Copy-Item (Join-Path $root "plugin-bundle\765T-Forge.bundle\PackageContents.xml") $pluginOut -Force
        Copy-Item (Join-Path $root "plugin-bundle\765T-Forge.bundle\README.md") $pluginOut -Force

        # Autodesk reference assemblies are never redistributed (see NOTICE.md).
        Get-ChildItem -Path $pluginOut -Recurse -File |
            Where-Object { $_.Name -match '^(AcCoreMgd|AcDbMgd|AcMgd)\.dll$' } |
            ForEach-Object { Remove-Item $_.FullName -Force }

        $pluginZip = Join-Path $releaseRoot "765T-Forge.Plugin.zip"
        if (Test-Path $pluginZip) { Remove-Item $pluginZip -Force }
        Compress-Archive -Path (Join-Path $pluginOut "*") -DestinationPath $pluginZip
        Write-Host "Wrote $pluginZip (Contents\2017 and Contents\2025 as packed; Autodesk Ac*.dll excluded)"
        $pluginPacked = $true
    }
}
else {
    Write-Host "SkipPlugin set - server-only pack (CI default)."
}

$pluginPackedText = if ($pluginPacked) { "true" } else { "false" }
$completeText = if ((-not $SkipPlugin.IsPresent) -and $pluginPacked) { "true" } else { "false" }
Write-Host "Pack complete. Server=yes PluginPacked=$pluginPackedText Series=$pluginPackedSeries Output=$releaseRoot"

$statusFile = Join-Path $releaseRoot "RELEASE_STATUS.txt"
@(
    "765T-Forge release pack status"
    "ServerZip=yes"
    "PluginZip=$pluginPackedText"
    "PluginSeries=$pluginPackedSeries"
    "Complete=$completeText"
    "Note=A GitHub Release without 765T-Forge.Plugin.zip is INCOMPLETE."
    "Note=A plugin zip without Contents/2017 is INCOMPLETE for AutoCAD 2017-2024."
) | Set-Content -LiteralPath $statusFile -Encoding utf8
Write-Host "Wrote $statusFile"

if ($SkipPlugin.IsPresent -or -not $pluginPacked) {
    Write-Warning "INCOMPLETE RELEASE ARTIFACT SET: attach 765T-Forge.Plugin.zip before announcing a public v* tag."
}
