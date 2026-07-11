<#
.SYNOPSIS
  Copy Forge.Plugin build output to a local install directory and print NETLOAD / TRUSTEDPATHS checklist.
.PARAMETER Configuration
  Build configuration used to locate default bin output when -SourceDir is omitted.
.PARAMETER SourceDir
  Directory containing Forge.Plugin.dll (default: plugin bin for Configuration).
.PARAMETER InstallDir
  Destination directory (default: %LOCALAPPDATA%\765T-Forge\plugin).
.PARAMETER SkipBuild
  Do not invoke build-plugin.ps1 first.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $SourceDir = "",
    [string] $InstallDir = "",
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-plugin.ps1") -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SourceDir) {
    $SourceDir = Join-Path $root "src\Forge.Plugin\bin\$Configuration\net8.0-windows"
}
if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "765T-Forge\plugin"
}

$dll = Join-Path $SourceDir "Forge.Plugin.dll"
if (-not (Test-Path $dll)) {
    Write-Error "Forge.Plugin.dll not found at $dll. Build the plugin first or pass -SourceDir."
    exit 1
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force
$installed = Join-Path $InstallDir "Forge.Plugin.dll"

Write-Host ""
Write-Host "Installed plugin to: $installed"
Write-Host ""
Write-Host "Checklist:"
Write-Host "  1. Add this folder to AutoCAD TRUSTEDPATHS (OPTIONS > Files > Trusted Locations):"
Write-Host "       $InstallDir"
Write-Host "  2. Set user env FORGE_AUTOCAD_TOKEN (and optional FORGE_PIPE_NAME) for the AutoCAD process,"
Write-Host "     matching the MCP server env. Restart AutoCAD after changing env."
Write-Host "  3. In AutoCAD 2026: NETLOAD -> $installed"
Write-Host "  4. Run MCP_STATUS and confirm the pipe name matches FORGE_PIPE_NAME."
Write-Host "  5. Optional Autoloader: copy plugin-bundle\765T-Forge.bundle into an ApplicationPlugins folder."
Write-Host "     See docs\install-plugin.md"
Write-Host ""
Write-Host "This script does not NETLOAD into a running AutoCAD session."
