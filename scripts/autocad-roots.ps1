<#
.SYNOPSIS
  Shared, deterministic AutoCAD install/reference-root resolution for the Forge plugin scripts.

.DESCRIPTION
  Mirrors src/Forge.Shared/ForgeEnvironment.DefaultAutoCadRoot for the runtime install root and
  src/Forge.Plugin/Forge.Plugin.csproj for the two reference-assembly roots.

  Runtime install root order (same as ForgeEnvironment):
    1. FORGE_AUTOCAD_ROOT
    2. AUTOCAD_2026_ROOT (deprecated alias)
    3. highest directory named exactly "AutoCAD <4 digits>" under C:\Program Files\Autodesk
       that contains accoreconsole.exe

  Modern reference root order (net8.0-windows, AutoCAD 2025-2026 = .NET 8):
    1. FORGE_AUTOCAD_MODERN_ROOT
    2. AUTOCAD_2025_ROOT
    3. FORGE_AUTOCAD_ROOT
    4. highest of C:\Program Files\Autodesk\AutoCAD 2026 / 2025 that contains all three reference DLLs
  AutoCAD 2027 is .NET 10 and cannot be referenced from a net8.0 build (CS1705), so it is never
  used as the modern reference root.

  Legacy reference root order (net462, AutoCAD 2017-2024):
    1. FORGE_AUTOCAD_LEGACY_ROOT
    2. AUTOCAD_2017_ROOT (deprecated alias)
  There is no fallback to a newer series for the legacy target.
#>

$script:ForgeAutoCadReferenceDlls = @("AcCoreMgd.dll", "AcDbMgd.dll", "AcMgd.dll")

function Test-ForgeAutoCadReferenceAssemblies {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) {
        return $false
    }

    foreach ($dll in $script:ForgeAutoCadReferenceDlls) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $dll) -PathType Leaf)) {
            return $false
        }
    }

    return $true
}

function Get-ForgeAutoCadInstallRoots {
    [CmdletBinding()]
    param()

    $parent = "C:\Program Files\Autodesk"
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        return @()
    }

    $roots = @()
    foreach ($directory in Get-ChildItem -LiteralPath $parent -Directory) {
        if ($directory.Name -notmatch '^AutoCAD [0-9]{4}$') {
            continue
        }

        if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName "accoreconsole.exe") -PathType Leaf)) {
            continue
        }

        $roots += [pscustomobject]@{
            Year = [int]$directory.Name.Substring("AutoCAD ".Length)
            Root = $directory.FullName
        }
    }

    return @($roots | Sort-Object -Property Year -Descending)
}

function Resolve-ForgeAutoCadRoot {
    [CmdletBinding()]
    param()

    foreach ($name in @("FORGE_AUTOCAD_ROOT", "AUTOCAD_2026_ROOT")) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    $installs = Get-ForgeAutoCadInstallRoots
    if ($installs.Count -gt 0) {
        return $installs[0].Root
    }

    return ""
}

function Resolve-ForgeAutoCadModernRoot {
    [CmdletBinding()]
    param()

    foreach ($name in @("FORGE_AUTOCAD_MODERN_ROOT", "AUTOCAD_2025_ROOT", "FORGE_AUTOCAD_ROOT")) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    foreach ($year in @(2026, 2025)) {
        $candidate = "C:\Program Files\Autodesk\AutoCAD $year"
        if (Test-ForgeAutoCadReferenceAssemblies -Root $candidate) {
            return $candidate
        }
    }

    return ""
}

function Resolve-ForgeAutoCadLegacyRoot {
    [CmdletBinding()]
    param()

    foreach ($name in @("FORGE_AUTOCAD_LEGACY_ROOT", "AUTOCAD_2017_ROOT")) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return ""
}

function Assert-ForgeAutoCadReferenceRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Label,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Root,
        [Parameter(Mandatory = $true)]
        [string] $Remedy
    )

    if ([string]::IsNullOrWhiteSpace($Root)) {
        Write-Host "MISSING: $Label reference root is not set. $Remedy"
        exit 1
    }

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        Write-Host "MISSING: $Label reference root '$Root' does not exist. $Remedy"
        exit 1
    }

    foreach ($dll in $script:ForgeAutoCadReferenceDlls) {
        $candidate = Join-Path $Root $dll
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            Write-Host "MISSING: $candidate"
            exit 1
        }
    }

    Write-Host "OK: $Label reference assemblies at $Root"
}
