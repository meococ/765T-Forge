<#
.SYNOPSIS
  Copy Forge.Plugin build output into a year-specific NETLOAD folder and the autoloader bundle layout.
.PARAMETER Configuration
  Build configuration used to locate default bin output when -SourceDir is omitted.
.PARAMETER SourceDir
  Directory containing Forge.Plugin.dll for -Year (ignored with -AllYears).
.PARAMETER InstallDir
  Destination directory (default: %LOCALAPPDATA%\765T-Forge\plugin).
.PARAMETER SkipBuild
  Do not invoke build-plugin.ps1 first.
.PARAMETER Year
  AutoCAD year to install. Default 2026.
.PARAMETER AllYears
  Install every year that has a built Forge.Plugin.dll (builds those whose AutoCAD root exists unless -SkipBuild).
#>
[CmdletBinding(DefaultParameterSetName = "SingleYear")]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $SourceDir = "",
    [string] $InstallDir = "",
    [switch] $SkipBuild,
    [Parameter(ParameterSetName = "SingleYear")]
    [ValidateRange(2017, 2026)]
    [int] $Year = 2026,
    [Parameter(ParameterSetName = "AllYears", Mandatory = $true)]
    [switch] $AllYears
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    $build = Join-Path $PSScriptRoot "build-plugin.ps1"
    if ($AllYears) {
        & $build -Configuration $Configuration -AllYears
    }
    else {
        & $build -Configuration $Configuration -Year $Year
    }

    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "765T-Forge\plugin"
}

$bundle = Join-Path $InstallDir "765T-Forge.bundle"
New-Item -ItemType Directory -Force -Path (Join-Path $bundle "Contents\Windows") | Out-Null
Copy-Item (Join-Path $root "plugin-bundle\765T-Forge.bundle\PackageContents.xml") $bundle -Force
Copy-Item (Join-Path $root "plugin-bundle\765T-Forge.bundle\README.md") $bundle -Force

function Copy-ForgePluginOutput([string] $FromDir, [string] $ToDir) {
    New-Item -ItemType Directory -Force -Path $ToDir | Out-Null
    $files = Get-ChildItem -Path $FromDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch '^(AcCoreMgd|AcDbMgd|AcMgd)\.dll$' }
    if (-not $files) {
        return $false
    }

    foreach ($file in $files) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $ToDir $file.Name) -Force
    }

    return $true
}

$years = if ($AllYears) { 2017..2026 } else { @($Year) }
$installedYears = @()
$skippedYears = @()

foreach ($candidate in $years) {
    $from = if ($SourceDir -and -not $AllYears) { $SourceDir } else {
        Join-Path $root "src\Forge.Plugin\bin\$Configuration\autocad-$candidate"
    }

    $dll = Join-Path $from "Forge.Plugin.dll"
    if (-not (Test-Path $dll)) {
        if ($AllYears) {
            $skippedYears += $candidate
            continue
        }

        Write-Error "Forge.Plugin.dll not found at $dll. Build AutoCAD $candidate first or pass -SourceDir."
        exit 1
    }

    $netloadDir = Join-Path $InstallDir "autocad-$candidate"
    $moduleDir = Join-Path $bundle "Contents\Windows\$candidate"
    Copy-ForgePluginOutput $from $netloadDir | Out-Null
    Copy-ForgePluginOutput $from $moduleDir | Out-Null
    $installedYears += $candidate
    Write-Host "Installed AutoCAD $candidate plugin to $netloadDir"
    Write-Host "  Autoloader module: $moduleDir\Forge.Plugin.dll"
}

if ($installedYears.Count -eq 0) {
    Write-Warning "No Forge.Plugin.dll was installed. Build a year whose AUTOCAD_<year>_ROOT exists."
    exit 0
}

Write-Host ""
Write-Host "Checklist:"
Write-Host "  1. Add the year folder to AutoCAD TRUSTEDPATHS (OPTIONS > Files > Trusted Locations)."
Write-Host "  2. Set user env FORGE_AUTOCAD_TOKEN (and optional FORGE_PIPE_NAME) for the AutoCAD process,"
Write-Host "     matching the MCP server env. Restart AutoCAD after changing env."
Write-Host "  3. NETLOAD the Forge.Plugin.dll built for THAT AutoCAD year (binaries are not interchangeable)."
Write-Host "  4. Run MCP_STATUS and confirm the pipe name matches FORGE_PIPE_NAME."
Write-Host "  5. Optional Autoloader: copy $bundle into an ApplicationPlugins folder."
Write-Host "     PackageContents.xml loads only the component whose SeriesMin=SeriesMax matches that release."
Write-Host "     See docs\install-plugin.md"
Write-Host ""
Write-Host "Installed years: $($installedYears -join ', ')"
if ($skippedYears.Count -gt 0) {
    Write-Host "Skipped years (no build output): $($skippedYears -join ', ')"
}

Write-Host "This script does not NETLOAD into a running AutoCAD session."
