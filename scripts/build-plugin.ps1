<#
.SYNOPSIS
  Build Forge.Plugin for the installed AutoCAD reference series.

.DESCRIPTION
  The modern net8.0-windows assembly (AutoCAD 2025-2027) is always built. -AllSeries additionally
  builds the legacy net462 assembly (AutoCAD 2017-2024), which requires AutoCAD 2017 reference
  assemblies; the legacy build fails with a clear error when that root is absent instead of
  silently compiling against a newer series.

  Reference roots follow src/Forge.Plugin/Forge.Plugin.csproj:
    modern -> FORGE_AUTOCAD_MODERN_ROOT, AUTOCAD_2025_ROOT, FORGE_AUTOCAD_ROOT, then AutoCAD 2026/2025
    legacy -> FORGE_AUTOCAD_LEGACY_ROOT, AUTOCAD_2017_ROOT

.PARAMETER Configuration
  Build configuration. Default Release.

.PARAMETER OutDir
  Optional alternate output directory when NETLOAD has locked the default bin path. When set, the
  plugin and its dependencies are copied into <OutDir>\Contents\2025 (modern) and, with -AllSeries,
  <OutDir>\Contents\2017 (legacy), mirroring the Autodesk bundle layout.

.PARAMETER AllSeries
  Also build the legacy net462 assembly for AutoCAD 2017-2024.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $OutDir = "",
    [switch] $AllSeries
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot "autocad-roots.ps1")

$csproj = Join-Path $root "src\Forge.Plugin\Forge.Plugin.csproj"
$modernTfm = "net8.0-windows"
$legacyTfm = "net462"

$modernRoot = Resolve-ForgeAutoCadModernRoot
Assert-ForgeAutoCadReferenceRoot `
    -Label "modern net8.0-windows reference series (AutoCAD 2025-2026)" `
    -Root $modernRoot `
    -Remedy "Set FORGE_AUTOCAD_MODERN_ROOT (or AUTOCAD_2025_ROOT / FORGE_AUTOCAD_ROOT) to an AutoCAD 2025/2026 install directory."

Write-Host "Building $modernTfm against $modernRoot..."
dotnet build $csproj --configuration $Configuration -p:ForgeAutoCadModernRoot="$modernRoot" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($AllSeries) {
    $legacyRoot = Resolve-ForgeAutoCadLegacyRoot
    Assert-ForgeAutoCadReferenceRoot `
        -Label "legacy net462 (AutoCAD 2017-2024)" `
        -Root $legacyRoot `
        -Remedy "Set FORGE_AUTOCAD_LEGACY_ROOT (or the deprecated AUTOCAD_2017_ROOT) to the AutoCAD 2017 install directory or the ObjectARX 2017 SDK 'inc' folder."

    Write-Host "Building $legacyTfm against $legacyRoot..."
    dotnet build $csproj --configuration $Configuration -f $legacyTfm -p:ForgeBuildLegacy=true --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($OutDir) {
    $fullOut = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
    $modernOut = Join-Path $fullOut "Contents\2025"
    New-Item -ItemType Directory -Force -Path $modernOut | Out-Null
    Copy-Item -Path (Join-Path $root "src\Forge.Plugin\bin\$Configuration\$modernTfm\*") -Destination $modernOut -Recurse -Force
    Remove-Item -Path (Join-Path $modernOut "AcCoreMgd.dll"), (Join-Path $modernOut "AcDbMgd.dll"), (Join-Path $modernOut "AcMgd.dll") -Force -ErrorAction SilentlyContinue
    Write-Host "Modern plugin laid out at $modernOut"

    if ($AllSeries) {
        $legacyOut = Join-Path $fullOut "Contents\2017"
        New-Item -ItemType Directory -Force -Path $legacyOut | Out-Null
        Copy-Item -Path (Join-Path $root "src\Forge.Plugin\bin\$Configuration\$legacyTfm\*") -Destination $legacyOut -Recurse -Force
        Remove-Item -Path (Join-Path $legacyOut "AcCoreMgd.dll"), (Join-Path $legacyOut "AcDbMgd.dll"), (Join-Path $legacyOut "AcMgd.dll") -Force -ErrorAction SilentlyContinue
        Write-Host "Legacy plugin laid out at $legacyOut"
    }
    else {
        Write-Warning "Contents\2017 was not produced because -AllSeries was not passed; AutoCAD 2017-2024 will not load this bundle."
    }

    Write-Host "Plugin build complete: $fullOut"
}
else {
    Write-Host "Plugin build complete (default output). If DLLs are locked, pass -OutDir artifacts\Forge.Plugin\"
}
