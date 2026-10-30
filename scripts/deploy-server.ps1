<#
.SYNOPSIS
  Build Forge.Server and deploy it into a dedicated folder nothing builds over.

.DESCRIPTION
  The live MCP entry must not run the server straight out of a build output
  folder: any build into src\Forge.Server\bin changes (or breaks) the server
  every open MCP client is running. This script publishes Forge.Server and
  copies the result into <RepoRoot>\deploy\server (the default -Destination)
  so the live entry can be switched to a folder no build touches.

  Refusals (nothing is deployed):
  - src\ under <RepoRoot> has uncommitted changes (unless -AllowDirty). The
    deploy must trace to a committed tree; commit first.
  - a Forge.Server.exe process is running from the destination - close or
    disable the MCP clients that use this folder, then retry. The script never
    stops or kills a process. Forge.Server.exe processes running from other
    folders (e.g. src\Forge.Server\bin\Debug) are not a reason to refuse.
  When git is unavailable or <RepoRoot> is not a git repository root (a plain
  folder, or a folder nested inside some other repo), the git fields in
  DEPLOYED.json are recorded as null and the deploy proceeds.

  Swap (a failed deploy never leaves a half-copied live folder):
    1. the source files are copied into a sibling staging folder
       "<Destination>.staging-<utc>-<id>",
    2. every staged file is verified by SHA256 against the source (any
       mismatch deletes only that staging folder and fails),
    3. DEPLOYED.json is written into staging (commit, branch, dirty flag,
       configuration, utc, source path, per-file sha256 and bytes),
    4. an existing destination is renamed to "<Destination>.prev" - exactly one
       previous copy is kept; an older .prev is removed first,
    5. staging is renamed to the destination. If this fails after step 4,
       .prev is renamed back and the failure is reported.
  Only the staging folder this run created and "<Destination>.prev" are ever
  deleted; nothing else is touched.

  -DryRun prints the full plan ([WOULD-*] lines) and writes nothing: no build,
  no staging folder, no destination changes. The refusal checks still run.

.PARAMETER Configuration
  Build configuration for dotnet publish (Release or Debug). Default Release.

.PARAMETER RepoRoot
  Repo checkout to publish and to read git state from. Defaults to the parent
  of this script's folder, so the script on master can deploy a server built
  from a checkout on another branch.

.PARAMETER Destination
  Folder the server is deployed into. Default: <RepoRoot>\deploy\server.

.PARAMETER SourceDir
  TEST HOOK: skip the dotnet publish step and deploy this folder's contents
  instead.

.PARAMETER DryRun
  Print the plan and write nothing.

.PARAMETER AllowDirty
  Deploy even when git reports uncommitted changes under <RepoRoot>\src.

.PARAMETER ProcessFixture
  TEST HOOK: path to a JSON file replacing live process enumeration for the
  running-process check - an array of { "pid": 123, "path": "D:\\...\\Forge.Server.exe" }.
  Lets tests simulate a server already running from the destination.

.PARAMETER CorruptStagedFile
  TEST HOOK: name (path relative to the staged folder) of a file to corrupt
  inside the staging folder after the copy but before hash verification, to
  prove a bad copy aborts the deploy without touching the destination.
#>
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),

    [string] $Destination = "",

    [string] $SourceDir,

    [switch] $DryRun,

    [switch] $AllowDirty,

    [string] $ProcessFixture,

    [string] $CorruptStagedFile
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- helpers

# Native stderr arrives as ErrorRecords that can terminate under
# $ErrorActionPreference='Stop'; run git with it relaxed and report the exit
# code instead.
function Invoke-Git {
    param([Parameter(Mandatory)][string[]] $Args)
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git -C $RepoRoot @Args 2>$null
        return @{ Out = $out; Code = $LASTEXITCODE }
    } finally {
        $ErrorActionPreference = $eap
    }
}

function Get-ForgeServerProcess {
    if ($ProcessFixture) {
        if (-not (Test-Path -LiteralPath $ProcessFixture)) {
            throw "-ProcessFixture not found: $ProcessFixture"
        }
        $fx = Get-Content -LiteralPath $ProcessFixture -Raw | ConvertFrom-Json
        return @($fx | ForEach-Object { [pscustomobject]@{ Pid = [int]$_.pid; Path = [string]$_.path } })
    }
    try {
        return @(Get-CimInstance Win32_Process -Filter "Name='Forge.Server.exe'" -ErrorAction Stop |
                 ForEach-Object { [pscustomobject]@{ Pid = [int]$_.ProcessId; Path = [string]$_.ExecutablePath } })
    } catch {
        return @(Get-Process -Name 'Forge.Server' -ErrorAction SilentlyContinue | ForEach-Object {
            $p = $null
            try { $p = $_.Path } catch { $p = $null }
            if ($p) { [pscustomobject]@{ Pid = $_.Id; Path = $p } }
        })
    }
}

# ---------------------------------------------------------------- paths

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "RepoRoot not found: $RepoRoot"
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path.TrimEnd('\')
if (-not $Destination) { $Destination = Join-Path $RepoRoot 'deploy\server' }
$Destination = ([IO.Path]::GetFullPath($Destination)).TrimEnd('\')
$prevDir = "$Destination.prev"
$destExe = Join-Path $Destination 'Forge.Server.exe'

$stampUtc   = (Get-Date).ToUniversalTime()
$stampText  = $stampUtc.ToString('yyyyMMddTHHmmssZ')
# Unique even for two deploys inside the same second; we only ever delete the
# exact staging folder this run created.
$stagingDir = "$Destination.staging-$stampText-" + [guid]::NewGuid().ToString('N').Substring(0, 8)

# ---------------------------------------------------------------- git state

$gitCommit = $null
$gitBranch = $null
$gitDirty  = $null
$gitNote   = 'git not available or RepoRoot is not a repository root'
if (Get-Command git -ErrorAction SilentlyContinue) {
    # git -C resolves the CONTAINING repo, so a RepoRoot nested inside another
    # repo (like a tree export under it) would wrongly record that repo's
    # state. Only a RepoRoot that is the repo's own top-level counts.
    $top = Invoke-Git @('rev-parse', '--show-toplevel')
    if ($top.Code -eq 0 -and $top.Out) {
        $topPath = ([IO.Path]::GetFullPath(([string]$top.Out).Trim())).TrimEnd('\')
        if ($topPath -ieq $RepoRoot) {
            $head = Invoke-Git @('rev-parse', 'HEAD')
            if ($head.Code -eq 0 -and $head.Out) {
                $gitCommit = ([string]$head.Out).Trim()
                $br = Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')
                if ($br.Code -eq 0 -and $br.Out) { $gitBranch = ([string]$br.Out).Trim() }
                $st = Invoke-Git @('status', '--porcelain', '--', 'src')
                if ($st.Code -eq 0) {
                    $gitDirty = [bool](@($st.Out | Where-Object { $_ }).Count)
                    $gitNote = "commit $gitCommit, branch $gitBranch, src dirty=$gitDirty"
                } else {
                    $gitNote = "commit $gitCommit, branch $gitBranch, src status unavailable"
                }
            }
        }
    }
}
if ($gitDirty -and -not $AllowDirty) {
    throw "Uncommitted changes under src\ in '$RepoRoot' - the deployed server must trace to a committed tree. Commit first, or pass -AllowDirty."
}

# ---------------------------------------------------------------- running-process check

$serverProcs = @(Get-ForgeServerProcess)
$runningUnderDest = @($serverProcs | Where-Object {
    if (-not $_.Path) { return $false }
    try { $exe = [IO.Path]::GetFullPath($_.Path) } catch { return $false }
    $exe.StartsWith($Destination + '\', [StringComparison]::OrdinalIgnoreCase)
})
if ($runningUnderDest.Count) {
    $list = ($runningUnderDest | ForEach-Object { "pid $($_.Pid) ($($_.Path))" }) -join '; '
    throw "Forge.Server.exe is running from the destination: $list - close or disable the MCP clients that use this folder, then retry. This script never stops a process."
}

# ---------------------------------------------------------------- source

$srcDir = $null
$buildDir = $null
$buildCmd = $null
if ($SourceDir) {
    if (-not (Test-Path -LiteralPath $SourceDir -PathType Container)) {
        throw "-SourceDir not found: $SourceDir"
    }
    $srcDir = (Resolve-Path -LiteralPath $SourceDir).Path.TrimEnd('\')
    $srcDesc = "$srcDir (prebuilt, -SourceDir)"
} else {
    $csproj = Join-Path $RepoRoot 'src\Forge.Server\Forge.Server.csproj'
    if (-not (Test-Path -LiteralPath $csproj)) { throw "Project not found: $csproj" }
    $buildDir = Join-Path $RepoRoot "artifacts\deploy-server-build\$stampText"
    $buildCmd = "dotnet publish `"$csproj`" -c $Configuration --artifacts-path `"$buildDir`""
    $srcDesc = "publish via: $buildCmd"
}

# ---------------------------------------------------------------- dry-run plan

if ($DryRun) {
    Write-Output "=== Forge server deploy plan (DRY RUN - nothing changed) ==="
    Write-Output "Configuration : $Configuration"
    Write-Output "RepoRoot      : $RepoRoot"
    Write-Output "Git           : $gitNote"
    Write-Output ("Processes     : {0} Forge.Server.exe process(es){1}; none running from the destination" -f $serverProcs.Count, $(if ($ProcessFixture) { " (fixture $ProcessFixture)" } else { "" }))
    Write-Output "Destination   : $Destination"
    Write-Output "Staging       : $stagingDir"
    Write-Output "Source        : $srcDesc"
    if ($SourceDir) {
        $planFiles = @(Get-ChildItem -LiteralPath $srcDir -Recurse -File -Force)
        foreach ($f in $planFiles) {
            Write-Output ("[WOULD-COPY]  " + $f.FullName.Substring($srcDir.Length).TrimStart('\'))
        }
        Write-Output "              ($($planFiles.Count) file(s))"
    } else {
        Write-Output "[WOULD-BUILD] $buildCmd"
        Write-Output "[WOULD-COPY]  <publish output folder under $buildDir\publish\...>"
    }
    Write-Output "[WOULD-VERIFY] SHA256 every staged file against the source"
    Write-Output "[WOULD-WRITE] $stagingDir\DEPLOYED.json"
    if (Test-Path -LiteralPath $Destination) {
        Write-Output "[WOULD-RENAME] $Destination -> $prevDir   (an older .prev is removed first - exactly one previous copy is kept)"
    } else {
        Write-Output "              (no existing destination - first deploy, no .prev)"
    }
    Write-Output "[WOULD-RENAME] $stagingDir -> $Destination"
    Write-Output "MCP entry exe : $destExe"
    return
}

# ---------------------------------------------------------------- build

if (-not $srcDir) {
    Write-Output "Publishing Forge.Server ($Configuration)..."
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet publish $csproj -c $Configuration --artifacts-path $buildDir
        $publishCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $eap
    }
    if ($publishCode -ne 0) { throw "dotnet publish failed with exit code $publishCode" }

    # The artifacts layout puts publish output under <artifacts>\publish\...;
    # locate the folder actually holding Forge.Server.exe rather than guessing it.
    $publishRoot = Join-Path $buildDir 'publish'
    $exeFile = $null
    if (Test-Path -LiteralPath $publishRoot) {
        $exeFile = Get-ChildItem -LiteralPath $publishRoot -Recurse -Filter 'Forge.Server.exe' -File | Select-Object -First 1
    }
    if (-not $exeFile) { throw "dotnet publish produced no Forge.Server.exe under $publishRoot" }
    $srcDir = $exeFile.Directory.FullName
    Write-Output "Publish output: $srcDir"
}

$srcFiles = @(Get-ChildItem -LiteralPath $srcDir -Recurse -File -Force)
if (-not $srcFiles.Count) { throw "Source folder is empty: $srcDir" }

# ---------------------------------------------------------------- staging + verify + manifest

New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null
$manifest = New-Object 'System.Collections.Generic.List[object]'
try {
    Copy-Item -Path (Join-Path $srcDir '*') -Destination $stagingDir -Recurse -Force

    if ($CorruptStagedFile) {
        $victim = Join-Path $stagingDir ($CorruptStagedFile -replace '/', '\')
        if (-not (Test-Path -LiteralPath $victim -PathType Leaf)) {
            throw "-CorruptStagedFile '$CorruptStagedFile' names no file inside the staging folder"
        }
        $fs = [IO.File]::Open($victim, [IO.FileMode]::Append, [IO.FileAccess]::Write)
        try { $fs.WriteByte(0) } finally { $fs.Dispose() }
    }

    $mismatches = New-Object 'System.Collections.Generic.List[string]'
    foreach ($f in $srcFiles) {
        $rel = $f.FullName.Substring($srcDir.Length).TrimStart('\')
        $staged = Join-Path $stagingDir $rel
        if (-not (Test-Path -LiteralPath $staged -PathType Leaf)) {
            $mismatches.Add("$rel (missing from staging)")
            continue
        }
        $hSrc = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        $hOut = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash
        if ($hSrc -ne $hOut) { $mismatches.Add($rel); continue }
        $manifest.Add([ordered]@{ path = ($rel -replace '\\', '/'); sha256 = $hSrc; bytes = $f.Length })
    }
    if ($mismatches.Count) {
        throw ("SHA256 verification failed for {0} staged file(s): {1} - the destination was not touched" -f $mismatches.Count, ($mismatches -join ', '))
    }

    [ordered]@{
        deployedBy    = 'scripts/deploy-server.ps1'
        commit        = $gitCommit
        branch        = $gitBranch
        dirty         = $gitDirty
        configuration = $Configuration
        utc           = $stampUtc.ToString('o')
        source        = $srcDir
        files         = $manifest
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stagingDir 'DEPLOYED.json') -Encoding UTF8
} catch {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
    throw
}

# ---------------------------------------------------------------- swap

$hadDest = Test-Path -LiteralPath $Destination
try {
    if (Test-Path -LiteralPath $prevDir) { Remove-Item -LiteralPath $prevDir -Recurse -Force }
    if ($hadDest) { Rename-Item -LiteralPath $Destination -NewName (Split-Path -Leaf $prevDir) }
    Rename-Item -LiteralPath $stagingDir -NewName (Split-Path -Leaf $Destination)
} catch {
    $swapErr = $_
    if ($hadDest -and (Test-Path -LiteralPath $prevDir) -and -not (Test-Path -LiteralPath $Destination)) {
        Rename-Item -LiteralPath $prevDir -NewName (Split-Path -Leaf $Destination)
        if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
        throw "Could not move staging into place ($($swapErr.Exception.Message)); the previous copy was restored to $Destination."
    }
    if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
    throw "Could not move staging into place: $($swapErr.Exception.Message)"
}

# ---------------------------------------------------------------- result

Write-Output ""
Write-Output "=== Forge server deployed ==="
Write-Output "Destination   : $Destination"
Write-Output "Files         : $($manifest.Count + 1) ($($manifest.Count) published + DEPLOYED.json)"
Write-Output ("Commit        : " + $(if ($gitCommit) { "$gitCommit ($gitBranch)$(if ($gitDirty) { ', src dirty - deployed with -AllowDirty' })" } else { 'not recorded (git unavailable or not a repository)' }))
Write-Output ("Previous copy : " + $(if ($hadDest) { $prevDir } else { '<none>' }))
Write-Output "MCP entry exe : $destExe"
if (-not (Test-Path -LiteralPath $destExe)) {
    Write-Warning "No Forge.Server.exe in the deployed content - the MCP entry path above needs a real server build."
}
