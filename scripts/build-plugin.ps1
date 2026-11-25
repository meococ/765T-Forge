<#
.SYNOPSIS
  Build Forge.Plugin against one AutoCAD year from build/AutoCadYears.props.
.PARAMETER Configuration
  Build configuration (Debug or Release).
.PARAMETER Year
  The AutoCAD year to build against. Must be a row of build/AutoCadYears.props; the default
  root for the year comes from that table. AUTOCAD_<YEAR>_ROOT is the manual root override
  for that year; AUTOCAD_2026_ROOT remains the legacy override for 2026.
.PARAMETER OutDir
  Optional alternate output directory; default is artifacts\Forge.Plugin\<Year> so two years
  can coexist side by side.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("2024", "2025", "2026")]
    [string] $Year = "2026",
    [string] $OutDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$propsPath = Join-Path $root "build\AutoCadYears.props"
if (-not (Test-Path $propsPath)) {
    Write-Error "AUTOCAD_YEARS_TABLE_MISSING: build/AutoCadYears.props not found."
    exit 2
}
[xml] $years = Get-Content $propsPath
$defaultRoot = $years.Project.PropertyGroup."AutoCadRoot$Year"
if (-not $defaultRoot) {
    Write-Error "AUTOCAD_YEAR_UNTABLED: year $Year has no row in build/AutoCadYears.props."
    exit 2
}

$yearEnvName = "AUTOCAD_${Year}_ROOT"
$yearEnv = (Get-Item -Path "Env:$yearEnvName" -ErrorAction SilentlyContinue).Value
$legacyEnv = if ($Year -eq "2026") { $env:AUTOCAD_2026_ROOT } else { "" }
$acad = if ($yearEnv) { $yearEnv } elseif ($legacyEnv) { $legacyEnv } else { $defaultRoot }
$prevAcadEnv = $env:AUTOCAD_2026_ROOT
try {
    $env:AUTOCAD_2026_ROOT = $acad
    if (-not (Test-Path (Join-Path $acad "AcCoreMgd.dll"))) {
        Write-Error "AUTOCAD_ROOT_MISSING: AcCoreMgd.dll not found under $yearEnvName/AUTOCAD_2026_ROOT=$acad (year $Year)."
        exit 2
    }

    $csproj = Join-Path $root "src\Forge.Plugin\Forge.Plugin.csproj"
    if (-not $OutDir) {
        $OutDir = Join-Path $root "artifacts\Forge.Plugin\$Year"
    }
    $fullOut = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
    New-Item -ItemType Directory -Force -Path $fullOut | Out-Null

    Write-Host "Building plugin for year $Year from root $acad to OutDir=$fullOut"
    dotnet build $csproj --configuration $Configuration -p:AutoCadRoot="$acad" -p:ForgeTargetYear="$Year" -p:OutDir="$fullOut"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "Plugin build complete (year $Year)."
}
finally {
    $env:AUTOCAD_2026_ROOT = $prevAcadEnv
}
