<#
.SYNOPSIS
  Copy Forge.Plugin build output to %LOCALAPPDATA%\765T-Forge\plugin\<Year> AND package/install the
  Autodesk Autoloader bundle into %APPDATA%\Autodesk\ApplicationPlugins\765T-Forge.bundle.
.PARAMETER Configuration
  Build configuration used to locate default bin output when -SourceDir is omitted.
.PARAMETER SourceDir
  Directory containing Forge.Plugin.dll (default: artifacts\Forge.Plugin\<Year>).
.PARAMETER InstallDir
  Destination directory (default: %LOCALAPPDATA%\765T-Forge\plugin\<Year>).
.PARAMETER BundleDir
  Autodesk ApplicationPlugins bundle directory (default: %APPDATA%\Autodesk\ApplicationPlugins\765T-Forge.bundle).
.PARAMETER SkipBundle
  Skip installing into %APPDATA%\Autodesk\ApplicationPlugins\765T-Forge.bundle.
.PARAMETER SkipBuild
  Do not invoke build-plugin.ps1 first.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [ValidateSet("2025", "2026")]
    [string] $Year = "2026",
    [string] $SourceDir = "",
    [string] $InstallDir = "",
    [string] $BundleDir = "",
    [switch] $SkipBundle,
    [switch] $SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-plugin.ps1") -Configuration $Configuration -Year $Year
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if (-not $SourceDir) {
    $SourceDir = Join-Path $root "artifacts\Forge.Plugin\$Year"
}
if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "765T-Forge\plugin\$Year"
}
if (-not $BundleDir) {
    $BundleDir = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\765T-Forge.bundle"
}

$dll = Join-Path $SourceDir "Forge.Plugin.dll"
if (-not (Test-Path $dll)) {
    Write-Error "Forge.Plugin.dll not found at $dll. Build the plugin first or pass -SourceDir."
    exit 1
}

# Never ship AutoCAD host assemblies inside plugin/bundle folders.
$excludedHostDlls = @("AcCoreMgd.dll", "AcDbMgd.dll", "AcMgd.dll")

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Get-ChildItem -Path $SourceDir -Recurse -File | Where-Object { $excludedHostDlls -notcontains $_.Name } | ForEach-Object {
    $rel = $_.FullName.Substring($SourceDir.Length).TrimStart('\')
    $dest = Join-Path $InstallDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
}
$installed = Join-Path $InstallDir "Forge.Plugin.dll"

Write-Host ""
Write-Host "Installed plugin (year $Year) to: $installed"

if (-not $SkipBundle) {
    $bundleXmlSrc = Join-Path $root "plugin-bundle\765T-Forge.bundle\PackageContents.xml"
    if (-not (Test-Path $bundleXmlSrc)) {
        Write-Error "PackageContents.xml not found at $bundleXmlSrc."
        exit 1
    }

    $bundleWinDir = Join-Path $BundleDir "Contents\Windows"
    New-Item -ItemType Directory -Force -Path $bundleWinDir | Out-Null
    Copy-Item -LiteralPath $bundleXmlSrc -Destination (Join-Path $BundleDir "PackageContents.xml") -Force

    Get-ChildItem -Path $SourceDir -Recurse -File | Where-Object { $excludedHostDlls -notcontains $_.Name } | ForEach-Object {
        $rel = $_.FullName.Substring($SourceDir.Length).TrimStart('\')
        $dest = Join-Path $bundleWinDir $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
    }

    $roslynCheck = Join-Path $bundleWinDir "Microsoft.CodeAnalysis.CSharp.Scripting.dll"
    Write-Host "Installed Autoloader bundle (LoadOnAutoCADStartup=True) to: $BundleDir"
    Write-Host ("Roslyn C# scripting bundled: " + (Test-Path $roslynCheck))
}

Write-Host ""
Write-Host "Checklist:"
Write-Host "  1. Autoloader bundle installed at: $BundleDir"
Write-Host "     AutoCAD 2025/2026 loads Forge.Plugin.dll automatically at startup (no manual NETLOAD needed)."
Write-Host "  2. Ensure user env FORGE_AUTOCAD_TOKEN (or FORGE_DEV_ALLOW_DEFAULT_TOKEN=true) matches the MCP server."
Write-Host "  3. Restart AutoCAD ${Year} and run MCP_STATUS to verify the named pipe is listening."
Write-Host ""
