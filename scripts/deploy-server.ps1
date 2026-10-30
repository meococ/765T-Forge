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
  - the -Destination is not safe: it equals <RepoRoot> or is an ancestor of
    it, it sits inside <RepoRoot>\src or <RepoRoot>\.git, or it is a drive
    root. The swap would rename that folder aside - refusing.
  - the -Destination exists, is not empty and has no DEPLOYED.json written by
    this script (it must parse and carry configuration/utc/files): it is not
    a folder this script deployed, so it is not replaced. Move it aside by
    hand, then retry.
  - the checkout is dirty: any tracked file modified or staged, any untracked
    file under <RepoRoot>\src\, or an untracked root build input
    (Directory.Build.props, Directory.Build.targets, Directory.Packages.props,
    global.json, NuGet.config, *.slnf, *.rsp). Other untracked files (docs\,
    examples\, ...) do
    not block. The deploy must trace to a committed tree; commit first or
    pass -AllowDirty. The dirty paths are recorded in DEPLOYED.json.
  - a Forge.Server.exe process is running from the destination - close or
    disable the MCP clients that use this folder, then retry. The script never
    stops or kills a process. Forge.Server.exe processes running from other
    folders (e.g. src\Forge.Server\bin\Debug) are not a reason to refuse.
  - a Forge.Server.exe process exists whose executable path cannot be read
    (other user, elevated): the script cannot verify it is not running from
    the destination, so it refuses and lists the pids. Close the process or
    run this script from a session that can read it.
  When git is unavailable or <RepoRoot> is not a git repository root (a plain
  folder, or a folder nested inside some other repo), the git fields in
  DEPLOYED.json are recorded as null and the deploy proceeds.

  Swap (a failed deploy never leaves a half-copied live folder):
    1. the source files are copied into a sibling staging folder
       "<Destination>.staging-<utc>-<id>",
    2. every staged file is verified by SHA256 against the source (any
       mismatch deletes only that staging folder and fails),
    3. DEPLOYED.json is written into staging (commit, branch, dirty flag,
       dirty paths, configuration, utc, source path, per-file sha256 and
       bytes),
    4. an existing destination is renamed to
       "<Destination>.prev-pending-<utc>-<id>". If this fails the deploy
       aborts with the real cause - the destination and any existing .prev
       backup are untouched,
    5. staging is renamed to the destination. If this fails, the pending
       folder is renamed back to the destination first,
    6. only now is the pending copy retired: an older "<Destination>.prev" is
       renamed to "<Destination>.prev-old-<utc>-<id>" and that folder deleted
       (a failed delete warns and leaves the folder, which later runs list),
       then the pending folder is renamed to ".prev" - exactly one previous
       copy is kept, and ".prev" is only ever absent or complete. If only
       this step fails the deploy still succeeded; a warning reports the
       pending folder that was kept.
  Only the staging folder this run created, "<Destination>.prev", and
  "<Destination>.prev-old-<utc>-<id>" are ever deleted; nothing else is
  touched. Leftover "<Destination>.staging-*", "<Destination>.prev-pending-*"
  and "<Destination>.prev-old-*" siblings from killed or failed runs are
  never deleted automatically - every run (including -DryRun) lists them in a
  warning so they can be removed by hand.

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
  Deploy even when git reports uncommitted changes in <RepoRoot>.

.PARAMETER ProcessFixture
  TEST HOOK: path to a JSON file replacing live process enumeration for the
  running-process check - an array of { "pid": 123, "path": "D:\\...\\Forge.Server.exe" }.
  Lets tests simulate a server already running from the destination.

.PARAMETER CorruptStagedFile
  TEST HOOK: name (path relative to the staged folder) of a file to corrupt
  inside the staging folder after the copy but before hash verification, to
  prove a bad copy aborts the deploy without touching the destination.

.PARAMETER FailSwapAt
  TEST HOOK: 'staging', 'prev' or 'final' - inject a failure into the matching
  swap step (the staging rename, the destination -> pending rename, or the
  pending -> .prev rotation) to prove the rollback behaviour.
#>
[CmdletBinding()]
param(
    [string] $Configuration = "Release",

    # "" resolves to the parent of this script's folder in the body. It cannot be a
    # parameter default: under `powershell -File` (5.1) neither $PSScriptRoot
    # nor $PSCommandPath/$MyInvocation.MyCommand.Path is set while defaults
    # are bound, so Split-Path threw a binding error and exited 0 - a silent
    # no-op. All three are populated in the body on both engines.
    [string] $RepoRoot = "",

    [string] $Destination = "",

    [string] $SourceDir,

    [switch] $DryRun,

    [switch] $AllowDirty,

    [string] $ProcessFixture,

    [string] $CorruptStagedFile,

    [string] $FailSwapAt
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
            # Entries with an unreadable path are kept so the caller can refuse
            # on them instead of silently skipping a possible server.
            [pscustomobject]@{ Pid = $_.Id; Path = [string]$p }
        })
    }
}

# $path equals $base or sits inside it (case-insensitive, normalized paths).
function Test-IsOrUnder([string] $Path, [string] $Base) {
    $Path -ieq $Base -or $Path.StartsWith($Base + '\', [StringComparison]::OrdinalIgnoreCase)
}

# Everything that can fail runs inside this try - the catch at the end of the
# script turns any terminating error into a non-zero process exit.
try {
    # `-RepoRoot` empty/omitted falls back to the script folder's parent.
    if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }
    # Validated in the body, not via ValidateSet: a parameter-binding failure
    # under `powershell -File` exits 0 without ever running the script.
    if ($Configuration -notin @('Debug', 'Release')) {
        throw "Configuration '$Configuration' must be Debug or Release."
    }
    if ($FailSwapAt -and $FailSwapAt -notin @('staging', 'prev', 'final')) {
        throw "FailSwapAt '$FailSwapAt' must be 'staging', 'prev' or 'final'."
    }

# ---------------------------------------------------------------- paths

if (-not (Test-Path -LiteralPath $RepoRoot -PathType Container)) {
    throw "RepoRoot not found: $RepoRoot"
}
$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path.TrimEnd('\')
if (-not $Destination) { $Destination = Join-Path $RepoRoot 'deploy\server' }
try {
    $destFull = [IO.Path]::GetFullPath($Destination)
} catch {
    throw "Destination is not a valid absolute path: '$Destination'"
}
if ($destFull -ieq [IO.Path]::GetPathRoot($destFull)) {
    throw "Destination '$destFull' is a drive root - refusing to deploy onto a whole drive."
}
$Destination = $destFull.TrimEnd('\')

# The swap renames whatever -Destination points at, so it must never be the
# checkout itself, an ancestor of it, or inside the folders a rename would gut.
if ((Test-IsOrUnder $RepoRoot $Destination) -or
    (Test-IsOrUnder $Destination "$RepoRoot\src") -or
    (Test-IsOrUnder $Destination "$RepoRoot\.git")) {
    throw "Destination '$Destination' overlaps the checkout '$RepoRoot' - it must not be the checkout root, an ancestor of it, or inside src\ or .git\. Pick a folder outside those paths."
}

# Takeover guard: an existing destination that is not ours is never replaced
# - the user moves it aside by hand. A folder counts as ours only when its
# DEPLOYED.json parses and carries the keys this script writes.
if (Test-Path -LiteralPath $Destination) {
    $destHasContent = [bool]@(Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1)
    if ($destHasContent) {
        $destManifest = Join-Path $Destination 'DEPLOYED.json'
        $isOurs = $false
        if (Test-Path -LiteralPath $destManifest -PathType Leaf) {
            try {
                $deployed = Get-Content -LiteralPath $destManifest -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                $isOurs = ($null -ne $deployed.PSObject.Properties['configuration'] -and
                           $null -ne $deployed.PSObject.Properties['utc'] -and
                           $null -ne $deployed.PSObject.Properties['files'])
            } catch { $isOurs = $false }
        }
        if (-not $isOurs) {
            throw "Destination '$Destination' exists and is not empty but has no DEPLOYED.json written by this script (it must parse and carry configuration/utc/files) - it does not look like a deploy made by this script. Refusing to replace it; move it aside by hand, then retry."
        }
    }
}
$prevDir = "$Destination.prev"
$destExe = Join-Path $Destination 'Forge.Server.exe'

$stampUtc   = (Get-Date).ToUniversalTime()
# Milliseconds plus a random suffix: unique even for two runs inside the same
# second, so the per-run build/staging/pending folders can never collide.
$stampText  = $stampUtc.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$stagingDir = "$Destination.staging-$stampText"
$pendingDir = "$Destination.prev-pending-$stampText"
$prevOldDir = "$Destination.prev-old-$stampText"

# Leftover siblings from killed or failed runs are never deleted
# automatically: list them on every run (including -DryRun) with their
# DEPLOYED.json identity, so they can be removed by hand. This run's own
# folders are excluded.
$destParentDir = Split-Path -Parent $Destination
$destLeafName  = Split-Path -Leaf $Destination
$leftovers = @()
if ($destParentDir -and (Test-Path -LiteralPath $destParentDir)) {
    $leftovers = @(Get-ChildItem -LiteralPath $destParentDir -Directory -Force | Where-Object {
        ($_.Name.StartsWith("$destLeafName.staging-", [StringComparison]::OrdinalIgnoreCase) -or
         $_.Name.StartsWith("$destLeafName.prev-pending-", [StringComparison]::OrdinalIgnoreCase) -or
         $_.Name.StartsWith("$destLeafName.prev-old-", [StringComparison]::OrdinalIgnoreCase)) -and
        $_.FullName -ne $stagingDir -and $_.FullName -ne $pendingDir -and $_.FullName -ne $prevOldDir
    })
}
if ($leftovers.Count) {
    $leftoverLines = foreach ($o in $leftovers) {
        $oNote = 'no DEPLOYED.json'
        $oManifest = Join-Path $o.FullName 'DEPLOYED.json'
        if (Test-Path -LiteralPath $oManifest -PathType Leaf) {
            try {
                $oJson = Get-Content -LiteralPath $oManifest -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
                # pwsh converts ISO date strings to DateTimeOffset at parse
                # time - print them back round-trip so both engines agree.
                $oUtc = $oJson.utc
                if ($oUtc -is [datetime] -or $oUtc -is [datetimeoffset]) { $oUtc = $oUtc.ToString('o') }
                $oNote = "commit $(if ($oJson.commit) { $oJson.commit } else { '<null>' }), utc $(if ($oUtc) { $oUtc } else { '<null>' })"
            } catch { $oNote = 'unreadable DEPLOYED.json' }
        }
        "  $($o.FullName)  [$oNote]"
    }
    Write-Warning ("Leftover deploy folder(s) next to '$Destination' - they are kept, remove by hand:`n" + ($leftoverLines -join "`n"))
}

# ---------------------------------------------------------------- git state

$gitCommit  = $null
$gitBranch  = $null
$gitDirty   = $null
$dirtyPaths = $null
$gitNote    = 'git not available or RepoRoot is not a repository root'
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
                # Dirty = any tracked change anywhere, or an untracked file
                # that feeds the deployed payload: under src\, or a root build
                # input (props/targets/global.json/NuGet.config/*.slnf/*.rsp).
                # Untracked files elsewhere (docs\, examples\, ...) do not
                # count.
                $st = Invoke-Git @('status', '--porcelain', '--untracked-files=no')
                $un = Invoke-Git @('ls-files', '--others', '--exclude-standard')
                if ($st.Code -eq 0 -and $un.Code -eq 0) {
                    $dirtyPaths = New-Object 'System.Collections.Generic.List[string]'
                    foreach ($line in @($st.Out)) {
                        if ([string]::IsNullOrWhiteSpace($line)) { continue }
                        $p = $line.Substring(3)
                        if ($p.StartsWith('"') -and $p.EndsWith('"')) { $p = $p.Substring(1, $p.Length - 2) }
                        $p = ($p -split ' -> ')[-1]
                        $dirtyPaths.Add($p)
                    }
                    $rootBuildInputs = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'global.json', 'NuGet.config')
                    foreach ($line in @($un.Out)) {
                        if ([string]::IsNullOrWhiteSpace($line)) { continue }
                        $p = [string]$line
                        if ($p.StartsWith('"') -and $p.EndsWith('"')) { $p = $p.Substring(1, $p.Length - 2) }
                        if ($p.StartsWith('src/', [StringComparison]::OrdinalIgnoreCase) -or
                            (($p.IndexOf('/') -lt 0) -and ($rootBuildInputs -contains $p -or $p -like '*.slnf' -or $p -like '*.rsp'))) {
                            $dirtyPaths.Add($p)
                        }
                    }
                    $gitDirty = $dirtyPaths.Count -gt 0
                    $gitNote = "commit $gitCommit, branch $gitBranch, dirty=$gitDirty ($($dirtyPaths.Count) dirty path(s))"
                } else {
                    $gitNote = "commit $gitCommit, branch $gitBranch, dirty status unavailable"
                }
            }
        }
    }
}
if ($gitDirty -and -not $AllowDirty) {
    throw "Uncommitted changes in '$RepoRoot' - the deployed server must trace to a committed tree. Commit first, or pass -AllowDirty. Dirty paths: $($dirtyPaths -join ', ')"
}

# ---------------------------------------------------------------- running-process check

$serverProcs = @(Get-ForgeServerProcess)
$unreadable = @($serverProcs | Where-Object { -not $_.Path })
if ($unreadable.Count) {
    $list = ($unreadable | ForEach-Object { "pid $($_.Pid)" }) -join '; '
    throw "Forge.Server.exe is running but its executable path cannot be read: $list - cannot verify that it does not run from the destination; close it or run this from a session that can read it."
}
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
        Write-Output "[WOULD-RENAME] $Destination -> $pendingDir"
        Write-Output "[WOULD-RENAME] $stagingDir -> $Destination"
        Write-Output "[WOULD-RENAME] $pendingDir -> $prevDir   (an older .prev is removed first - exactly one previous copy is kept)"
    } else {
        Write-Output "              (no existing destination - first deploy, no .prev)"
        Write-Output "[WOULD-RENAME] $stagingDir -> $Destination"
    }
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
        dirtyPaths    = $dirtyPaths
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
$swapStep = 'dest'
try {
    # (a) move the live destination aside first - if this fails nothing is
    # lost: the destination and any existing .prev backup are untouched.
    if ($hadDest) {
        if ($FailSwapAt -eq 'prev') { throw "simulated swap failure injected by -FailSwapAt prev" }
        Rename-Item -LiteralPath $Destination -NewName (Split-Path -Leaf $pendingDir)
    }
    # (b) move staging into place.
    $swapStep = 'staging'
    if ($FailSwapAt -eq 'staging') { throw "simulated swap failure injected by -FailSwapAt staging" }
    Rename-Item -LiteralPath $stagingDir -NewName (Split-Path -Leaf $Destination)
    # (c) only now retire the pending copy: the older .prev is renamed to
    # .prev-old-<stamp> first and that folder deleted, then pending -> .prev.
    # .prev is only ever absent or complete: a delete that fails mid-way can
    # never leave a half-gutted backup under the .prev name.
    $swapStep = 'final'
    if (Test-Path -LiteralPath $pendingDir) {
        if ($FailSwapAt -eq 'final') { throw "simulated swap failure injected by -FailSwapAt final" }
        if (Test-Path -LiteralPath $prevDir) {
            Rename-Item -LiteralPath $prevDir -NewName (Split-Path -Leaf $prevOldDir)
        }
        try {
            if (Test-Path -LiteralPath $prevOldDir) { Remove-Item -LiteralPath $prevOldDir -Recurse -Force -ErrorAction Stop }
        } catch {
            Write-Warning "Could not remove the old backup '$prevOldDir': $($_.Exception.Message) - it is left in place and will be listed as a leftover on the next run."
        }
        Rename-Item -LiteralPath $pendingDir -NewName (Split-Path -Leaf $prevDir)
    }
} catch {
    $swapErr = $_
    if (Test-Path -LiteralPath $stagingDir) { Remove-Item -LiteralPath $stagingDir -Recurse -Force -ErrorAction SilentlyContinue }
    if ($swapStep -eq 'dest') {
        throw "Could not move the existing destination '$Destination' aside to '$pendingDir': $($swapErr.Exception.Message) - the destination and any .prev backup were left untouched."
    }
    if ($swapStep -eq 'staging') {
        if ($hadDest -and (Test-Path -LiteralPath $pendingDir) -and -not (Test-Path -LiteralPath $Destination)) {
            Rename-Item -LiteralPath $pendingDir -NewName (Split-Path -Leaf $Destination)
            throw "Could not move staging into place ($($swapErr.Exception.Message)); the previous copy was restored to $Destination."
        }
        throw "Could not move staging into place: $($swapErr.Exception.Message)"
    }
    Write-Warning "Deployed to '$Destination', but the previous copy could not be rotated into '$prevDir': $($swapErr.Exception.Message). It was left at '$pendingDir'."
}

# ---------------------------------------------------------------- result

Write-Output ""
Write-Output "=== Forge server deployed ==="
Write-Output "Destination   : $Destination"
Write-Output "Files         : $($manifest.Count + 1) ($($manifest.Count) published + DEPLOYED.json)"
Write-Output ("Commit        : " + $(if ($gitCommit) { "$gitCommit ($gitBranch)$(if ($gitDirty) { ', dirty - deployed with -AllowDirty' })" } else { 'not recorded (git unavailable or not a repository)' }))
Write-Output ("Previous copy : " + $(if ($hadDest) { $prevDir } else { '<none>' }))
Write-Output "MCP entry exe : $destExe"
if (-not (Test-Path -LiteralPath $destExe)) {
    Write-Warning "No Forge.Server.exe in the deployed content - the MCP entry path above needs a real server build."
}

} catch {
    # A failure must never surface as exit 0. At the top of the process
    # (`powershell -File` / `pwsh -File`) print the error and exit hard;
    # invoked in-process (&, dot-source, a calling script) re-throw so the
    # caller catches the original error.
    if ($MyInvocation.InvocationName -eq '&' -or
        $MyInvocation.InvocationName -eq '.' -or
        (Get-PSCallStack).Count -gt 1) {
        throw
    }
    Write-Error -ErrorRecord $_ -ErrorAction Continue
    exit 1
}
