<#
.SYNOPSIS
  Build Forge.Plugin for one AutoCAD year, or every year whose install is present.
.DESCRIPTION
  Default year is 2026 (the verified host). -AllYears builds 2017-2026 when
  AUTOCAD_<year>_ROOT (or the default Autodesk install folder) contains AcCoreMgd.dll,
  and lists years that were skipped. A missing install does not fail -AllYears.
  Passing -Year builds that year only and fails closed when its reference DLLs are missing
  or belong to a different release. See AutoCadHostCatalog for TFMs.
.PARAMETER OutDir
  Optional alternate output directory when NETLOAD has locked the default bin path.
  With -AllYears, each year is written to OutDir\autocad-<year>\.
#>
[CmdletBinding(DefaultParameterSetName = "SingleYear")]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $OutDir = "",
    [Parameter(ParameterSetName = "SingleYear")]
    [ValidateRange(2017, 2026)]
    [int] $Year = 2026,
    [Parameter(ParameterSetName = "AllYears", Mandatory = $true)]
    [switch] $AllYears
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Get-ForgeAutoCadRoot([int] $AutoCadYear) {
    $name = "AUTOCAD_${AutoCadYear}_ROOT"
    $value = [Environment]::GetEnvironmentVariable($name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return "C:\Program Files\Autodesk\AutoCAD $AutoCadYear"
    }

    return $value
}

function Invoke-ForgePluginBuild([int] $AutoCadYear) {
    $csproj = Join-Path $root "src\Forge.Plugin\Forge.Plugin.csproj"
    $buildArgs = @(
        "build", $csproj,
        "--configuration", $Configuration,
        "-p:AutoCadYear=$AutoCadYear",
        "-p:IntermediateOutputPath=obj\autocad-$AutoCadYear\"
    )

    if ($OutDir) {
        $fullOut = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }
        if ($AllYears) {
            $fullOut = Join-Path $fullOut "autocad-$AutoCadYear"
        }

        New-Item -ItemType Directory -Force -Path $fullOut | Out-Null
        if (-not $fullOut.EndsWith("\") -and -not $fullOut.EndsWith("/")) {
            $fullOut = "$fullOut\"
        }

        $buildArgs += "-p:OutDir=$fullOut"
        Write-Host "Building AutoCAD $AutoCadYear plugin to OutDir=$fullOut"
    }
    else {
        Write-Host "Building AutoCAD $AutoCadYear plugin to bin\$Configuration\autocad-$AutoCadYear\"
    }

    dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($AllYears) {
    $built = @()
    $skipped = @()
    foreach ($candidate in 2017..2026) {
        $acad = Get-ForgeAutoCadRoot $candidate
        $dll = Join-Path $acad "AcCoreMgd.dll"
        if (-not (Test-Path $dll)) {
            $skipped += "$candidate ($acad)"
            continue
        }

        Invoke-ForgePluginBuild $candidate
        $built += "$candidate ($acad)"
    }

    Write-Host "Plugin years built: $(if ($built.Count) { $built -join '; ' } else { '(none)' })"
    Write-Host "Plugin years skipped (install not found): $(if ($skipped.Count) { $skipped -join '; ' } else { '(none)' })"
    if ($built.Count -eq 0) {
        Write-Warning "No AutoCAD install was found for 2017-2026. Nothing was built."
    }

    return
}

$acadRoot = Get-ForgeAutoCadRoot $Year
if (-not (Test-Path (Join-Path $acadRoot "AcCoreMgd.dll"))) {
    Write-Warning "AcCoreMgd.dll not found under AUTOCAD_${Year}_ROOT=$acadRoot. Plugin build will fail closed."
}

Invoke-ForgePluginBuild $Year
Write-Host "Plugin build complete for AutoCAD $Year."
