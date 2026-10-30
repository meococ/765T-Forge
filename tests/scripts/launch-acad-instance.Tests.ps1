<#
.SYNOPSIS
  Standalone test runner for launch-acad-instance.ps1 / stop-acad-instance.ps1.

.DESCRIPTION
  No Pester dependency (this repo's machines may only have Pester 3.x).
  Run:  powershell -NoProfile -File tests\scripts\launch-acad-instance.Tests.ps1

  Everything runs through -DryRun plus injected test hooks (-InstanceRoot,
  -RegistryFixture, -AcadExe) so nothing touches a real AutoCAD, a real
  registry, or the live Forge pipe. Registry writes the tests need happen only
  under a throwaway HKCU:\Software\ForgeLauncherTests\<guid> root. Exit code
  0 = all passed, 1 = failures.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$launch   = Join-Path $repoRoot 'scripts\launch-acad-instance.ps1'
$stop     = Join-Path $repoRoot 'scripts\stop-acad-instance.ps1'

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

function New-InstanceRoot {
    $dir = Join-Path $testRoot ('ir-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    return $dir   # deliberately not created: scripts must create it (or, in DryRun, never create it)
}

function Write-StateFile([string] $instanceRoot, [string] $name, [hashtable] $state) {
    $dir = Join-Path $instanceRoot $name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $dir 'instance.json') -Encoding UTF8
}

# Reads the (Default) value of a HKCU subkey; returns $missing when the key or
# the value is absent so absent-vs-empty stays distinguishable.
function Get-RegDefault([string] $subKey, $missing = '__REGTEST_MISSING__') {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($subKey)
    if (-not $k) { return $missing }
    try { return $k.GetValue('', $missing) } finally { $k.Dispose() }
}

# Deletes a per-test key tree under HKCU:\Software\ForgeLauncherTests and prunes
# the test root once it is empty again.
function Remove-RegTestKey([string] $regTestRoot) {
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($regTestRoot, $false) } catch {
        # A leftover deny ACE can block the writable opens DeleteSubKeyTree
        # makes internally; delete via a parent handle that only requests
        # rights the deny does not cover.
        $delRights = [System.Security.AccessControl.RegistryRights]::Delete -bor
                     [System.Security.AccessControl.RegistryRights]::EnumerateSubKeys -bor
                     [System.Security.AccessControl.RegistryRights]::QueryValues
        $leaf = Split-Path -Leaf $regTestRoot
        $parentPath = $regTestRoot.Substring(0, $regTestRoot.Length - $leaf.Length - 1)
        $hp = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
            $parentPath, [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree, $delRights)
        if ($hp) { try { $hp.DeleteSubKeyTree($leaf, $false) } finally { $hp.Dispose() } }
    }
    $parent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\ForgeLauncherTests')
    if ($parent -and -not $parent.GetSubKeyNames().Count -and -not $parent.GetValueNames().Count) {
        $parent.Dispose()
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\ForgeLauncherTests', $false)
    } elseif ($parent) { $parent.Dispose() }
}

# ------------------------------------------------------------------ sandbox

$testRoot = Join-Path $env:TEMP ('forge-launcher-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null

$fakeAcadDir = Join-Path $testRoot 'FakeAutoCAD'
New-Item -ItemType Directory -Force -Path $fakeAcadDir | Out-Null
$dummyAcad = Join-Path $fakeAcadDir 'acad.exe'
New-Item -ItemType File -Force -Path $dummyAcad | Out-Null

$fakeDrawing = Join-Path $testRoot 'plan.dwg'
New-Item -ItemType File -Force -Path $fakeDrawing | Out-Null

$fixturePath = Join-Path $testRoot 'fixture.json'
[ordered]@{
    release          = 'R25.0'
    productKey       = 'ACAD-8101:409'
    currentProfile   = 'TestUser Profile'
    existingProfiles = @('TestUser Profile')
    acadInstallDir   = $fakeAcadDir
    values           = [ordered]@{
        'Profiles\TestUser Profile\Editor Configuration' = @{
            LogFilePath  = 'C:\Users\fake\AppData\Local\Autodesk\AutoCAD 2025\R25.0\enu\'
            SaveFilePath = 'C:\Users\fake\AppData\Local\Temp\'
            XrefLoadPath = 'C:\Users\fake\AppData\Local\Temp\'
        }
        'Profiles\TestUser Profile\General' = [ordered]@{
            PlotLogPath        = '%UserProfile%\AppData\Local\Autodesk\AutoCAD 2025\R25.0\enu\'
            ProfileStorage     = '%USERPROFILE%\appdata\roaming\autodesk\acad\profile.aws'
            PrinterConfigDir   = '%UserProfile%\AppData\Roaming\Autodesk\AutoCAD 2025\R25.0\enu\plotters'
            HideSystemPrinters = 0
        }
        'Profiles\TestUser Profile\General Configuration' = @{
            PlotSpoolerDirectory = 'C:\Users\fake\AppData\Local\Temp\'
        }
        'Profiles\TestUser Profile\Variables' = [ordered]@{
            LASTDIRECTORY        = 'D:\drawings'
            TempDirectory        = 'C:\Users\fake\AppData\Local\Temp\'
            XCOMPAREBAKPATH      = 'C:\Users\fake\Documents\XCompareBackup'
            ACTIVITYINSIGHTSPATH = '%UserProfile%\AppData\Local\Autodesk\ActivityInsights\Common'
        }
    }
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fixturePath -Encoding UTF8
$fixtureHash = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash

$me = Get-Process -Id $PID
$deadPid = 4999999   # not a valid live pid on a normal system; verified below anyway
if (Get-Process -Id $deadPid -ErrorAction SilentlyContinue) { $deadPid = 5999999 }

try {

# ------------------------------------------------------------------ launcher tests

Test-Case 'invalid names are refused' {
    foreach ($bad in @('bad name', '-abc', '_abc', ('a' * 33), 'a.b', '!x', ('x' * 40))) {
        Assert-Throws {
            & $launch -Name $bad -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath | Out-Null
        } 'Invalid instance name' "name '$bad' must be refused"
    }
}

Test-Case 'pipe already in use is refused' {
    $pipe = New-Object System.IO.Pipes.NamedPipeServerStream(
        '765T.Forge.AutoCAD.busy01', [System.IO.Pipes.PipeDirection]::InOut, 1)
    try {
        Assert-Throws {
            & $launch -Name busy01 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath | Out-Null
        } 'already exists' 'a live pipe named 765T.Forge.AutoCAD.busy01 must block the launch'
    } finally { $pipe.Dispose() }
}

Test-Case 'live state file is refused' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'liverun' @{
        name = 'liverun'; pipe = '765T.Forge.AutoCAD.liverun'; pid = $PID
        processStartTimeUtcFileTime = $me.StartTime.ToFileTimeUtc()
        acadPath = $me.Path
    }
    Assert-Throws {
        & $launch -Name liverun -DryRun -InstanceRoot $ir -RegistryFixture $fixturePath | Out-Null
    } 'already running' 'a state file describing a live process must block the launch'
}

Test-Case 'concurrent same-name launch is refused while the mutex is held' {
    $mtx = New-Object System.Threading.Mutex($false, 'Local\765T-Forge-Launch-mtx1')
    $held = $false
    try {
        try { $held = $mtx.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $held = $true }
        Assert-True $held 'test could not take the launch mutex'
        # A same-process run could re-acquire the mutex reentrantly, so the
        # launcher runs in a child powershell - its WaitOne must fail. The
        # child's stderr arrives as ErrorRecords, so Continue not Stop here.
        $eap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $out = (& powershell -NoProfile -ExecutionPolicy Bypass -File $launch -Name mtx1 -DryRun `
                    -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath 2>&1) | Out-String
        } finally { $ErrorActionPreference = $eap }
        Assert-True ($LASTEXITCODE -ne 0) 'the locked-out launch must exit non-zero'
        Assert-Match $out 'in progress' 'a held launch mutex must refuse the second launch'
    } finally {
        if ($held) { $mtx.ReleaseMutex() }
        $mtx.Dispose()
    }
}

Test-Case 'fixture and registry-root hooks require -DryRun' {
    Assert-Throws {
        & $launch -Name nodry1 -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath | Out-Null
    } 'only supported together with -DryRun' '-RegistryFixture without -DryRun must refuse'
    Assert-Throws {
        & $launch -Name nodry2 -InstanceRoot (New-InstanceRoot) -RegistryRoot 'HKCU:\Software\Whatever' | Out-Null
    } 'only supported together with -DryRun' '-RegistryRoot without -DryRun must refuse'
    Assert-Throws {
        & $launch -Name nodry3 -InstanceRoot (New-InstanceRoot) -HklmRegistryRoot 'HKLM:\SOFTWARE\Whatever' | Out-Null
    } 'only supported together with -DryRun' '-HklmRegistryRoot without -DryRun must refuse'
}

Test-Case '-Drawing is resolved to an absolute path and a trailing backslash is rejected' {
    Push-Location $testRoot
    try {
        $out = (& $launch -Name drw1 -DryRun -Drawing '.\plan.dwg' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath) | Out-String
        Assert-Match $out ([regex]::Escape("`"$fakeDrawing`"")) 'a relative -Drawing must be embedded as a resolved absolute path'
    } finally { Pop-Location }
    Assert-Throws {
        & $launch -Name drw2 -DryRun -Drawing ($fakeAcadDir + '\') -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath | Out-Null
    } 'backslash' 'a -Drawing ending in \ must be refused'
    Assert-Throws {
        & $launch -Name drw3 -DryRun -Drawing 'D:\definitely\missing\plan.dwg' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath | Out-Null
    } 'not found' 'a missing -Drawing must be refused'
}

Test-Case 'multiple ACAD-* product keys require -ProductKey' {
    $fxP = Join-Path $testRoot 'fixture-multi-prod.json'
    [ordered]@{
        release = 'R25.0'; productKey = 'ACAD-8101:409'
        productKeys = @('ACAD-8101:409', 'ACA-8200:409')
        currentProfile = 'TestUser Profile'; existingProfiles = @('TestUser Profile')
        acadInstallDir = $fakeAcadDir
        values = @{ 'Profiles\TestUser Profile\General' = @{ ProfileStorage = '%u%\x\profile.aws' } }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fxP -Encoding UTF8
    Assert-Throws {
        & $launch -Name pk1 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxP | Out-Null
    } 'Multiple ACAD-\* product keys' 'ambiguous product keys must refuse'
    Assert-Throws {
        & $launch -Name pk1 -DryRun -ProductKey 'ACAD-9999:409' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxP | Out-Null
    } 'not one of' 'an unknown -ProductKey must refuse'
    $out = (& $launch -Name pk1 -DryRun -ProductKey 'ACA-8200:409' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxP) | Out-String
    Assert-Match $out 'product ACA-8200:409' 'the chosen -ProductKey must be used'
}

Test-Case 'a Forge-* profile pointer is refused; -RestoreProfilesDefault plans the restore' {
    $fxH = Join-Path $testRoot 'fixture-hijack.json'
    [ordered]@{
        release = 'R25.0'; productKey = 'ACAD-8101:409'
        currentProfile = 'TestUser Profile'; profilesDefault = 'Forge-old9'
        existingProfiles = @('TestUser Profile')
        acadInstallDir = $fakeAcadDir
        values = @{ 'Profiles\TestUser Profile\General' = @{ ProfileStorage = '%u%\x\profile.aws' } }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fxH -Encoding UTF8
    Assert-Throws {
        & $launch -Name hj1 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxH | Out-Null
    } 'leftover Forge profile pointer' 'a Forge-* Profiles (Default) must be refused without an override'
    Assert-Throws {
        & $launch -Name hj1 -DryRun -RestoreProfilesDefault 'Forge-x' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxH | Out-Null
    } "must not start with 'Forge-'" 'a Forge-* -RestoreProfilesDefault must be refused'
    $out = (& $launch -Name hj1 -DryRun -RestoreProfilesDefault 'TestUser Profile' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxH) | Out-String
    Assert-Match $out "\[WOULD-RESTORE\] .*\(Default\).*'Forge-old9' -> 'TestUser Profile'" 'the plan must restore the hijacked pointer to the declared profile'
}

Test-Case 'plan records profilesDefault and the post-start restore watch' {
    $out = (& $launch -Name ptr1 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath) | Out-String
    Assert-Match $out "recorded\s+:\s+'TestUser Profile'" 'the plan must record the current profile pointer for instance.json'
    Assert-Match $out '\[WOULD-RESTORE\]' 'the plan must show a pointer-restore step'
    Assert-Match $out 'watch after start' 'the plan must show the post-start pointer watch'
}

Test-Case 'a real pointer differing from the declared restore value is left as-is in the plan' {
    $fxL = Join-Path $testRoot 'fixture-leftasis.json'
    [ordered]@{
        release = 'R25.0'; productKey = 'ACAD-8101:409'
        currentProfile = 'RealProfile'; profilesDefault = 'RealProfile'
        existingProfiles = @('RealProfile', 'DeclaredProfile')
        acadInstallDir = $fakeAcadDir
        values = @{ 'Profiles\DeclaredProfile\General' = @{ ProfileStorage = '%u%\x\profile.aws' } }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fxL -Encoding UTF8
    $out = (& $launch -Name la1 -DryRun -RestoreProfilesDefault 'DeclaredProfile' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fxL) | Out-String
    Assert-Match $out "left as-is \(now 'RealProfile', recorded 'DeclaredProfile'\)" 'a real pointer differing from the declared restore value must be left as-is'
    Assert-True ($out -notmatch "WOULD-RESTORE.*\(Default\).*->") 'no pointer-rewrite step may be planned for a real profile name'
}

Test-Case 'dry-run writes nothing' {
    $ir = New-InstanceRoot
    & $launch -Name dry1 -DryRun -InstanceRoot $ir -RegistryFixture $fixturePath | Out-Null
    $leftovers = @(Get-ChildItem -LiteralPath $ir -Recurse -Force -ErrorAction SilentlyContinue)
    Assert-Equal $leftovers.Count 0 'instance root must stay empty after dry-run'
    Assert-True (-not (Test-Path -LiteralPath $ir)) 'instance root dir itself must not be created by dry-run'
    Assert-Equal (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash $fixtureHash 'registry fixture must be byte-identical'
    Assert-True (-not (Test-Path 'HKCU:\Software\ForgeTestsCanary')) 'registry canary absent'
}

Test-Case 'plan copies profile to Forge-<n> and never writes the source key' {
    $ir = New-InstanceRoot
    $out = (& $launch -Name dry1 -DryRun -InstanceRoot $ir -RegistryFixture $fixturePath) | Out-String
    Assert-Match $out 'Profiles\\Forge-dry1' 'plan must show the Forge-dry1 profile key'
    Assert-Match $out '\[WOULD-COPY\]' 'plan must show a profile copy step'
    Assert-Match $out '->.*Profiles\\Forge-dry1' 'copy destination must be Forge-dry1'
    Assert-Match $out 'FORGE_PIPE_NAME=765T\.Forge\.AutoCAD\.dry1' 'plan must show the per-instance pipe'
    Assert-Match $out '765t-forge-dry1' 'plan must show the MCP server name'
    Assert-Match $out 'LogFilePath' 'plan must re-point LogFilePath'
    Assert-Match $out 'ForgeManaged = 1' 'plan must stamp the ForgeManaged marker'

    $setLines = @($out -split "`r?`n" | Where-Object { $_ -match '\[WOULD-SET\]' })
    Assert-True ($setLines.Count -ge 3) "expected >=3 profile value writes, got $($setLines.Count)"
    foreach ($l in $setLines) {
        Assert-Match $l 'Forge-dry1' 'every WOULD-SET must target the Forge-<n> copy'
        Assert-True ($l -notmatch 'TestUser Profile') "WOULD-SET must never touch the source profile key: $l"
    }
    Assert-True (-not ($setLines | Where-Object { $_ -match 'PrinterConfigDir' })) 'shared resource dirs must not be re-pointed'
    $writeLines = @($out -split "`r?`n" | Where-Object { $_ -match 'WOULD-(SET|WRITE-FILE)' })
    foreach ($l in $writeLines) {
        Assert-True ($l -notmatch 'Profiles\\TestUser Profile') "no write may target the source profile key: $l"
    }
}

Test-Case 'per-session write paths are retargeted into the instance profile dir' {
    $ir = New-InstanceRoot
    $out = (& $launch -Name dry2 -DryRun -InstanceRoot $ir -RegistryFixture $fixturePath) | Out-String
    $setLines = @($out -split "`r?`n" | Where-Object { $_ -match '\[WOULD-SET\]' })
    $profileDirPrefix = [regex]::Escape((Join-Path $ir 'dry2\profile'))
    foreach ($name in 'LogFilePath', 'SaveFilePath', 'XrefLoadPath', 'PlotLogPath', 'ProfileStorage', 'PlotSpoolerDirectory', 'XCOMPAREBAKPATH', 'ACTIVITYINSIGHTSPATH', 'TempDirectory') {
        $hit = @($setLines | Where-Object { $_ -match ":: $name = " })
        Assert-Equal $hit.Count 1 "expected exactly one WOULD-SET for $name"
        Assert-Match $hit[0] $profileDirPrefix "WOULD-SET for $name must point inside the instance profile dir"
    }
    foreach ($l in ($setLines | Where-Object { $_ -notmatch 'ForgeManaged' })) {
        Assert-Match $l $profileDirPrefix "every value retarget must land inside the instance profile dir: $l"
    }
}

Test-Case 'existing managed Forge-<n> profile is reused (SKIP-COPY)' {
    $fx2Path = Join-Path $testRoot 'fixture2.json'
    [ordered]@{
        release = 'R25.0'; productKey = 'ACAD-8101:409'; currentProfile = 'TestUser Profile'
        existingProfiles = @('TestUser Profile', 'Forge-skip1')
        forgeManagedProfiles = @('Forge-skip1')
        acadInstallDir = $fakeAcadDir
        values = @{ 'Profiles\TestUser Profile\General' = @{ ProfileStorage = '%u%\x\profile.aws' } }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fx2Path -Encoding UTF8
    $out = (& $launch -Name skip1 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fx2Path) | Out-String
    Assert-Match $out 'SKIP-COPY' 'existing Forge-skip1 key carrying the marker must be reused'
    Assert-True ($out -notmatch '\[WOULD-COPY\]') 'no copy may be planned when the managed profile exists'
}

Test-Case 'existing Forge-<n> profile without the marker is recreated' {
    $fx3Path = Join-Path $testRoot 'fixture3.json'
    [ordered]@{
        release = 'R25.0'; productKey = 'ACAD-8101:409'; currentProfile = 'TestUser Profile'
        existingProfiles = @('TestUser Profile', 'Forge-re1')
        acadInstallDir = $fakeAcadDir
        values = @{ 'Profiles\TestUser Profile\General' = @{ ProfileStorage = '%u%\x\profile.aws' } }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fx3Path -Encoding UTF8
    $out = (& $launch -Name re1 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fx3Path) | Out-String
    Assert-Match $out '\[WOULD-RECREATE\]' 'an unmanaged Forge-re1 key must be replaced'
    Assert-Match $out '\[WOULD-COPY\]' 'a fresh copy must be planned'
}

Test-Case 'environment is restored after the call' {
    $ir = New-InstanceRoot
    $saved = @{}
    foreach ($k in 'FORGE_PIPE_NAME', 'FORGE_AUDIT_DIR', 'FORGE_BACKUP_DIR') {
        $saved[$k] = [Environment]::GetEnvironmentVariable($k)
        Set-Item -Path "Env:$k" -Value "SENTINEL_$k"
    }
    try {
        & $launch -Name dry3 -DryRun -InstanceRoot $ir -RegistryFixture $fixturePath | Out-Null
        foreach ($k in $saved.Keys) {
            Assert-Equal ([Environment]::GetEnvironmentVariable($k)) "SENTINEL_$k" "$k must be restored"
        }
    } finally {
        foreach ($k in $saved.Keys) {
            if ($null -eq $saved[$k]) { Remove-Item -Path "Env:$k" -ErrorAction SilentlyContinue }
            else { Set-Item -Path "Env:$k" -Value $saved[$k] }
        }
    }
}

Test-Case 'acad.exe resolution order' {
    $ir = New-InstanceRoot
    # 1) -AcadExe wins
    $out = (& $launch -Name a1 -DryRun -AcadExe $dummyAcad -InstanceRoot $ir -RegistryFixture $fixturePath) | Out-String
    Assert-Match $out ([regex]::Escape($dummyAcad)) 'explicit -AcadExe must be used'
    Assert-Match $out 'via -AcadExe' 'source must be reported'
    # 2) AUTOCAD_2026_ROOT next
    $savedRoot = [Environment]::GetEnvironmentVariable('AUTOCAD_2026_ROOT')
    try {
        $env:AUTOCAD_2026_ROOT = $fakeAcadDir
        $out = (& $launch -Name a2 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath) | Out-String
        Assert-Match $out 'via AUTOCAD_2026_ROOT' 'env var must be honoured'
    } finally {
        if ($null -eq $savedRoot) { Remove-Item Env:AUTOCAD_2026_ROOT -ErrorAction SilentlyContinue }
        else { $env:AUTOCAD_2026_ROOT = $savedRoot }
    }
    # 3) fixture/registry install dir last (env var must be absent - it wins otherwise;
    #    this machine may legitimately have AUTOCAD_2026_ROOT set)
    $savedRoot2 = [Environment]::GetEnvironmentVariable('AUTOCAD_2026_ROOT')
    try {
        Remove-Item Env:AUTOCAD_2026_ROOT -ErrorAction SilentlyContinue
        $out = (& $launch -Name a3 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath) | Out-String
        Assert-Match $out 'via registry AcadLocation' 'registry install path must be honoured'
    } finally {
        if ($null -ne $savedRoot2) { $env:AUTOCAD_2026_ROOT = $savedRoot2 }
    }
    # 4) missing -AcadExe refuses
    Assert-Throws {
        & $launch -Name a4 -DryRun -AcadExe 'D:\definitely\missing\acad.exe' -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath | Out-Null
    } 'not found' 'missing -AcadExe must refuse'
}

Test-Case 'stale state file is overwritten, not refused' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'stale1' @{
        name = 'stale1'; pipe = '765T.Forge.AutoCAD.stale1'; pid = $deadPid
        processStartTimeUtcFileTime = 0; acadPath = 'D:\gone\acad.exe'
    }
    $out = (& $launch -Name stale1 -DryRun -InstanceRoot $ir -RegistryFixture $fixturePath 3>&1) | Out-String
    Assert-Match $out 'Stale state file' 'a stale state file must warn but proceed'
    Assert-Match $out 'WOULD-START' 'plan must still be produced'
}

Test-Case 'the MCP snippet defaults to the live deploy server path' {
    $out = (& $launch -Name mcp1 -DryRun -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath) | Out-String
    # ConvertTo-Json escapes '\' as '\\' in the printed snippet.
    Assert-Match $out ([regex]::Escape('deploy\\server\\Forge.Server.exe')) 'the snippet must point at the live deploy server path'
    Assert-True ($out -notmatch 'bin\\\\(Debug|Release)') 'the snippet must not point at a build output path'
}

Test-Case '-ServerExe overrides the snippet server path' {
    $custom = 'D:\deploy\server\Forge.Server.exe'
    $out = (& $launch -Name mcp2 -DryRun -ServerExe $custom -InstanceRoot (New-InstanceRoot) -RegistryFixture $fixturePath) | Out-String
    Assert-Match $out ([regex]::Escape(($custom -replace '\\', '\\'))) 'the snippet must carry the -ServerExe path'
}

# ------------------------------------------------------------------ stop tests

Test-Case 'stop refuses when state file is missing' {
    Assert-Throws {
        & $stop -Name ghost -DryRun -InstanceRoot (New-InstanceRoot) | Out-Null
    } 'No state file' 'missing instance.json must refuse'
}

Test-Case 'stop refuses when required state fields are missing' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'nof1' @{
        name = 'nof1'; acadPath = 'D:\x\acad.exe'; processStartTimeUtcFileTime = 0
    }
    Assert-Throws {
        & $stop -Name nof1 -DryRun -InstanceRoot $ir | Out-Null
    } "missing 'pid'" 'a state file without pid must refuse'
    Write-StateFile $ir 'nof2' @{
        name = 'nof2'; pid = $deadPid; processStartTimeUtcFileTime = 0
    }
    Assert-Throws {
        & $stop -Name nof2 -DryRun -InstanceRoot $ir | Out-Null
    } "missing 'acadPath'" 'a state file without acadPath must refuse'
    Write-StateFile $ir 'nof3' @{
        name = 'nof3'; pid = $deadPid; acadPath = 'D:\x\acad.exe'
    }
    Assert-Throws {
        & $stop -Name nof3 -DryRun -InstanceRoot $ir | Out-Null
    } 'no process start time' 'a state file without a start time must refuse'
}

Test-Case 'stop refuses on pid mismatch (process gone)' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'dead1' @{
        name = 'dead1'; pid = $deadPid; acadPath = 'D:\whatever\acad.exe'
        processStartTimeUtcFileTime = 0
    }
    Assert-Throws {
        & $stop -Name dead1 -DryRun -InstanceRoot $ir | Out-Null
    } 'no running process' 'a dead recorded pid must refuse'
}

Test-Case 'stop refuses on start-time mismatch (pid reused)' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'time1' @{
        name = 'time1'; pid = $PID; acadPath = $me.Path
        processStartTimeUtcFileTime = ($me.StartTime.ToFileTimeUtc() + 36000000000)
    }
    Assert-Throws {
        & $stop -Name time1 -DryRun -InstanceRoot $ir | Out-Null
    } 'start time' 'a live pid with a different start time must refuse'
}

Test-Case 'stop refuses on path mismatch' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'path1' @{
        name = 'path1'; pid = $PID; acadPath = 'D:\fake\acad.exe'
        processStartTimeUtcFileTime = $me.StartTime.ToFileTimeUtc()
    }
    Assert-Throws {
        & $stop -Name path1 -DryRun -InstanceRoot $ir | Out-Null
    } 'runs.*recorded' 'a live pid running a different exe must refuse'
}

Test-Case 'stop refuses when the command line lacks /p "Forge-<n>"' {
    $ir = New-InstanceRoot
    Write-StateFile $ir 'ok1' @{
        name = 'ok1'; pipe = '765T.Forge.AutoCAD.ok1'; profile = 'Forge-ok1'
        pid = $PID; acadPath = $me.Path
        processStartTimeUtcFileTime = $me.StartTime.ToFileTimeUtc()
    }
    Assert-Throws {
        & $stop -Name ok1 -DryRun -InstanceRoot $ir | Out-Null
    } 'command line' 'a matching pid/path without the /p Forge-<n> switch must still refuse'
}

Test-Case 'stop (dry-run) accepts a process started with /p "Forge-<n>"' {
    $ir = New-InstanceRoot
    # A harmless long-lived process whose command line carries the launcher
    # switch - proves the command-line check passes for a Forge-<n> start.
    $fake = Start-Process -FilePath "$env:WINDIR\System32\cmd.exe" `
        -ArgumentList '/c ping -n 120 127.0.0.1 >nul & rem /p "Forge-cmdl1"' -PassThru
    try {
        $fakeProc = Get-Process -Id $fake.Id
        Write-StateFile $ir 'cmdl1' @{
            name = 'cmdl1'; pipe = '765T.Forge.AutoCAD.cmdl1'; profile = 'Forge-cmdl1'
            pid = $fake.Id; acadPath = $fakeProc.Path
            processStartTimeUtcFileTime = $fakeProc.StartTime.ToFileTimeUtc()
        }
        $out = (& $stop -Name cmdl1 -DryRun -InstanceRoot $ir) | Out-String
        Assert-Match $out 'WOULD-STOP' 'a fully matching state plus /p command line must produce a stop plan'
        Assert-Match $out 'nothing stopped' 'dry-run must stop nothing'
        Assert-True (Get-Process -Id $fake.Id -ErrorAction SilentlyContinue) 'the fake process must still be alive'
    } finally {
        Stop-Process -Id $fake.Id -Force -ErrorAction SilentlyContinue
    }
}

Test-Case 'stop -RestoreOnly plans the pointer restore on a throwaway key' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $pk.SetValue('', 'Forge-hijack', [Microsoft.Win32.RegistryValueKind]::String)
        $pk.Dispose()
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst1' @{
            name = 'rst1'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'TestUser Profile'
        }
        $out = (& $stop -Name rst1 -DryRun -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "\[WOULD-RESTORE\].*\(Default\): 'Forge-hijack' -> 'TestUser Profile'" 'a differing pointer must produce a restore plan'
        $pk2 = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$regTestRoot\Profiles")
        Assert-Equal ([string]$pk2.GetValue('')) 'Forge-hijack' 'dry-run must not actually write the pointer'
        $pk2.Dispose()
    } finally {
        if ($pk) { $pk.Dispose() }
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($regTestRoot, $false)
        $parent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\ForgeLauncherTests')
        if ($parent -and -not $parent.GetSubKeyNames().Count -and -not $parent.GetValueNames().Count) {
            $parent.Dispose()
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\ForgeLauncherTests', $false)
        } elseif ($parent) { $parent.Dispose() }
    }
}

Test-Case 'stop -RestoreOnly reports nothing to restore when the pointer already matches' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $pk.SetValue('', 'TestUser Profile', [Microsoft.Win32.RegistryValueKind]::String)
        $pk.Dispose()
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst2' @{
            name = 'rst2'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'TestUser Profile'
        }
        $out = (& $stop -Name rst2 -DryRun -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out 'nothing to restore' 'a matching pointer must be a no-op'
    } finally {
        if ($pk) { $pk.Dispose() }
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($regTestRoot, $false)
        $parent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\ForgeLauncherTests')
        if ($parent -and -not $parent.GetSubKeyNames().Count -and -not $parent.GetValueNames().Count) {
            $parent.Dispose()
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\ForgeLauncherTests', $false)
        } elseif ($parent) { $parent.Dispose() }
    }
}

Test-Case 'stop -RestoreOnly restores a Forge-* pointer but leaves a real switch as-is' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst3' @{
            name = 'rst3'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'TestUser Profile'
        }
        # A Forge-* leftover pointer is the failure mode the restore undoes.
        $pk.SetValue('', 'Forge-x', [Microsoft.Win32.RegistryValueKind]::String)
        $out = (& $stop -Name rst3 -DryRun -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "\[WOULD-RESTORE\].*\(Default\): 'Forge-x' -> 'TestUser Profile'" 'a Forge-* pointer must produce a restore plan'
        Assert-Equal ([string]$pk.GetValue('')) 'Forge-x' 'dry-run must not write the pointer'
        # A real, different profile name means the user switched - leave it.
        $pk.SetValue('', 'WorkProfile', [Microsoft.Win32.RegistryValueKind]::String)
        $out = (& $stop -Name rst3 -DryRun -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "left as-is \(now 'WorkProfile', recorded 'TestUser Profile'\)" 'a real non-Forge pointer must be left as-is'
        Assert-True ($out -notmatch 'WOULD-RESTORE') 'no restore may be planned over a real profile name'
        Assert-Equal ([string]$pk.GetValue('')) 'WorkProfile' "the user's own pointer must be untouched"
        # Pointer already equal to the recorded value is a no-op.
        $pk.SetValue('', 'TestUser Profile', [Microsoft.Win32.RegistryValueKind]::String)
        $out = (& $stop -Name rst3 -DryRun -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out 'nothing to restore' 'an already-matching pointer must be a no-op'
        Assert-True ($out -notmatch 'WOULD-RESTORE') 'no restore may be planned when the pointer matches'
    } finally {
        if ($pk) { $pk.Dispose() }
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($regTestRoot, $false)
        $parent = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\ForgeLauncherTests')
        if ($parent -and -not $parent.GetSubKeyNames().Count -and -not $parent.GetValueNames().Count) {
            $parent.Dispose()
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\ForgeLauncherTests', $false)
        } elseif ($parent) { $parent.Dispose() }
    }
}

Test-Case 'stop -RestoreOnly refuses a hand-edited key outside Software\Autodesk\AutoCAD' {
    $ir = New-InstanceRoot
    foreach ($badKey in @('HKCU:\Software\NotAutodesk\Whatever\Profiles',
                         'HKCU:\Software\Autodesk\OtherProduct\X\Y\Profiles',
                         'HKLM:\SOFTWARE\Autodesk\AutoCAD\R25.0\ACAD-8101:409\Profiles')) {
        Write-StateFile $ir 'badkey' @{
            name = 'badkey'; pid = $deadPid
            profilesKey = $badKey; profilesDefault = 'TestUser Profile'
        }
        Assert-Throws {
            & $stop -Name badkey -DryRun -RestoreOnly -InstanceRoot $ir | Out-Null
        } 'it is not a HKCU' "profilesKey '$badKey' must be refused"
    }
    # A Forge-* recorded value must be refused before any write, even on a
    # real-shaped key (never opened: validation throws first).
    Write-StateFile $ir 'badkey' @{
        name = 'badkey'; pid = $deadPid
        profilesKey = 'HKCU:\Software\Autodesk\AutoCAD\R25.0\ACAD-8101:409\Profiles'
        profilesDefault = 'Forge-x'
    }
    Assert-Throws {
        & $stop -Name badkey -DryRun -RestoreOnly -InstanceRoot $ir | Out-Null
    } "starts with 'Forge-'" 'a Forge-* recorded profilesDefault must be refused'
}

# ---- real (non-DryRun) -RestoreOnly runs ------------------------------------
# Every restore test above is -DryRun, which returns before the writable half
# of Restore-ProfilesDefault. These run for real against per-test
# HKCU:\Software\ForgeLauncherTests\<guid>\Profiles keys - the only root the
# script's key-shape gate allows besides the real Autodesk one - and assert the
# registry value, not only the printed line.

Test-Case 'stop -RestoreOnly (real run) restores a Forge-* pointer to the recorded value' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $pk.SetValue('', 'Forge-x', [Microsoft.Win32.RegistryValueKind]::String)
        $pk.Dispose()
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst4' @{
            name = 'rst4'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'WorkProfile'
        }
        $out = (& $stop -Name rst4 -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "\[RESTORED\].*\(Default\): 'Forge-x' -> 'WorkProfile'" 'a Forge-* pointer must be restored to the recorded value'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") 'WorkProfile' 'the registry pointer must now read the recorded value'
    } finally {
        if ($pk) { $pk.Dispose() }
        Remove-RegTestKey $regTestRoot
    }
}

Test-Case 'stop -RestoreOnly (real run) restores an empty or missing pointer' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst5' @{
            name = 'rst5'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'WorkProfile'
        }
        $pk.SetValue('', '', [Microsoft.Win32.RegistryValueKind]::String)
        $out = (& $stop -Name rst5 -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "\[RESTORED\].*\(Default\): '' -> 'WorkProfile'" 'an empty pointer must be restored'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") 'WorkProfile' 'the pointer must read the recorded value'
        $pk.DeleteValue('', $false)
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") '__REGTEST_MISSING__' 'setup must leave (Default) absent'
        $out = (& $stop -Name rst5 -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "\[RESTORED\].*\(Default\): '' -> 'WorkProfile'" 'a missing pointer must be restored'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") 'WorkProfile' 'the pointer must read the recorded value'
    } finally {
        if ($pk) { $pk.Dispose() }
        Remove-RegTestKey $regTestRoot
    }
}

Test-Case 'stop -RestoreOnly (real run) deletes the pointer when the recorded value is null' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst6' @{
            name = 'rst6'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = $null
        }
        $pk.SetValue('', 'Forge-x', [Microsoft.Win32.RegistryValueKind]::String)
        $out = (& $stop -Name rst6 -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "\[RESTORED\].*\(Default\): 'Forge-x' -> ''" 'a null recorded value must delete the pointer'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") '__REGTEST_MISSING__' '(Default) must be gone after the run'
        # A state file with no profilesDefault property at all skips the restore.
        Write-StateFile $ir 'rst6' @{
            name = 'rst6'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"
        }
        $pk.SetValue('', 'Forge-x', [Microsoft.Win32.RegistryValueKind]::String)
        $out = (& $stop -Name rst6 -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out 'skipping' 'a missing profilesDefault property must skip the restore'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") 'Forge-x' 'the pointer must be untouched when nothing was recorded'
    } finally {
        if ($pk) { $pk.Dispose() }
        Remove-RegTestKey $regTestRoot
    }
}

Test-Case 'stop -RestoreOnly (real run) leaves a real, different pointer untouched' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    try {
        $pk.SetValue('', 'MyRealProfile', [Microsoft.Win32.RegistryValueKind]::String)
        $pk.Dispose()
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst7' @{
            name = 'rst7'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'WorkProfile'
        }
        $out = (& $stop -Name rst7 -RestoreOnly -InstanceRoot $ir) | Out-String
        Assert-Match $out "left as-is \(now 'MyRealProfile', recorded 'WorkProfile'\)" 'a real non-Forge pointer must be left as-is'
        Assert-True ($out -notmatch 'RESTORED') 'a real run must not write over a user profile switch'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") 'MyRealProfile' 'the pointer must be untouched'
    } finally {
        if ($pk) { $pk.Dispose() }
        Remove-RegTestKey $regTestRoot
    }
}

Test-Case 'stop -RestoreOnly (real run) no-ops on a matching pointer without a writable open' {
    $regTestRoot = 'Software\ForgeLauncherTests\' + [guid]::NewGuid().ToString('N')
    $pk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$regTestRoot\Profiles")
    # A FullControl handle opened BEFORE the deny ACE: its granted rights
    # survive the ACL change and are used to remove the ACE afterwards.
    $pkSec = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        "$regTestRoot\Profiles",
        [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree,
        [System.Security.AccessControl.RegistryRights]::FullControl)
    $denyRule = New-Object System.Security.AccessControl.RegistryAccessRule(
        [System.Security.Principal.WindowsIdentity]::GetCurrent().User,
        [System.Security.AccessControl.RegistryRights]::SetValue,
        [System.Security.AccessControl.InheritanceFlags]::None,
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Deny)
    try {
        Assert-True ($null -ne $pkSec) 'setup: could not open the test key with FullControl'
        $pk.SetValue('', 'WorkProfile', [Microsoft.Win32.RegistryValueKind]::String)
        $ir = New-InstanceRoot
        Write-StateFile $ir 'rst8' @{
            name = 'rst8'; pid = $deadPid
            profilesKey = "HKCU:\$regTestRoot\Profiles"; profilesDefault = 'WorkProfile'
        }
        # Deny ourselves the right the writable open requests. A no-op returns
        # before that open; if the code ever tried it, the open fails and the
        # run can never reach "nothing to restore".
        $acl = $pkSec.GetAccessControl()
        $acl.AddAccessRule($denyRule)
        $pkSec.SetAccessControl($acl)
        $probe = $null
        try { $probe = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("$regTestRoot\Profiles", $true) } catch { $probe = $null }
        try { Assert-True ($null -eq $probe) 'setup: the deny ACE must make the writable open fail' }
        finally { if ($probe) { $probe.Dispose() } }
        $out = (& $stop -Name rst8 -RestoreOnly -InstanceRoot $ir 3>&1) | Out-String
        Assert-Match $out 'nothing to restore' 'an already-matching pointer must be a no-op'
        Assert-True ($out -notmatch 'Cannot open') 'a no-op must never attempt the writable open'
        Assert-Equal (Get-RegDefault "$regTestRoot\Profiles") 'WorkProfile' 'the matching pointer must be untouched'
    } finally {
        if ($pkSec) {
            try {
                $acl = $pkSec.GetAccessControl()
                $acl.RemoveAccessRuleSpecific($denyRule)
                $pkSec.SetAccessControl($acl)
            } catch { }
            $pkSec.Dispose()
        }
        if ($pk) { $pk.Dispose() }
        Remove-RegTestKey $regTestRoot
    }
}

Test-Case 'stop refuses invalid names' {
    Assert-Throws {
        & $stop -Name 'bad name' -DryRun -InstanceRoot (New-InstanceRoot) | Out-Null
    } 'Invalid instance name' 'invalid names must be refused by stop too'
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
