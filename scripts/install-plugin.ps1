<#
.SYNOPSIS
  Copy Forge.Plugin build output to a local install directory and print NETLOAD / TRUSTEDPATHS checklist.

.DESCRIPTION
  By default the modern net8.0-windows assembly (AutoCAD 2025-2027) is installed to
  <InstallDir>\Contents\2025. With -AllSeries the legacy net462 assembly (AutoCAD 2017-2024) is also
  installed to <InstallDir>\Contents\2017, mirroring the Autodesk bundle layout.

.PARAMETER Configuration
  Build configuration used to locate default bin output when -SourceDir is omitted.

.PARAMETER SourceDir
  Directory containing Forge.Plugin.dll. When set, its contents are copied flat into -InstallDir and
  no build layout is assumed.

.PARAMETER InstallDir
  Destination directory (default: %LOCALAPPDATA%\765T-Forge\plugin).

.PARAMETER AllSeries
  Also install the legacy net462 assembly for AutoCAD 2017-2024 (requires AutoCAD 2017 refs to build).

.PARAMETER SkipBuild
  Do not invoke build-plugin.ps1 first.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $SourceDir = "",
    [string] $InstallDir = "",
    [switch] $AllSeries,
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    $buildArgs = @{ Configuration = $Configuration }
    if ($AllSeries) { $buildArgs.AllSeries = $true }
    & (Join-Path $PSScriptRoot "build-plugin.ps1") @buildArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "765T-Forge\plugin"
}

if ($SourceDir) {
    $dll = Join-Path $SourceDir "Forge.Plugin.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        Write-Error "Forge.Plugin.dll not found at $dll. Build the plugin first or pass -SourceDir."
        exit 1
    }

    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force
    $installed = Join-Path $InstallDir "Forge.Plugin.dll"
}
else {
    $installed = ""
    $components = @(
        @{ Tfm = "net8.0-windows"; Folder = "2025" }
    )
    if ($AllSeries) {
        $components += @{ Tfm = "net462"; Folder = "2017" }
    }

    foreach ($component in $components) {
        $source = Join-Path $root "src\Forge.Plugin\bin\$Configuration\$($component.Tfm)"
        $dll = Join-Path $source "Forge.Plugin.dll"
        if (-not (Test-Path -LiteralPath $dll)) {
            Write-Error "Forge.Plugin.dll not found at $dll. Build the plugin first or pass -SourceDir."
            exit 1
        }

        $destination = Join-Path $InstallDir "Contents\$($component.Folder)"
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item -Path (Join-Path $source "*") -Destination $destination -Recurse -Force
        Remove-Item -Path (Join-Path $destination "AcCoreMgd.dll"), (Join-Path $destination "AcDbMgd.dll"), (Join-Path $destination "AcMgd.dll") -Force -ErrorAction SilentlyContinue
        Write-Host "Installed $($component.Tfm) to $destination"

        if ($component.Folder -eq "2025") {
            $installed = Join-Path $destination "Forge.Plugin.dll"
        }
    }
}

$legacyInstalled = Join-Path $InstallDir "Contents\2017\Forge.Plugin.dll"
Write-Host ""
Write-Host "Installed plugin to: $installed"
Write-Host ""
Write-Host "Checklist:"
Write-Host "  1. Add this folder to AutoCAD TRUSTEDPATHS (OPTIONS > Files > Trusted Locations):"
Write-Host "       $(Split-Path -Parent $installed)"
Write-Host "  2. Set user env FORGE_AUTOCAD_TOKEN (and optional FORGE_PIPE_NAME) for the AutoCAD process,"
Write-Host "     matching the MCP server env. Restart AutoCAD after changing env."
Write-Host "  3. NETLOAD the DLL for your series:"
Write-Host "       AutoCAD 2025-2027: $installed"
Write-Host "       AutoCAD 2017-2024: $legacyInstalled"
Write-Host "  4. Run MCP_STATUS and confirm the pipe name matches FORGE_PIPE_NAME."
Write-Host "  5. Optional Autoloader: copy plugin-bundle\765T-Forge.bundle into an ApplicationPlugins folder."
Write-Host "     See docs\install\plugin.md"
Write-Host ""
Write-Host "This script does not NETLOAD into a running AutoCAD session."
