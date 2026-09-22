<#
.SYNOPSIS
  Keep an autoloader bundle's PackageContents.xml aligned with the year folders that actually contain Forge.Plugin.dll.
.DESCRIPTION
  The repository template lists AutoCAD 2017-2026. A packed or installed bundle must list only the years
  whose DLL was copied to Contents/Windows/<year>/Forge.Plugin.dll. This matches AutoCadHostCatalog.FilterPackageContentsXml.
#>

function Select-ForgePackageContents {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $TemplatePath,
        [Parameter(Mandatory = $true)]
        [string] $DestinationPath,
        [int[]] $Years = @()
    )

    $wanted = @{}
    foreach ($year in $Years) {
        $wanted[[int]$year] = $true
    }

    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $true
    $doc.Load($TemplatePath)
    $package = $doc.DocumentElement
    $remove = New-Object System.Collections.Generic.List[System.Xml.XmlNode]
    foreach ($component in @($package.SelectNodes("./Components"))) {
        $entry = $component.SelectSingleNode("./ComponentEntry")
        $module = ""
        if ($null -ne $entry) {
            $module = $entry.GetAttribute("ModuleName")
        }

        $keep = $false
        if ($module -match 'Windows/(\d+)/Forge\.Plugin\.dll') {
            $year = [int]$Matches[1]
            $keep = $wanted.ContainsKey($year)
        }

        if (-not $keep) {
            $remove.Add($component) | Out-Null
        }
    }

    foreach ($node in $remove) {
        [void]$package.RemoveChild($node)
    }

    $parent = Split-Path -Parent $DestinationPath
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    $doc.Save($DestinationPath)
}

function Sync-ForgePackageContents {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $BundleRoot,
        [Parameter(Mandatory = $true)]
        [string] $TemplatePath
    )

    $years = New-Object System.Collections.Generic.List[int]
    $windows = Join-Path $BundleRoot "Contents\Windows"
    if (Test-Path -LiteralPath $windows) {
        foreach ($dir in @(Get-ChildItem -LiteralPath $windows -Directory -ErrorAction SilentlyContinue)) {
            $dll = Join-Path $dir.FullName "Forge.Plugin.dll"
            if (-not (Test-Path -LiteralPath $dll)) {
                continue
            }

            $parsed = 0
            if ([int]::TryParse($dir.Name, [ref]$parsed)) {
                $years.Add($parsed) | Out-Null
            }
        }
    }

    Select-ForgePackageContents -TemplatePath $TemplatePath -DestinationPath (Join-Path $BundleRoot "PackageContents.xml") -Years $years.ToArray()
}
