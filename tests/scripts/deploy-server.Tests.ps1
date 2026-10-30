<#
.SYNOPSIS
  Standalone test runner for deploy-server.ps1.

.DESCRIPTION
  No Pester dependency (this repo's machines may only have Pester 3.x).
  Run:  powershell -NoProfile -File tests\scripts\deploy-server.Tests.ps1

  Every deploy target lives under a temp root - the tests never touch the real
  deploy\ folder, a real checkout's git state, or a live Forge.Server process
  (the running-process check is exercised through the documented
  -ProcessFixture hook). Exit code 0 = all passed, 1 = failures.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$deploy   = Join-Path $repoRoot 'scripts\deploy-server.ps1'

# ------------------------------------------------------------------ harness

$script:passCount = 0
$script:failCount = 0
$script:failures  = @()

function Test-Case([string] $name, [scriptblock] $body) {
    try {
        & $body
        $script:passCount++
        Write-Host "[PASS] $name" -ForegroundColor Green
    } catch {
        $script:failCount++
        $script:failures += "$name :: $($_.Exception.Message)"
        Write-Host "[FAIL] $name :: $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-True($cond, [string] $msg) { if (-not $cond) { throw "assert failed: $msg" } }
function Assert-Equal($actual, $expected, [string] $msg) {
    if ($actual -ne $expected) { throw "assert failed: $msg (expected '$expected', got '$actual')" }
}
function Assert-Match([string] $s, [string] $re, [string] $msg) {
    if ($s -notmatch $re) { throw "assert failed: $msg -- text does not match /$re/`n--- text ---`n$s`n---" }
}
function Assert-Throws([scriptblock] $body, [string] $match, [string] $msg) {
    $err = $null
    try { & $body } catch { $err = $_ }
    if (-not $err) { throw "assert failed: $msg -- expected an error matching /$match/, none was thrown" }
    if ($match -and ($err.Exception.Message -notmatch $match)) {
        throw "assert failed: $msg -- error '$($err.Exception.Message)' does not match /$match/"
    }
}

# ------------------------------------------------------------------ helpers

# Creates a fake publish/source folder; $files maps relative paths to content.
function New-FakeSource([string] $name, [hashtable] $files) {
    $dir = Join-Path $testRoot ("src-$name")
    foreach ($rel in $files.Keys) {
        $p = Join-Path $dir ($rel -replace '/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $p) | Out-Null
        [IO.File]::WriteAllText($p, [string]$files[$rel])
    }
    return $dir
}

# rel-path -> sha256 for every file under $dir ({} when absent).
function Get-TreeHashes([string] $dir) {
    $map = @{}
    if (-not (Test-Path -LiteralPath $dir)) { return $map }
    $root = $dir.TrimEnd('\')
    foreach ($f in (Get-ChildItem -LiteralPath $dir -Recurse -File -Force)) {
        $rel = $f.FullName.Substring($root.Length).TrimStart('\')
        $map[$rel] = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
    }
    return $map
}

function Assert-TreeEqual($expected, $actual, [string] $msg) {
    Assert-Equal $actual.Count $expected.Count "$msg - file count differs"
    foreach ($k in $expected.Keys) {
        Assert-True $actual.ContainsKey($k) "$msg - '$k' missing"
        Assert-Equal $actual[$k] $expected[$k] "$msg - '$k' content differs"
    }
}

# Sibling folders next to $dest matching "<leaf>.staging-*" or "<leaf>.prev".
function Get-DeploySiblings([string] $dest, [string] $pattern) {
    $parent = Split-Path -Parent $dest
    $leaf   = Split-Path -Leaf $dest
    if (-not (Test-Path -LiteralPath $parent)) { return @() }
    return @(Get-ChildItem -LiteralPath $parent -Directory -Filter "$leaf$pattern")
}

# Native stderr arrives as ErrorRecords; run git with EAP relaxed.
function Invoke-TestGit([string] $repo, [string[]] $gargs) {
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git -C $repo @gargs 2>&1
        return @{ Out = ($out | Out-String); Code = $LASTEXITCODE }
    } finally { $ErrorActionPreference = $eap }
}

# ------------------------------------------------------------------ sandbox

$testRoot = Join-Path $env:TEMP ('forge-deploy-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

$nonRepo = Join-Path $testRoot 'notarepo'
New-Item -ItemType Directory -Force -Path $nonRepo | Out-Null

# A process fixture with no Forge.Server.exe: keeps child -File runs
# deterministic even when a real server process is running on this machine.
$fxEmpty = Join-Path $testRoot 'procs-empty.json'
'[]' | Set-Content -LiteralPath $fxEmpty -Encoding UTF8

$srcA = New-FakeSource 'a' ([ordered]@{
    'Forge.Server.exe'  = 'fake server exe v1'
    'Forge.Server.dll'  = 'fake dll v1'
    'docs\capability-matrix.md' = 'fake doc v1'
})
$srcB = New-FakeSource 'b' ([ordered]@{
    'Forge.Server.exe'  = 'fake server exe v2'
    'Forge.Server.dll'  = 'fake dll v2'
    'docs\capability-matrix.md' = 'fake doc v2'
    'extra\plugin.json' = '{}'
})
$srcC = New-FakeSource 'c' ([ordered]@{
    'Forge.Server.exe'  = 'fake server exe v3'
    'Forge.Server.dll'  = 'fake dll v3'
    'docs\capability-matrix.md' = 'fake doc v3'
    'extra\plugin.json' = '{}'
    'newfile.txt'       = 'new in v3'
})

try {

# ------------------------------------------------------------------ tests

Test-Case 'dry run prints the plan and writes nothing' {
    $dest = Join-Path $testRoot 'dry1\server'
    $destParent = Split-Path -Parent $dest
    $before = Get-TreeHashes $srcA
    $out = (& $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -DryRun) | Out-String
    Assert-Match $out 'DRY RUN - nothing changed' 'the plan must be marked as dry-run'
    Assert-Match $out '\[WOULD-COPY\]' 'the plan must list file copies'
    Assert-Match $out '\[WOULD-WRITE\].*DEPLOYED\.json' 'the plan must show the manifest write'
    Assert-Match $out ([regex]::Escape($dest)) 'the plan must name the destination'
    Assert-True (-not (Test-Path -LiteralPath $destParent)) 'the destination parent must not be created'
    Assert-Equal (Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
    Assert-TreeEqual $before (Get-TreeHashes $srcA) 'the source folder must be untouched'
}

Test-Case 'fresh deploy copies all files and writes DEPLOYED.json' {
    $dest = Join-Path $testRoot 'fresh\server'
    $out = (& $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo) | Out-String
    Assert-Match $out '=== Forge server deployed ===' 'a success banner must print'
    Assert-Match $out ([regex]::Escape((Join-Path $dest 'Forge.Server.exe'))) 'the MCP entry path must print'

    $expected = Get-TreeHashes $srcA
    $actual   = Get-TreeHashes $dest
    Assert-Equal $actual.Count ($expected.Count + 1) 'destination must hold the source files + DEPLOYED.json'
    foreach ($k in $expected.Keys) {
        Assert-Equal $actual[$k] $expected[$k] "deployed file '$k' must be byte-identical to the source"
    }

    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-Equal $dep.configuration 'Release' 'DEPLOYED.json must record the configuration'
    Assert-Equal $dep.source $srcA 'DEPLOYED.json must record the source path'
    Assert-True ($dep.utc -and ([string]$dep.utc).Length -gt 10) 'DEPLOYED.json must record the utc stamp'
    Assert-Equal @($dep.files).Count $expected.Count 'DEPLOYED.json must list every deployed file'
    foreach ($f in @($dep.files)) {
        $rel = $f.path -replace '/', '\'
        Assert-Equal $f.sha256 $expected[$rel] "manifest sha256 for '$($f.path)' must match the file"
        $bytes = (Get-Item -LiteralPath (Join-Path $dest $rel)).Length
        Assert-Equal $f.bytes $bytes "manifest bytes for '$($f.path)' must match the file"
    }
    Assert-Equal (Get-DeploySiblings $dest '.prev').Count 0 'a first deploy must leave no .prev'
    Assert-Equal (Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
}

Test-Case 'redeploy keeps exactly one .prev holding the previous content' {
    $dest = Join-Path $testRoot 'swap\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    $d1 = Get-TreeHashes $dest   # .prev holds the previous DESTINATION content (incl. DEPLOYED.json)
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $d2 = Get-TreeHashes $dest

    $prev = @(Get-DeploySiblings $dest '.prev')
    Assert-Equal $prev.Count 1 'exactly one .prev sibling must exist'
    $prevDir = Join-Path (Split-Path -Parent $dest) ((Split-Path -Leaf $dest) + '.prev')
    Assert-TreeEqual $d1 (Get-TreeHashes $prevDir) '.prev must hold the first destination content'

    & $deploy -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo | Out-Null
    Assert-Equal @(Get-DeploySiblings $dest '.prev').Count 1 'still exactly one .prev after the third deploy'
    Assert-TreeEqual $d2 (Get-TreeHashes $prevDir) '.prev must now hold the second destination content'
    $v3 = Get-TreeHashes $srcC
    $destHash = Get-TreeHashes $dest
    foreach ($k in $v3.Keys) {
        Assert-Equal $destHash[$k] $v3[$k] "destination must hold the v3 file '$k'"
    }
    Assert-True ($d1['Forge.Server.exe'] -ne $d2['Forge.Server.exe'] -and $destHash['Forge.Server.exe'] -ne $d1['Forge.Server.exe']) 'the first-deployed exe must be gone from destination and .prev'
    Assert-Equal (Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
}

Test-Case 'a Forge.Server.exe running from the destination is refused' {
    $dest = Join-Path $testRoot 'busy\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    $before = Get-TreeHashes $dest

    $fx = Join-Path $testRoot 'procs-busy.json'
    @(
        [pscustomobject]@{ pid = 424242; path = 'D:\765T-Forge\src\Forge.Server\bin\Debug\net8.0\Forge.Server.exe' },
        [pscustomobject]@{ pid = 424243; path = (Join-Path $dest 'Forge.Server.exe') }
    ) | ConvertTo-Json | Set-Content -LiteralPath $fx -Encoding UTF8
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fx | Out-Null
    } 'close or disable the MCP clients that use this folder, then retry' 'a server running from the destination must refuse'
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fx | Out-Null
    } '424243' 'the refusal must list the offending pid'
    Assert-TreeEqual $before (Get-TreeHashes $dest) 'the refused deploy must leave the destination untouched'
    Assert-Equal (Get-DeploySiblings $dest '.staging-*').Count 0 'a refused deploy must leave no staging folder'

    # A Forge.Server.exe running from any OTHER folder is not a reason to refuse.
    @(
        [pscustomobject]@{ pid = 424242; path = 'D:\765T-Forge\src\Forge.Server\bin\Debug\net8.0\Forge.Server.exe' }
    ) | ConvertTo-Json | Set-Content -LiteralPath $fx -Encoding UTF8
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fx | Out-Null
    $destHashes = Get-TreeHashes $dest
    foreach ($k in (Get-TreeHashes $srcB).Keys) {
        Assert-Equal $destHashes[$k] (Get-TreeHashes $srcB)[$k] "the deploy must proceed past a server running elsewhere: '$k'"
    }
}

Test-Case 'a staged hash mismatch aborts and leaves destination + .prev byte-identical' {
    $dest = Join-Path $testRoot 'corrupt\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $prevDir = Join-Path (Split-Path -Parent $dest) ((Split-Path -Leaf $dest) + '.prev')
    $destBefore = Get-TreeHashes $dest
    $prevBefore = Get-TreeHashes $prevDir

    Assert-Throws {
        & $deploy -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo -CorruptStagedFile 'Forge.Server.exe' | Out-Null
    } 'SHA256 verification failed' 'a corrupted staged file must abort the deploy'

    Assert-TreeEqual $destBefore (Get-TreeHashes $dest) 'the destination must be byte-identical after the abort'
    Assert-TreeEqual $prevBefore (Get-TreeHashes $prevDir) 'the .prev copy must be byte-identical after the abort'
    Assert-Equal @(Get-DeploySiblings $dest '.prev').Count 1 'exactly one .prev must remain'
    Assert-Equal (Get-DeploySiblings $dest '.staging-*').Count 0 'the failed staging folder must be deleted'
}

Test-Case 'uncommitted src changes are refused; -AllowDirty deploys and records git fields' {
    $repo = Join-Path $testRoot 'repo'
    New-Item -ItemType Directory -Force -Path (Join-Path $repo 'src') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'src\main.cs'), 'v1')
    $r = Invoke-TestGit $repo @('init', '-q');            Assert-Equal $r.Code 0 "git init failed: $($r.Out)"
    $r = Invoke-TestGit $repo @('-c', 'core.autocrlf=false', 'add', '.'); Assert-Equal $r.Code 0 "git add failed: $($r.Out)"
    $r = Invoke-TestGit $repo @('-c', 'user.name=Deploy Tests', '-c', 'user.email=deploy-tests@local', 'commit', '-qm', 'init')
    Assert-Equal $r.Code 0 "git commit failed: $($r.Out)"
    $sha    = (Invoke-TestGit $repo @('rev-parse', 'HEAD')).Out.Trim()
    $branch = (Invoke-TestGit $repo @('rev-parse', '--abbrev-ref', 'HEAD')).Out.Trim()
    Assert-True ($sha.Length -eq 40) 'setup: the temp repo must have a HEAD commit'

    $dest = Join-Path $testRoot 'gitdep\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $repo | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-Equal $dep.commit $sha 'DEPLOYED.json must record the HEAD commit'
    Assert-Equal $dep.branch $branch 'DEPLOYED.json must record the branch'
    Assert-True ($dep.dirty -eq $false) 'a clean repo must record dirty=false'

    [IO.File]::WriteAllText((Join-Path $repo 'src\main.cs'), 'v2 - uncommitted')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'Uncommitted changes' 'a dirty tree must refuse without -AllowDirty'

    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo -AllowDirty | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-True ($dep.dirty -eq $true) 'DEPLOYED.json must record dirty=true for an -AllowDirty deploy'
    Assert-Equal $dep.commit $sha 'the commit must still be recorded'

    # A RepoRoot nested inside a repo is not that repo's tree: git fields must be null.
    $nested = Join-Path $repo 'inner\checkout'
    New-Item -ItemType Directory -Force -Path $nested | Out-Null
    $nestedDest = Join-Path $testRoot 'nesteddep\server'
    & $deploy -SourceDir $srcA -Destination $nestedDest -RepoRoot $nested | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $nestedDest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-True ($null -eq $dep.commit -and $null -eq $dep.dirty) 'a RepoRoot nested inside a repo must record null git fields'
}

Test-Case 'non-repo RepoRoot records null git fields and proceeds' {
    $dest = Join-Path $testRoot 'norepo\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-True ($null -eq $dep.commit) 'commit must be null for a non-repo RepoRoot'
    Assert-True ($null -eq $dep.branch) 'branch must be null for a non-repo RepoRoot'
    Assert-True ($null -eq $dep.dirty)  'dirty must be null for a non-repo RepoRoot'
}

Test-Case 'exit codes: success exits 0 and a refusal exits non-zero' {
    $dest = Join-Path $testRoot 'exitcodes\server'
    $fx = Join-Path $testRoot 'procs-exit.json'
    @([pscustomobject]@{ pid = 424244; path = (Join-Path $dest 'Forge.Server.exe') }) |
        ConvertTo-Json | Set-Content -LiteralPath $fx -Encoding UTF8
    # The child's stderr arrives as ErrorRecords, so Continue not Stop here.
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $okOut = (& powershell -NoProfile -ExecutionPolicy Bypass -File $deploy `
                 -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo 2>&1) | Out-String
        $okCode = $LASTEXITCODE
        $refOut = (& powershell -NoProfile -ExecutionPolicy Bypass -File $deploy `
                  -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fx 2>&1) | Out-String
        $refCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $eap }
    Assert-Equal $okCode 0 "a successful deploy must exit 0`n$okOut"
    Assert-True ($refCode -ne 0) "a refusal must exit non-zero`n$refOut"
    # The console wraps long error-record lines; normalize whitespace before matching.
    Assert-Match ($refOut -replace '\s+', ' ') 'close or disable the MCP clients that use this folder' 'the refusal message must reach the console'
}

Test-Case 'bad inputs are refused before anything is written' {
    $dest = Join-Path $testRoot 'badinput\server'
    Assert-Throws {
        & $deploy -SourceDir (Join-Path $testRoot 'missing-src') -Destination $dest -RepoRoot $nonRepo | Out-Null
    } '-SourceDir not found' 'a missing -SourceDir must refuse'
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination $dest -RepoRoot (Join-Path $testRoot 'missing-root') | Out-Null
    } 'RepoRoot not found' 'a missing -RepoRoot must refuse'
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -ProcessFixture (Join-Path $testRoot 'missing.json') | Out-Null
    } '-ProcessFixture not found' 'a missing fixture file must refuse'
    Assert-True (-not (Test-Path -LiteralPath (Split-Path -Parent $dest))) 'no deploy folders may be created by refused runs'
}

Test-Case 'destination overlapping the checkout is refused (P1, P12)' {
    $fakeRepo = Join-Path $testRoot 'p1\reporoot'
    New-Item -ItemType Directory -Force -Path (Join-Path $fakeRepo 'src\Forge.Server') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $fakeRepo '.git') | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeRepo 'precious.txt'), 'keep me')
    $before = Get-TreeHashes $fakeRepo

    # P1: -Destination IS the checkout root.
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination $fakeRepo -RepoRoot $fakeRepo | Out-Null
    } 'overlaps the checkout' 'P1: destination = RepoRoot must refuse'

    # P12: -Destination is an ancestor of the checkout root.
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination (Split-Path -Parent $fakeRepo) -RepoRoot $fakeRepo | Out-Null
    } 'overlaps the checkout' 'P12: destination = ancestor of RepoRoot must refuse'

    # Inside <RepoRoot>\src or <RepoRoot>\.git.
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination (Join-Path $fakeRepo 'src\server') -RepoRoot $fakeRepo | Out-Null
    } 'overlaps the checkout' 'a destination inside src\ must refuse'
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination (Join-Path $fakeRepo '.git\server') -RepoRoot $fakeRepo | Out-Null
    } 'overlaps the checkout' 'a destination inside .git\ must refuse'

    Assert-TreeEqual $before (Get-TreeHashes $fakeRepo) 'the fake checkout must be byte-identical after the refused runs'
    Assert-Equal @(Get-DeploySiblings $fakeRepo '.prev*').Count 0 'no .prev sibling of the repo may exist'
    Assert-Equal @(Get-DeploySiblings $fakeRepo '.staging-*').Count 0 'no staging sibling of the repo may exist'
}

Test-Case 'drive-root destination is refused (P11)' {
    # A fake root string: the guard is pure path logic, nothing is touched.
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination 'X:\' -RepoRoot $nonRepo | Out-Null
    } 'drive root' 'a drive-root destination must refuse with a clear message'
}

Test-Case 'a non-empty destination without DEPLOYED.json is not taken over' {
    $dest = Join-Path $testRoot 'foreign\server'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    [IO.File]::WriteAllText((Join-Path $dest 'someone-elses.txt'), 'precious')
    $before = Get-TreeHashes $dest
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    } 'move it aside' 'a foreign folder must be refused, not renamed aside'
    Assert-TreeEqual $before (Get-TreeHashes $dest) 'the foreign folder must be untouched'

    # An existing EMPTY folder is still deployable.
    $emptyDest = Join-Path $testRoot 'emptydest\server'
    New-Item -ItemType Directory -Force -Path $emptyDest | Out-Null
    & $deploy -SourceDir $srcA -Destination $emptyDest -RepoRoot $nonRepo | Out-Null
    Assert-True (Test-Path -LiteralPath (Join-Path $emptyDest 'Forge.Server.exe')) 'an existing empty folder may be deployed into'
}

Test-Case 'dirty check: tracked changes anywhere, untracked under src or root build inputs' {
    $repo = Join-Path $testRoot 'repo2'
    New-Item -ItemType Directory -Force -Path (Join-Path $repo 'src') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $repo 'docs') | Out-Null
    [IO.File]::WriteAllText((Join-Path $repo 'src\a.cs'), 'a')
    [IO.File]::WriteAllText((Join-Path $repo 'docs\cap.md'), 'cap')
    [IO.File]::WriteAllText((Join-Path $repo 'Directory.Build.props'), '<Project/>')
    $r = Invoke-TestGit $repo @('init', '-q');            Assert-Equal $r.Code 0 "git init failed: $($r.Out)"
    $r = Invoke-TestGit $repo @('-c', 'core.autocrlf=false', 'add', '.'); Assert-Equal $r.Code 0 "git add failed: $($r.Out)"
    $r = Invoke-TestGit $repo @('-c', 'user.name=Deploy Tests', '-c', 'user.email=deploy-tests@local', 'commit', '-qm', 'init')
    Assert-Equal $r.Code 0 "git commit failed: $($r.Out)"

    $dest = Join-Path $testRoot 'dirtydep\server'

    # Untracked files outside the deploy payload must NOT make it dirty.
    [IO.File]::WriteAllText((Join-Path $repo 'docs\wp3-note.md'), 'wip')
    [IO.File]::WriteAllText((Join-Path $repo 'scratch.txt'), 'wip')
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $repo | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-True ($dep.dirty -eq $false) 'untracked docs\ and root scratch files must not count as dirty'
    Assert-True (@($dep.dirtyPaths).Count -eq 0) 'a clean deploy must record an empty dirtyPaths list'

    # Untracked file under src\ -> dirty.
    [IO.File]::WriteAllText((Join-Path $repo 'src\new.cs'), 'new')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'src/new\.cs' 'an untracked file under src\ must refuse and be listed'
    Remove-Item -LiteralPath (Join-Path $repo 'src\new.cs')

    # Untracked root build inputs -> dirty.
    [IO.File]::WriteAllText((Join-Path $repo 'Directory.Packages.props'), '<Project/>')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'Directory\.Packages\.props' 'an untracked Directory.Packages.props must refuse and be listed'
    Remove-Item -LiteralPath (Join-Path $repo 'Directory.Packages.props')
    [IO.File]::WriteAllText((Join-Path $repo 'extra.slnf'), '{}')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'extra\.slnf' 'an untracked *.slnf must refuse and be listed'
    Remove-Item -LiteralPath (Join-Path $repo 'extra.slnf')
    [IO.File]::WriteAllText((Join-Path $repo 'Directory.Build.targets'), '<Project/>')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'Directory\.Build\.targets' 'an untracked Directory.Build.targets must refuse and be listed'
    Remove-Item -LiteralPath (Join-Path $repo 'Directory.Build.targets')
    [IO.File]::WriteAllText((Join-Path $repo 'extra.rsp'), '/nowarn:1701')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'extra\.rsp' 'an untracked root *.rsp must refuse and be listed'
    Remove-Item -LiteralPath (Join-Path $repo 'extra.rsp')
    # A build input nested below the repo root does not count.
    [IO.File]::WriteAllText((Join-Path $repo 'docs\note.rsp'), 'x')
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-True ($dep.dirty -eq $false) 'an untracked .rsp under docs\ must not count as dirty'

    # Modified tracked file outside src\ -> dirty.
    [IO.File]::WriteAllText((Join-Path $repo 'docs\cap.md'), 'cap v2')
    Assert-Throws {
        & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo | Out-Null
    } 'Uncommitted changes' 'a modified tracked file outside src\ must refuse'

    # -AllowDirty deploys and records the dirty paths (only the dirty ones).
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $repo -AllowDirty | Out-Null
    $dep = Get-Content -LiteralPath (Join-Path $dest 'DEPLOYED.json') -Raw | ConvertFrom-Json
    Assert-True ($dep.dirty -eq $true) 'dirty must be recorded as true'
    Assert-True (@($dep.dirtyPaths) -contains 'docs/cap.md') 'dirtyPaths must list the modified tracked file'
    Assert-True (@($dep.dirtyPaths) -notcontains 'docs/wp3-note.md') 'untracked files outside the payload must not be listed'
}

Test-Case 'a Forge.Server.exe with an unreadable path is refused' {
    $fx = Join-Path $testRoot 'procs-unreadable.json'
    @(
        [pscustomobject]@{ pid = 424301; path = 'D:\other\place\Forge.Server.exe' },
        [pscustomobject]@{ pid = 424302; path = $null }
    ) | ConvertTo-Json | Set-Content -LiteralPath $fx -Encoding UTF8
    $dest = Join-Path $testRoot 'unread\server'
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fx | Out-Null
    } 'cannot verify that it does not run from the destination' 'a server whose path cannot be read must refuse'
    Assert-Throws {
        & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fx | Out-Null
    } '424302' 'the refusal must list the unreadable pid'
    Assert-True (-not (Test-Path -LiteralPath $dest)) 'nothing may be deployed'
}

Test-Case 'a locked file fails the destination rename; destination and .prev survive (P6d)' {
    $dest = Join-Path $testRoot 'locked\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $prevDir = "$dest.prev"
    $destBefore = Get-TreeHashes $dest
    $prevBefore = Get-TreeHashes $prevDir

    $fs = [IO.File]::Open((Join-Path $dest 'Forge.Server.dll'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = (& powershell -NoProfile -ExecutionPolicy Bypass -File $deploy `
               -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo 2>&1) | Out-String
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $eap
        $fs.Dispose()
    }
    Assert-True ($code -ne 0) "the deploy must fail while a destination file is open`n$out"
    Assert-Match ($out -replace '\s+', ' ') 'move the existing destination .* aside' 'the error must name the failed step'
    Assert-TreeEqual $destBefore (Get-TreeHashes $dest) 'the destination must be byte-identical'
    Assert-TreeEqual $prevBefore (Get-TreeHashes $prevDir) 'the .prev backup must NOT be lost'
    Assert-Equal @(Get-DeploySiblings $dest '.prev-pending-*').Count 0 'no pending folder may remain'
    Assert-Equal @(Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
}

Test-Case '-FailSwapAt staging restores the previous destination' {
    $dest = Join-Path $testRoot 'failstaging\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $prevDir = "$dest.prev"
    $destBefore = Get-TreeHashes $dest
    $prevBefore = Get-TreeHashes $prevDir

    Assert-Throws {
        & $deploy -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo -FailSwapAt staging | Out-Null
    } 'previous copy was restored' 'a failed staging rename must restore the destination'

    Assert-TreeEqual $destBefore (Get-TreeHashes $dest) 'the destination must hold the pre-deploy content'
    Assert-TreeEqual $prevBefore (Get-TreeHashes $prevDir) '.prev must be byte-identical'
    Assert-Equal @(Get-DeploySiblings $dest '.prev-pending-*').Count 0 'no pending folder may remain'
    Assert-Equal @(Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
}

Test-Case '-FailSwapAt prev leaves destination and .prev untouched' {
    $dest = Join-Path $testRoot 'failprev\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $prevDir = "$dest.prev"
    $destBefore = Get-TreeHashes $dest
    $prevBefore = Get-TreeHashes $prevDir

    Assert-Throws {
        & $deploy -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo -FailSwapAt prev | Out-Null
    } 'move the existing destination .* aside' 'a failed destination rename must abort naming the step'

    Assert-TreeEqual $destBefore (Get-TreeHashes $dest) 'the destination must be byte-identical'
    Assert-TreeEqual $prevBefore (Get-TreeHashes $prevDir) '.prev must be byte-identical'
    Assert-Equal @(Get-DeploySiblings $dest '.prev-pending-*').Count 0 'no pending folder may remain'
    Assert-Equal @(Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
}

Test-Case '-FailSwapAt final still deploys but keeps the pending folder' {
    $dest = Join-Path $testRoot 'failfinal\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $prevDir = "$dest.prev"
    $destBefore = Get-TreeHashes $dest
    $prevBefore = Get-TreeHashes $prevDir

    $out = (& $deploy -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo -FailSwapAt final 3>&1) | Out-String
    Assert-Match $out '=== Forge server deployed ===' 'the deploy must still succeed when only the .prev rotation fails'
    Assert-Match $out 'prev-pending' 'the warning must name the kept pending folder'

    $v3 = Get-TreeHashes $srcC
    $destHash = Get-TreeHashes $dest
    foreach ($k in $v3.Keys) {
        Assert-Equal $destHash[$k] $v3[$k] "destination must hold the v3 file '$k'"
    }
    Assert-TreeEqual $prevBefore (Get-TreeHashes $prevDir) 'the old .prev must be byte-identical'
    $pending = @(Get-DeploySiblings $dest '.prev-pending-*')
    Assert-Equal $pending.Count 1 'exactly one pending folder must be kept'
    Assert-TreeEqual $destBefore (Get-TreeHashes $pending[0].FullName) 'the pending folder must hold the pre-deploy destination content'
    Assert-Equal @(Get-DeploySiblings $dest '.staging-*').Count 0 'no staging folder may remain'
}

Test-Case 'each run gets a unique build/staging stamp' {
    $d1 = Join-Path $testRoot 'stamp1\server'
    $d2 = Join-Path $testRoot 'stamp2\server'
    $o1 = (& $deploy -SourceDir $srcA -Destination $d1 -RepoRoot $nonRepo -DryRun) | Out-String
    $o2 = (& $deploy -SourceDir $srcA -Destination $d2 -RepoRoot $nonRepo -DryRun) | Out-String
    Assert-Match $o1 'staging-\d{8}T\d{6}\d{3}Z-[0-9a-f]{8}' 'the stamp must be UTC with milliseconds plus a random suffix'
    Assert-Match $o2 'staging-\d{8}T\d{6}\d{3}Z-[0-9a-f]{8}' 'the stamp must be UTC with milliseconds plus a random suffix'
    $s1 = [regex]::Match($o1, 'Staging\s+:\s+(\S+)').Groups[1].Value
    $s2 = [regex]::Match($o2, 'Staging\s+:\s+(\S+)').Groups[1].Value
    Assert-True ($s1 -and $s2 -and ($s1 -ne $s2)) "two runs must never share a staging folder ('$s1' vs '$s2')"
}

Test-Case '.gitignore anchors deploy/ at the repo root' {
    $gi = Get-Content -LiteralPath (Join-Path $repoRoot '.gitignore') -Raw
    Assert-Match $gi '(?m)^/deploy/\r?$' '.gitignore must contain a root-anchored /deploy/ entry'
}

Test-Case 'powershell -File / pwsh -File resolve the default RepoRoot and exit non-zero on any error' {
    # Under `powershell -File` (5.1) $PSScriptRoot is empty while parameter
    # defaults are bound, so the old RepoRoot default died during binding and
    # exited 0 - a silent no-op. These runs pass no -RepoRoot at all.
    $expectedRoot = (Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd('\')
    $dest = Join-Path $testRoot 'filemode\server'
    $engines = @(
        @{ exe = 'powershell'; args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File') },
        @{ exe = 'pwsh';       args = @('-NoProfile', '-File') }
    )
    foreach ($e in $engines) {
        if (-not (Get-Command $e.exe -ErrorAction SilentlyContinue)) { continue }
        $eap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $dryOut = (& $e.exe @($e.args) $deploy -DryRun -SourceDir $srcA -Destination $dest -ProcessFixture $fxEmpty 2>&1) | Out-String
            $dryCode = $LASTEXITCODE
            $cfgOut = (& $e.exe @($e.args) $deploy -DryRun -SourceDir $srcA -Destination $dest -ProcessFixture $fxEmpty -Configuration Bogus 2>&1) | Out-String
            $cfgCode = $LASTEXITCODE
            $rootOut = (& $e.exe @($e.args) $deploy -DryRun -SourceDir $srcA -Destination 'X:\' -ProcessFixture $fxEmpty 2>&1) | Out-String
            $rootCode = $LASTEXITCODE
        } finally { $ErrorActionPreference = $eap }
        Assert-Equal $dryCode 0 "$($e.exe) -File -DryRun without -RepoRoot must exit 0`n$dryOut"
        # The console wraps long lines mid-path; match on whitespace-free text.
        Assert-Match ($dryOut -replace '\s+', '') ('RepoRoot:' + [regex]::Escape($expectedRoot)) "$($e.exe) -File must resolve the default RepoRoot`n$dryOut"
        Assert-True ($cfgCode -ne 0) "$($e.exe) -File with an invalid -Configuration must exit non-zero`n$cfgOut"
        Assert-Match ($cfgOut -replace '\s+', '') 'mustbeDebugorRelease' "$($e.exe) must report the bad configuration`n$cfgOut"
        Assert-True ($rootCode -ne 0) "$($e.exe) -File with a drive-root destination must exit non-zero`n$rootOut"
        Assert-Match ($rootOut -replace '\s+', '') 'driveroot' "$($e.exe) must report the drive-root refusal`n$rootOut"
    }
}

Test-Case 'leftover staging/prev-pending/prev-old siblings are listed, never deleted' {
    $dest = Join-Path $testRoot 'orphans\server'
    $oStage = "$dest.staging-19990101T000000000Z-deadbeef"
    $oPend  = "$dest.prev-pending-19990101T000000000Z-deadbeef"
    $oOld   = "$dest.prev-old-19990101T000000000Z-deadbeef"
    New-Item -ItemType Directory -Force -Path $oStage, $oPend, $oOld | Out-Null
    [IO.File]::WriteAllText((Join-Path $oStage 'DEPLOYED.json'), '{"deployedBy":"scripts/deploy-server.ps1","commit":"0123456789abcdef0123456789abcdef01234567","utc":"2026-01-02T03:04:05.0000000Z","files":[]}')
    [IO.File]::WriteAllText((Join-Path $oStage 'payload.dll'), 'x')
    [IO.File]::WriteAllText((Join-Path $oPend 'payload.dll'), 'y')
    [IO.File]::WriteAllText((Join-Path $oOld 'DEPLOYED.json'), 'not json')

    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = (& $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -DryRun -ProcessFixture $fxEmpty 3>&1) | Out-String
    } finally { $ErrorActionPreference = $eap }
    # The console wraps long warning lines mid-path; match whitespace-free.
    $flat = $out -replace '\s+', ''
    foreach ($o in @($oStage, $oPend, $oOld)) {
        Assert-Match $flat ([regex]::Escape($o)) 'every leftover folder must be listed'
    }
    Assert-Match $flat 'removebyhand' 'the listing must say the folders are removed by hand'
    Assert-Match $flat '0123456789abcdef0123456789abcdef01234567' 'the leftover DEPLOYED.json commit must be listed'
    Assert-Match $flat '2026-01-02T03:04:05' 'the leftover DEPLOYED.json utc must be listed'

    # A real deploy still never deletes them.
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -ProcessFixture $fxEmpty 3>$null | Out-Null
    Assert-True (Test-Path -LiteralPath (Join-Path $dest 'Forge.Server.exe')) 'the deploy itself must still succeed'
    foreach ($o in @($oStage, $oPend, $oOld)) {
        Assert-True (Test-Path -LiteralPath $o) "leftover '$o' must survive a deploy"
    }
}

Test-Case 'a locked file inside .prev leaves .prev complete and keeps the pending copy' {
    $dest = Join-Path $testRoot 'prevlock\server'
    & $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo | Out-Null
    & $deploy -SourceDir $srcB -Destination $dest -RepoRoot $nonRepo | Out-Null
    $prevDir = "$dest.prev"
    $destBefore = Get-TreeHashes $dest
    $prevBefore = Get-TreeHashes $prevDir

    # A file locked inside .prev makes the .prev -> .prev-old rename fail, so
    # the rotation aborts: .prev must stay complete (the old code deleted its
    # contents first) and the old destination must be kept at .prev-pending.
    $fs = [IO.File]::Open((Join-Path $prevDir 'Forge.Server.dll'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = (& $deploy -SourceDir $srcC -Destination $dest -RepoRoot $nonRepo 3>&1) | Out-String
    } finally {
        $ErrorActionPreference = $eap
        $fs.Dispose()
    }
    Assert-Match $out '=== Forge server deployed ===' 'the deploy must succeed even when .prev cannot be rotated'
    Assert-Match ($out -replace '\s+', ' ') 'prev-pending' 'the warning must name the kept pending folder'
    Assert-TreeEqual $prevBefore (Get-TreeHashes $prevDir) '.prev must be complete and byte-identical after the failed rotation'
    $pend = @(Get-DeploySiblings $dest '.prev-pending-*')
    Assert-Equal $pend.Count 1 'the pre-deploy destination must be kept at exactly one pending folder'
    Assert-TreeEqual $destBefore (Get-TreeHashes $pend[0].FullName) 'the pending folder must hold the pre-deploy destination'
    Assert-Equal @(Get-DeploySiblings $dest '.prev-old-*').Count 0 'no .prev-old husk may remain'
    $v3 = Get-TreeHashes $srcC
    $destHash = Get-TreeHashes $dest
    foreach ($k in $v3.Keys) {
        Assert-Equal $destHash[$k] $v3[$k] "destination must hold the v3 file '$k'"
    }
    # And the kept pending folder is listed as a leftover on the next run.
    $out2 = (& $deploy -SourceDir $srcA -Destination $dest -RepoRoot $nonRepo -DryRun -ProcessFixture $fxEmpty 3>&1) | Out-String
    Assert-Match ($out2 -replace '\s+', '') ([regex]::Escape($pend[0].FullName)) 'the next run must list the kept pending folder'
}

} finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

# ------------------------------------------------------------------ summary

$stopwatch.Stop()
Write-Host ""
Write-Host ("Passed {0}/{1} in {2:n1}s" -f $script:passCount, ($script:passCount + $script:failCount), $stopwatch.Elapsed.TotalSeconds)
if ($script:failures.Count) {
    $script:failures | ForEach-Object { Write-Host "  FAILED: $_" -ForegroundColor Red }
    exit 1
}
exit 0
