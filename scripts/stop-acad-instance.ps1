<#
.SYNOPSIS
  Stop an AutoCAD instance previously started by launch-acad-instance.ps1.

.DESCRIPTION
  Reads %LOCALAPPDATA%\765T-Forge\instances\<Name>\instance.json and stops the
  recorded process ONLY when ALL of these hold:
    - a process with the recorded pid exists
    - its start time equals the recorded start time (guards pid reuse)
    - its executable path equals the recorded acad.exe path
    - its command line contains /p "Forge-<Name>" (guards a hand-edited
      state file - the launcher always starts acad.exe that way)
  Otherwise it refuses and stops nothing. It never touches a process that is
  not the one the launcher recorded (e.g. the user's own AutoCAD).

  After the recorded pid has exited it also restores the current-profile
  pointer: the (Default) value of the recorded Profiles key is written back to
  the recorded 'profilesDefault' value, in case AutoCAD rewrote it while
  running on /p "Forge-<Name>" - but only when it reads a Forge-* profile or
  is empty/absent, the failure mode the restore exists to undo. A real,
  different profile name is the user's own switch and is left as-is. Only the
  recorded value is ever written, and only after the recorded key is proved to
  be a real ...\Profiles key under HKCU:\Software\Autodesk\AutoCAD and the
  recorded value is proved not to start with Forge- - a hand-edited state
  file can aim the write neither elsewhere nor at a Forge pointer.

  -RestoreOnly skips the stop entirely and just performs the pointer restore;
  use it when the instance window was already closed by hand and only the
  pointer may be wrong.

  -DryRun performs every check and prints [WOULD-STOP] / [WOULD-RESTORE]
  instead of stopping or writing.

  -InstanceRoot redirects the state folder (test hook).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Name,

    [switch] $DryRun,

    [string] $InstanceRoot = (Join-Path $env:LOCALAPPDATA '765T-Forge\instances'),

    [int] $WaitSeconds = 30,

    [switch] $RestoreOnly
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- helpers

function Resolve-RegistryPath {
    param([Parameter(Mandatory)][string] $Path)
    $p = $Path.Trim().Replace('/', '\').TrimEnd('\')
    if ($p -match '^(?i)(HKCU|HKEY_CURRENT_USER)[:\\]+(.*)$') {
        return @{ Hive = 'CurrentUser'; SubKey = $Matches[2]; Display = "HKCU:\$($Matches[2])" }
    }
    if ($p -match '^(?i)(HKLM|HKEY_LOCAL_MACHINE)[:\\]+(.*)$') {
        return @{ Hive = 'LocalMachine'; SubKey = $Matches[2]; Display = "HKLM:\$($Matches[2])" }
    }
    throw "Unsupported registry root '$Path' (expected HKCU:\... or HKLM:\...)."
}

function Open-RegKey {
    param([Parameter(Mandatory)] $RegPath, [switch] $Writable)
    $hive = if ($RegPath.Hive -eq 'LocalMachine') { [Microsoft.Win32.Registry]::LocalMachine } else { [Microsoft.Win32.Registry]::CurrentUser }
    $hive.OpenSubKey($RegPath.SubKey, [bool]$Writable)
}

# Writes Profiles (Default) back to the recorded value, but only when the
# pointer is in the failure mode this exists to undo: it reads a Forge-*
# profile or is empty/absent. A real, different profile name means the user
# switched their current profile while the instance ran - it is left as-is.
# Only ever writes the recorded 'profilesDefault' - never anything else.
function Restore-ProfilesDefault {
    param($State, [switch] $DryRun)
    $keyStr = [string]$State.profilesKey
    $hasDefault = ($null -ne $State.PSObject.Properties['profilesDefault'])
    if (-not $keyStr -or -not $hasDefault) {
        Write-Output "No profilesKey/profilesDefault recorded in the state file - skipping the current-profile pointer restore."
        return
    }
    # instance.json is user-editable, so re-validate what the launcher claims
    # it recorded before touching anything: the key must be a real ...\Profiles
    # key under the AutoCAD root (the throwaway ForgeLauncherTests root is
    # honoured so tests can exercise this path) and the recorded value must not
    # itself be a Forge-* leftover pointer.
    $regPath = Resolve-RegistryPath $keyStr
    $isProfilesKey = ($regPath.Hive -eq 'CurrentUser') -and
        ($regPath.SubKey -match '^(?i)Software\\(Autodesk\\AutoCAD\\[^\\]+\\[^\\]+|ForgeLauncherTests\\[^\\]+)\\Profiles$')
    if (-not $isProfilesKey) {
        throw "Refusing to write (Default) of '$keyStr' - it is not a HKCU:\Software\Autodesk\AutoCAD\<release>\<product>\Profiles key."
    }
    $rec = $State.profilesDefault
    if (([string]$rec) -match '^(?i)Forge-') {
        throw "Refusing to restore '$keyStr' (Default) to '$rec' - the recorded profilesDefault itself starts with 'Forge-'."
    }
    # Read-only open first; the key is opened writable only when a write will
    # actually happen, so a -DryRun or a no-op never holds a writable handle.
    $pk = Open-RegKey $regPath
    if (-not $pk) {
        Write-Warning "Cannot open $keyStr - cannot check/restore Profiles (Default)."
        return
    }
    try { $cur = $pk.GetValue('') } finally { $pk.Dispose() }
    if (([string]$cur) -ceq ([string]$rec)) {
        Write-Output "Profiles (Default) is already '$rec' - nothing to restore."
        return
    }
    if ([string]$cur -and (([string]$cur) -notmatch '^(?i)Forge-')) {
        Write-Output ("Profiles (Default) left as-is (now '{0}', recorded '{1}')" -f $cur, $rec)
        return
    }
    if ($DryRun) {
        Write-Output ("[WOULD-RESTORE] {0} (Default): '{1}' -> '{2}'" -f $keyStr, $cur, $rec)
        return
    }
    $pkW = Open-RegKey $regPath -Writable
    if (-not $pkW) {
        Write-Warning "Cannot open $keyStr for writing - cannot restore Profiles (Default)."
        return
    }
    try {
        $cur = $pkW.GetValue('')   # re-read inside the writable handle
        if (([string]$cur) -ceq ([string]$rec)) {
            Write-Output "Profiles (Default) is already '$rec' - nothing to restore."
        } elseif ([string]$cur -and (([string]$cur) -notmatch '^(?i)Forge-')) {
            Write-Output ("Profiles (Default) left as-is (now '{0}', recorded '{1}')" -f $cur, $rec)
        } else {
            if ($null -eq $rec) { $pkW.DeleteValue('', $false) }
            else { $pkW.SetValue('', [string]$rec, [Microsoft.Win32.RegistryValueKind]::String) }
            Write-Output ("[RESTORED] {0} (Default): '{1}' -> '{2}'" -f $keyStr, $cur, $rec)
        }
    } finally { $pkW.Dispose() }
}

# ---------------------------------------------------------------- validate

if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') {
    throw "Invalid instance name '$Name'. Must match ^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$."
}

$instanceDir = Join-Path $InstanceRoot $Name
$statePath   = Join-Path $instanceDir 'instance.json'

if (-not (Test-Path -LiteralPath $statePath)) {
    throw "No state file at $statePath. This script only stops instances started by launch-acad-instance.ps1; nothing stopped."
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json

# -RestoreOnly: the user closed the window themselves - just fix the pointer.
if ($RestoreOnly) {
    if ($null -ne $state.pid -and (Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue)) {
        Write-Warning "Recorded pid $($state.pid) is still running - -RestoreOnly only restores the current-profile pointer; it stops nothing."
    }
    Restore-ProfilesDefault -State $state -DryRun:$DryRun
    if ($DryRun) { Write-Output "DRY RUN - nothing changed." }
    return
}

foreach ($field in 'pid', 'acadPath') {
    if ($null -eq $state.$field) { throw "State file $statePath is missing '$field' - refusing to stop anything." }
}
if ($null -eq $state.processStartTimeUtcFileTime -and -not $state.processStartTimeUtc) {
    throw "State file $statePath has no process start time - refusing to stop anything."
}
if ($state.name -and $state.name -ne $Name) {
    Write-Warning "State file name '$($state.name)' differs from -Name '$Name'; continuing with the state file's own pid."
}

$pid_ = [int]$state.pid
$proc = Get-Process -Id $pid_ -ErrorAction SilentlyContinue
if (-not $proc) {
    throw "Refusing: no running process has the recorded pid $pid_ (stale state file - the instance is already gone; re-run with -RestoreOnly to restore the profile pointer)."
}

$recordedFt = $null
if ($null -ne $state.processStartTimeUtcFileTime) {
    $recordedFt = [decimal]$state.processStartTimeUtcFileTime
} else {
    $recordedFt = [decimal]([datetimeoffset]::Parse([string]$state.processStartTimeUtc)).UtcDateTime.ToFileTimeUtc()
}
$actualFt = $null
try { $actualFt = [decimal]$proc.StartTime.ToFileTimeUtc() } catch { $actualFt = $null }
if ($null -eq $actualFt -or $actualFt -ne $recordedFt) {
    throw "Refusing: pid $pid_ exists but its start time does not match the recorded one (pid was probably reused by another process)."
}

$procPath = $null
try { $procPath = $proc.Path } catch { $procPath = $null }
if (-not $procPath) {
    throw "Refusing: cannot read the path of pid $pid_ - cannot prove it is the recorded acad.exe."
}
if ($procPath -ine [string]$state.acadPath) {
    throw "Refusing: pid $pid_ runs '$procPath' but the state file recorded '$($state.acadPath)'."
}

# The launcher always starts acad.exe with /p "Forge-<Name>"; a process without
# that switch was never started by it. Fails closed when unreadable.
$expectedProfile = "Forge-$Name"
$cmdLine = $null
try {
    $cmdLine = (Get-CimInstance Win32_Process -Filter "ProcessId=$pid_" -ErrorAction Stop).CommandLine
} catch { $cmdLine = $null }
if (-not $cmdLine) {
    throw "Refusing: cannot read the command line of pid $pid_ - cannot prove it was started with /p `"$expectedProfile`"."
}
if ($cmdLine -notmatch ('/p\s+"?' + [regex]::Escape($expectedProfile) + '"?(\s|$)')) {
    throw "Refusing: the command line of pid $pid_ does not contain /p `"$expectedProfile`" (got: $cmdLine) - it was not started by the launcher."
}

$msg = "pid $pid_ ($procPath, started $($proc.StartTime.ToUniversalTime().ToString('o')), pipe $($state.pipe), profile $($state.profile))"
if ($DryRun) {
    Write-Output "[WOULD-STOP] $msg"
    Restore-ProfilesDefault -State $state -DryRun
    Write-Output "DRY RUN - nothing stopped or changed."
    return
}

Write-Output "Stopping $msg"
Stop-Process -Id $pid_ -Force
$deadline = (Get-Date).AddSeconds($WaitSeconds)
$stopped = $false
while ((Get-Date) -lt $deadline) {
    if (-not (Get-Process -Id $pid_ -ErrorAction SilentlyContinue)) { $stopped = $true; break }
    Start-Sleep -Milliseconds 250
}
if (-not $stopped) {
    Write-Warning "Stop-Process was sent but pid $pid_ is still running after ${WaitSeconds}s - skipping the profile-pointer restore."
    return
}
Write-Output "Stopped: pid $pid_ has exited."
# A forced stop prevents the instance's own exit-time writes; if anything still
# rewrote the current-profile pointer, put the recorded value back.
Restore-ProfilesDefault -State $state
