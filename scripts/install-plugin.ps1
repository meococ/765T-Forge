<#
.SYNOPSIS
  Build/copy Forge.Plugin and install the Autodesk Autoloader bundle for per-user auto-load.
  By default also configures the per-user Forge environment so AutoCAD and the MCP server
  share the same non-secret-printed authentication token and all toolsets are available.
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
.PARAMETER SkipUserEnvironment
  Do not configure per-user Forge environment variables.
.PARAMETER Toolsets
  Value written to user FORGE_TOOLSETS. Defaults to all. Use an empty string to clear it.
.PARAMETER EnableUnsafeOps
  Explicitly set user FORGE_ENABLE_UNSAFE_OPS=true. This enables raw command/LISP/C# execution
  and should only be used on a controlled local machine/session.
.PARAMETER DisableUnsafeOps
  Explicitly set user FORGE_ENABLE_UNSAFE_OPS=false. If neither switch is supplied, the
  existing unsafe-ops setting is left unchanged.
.PARAMETER ForceTokenReset
  Generate and store a new user token even if FORGE_AUTOCAD_TOKEN already exists.
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
    [switch] $SkipBuild,
    [switch] $SkipUserEnvironment,
    [string] $Toolsets = "all",
    [switch] $EnableUnsafeOps,
    [switch] $DisableUnsafeOps,
    [switch] $ForceTokenReset
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($EnableUnsafeOps -and $DisableUnsafeOps) {
    Write-Error "Pass only one of -EnableUnsafeOps or -DisableUnsafeOps."
    exit 1
}

function Set-UserEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)] [string] $Name,
        [AllowEmptyString()] [string] $Value
    )

    [Environment]::SetEnvironmentVariable($Name, $Value, "User")
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

function New-ForgeToken {
    $bytes = New-Object byte[] 32
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($bytes)
    }
    finally {
        $rng.Dispose()
    }

    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

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

if (-not $SkipUserEnvironment) {
    $token = [Environment]::GetEnvironmentVariable("FORGE_AUTOCAD_TOKEN", "User")
    if ([string]::IsNullOrWhiteSpace($token)) {
        $token = [Environment]::GetEnvironmentVariable("MCP_AUTOCAD_TOKEN", "User")
    }

    $tokenAction = "kept existing"
    if ($ForceTokenReset -or [string]::IsNullOrWhiteSpace($token)) {
        $token = New-ForgeToken
        $tokenAction = if ($ForceTokenReset) { "generated new (forced)" } else { "generated new" }
    }

    Set-UserEnvironmentValue -Name "FORGE_AUTOCAD_TOKEN" -Value $token
    Set-UserEnvironmentValue -Name "FORGE_DEV_ALLOW_DEFAULT_TOKEN" -Value "false"
    Set-UserEnvironmentValue -Name "FORGE_TOOLSETS" -Value $Toolsets

    $autoCadRoot = [Environment]::GetEnvironmentVariable("AUTOCAD_2026_ROOT", "User")
    if ([string]::IsNullOrWhiteSpace($autoCadRoot)) {
        $defaultRoot = "C:\Program Files\Autodesk\AutoCAD $Year"
        if (Test-Path (Join-Path $defaultRoot "accoreconsole.exe")) {
            Set-UserEnvironmentValue -Name "AUTOCAD_2026_ROOT" -Value $defaultRoot
            $autoCadRoot = $defaultRoot
        }
    }

    if ($EnableUnsafeOps) {
        Set-UserEnvironmentValue -Name "FORGE_ENABLE_UNSAFE_OPS" -Value "true"
    }
    elseif ($DisableUnsafeOps) {
        Set-UserEnvironmentValue -Name "FORGE_ENABLE_UNSAFE_OPS" -Value "false"
    }

    Write-Host ""
    Write-Host "User environment configured:"
    Write-Host "  FORGE_AUTOCAD_TOKEN: $tokenAction (value intentionally not printed)"
    Write-Host "  FORGE_DEV_ALLOW_DEFAULT_TOKEN=false"
    Write-Host "  FORGE_TOOLSETS=$Toolsets"
    if (-not [string]::IsNullOrWhiteSpace($autoCadRoot)) {
        Write-Host "  AUTOCAD_2026_ROOT=$autoCadRoot"
    }
    if ($EnableUnsafeOps) {
        Write-Host "  FORGE_ENABLE_UNSAFE_OPS=true (explicit opt-in; raw command/LISP/C# execution enabled)"
    }
    elseif ($DisableUnsafeOps) {
        Write-Host "  FORGE_ENABLE_UNSAFE_OPS=false"
    }
    else {
        Write-Host "  FORGE_ENABLE_UNSAFE_OPS unchanged (pass -EnableUnsafeOps or -DisableUnsafeOps to change it)"
    }
}

Write-Host ""
Write-Host "Checklist:"
Write-Host "  1. Autoloader bundle installed at: $BundleDir"
Write-Host "     AutoCAD 2025/2026 loads Forge.Plugin.dll automatically at startup (no manual NETLOAD needed)."
Write-Host "  2. User token/toolset environment is configured without printing secrets."
Write-Host "  3. Restart AutoCAD $Year and run MCP_STATUS to verify the named pipe is listening."
Write-Host ""
