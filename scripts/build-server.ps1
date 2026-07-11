<#
.SYNOPSIS
  Restore, build, and test 765T-Forge.ServerOnly.slnf (no AutoCAD required).
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipTest
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$filter = Join-Path $root "765T-Forge.ServerOnly.slnf"
if (-not (Test-Path $filter)) {
    throw "Missing solution filter: $filter"
}

Write-Host "Restoring ServerOnly..."
dotnet restore $filter
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building ServerOnly ($Configuration)..."
dotnet build $filter --no-restore --configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $SkipTest) {
    Write-Host "Testing ServerOnly ($Configuration)..."
    dotnet test $filter --no-build --configuration $Configuration --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "ServerOnly build complete."
