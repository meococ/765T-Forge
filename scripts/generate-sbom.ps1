<#
.SYNOPSIS
  Generate a CycloneDX SBOM for Forge.Server (and optionally the solution) for GitHub Releases.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",
    [string] $OutputDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "artifacts\release"
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$serverProj = Join-Path $root "src\Forge.Server\Forge.Server.csproj"
$sbomPath = Join-Path $OutputDir "765T-Forge.Server.sbom.cdx.json"

Write-Host "Generating CycloneDX SBOM for Forge.Server..."
dotnet tool restore 2>$null | Out-Null

# Prefer local tool if present; otherwise use `dotnet CycloneDX` package via temporary install.
$generated = $false
try {
    dotnet tool run cyclonedx -- $serverProj -o $OutputDir -j -f "765T-Forge.Server.sbom.cdx.json" 2>$null
    if ($LASTEXITCODE -eq 0 -and (Test-Path $sbomPath)) { $generated = $true }
} catch { }

if (-not $generated) {
    Write-Host "Falling back to Microsoft.Sbom.DotNetTool / manual package list..."
    $deps = Join-Path $OutputDir "765T-Forge.Server.deps.json"
    dotnet publish $serverProj -c $Configuration -r win-x64 --self-contained false -o (Join-Path $OutputDir "sbom-publish") | Out-Null
    $publishDeps = Get-ChildItem -Path (Join-Path $OutputDir "sbom-publish") -Filter "*.deps.json" -Recurse | Select-Object -First 1
    $components = @()
    if ($publishDeps) {
        $json = Get-Content $publishDeps.FullName -Raw | ConvertFrom-Json
        foreach ($lib in $json.libraries.PSObject.Properties) {
            if ($lib.Value.type -eq "package") {
                $parts = $lib.Name -split "/"
                if ($parts.Length -ge 2) {
                    $components += [ordered]@{
                        type = "library"
                        name = $parts[0]
                        version = $parts[1]
                        "bom-ref" = "pkg:nuget/$($parts[0])@$($parts[1])"
                    }
                }
            }
        }
    }

    $bom = [ordered]@{
        bomFormat = "CycloneDX"
        specVersion = "1.5"
        version = 1
        metadata = [ordered]@{
            timestamp = (Get-Date).ToUniversalTime().ToString("o")
            component = [ordered]@{
                type = "application"
                name = "765T-Forge.Server"
                version = (Select-Xml -Path (Join-Path $root "Directory.Build.props") -XPath "//Version").Node.InnerText
            }
        }
        components = $components
    }
    $bom | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $sbomPath -Encoding utf8
}

Write-Host "Wrote $sbomPath"
