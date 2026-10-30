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
  - the -Destination exists, is not empty and has no DEPLOYED.json: it is not
    a folder this script deployed, so it is not replaced. Move it aside by
    hand, then retry.
  - the checkout is dirty: any tracked file modified or staged, any untracked
    file under <RepoRoot>\src\, or an untracked root build input
    (Directory.Build.props, Directory.Packages.props, global.json,
    NuGet.config, *.slnf). Other untracked files (docs\, examples\, ...) do
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
    6. only now is an older "<Destination>.prev" deleted and the pending
       folder renamed to ".prev" - exactly one previous copy is kept. If only
       this step fails the deploy still succeeded; a warning reports the
       pending folder that was kept.
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
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot),

    [string] $Destination = "",

    [string] $SourceDir,

    [switch] $DryRun,

    [switch] $AllowDirty,

    [string] $ProcessFixture,

    [string] $CorruptStagedFile,

    [ValidateSet('staging', 'prev', 'final')]
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

# Takeover guard: an existing destination that is not ours (no DEPLOYED.json)
# is never replaced - the user moves it aside by hand.
if (Test-Path -LiteralPath $Destination) {
    $destHasContent = [bool]@(Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1)
    if ($destHasContent -and -not (Test-Path -LiteralPath (Join-Path $Destination 'DEPLOYED.json'))) {
        throw "Destination '$Destination' exists and is not empty but has no DEPLOYED.json - it does not look like a deploy made by this script. Refusing to replace it; move it aside by hand, then retry."
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
                # input (props/global.json/NuGet.config/*.slnf). Untracked
                # files elsewhere (docs\, examples\, ...) do not count.
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
                    $rootBuildInputs = @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'NuGet.config')
                    foreach ($line in @($un.Out)) {
                        if ([string]::IsNullOrWhiteSpace($line)) { continue }
                        $p = [string]$line
                        if ($p.StartsWith('"') -and $p.EndsWith('"')) { $p = $p.Substring(1, $p.Length - 2) }
                        if ($p.StartsWith('src/', [StringComparison]::OrdinalIgnoreCase) -or
                            (($p.IndexOf('/') -lt 0) -and ($rootBuildInputs -contains $p -or $p -like '*.slnf'))) {
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
    # (c) only now retire the pending copy: drop the older .prev, then rename.
    $swapStep = 'final'
    if (Test-Path -LiteralPath $pendingDir) {
        if ($FailSwapAt -eq 'final') { throw "simulated swap failure injected by -FailSwapAt final" }
        if (Test-Path -LiteralPath $prevDir) { Remove-Item -LiteralPath $prevDir -Recurse -Force }
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
