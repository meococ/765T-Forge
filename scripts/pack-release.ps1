<#
.SYNOPSIS
  Pack release artifacts: always publish Forge.Server; optionally pack plugin when AutoCAD refs exist.
.PARAMETER SkipPlugin
  Skip plugin packaging (default for CI without AutoCAD).
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [switch] $SkipPlugin
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot "ForgeBundleLayout.ps1")

$releaseRoot = Join-Path $root "artifacts\release"
$serverOut = Join-Path $releaseRoot "server"
New-Item -ItemType Directory -Force -Path $serverOut | Out-Null

$serverProj = Join-Path $root "src\Forge.Server\Forge.Server.csproj"
Write-Host "Publishing Forge.Server (win-x64)..."
dotnet publish $serverProj `
    --configuration $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $serverOut
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Honesty docs must be present next to the exe for forge:// MCP resources.
$matrixInZip = Join-Path $serverOut "docs\capability-matrix.md"
$safetyInZip = Join-Path $serverOut "docs\safety.md"
if (-not (Test-Path $matrixInZip) -or -not (Test-Path $safetyInZip)) {
    Write-Warning "docs missing from publish output - copying from repo."
    $docsOut = Join-Path $serverOut "docs"
    New-Item -ItemType Directory -Force -Path $docsOut | Out-Null
    Copy-Item (Join-Path $root "docs\capability-matrix.md") $docsOut -Force
    Copy-Item (Join-Path $root "docs\safety.md") $docsOut -Force
}

$serverZip = Join-Path $releaseRoot "765T-Forge.Server-win-x64.zip"
if (Test-Path $serverZip) { Remove-Item $serverZip -Force }
Compress-Archive -Path (Join-Path $serverOut "*") -DestinationPath $serverZip
Write-Host "Wrote $serverZip"

$pluginPacked = $false
$pluginYearsPacked = @()
if (-not $SkipPlugin) {
    & (Join-Path $PSScriptRoot "build-plugin.ps1") -Configuration $Configuration -AllYears
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $pluginBin = Join-Path $root "src\Forge.Plugin\bin\$Configuration"
    $yearDirs = @()
    if (Test-Path $pluginBin) {
        $yearDirs = @(Get-ChildItem -Path $pluginBin -Directory -Filter "autocad-*" -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName "Forge.Plugin.dll") })
    }

    if ($yearDirs.Count -eq 0) {
        Write-Warning "No per-year Forge.Plugin.dll was produced. Skipping plugin pack."
    }
    else {
        $stageRoot = Join-Path $releaseRoot "plugin-stage"
        $bundleStage = Join-Path $stageRoot "765T-Forge.bundle"
        if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
        New-Item -ItemType Directory -Force -Path (Join-Path $bundleStage "Contents\Windows") | Out-Null
        Copy-Item (Join-Path $root "plugin-bundle\765T-Forge.bundle\README.md") $bundleStage -Force

        foreach ($dir in $yearDirs) {
            $yearName = $dir.Name.Substring("autocad-".Length)
            $moduleDir = Join-Path $bundleStage "Contents\Windows\$yearName"
            New-Item -ItemType Directory -Force -Path $moduleDir | Out-Null
            Get-ChildItem -Path $dir.FullName -File |
                Where-Object { $_.Name -notmatch '^(AcCoreMgd|AcDbMgd|AcMgd)\.dll$' } |
                ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $moduleDir $_.Name) -Force }
            $pluginYearsPacked += $yearName
        }

        # Template lists 2017-2026. The zip lists only years whose DLL was copied.
        Sync-ForgePackageContents -BundleRoot $bundleStage -TemplatePath (Join-Path $root "plugin-bundle\765T-Forge.bundle\PackageContents.xml")

        $pluginZip = Join-Path $releaseRoot "765T-Forge.Plugin.zip"
        if (Test-Path $pluginZip) { Remove-Item $pluginZip -Force }
        Compress-Archive -Path $bundleStage -DestinationPath $pluginZip
        Remove-Item $stageRoot -Recurse -Force
        Write-Host "Wrote $pluginZip for AutoCAD years $($pluginYearsPacked -join ', ') (Autodesk Ac*.dll excluded)"
        $pluginPacked = $true
    }
}
else {
    Write-Host "SkipPlugin set - server-only pack (CI default)."
}

$pluginPackedText = if ($pluginPacked) { "true" } else { "false" }
$completeText = if ((-not $SkipPlugin.IsPresent) -and $pluginPacked) { "true" } else { "false" }
Write-Host "Pack complete. Server=yes PluginPacked=$pluginPackedText Output=$releaseRoot"

$statusFile = Join-Path $releaseRoot "RELEASE_STATUS.txt"
@(
    "765T-Forge release pack status"
    "ServerZip=yes"
    "PluginZip=$pluginPackedText"
    "PluginYears=$(if ($pluginYearsPacked.Count) { $pluginYearsPacked -join ',' } else { 'none' })"
    "Complete=$completeText"
    "Note=A GitHub Release without 765T-Forge.Plugin.zip is INCOMPLETE."
) | Set-Content -LiteralPath $statusFile -Encoding utf8
Write-Host "Wrote $statusFile"

if ($SkipPlugin.IsPresent -or -not $pluginPacked) {
    Write-Warning "INCOMPLETE RELEASE ARTIFACT SET: attach 765T-Forge.Plugin.zip before announcing a public v* tag."
}
