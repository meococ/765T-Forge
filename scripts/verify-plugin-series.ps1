<#
.SYNOPSIS
  Deterministic per-series build gate for Forge.Plugin.

.DESCRIPTION
  For every requested AutoCAD series year this script:
    1. resolves the exact reference-assembly root that series must be built against,
    2. asserts with Test-Path that AcCoreMgd.dll, AcDbMgd.dll and AcMgd.dll all exist there,
    3. builds the matching plugin TFM.

  Nothing is skipped silently: a missing root or DLL prints the exact path that is missing and
  exits with code 1.

  Series mapping (Autodesk official):
    2017-2024 -> net462 legacy build against AutoCAD 2017 reference assemblies
                 (FORGE_AUTOCAD_LEGACY_ROOT, then AUTOCAD_2017_ROOT)
    2025, 2026 -> net8.0-windows modern build against that exact installed year
    2027       -> net8.0-windows artifact target only. AutoCAD 2027 is .NET 10 and its assemblies
                  cannot be referenced from a net8.0 compilation (CS1705), so the install is
                  verified exactly and the modern assembly is built against the .NET 8 reference
                  series root (2026 then 2025). The resulting net8.0 assembly loads on AutoCAD
                  2027's .NET 10 runtime.

.PARAMETER Series
  Required. AutoCAD years to verify, for example 2017,2025. Accepts a comma list or an array.

.PARAMETER ModernYear
  Optional. Exact installed AutoCAD year to use as the modern reference root. Must be 2025 or 2026
  because the net8.0-windows reference series is AutoCAD 2025-2026 (.NET 8).

.PARAMETER Configuration
  Build configuration. Default Release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $Series,
    [int] $ModernYear = 0,
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot "autocad-roots.ps1")

$csproj = Join-Path $root "src\Forge.Plugin\Forge.Plugin.csproj"

$years = @()
foreach ($entry in $Series) {
    foreach ($part in ($entry -split ",")) {
        $trimmed = $part.Trim()
        if ($trimmed.Length -eq 0) {
            continue
        }

        $parsed = 0
        if (-not [int]::TryParse($trimmed, [ref] $parsed)) {
            Write-Host "INVALID: series '$trimmed' is not a four-digit AutoCAD year."
            exit 1
        }

        $years += $parsed
    }
}

if ($years.Count -eq 0) {
    Write-Host "INVALID: -Series is empty."
    exit 1
}

$legacyYears = @($years | Where-Object { $_ -le 2024 } | Sort-Object -Unique)
$modernYears = @($years | Where-Object { $_ -ge 2025 } | Sort-Object -Unique)

if ($legacyYears.Count -gt 0) {
    $legacyRoot = Resolve-ForgeAutoCadLegacyRoot
    Assert-ForgeAutoCadReferenceRoot `
        -Label "legacy net462 (AutoCAD 2017-2024, requested series $($legacyYears -join ','))" `
        -Root $legacyRoot `
        -Remedy "Set FORGE_AUTOCAD_LEGACY_ROOT (or the deprecated AUTOCAD_2017_ROOT) to the AutoCAD 2017 install directory or the ObjectARX 2017 SDK 'inc' folder."

    Write-Host "Building net462 against AutoCAD 2017 reference assemblies..."
    dotnet build $csproj -p:ForgeBuildLegacy=true -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($modernYears.Count -gt 0) {
    foreach ($year in $modernYears) {
        $installRoot = "C:\Program Files\Autodesk\AutoCAD $year"
        Assert-ForgeAutoCadReferenceRoot `
            -Label "installed AutoCAD $year" `
            -Root $installRoot `
            -Remedy "Install AutoCAD $year or point FORGE_AUTOCAD_ROOT at an existing install."
    }

    $modernRoot = ""
    if ($ModernYear -ne 0) {
        if ($ModernYear -ne 2025 -and $ModernYear -ne 2026) {
            Write-Host "INVALID: -ModernYear $ModernYear cannot be a net8.0-windows reference root. AutoCAD 2025 and 2026 are .NET 8; AutoCAD 2027 is .NET 10 (CS1705)."
            exit 1
        }

        $modernRoot = "C:\Program Files\Autodesk\AutoCAD $ModernYear"
        Assert-ForgeAutoCadReferenceRoot `
            -Label "modern net8.0-windows reference series" `
            -Root $modernRoot `
            -Remedy "Install AutoCAD $ModernYear or set FORGE_AUTOCAD_MODERN_ROOT."
    }
    else {
        $modernRoot = Resolve-ForgeAutoCadModernRoot
        Assert-ForgeAutoCadReferenceRoot `
            -Label "modern net8.0-windows reference series (AutoCAD 2025-2026)" `
            -Root $modernRoot `
            -Remedy "Set FORGE_AUTOCAD_MODERN_ROOT (or AUTOCAD_2025_ROOT / FORGE_AUTOCAD_ROOT) to an AutoCAD 2025/2026 install directory."
    }

    if ($modernYears -contains 2027) {
        Write-Host "NOTE: AutoCAD 2027 is .NET 10; the net8.0-windows assembly is compiled against $modernRoot and loads on AutoCAD 2027's .NET 10 runtime."
    }

    Write-Host "Building net8.0-windows against $modernRoot..."
    dotnet build $csproj -c $Configuration -p:ForgeAutoCadModernRoot="$modernRoot" --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

Write-Host "verify-plugin-series: OK ($($years -join ', '))"
