<#
.SYNOPSIS
  Build Forge.Plugin. Requires AutoCAD 2026 reference assemblies.
.PARAMETER OutDir
  Optional alternate output directory when NETLOAD has locked the default bin path.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $OutDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $env:AUTOCAD_2026_ROOT) {
    $env:AUTOCAD_2026_ROOT = "C:\Program Files\Autodesk\AutoCAD 2026"
}

$acad = $env:AUTOCAD_2026_ROOT
if (-not (Test-Path (Join-Path $acad "AcCoreMgd.dll"))) {
    Write-Warning "AcCoreMgd.dll not found under AUTOCAD_2026_ROOT=$acad. Plugin build will likely fail."
}

$csproj = Join-Path $root "src\Forge.Plugin\Forge.Plugin.csproj"
$args = @("build", $csproj, "--configuration", $Configuration)

if ($OutDir) {
    $fullOut = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
    New-Item -ItemType Directory -Force -Path $fullOut | Out-Null
    $args += "-p:OutDir=$fullOut"
    Write-Host "Building plugin to OutDir=$fullOut"
}
else {
    Write-Host "Building plugin (default output). If DLLs are locked, pass -OutDir artifacts\Forge.Plugin\"
}

dotnet @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Plugin build complete."
