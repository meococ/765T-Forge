<#
.SYNOPSIS
  Launch an additional AutoCAD instance bound to its own 765T-Forge named pipe.

.DESCRIPTION
  One pipe per AutoCAD instance: this script starts acad.exe on a dedicated
  AutoCAD profile "Forge-<Name>" with FORGE_PIPE_NAME=765T.Forge.AutoCAD.<Name>
  so a second Forge.Server MCP entry can drive it independently.

  Profile isolation (the load-bearing part): AutoCAD rewrites LogFilePath and
  other per-session values in the CURRENT profile when it exits. A second
  instance sharing the user's profile would overwrite their settings. So the
  first run COPIES the current profile registry key to a sibling "Forge-<Name>"
  key and re-points its per-session write paths (LogFilePath, SaveFilePath,
  XrefLoadPath, PlotLogPath, ProfileStorage, PlotSpoolerDirectory, plus
  XCOMPAREBAKPATH / ACTIVITYINSIGHTSPATH when present and any other
  *log*/temp/save/spool path values found) into
  %LOCALAPPDATA%\765T-Forge\instances\<Name>\profile\. The user's own profile
  key is never written. A completed copy is stamped ForgeManaged=1; only a key
  carrying that marker is reused, and a partial copy left by a failed run is
  deleted and recreated.

  Current-profile pointer guard: the (Default) value of ...\Profiles is the
  profile every normal acad.exe start opens. AutoCAD may rewrite it to the /p
  profile at startup (observed on R19.1), which would hijack the user's next
  normal launch onto Forge-<Name>. This script records the pointer into
  instance.json (profilesDefault) before starting, refuses to run when it is
  already a Forge-* leftover (declare the real profile with
  -RestoreProfilesDefault <name>), restores it during a short post-start watch
  when it reads a Forge-* profile or is empty (a real, different profile name
  is left as-is - it means the user switched it deliberately), and
  stop-acad-instance.ps1 applies the same rule after the instance exits.

  Environment: FORGE_PIPE_NAME / FORGE_AUDIT_DIR / FORGE_BACKUP_DIR are set only
  in this script's process right before Start-Process and restored afterwards,
  so only the child acad.exe inherits them. FORGE_AUTOCAD_TOKEN is inherited
  unchanged. No User/Machine environment writes ever happen.

  State: %LOCALAPPDATA%\765T-Forge\instances\<Name>\instance.json records the
  pipe, profile, pid, process start time (UTC), command line, acad path, the
  Profiles key path and the recorded pointer. It is written best-effort right
  after Start-Process and upgraded once the process start time is confirmed,
  so a post-start failure never orphans a running acad.exe silently.
  stop-acad-instance.ps1 refuses to stop anything that does not match this
  file. A named mutex (Local\765T-Forge-Launch-<Name>) makes concurrent
  launches for the same name fail instead of racing the checks.

  The printed MCP server entry points at deploy\server\Forge.Server.exe
  under the repo root so the snippet matches the live entry;
  -ServerExe <path> overrides it. The deploy folder is produced by
  scripts\deploy-server.ps1 (see "Deploying the server" in
  docs\parallel-acad-instances.md).

  -DryRun performs every check and prints the full plan
  ([WOULD-COPY] / [WOULD-RECREATE] / [WOULD-SET] / [WOULD-RESTORE] / [WOULD-ENV]
  / [WOULD-START] / [WOULD-WRITE-FILE]) and changes nothing: no registry
  writes, no folders, no process.

  Test hooks: -RegistryFixture <json> replaces ALL live registry reads (release,
  product key(s), current profile, pointer, profile value tree, acad install
  dir) with the contents of a JSON file, and -InstanceRoot / -AcadExe redirect
  the state folder and the executable. -RegistryRoot / -HklmRegistryRoot
  redirect the registry roots. All of these are only supported together with
  -DryRun. Fixture shape:
    {
      "release": "R25.0", "productKey": "ACAD-8101:409",
      "productKeys": ["ACAD-8101:409"],            (optional, default [productKey])
      "currentProfile": "<<Unnamed Profile>>",
      "profilesDefault": "<<Unnamed Profile>>",    (optional, default = currentProfile)
      "existingProfiles": ["<<Unnamed Profile>>"],
      "forgeManagedProfiles": ["Forge-old"],       (optional: keys stamped ForgeManaged=1)
      "acadInstallDir": "D:\\FakeAutoCAD",
      "values": {
        "Profiles\\<<Unnamed Profile>>\\Editor Configuration": { "LogFilePath": "C:\\..." },
        "Profiles\\<<Unnamed Profile>>\\General": { "PlotLogPath": "%UserProfile%\\...", "ProfileStorage": "...\\profile.aws" }
      }
    }
  "values" keys are registry paths relative to the product key; a value entry is
  either a scalar (string -> ExpandString when it contains '%' else String;
  number -> DWord) or { "data": ..., "kind": "ExpandString" }.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Name,

    [string] $AcadExe,

    [string] $ServerExe,

    [string] $Drawing,

    [string] $ProductKey,

    [string] $RestoreProfilesDefault,

    [switch] $DryRun,

    [string] $RegistryRoot = 'HKCU:\Software\Autodesk\AutoCAD',

    [string] $HklmRegistryRoot = 'HKLM:\SOFTWARE\Autodesk\AutoCAD',

    [string] $RegistryFixture,

    [string] $InstanceRoot = (Join-Path $env:LOCALAPPDATA '765T-Forge\instances'),

    [switch] $SkipPipeCheck
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

function Get-RegTree {
    param([Parameter(Mandatory)][Microsoft.Win32.RegistryKey] $Key)
    $tree = @{ values = @{}; subkeys = @{} }
    foreach ($vn in $Key.GetValueNames()) {
        $tree.values[$vn] = @{
            kind = $Key.GetValueKind($vn).ToString()
            data = $Key.GetValue($vn, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
    }
    foreach ($sn in $Key.GetSubKeyNames()) {
        $sub = $Key.OpenSubKey($sn)
        if ($sub) {
            $tree.subkeys[$sn] = Get-RegTree $sub
            $sub.Dispose()
        }
    }
    return $tree
}

function Copy-RegTree {
    param([Parameter(Mandatory)] $SrcTree, [Parameter(Mandatory)][Microsoft.Win32.RegistryKey] $DestKey)
    foreach ($vn in $SrcTree.values.Keys) {
        $v = $SrcTree.values[$vn]
        $DestKey.SetValue($vn, $v.data, [Microsoft.Win32.RegistryValueKind]$v.kind)
    }
    foreach ($sn in $SrcTree.subkeys.Keys) {
        $child = $DestKey.CreateSubKey($sn)
        try { Copy-RegTree $SrcTree.subkeys[$sn] $child } finally { $child.Dispose() }
    }
}

function Write-RegValue {
    param([Parameter(Mandatory)][Microsoft.Win32.RegistryKey] $RootKey,
          [Parameter(Mandatory)][string] $RelSubKey,
          [Parameter(Mandatory)][string] $ValueName,
          [Parameter(Mandatory)] $Data,
          [Parameter(Mandatory)][string] $Kind)
    $k = if ($RelSubKey -eq '') { $RootKey } else { $RootKey.CreateSubKey($RelSubKey) }
    try { $k.SetValue($ValueName, $Data, [Microsoft.Win32.RegistryValueKind]$Kind) }
    finally { if ($RelSubKey -ne '') { $k.Dispose() } }
}

function ConvertTo-FixtureValue {
    param($Raw)
    if ($Raw -is [string]) {
        return @{ kind = $(if ($Raw -match '%') { 'ExpandString' } else { 'String' }); data = $Raw }
    }
    if ($Raw -is [int] -or $Raw -is [long]) {
        return @{ kind = 'DWord'; data = [int]$Raw }
    }
    if ($null -ne $Raw -and $null -ne $Raw.data) {
        $kind = if ($Raw.kind) { [string]$Raw.kind } else { 'String' }
        return @{ kind = $kind; data = $Raw.data }
    }
    return @{ kind = 'String'; data = [string]$Raw }
}

# Builds the same @{values=;subkeys=} tree shape as Get-RegTree from fixture
# "values" entries whose relative path starts with "Profiles\<currentProfile>".
function Get-FixtureProfileTree {
    param($Fixture, [string] $ProfileName)
    $tree = @{ values = @{}; subkeys = @{} }
    $prefix = "Profiles\$ProfileName"
    if (-not $Fixture.values) { return $tree }
    foreach ($prop in $Fixture.values.PSObject.Properties) {
        $rel = $prop.Name.TrimEnd('\')
        if ($rel -ne $prefix -and -not $rel.StartsWith("$prefix\")) { continue }
        $inside = $rel.Substring($prefix.Length).TrimStart('\')
        $node = $tree
        if ($inside -ne '') {
            foreach ($seg in ($inside -split '\\')) {
                if (-not $node.subkeys.ContainsKey($seg)) { $node.subkeys[$seg] = @{ values = @{}; subkeys = @{} } }
                $node = $node.subkeys[$seg]
            }
        }
        foreach ($vp in $prop.Value.PSObject.Properties) {
            $node.values[$vp.Name] = ConvertTo-FixtureValue $vp.Value
        }
    }
    return $tree
}

function Test-PathLike {
    param($Data)
    return ($Data -is [string] -and $Data -match '^([a-zA-Z]:\\|%|\\\\)')
}

# Enumerates (relSubKey, valueName, value) triples across a profile tree.
function Get-TreeValues {
    param($Node, [string] $Rel = '')
    foreach ($vn in $Node.values.Keys) {
        [pscustomobject]@{ RelKey = $Rel; Name = $vn; Kind = $Node.values[$vn].kind; Data = $Node.values[$vn].data }
    }
    foreach ($sn in $Node.subkeys.Keys) {
        $childRel = if ($Rel -eq '') { $sn } else { "$Rel\$sn" }
        Get-TreeValues $Node.subkeys[$sn] $childRel
    }
}

function Get-RecordedStartFileTime {
    param($State)
    if ($null -eq $State) { return $null }
    $raw = $State.processStartTimeUtcFileTime
    if ($null -ne $raw) { return [decimal]$raw }
    if ($State.processStartTimeUtc) {
        return [decimal]([datetimeoffset]::Parse([string]$State.processStartTimeUtc)).UtcDateTime.ToFileTimeUtc()
    }
    return $null
}

function Test-RecordedProcessAlive {
    param($State)
    if ($null -eq $State -or $null -eq $State.pid) { return $false }
    $proc = Get-Process -Id ([int]$State.pid) -ErrorAction SilentlyContinue
    if (-not $proc) { return $false }
    $ft = $null
    try { $ft = [decimal]$proc.StartTime.ToFileTimeUtc() } catch { return $false }
    $rec = Get-RecordedStartFileTime $State
    if ($null -eq $rec -or $ft -ne $rec) { return $false }
    $procPath = $null
    try { $procPath = $proc.Path } catch { $procPath = $null }
    if (-not $procPath -or -not $State.acadPath) { return $false }
    return ($procPath -ieq [string]$State.acadPath)
}

function Get-LivePipeNames {
    [System.IO.Directory]::GetFiles('\\.\pipe\') | ForEach-Object { $_.Substring($_.LastIndexOf('\') + 1) }
}

function Format-ProfilePointer {
    param($Value)
    if ($null -eq $Value) { return '<not set>' }
    return "'$Value'"
}

# The (Default) value of ...\Profiles is the profile every normal acad.exe
# start opens. If it already reads Forge-*, an earlier run (or AutoCAD itself)
# left the pointer hijacked; without an explicit -RestoreProfilesDefault this
# script refuses rather than recording a Forge profile as "the user's own".
function Assert-ProfilePointerUsable {
    param($Pointer, [switch] $HasOverride)
    if ([string]$Pointer -match '^(?i)Forge-' -and -not $HasOverride) {
        throw "Profiles (Default) is '$Pointer' - a leftover Forge profile pointer. A normal AutoCAD start would now open that Forge profile, not yours. Set ...\Profiles (Default) back to your real profile name first, or re-run with -RestoreProfilesDefault '<your real profile name>' so this launch records and restores the correct value."
    }
}

# ---------------------------------------------------------------- validate

if ($Name -notmatch '^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$') {
    throw "Invalid instance name '$Name'. Must match ^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$ (1-32 chars, starts alphanumeric)."
}

# Like -RegistryFixture, these redirect real registry reads AND writes, so they
# are gated to -DryRun the same way.
if (-not $DryRun -and ($PSBoundParameters.ContainsKey('RegistryRoot') -or $PSBoundParameters.ContainsKey('HklmRegistryRoot'))) {
    throw "-RegistryRoot and -HklmRegistryRoot redirect real registry reads and writes, so they are only supported together with -DryRun."
}

if ($PSBoundParameters.ContainsKey('RestoreProfilesDefault') -and -not $RestoreProfilesDefault.Trim()) {
    throw "-RestoreProfilesDefault cannot be empty - pass the name of your real AutoCAD profile (e.g. '<<Unnamed Profile>>')."
}
if ($RestoreProfilesDefault -match '^(?i)Forge-') {
    throw "-RestoreProfilesDefault declares YOUR real profile name; it must not start with 'Forge-' (that is exactly the leftover this switch exists to fix)."
}

$pipeName    = "765T.Forge.AutoCAD.$Name"
$profileName = "Forge-$Name"
$instanceDir = Join-Path $InstanceRoot $Name
$profileDir  = Join-Path $instanceDir 'profile'
$auditDir    = Join-Path $instanceDir 'audit'
$backupDir   = Join-Path $instanceDir 'backups'
$statePath   = Join-Path $instanceDir 'instance.json'

if ($Drawing) {
    if ($Drawing.TrimEnd().EndsWith('\')) {
        throw "-Drawing must not end with a backslash ('$Drawing') - a trailing \ escapes the closing quote on the acad.exe command line."
    }
    if (-not (Test-Path -LiteralPath $Drawing)) {
        throw "Drawing not found: $Drawing"
    }
    # acad.exe resolves relative paths against ITS working directory, not ours.
    $Drawing = (Resolve-Path -LiteralPath $Drawing).Path
}

# ---------------------------------------------------------------- interlock
# One launch per name at a time: the pipe check, state check and state write
# below are not atomic on their own. A named mutex auto-releases if this
# process dies, so no stale-lock file can block later launches.

$launchMutex = New-Object System.Threading.Mutex($false, "Local\765T-Forge-Launch-$Name")
$lockHeld = $false
try {
    try { $lockHeld = $launchMutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $lockHeld = $true }
    if (-not $lockHeld) {
        throw "Another launch-acad-instance.ps1 run for instance '$Name' is still in progress; wait for it to finish (or stop it) and retry."
    }

    # ------------------------------------------------------------ refusal checks (also in DryRun)

    $existingPipes = @()
    if (-not $SkipPipeCheck) {
        try { $existingPipes = @(Get-LivePipeNames) }
        catch { throw "Cannot enumerate \\.\pipe\ ($($_.Exception.Message)). Pass -SkipPipeCheck to override." }
        if ($existingPipes -icontains $pipeName) {
            throw "Pipe '$pipeName' already exists (a live AutoCAD is listening or a stale pipe remains). Choose another -Name or stop that instance."
        }
    }

    $existingState = $null
    if (Test-Path -LiteralPath $statePath) {
        try { $existingState = (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json) } catch { $existingState = $null }
        if (Test-RecordedProcessAlive $existingState) {
            throw "Instance '$Name' is already running (pid $($existingState.pid), start $($existingState.processStartTimeUtc)). Refusing to start a duplicate."
        }
        Write-Warning "Stale state file at $statePath (recorded process is gone); it will be overwritten."
    }

    # ------------------------------------------------------------ registry discovery

    $pointerNow       = $null   # what ...\Profiles (Default) reads right now
    $targetExists     = $false
    $targetManaged    = $false  # exists AND carries our ForgeManaged marker
    $acadInstallDir   = $null

    if ($RegistryFixture) {
        if (-not $DryRun) { throw "-RegistryFixture replaces live registry reads, so it is only supported together with -DryRun." }
        if (-not (Test-Path -LiteralPath $RegistryFixture)) { throw "Registry fixture not found: $RegistryFixture" }
        $fx = Get-Content -LiteralPath $RegistryFixture -Raw | ConvertFrom-Json
        $release        = [string]$fx.release
        $productKeySel  = [string]$fx.productKey
        $currentProfile = [string]$fx.currentProfile
        if (-not $release -or -not $productKeySel -or -not $currentProfile) {
            throw "Fixture must provide release, productKey and currentProfile."
        }
        $productKeys = @($fx.productKeys | Where-Object { $_ })
        if (-not $productKeys.Count) { $productKeys = @($productKeySel) }
        if ($ProductKey) {
            if ($productKeys -inotcontains $ProductKey) {
                throw "-ProductKey '$ProductKey' is not one of the fixture's productKeys ($($productKeys -join ', '))."
            }
            $productKeySel = $ProductKey
        } elseif ($productKeys.Count -gt 1) {
            throw "Fixture lists multiple ACAD-* product keys ($($productKeys -join ', ')); pass -ProductKey to choose one."
        }
        if ($fx.PSObject.Properties['profilesDefault']) { $pointerNow = $fx.profilesDefault } else { $pointerNow = $currentProfile }
        Assert-ProfilePointerUsable $pointerNow -HasOverride:([bool]$RestoreProfilesDefault)
        if ($RestoreProfilesDefault) { $currentProfile = $RestoreProfilesDefault }
        $existingProfiles     = @($fx.existingProfiles | Where-Object { $_ })
        $forgeManagedProfiles = @($fx.forgeManagedProfiles | Where-Object { $_ })
        $profileTree  = Get-FixtureProfileTree $fx $currentProfile
        $targetExists = ($existingProfiles -icontains $profileName)
        $targetManaged = ($targetExists -and ($forgeManagedProfiles -icontains $profileName))
        $acadInstallDir = [string]$fx.acadInstallDir
    } else {
        $rootPath = Resolve-RegistryPath $RegistryRoot
        $rootKey = Open-RegKey $rootPath
        if (-not $rootKey) { throw "AutoCAD registry root not found: $RegistryRoot" }
        try {
            $release = $rootKey.GetValue('CurVer')
            if (-not $release) {
                $release = $rootKey.GetSubKeyNames() | Where-Object { $_ -match '^R\d+\.\d+$' } | Sort-Object { [version]($_.TrimStart('R')) } | Select-Object -Last 1
            }
            if (-not $release) { throw "No AutoCAD release (Rxx.y) under $RegistryRoot" }
            $relKey = $rootKey.OpenSubKey($release)
            if (-not $relKey) { throw "Release key '$release' not found under $RegistryRoot" }
            try {
                $productKeys = @($relKey.GetSubKeyNames() | Where-Object { $_ -match '^ACAD-' } | Sort-Object)
                if (-not $productKeys.Count) { throw "No ACAD-* product key under $RegistryRoot\$release" }
                if ($ProductKey) {
                    if ($productKeys -inotcontains $ProductKey) {
                        throw "-ProductKey '$ProductKey' not found under $RegistryRoot\$release (found: $($productKeys -join ', '))."
                    }
                    $productKeySel = $ProductKey
                } elseif ($productKeys.Count -gt 1) {
                    throw "Multiple ACAD-* product keys under $RegistryRoot\$release ($($productKeys -join ', ')); pass -ProductKey to choose one."
                } else {
                    $productKeySel = $productKeys[0]
                }
                $prodKey = $relKey.OpenSubKey($productKeySel)
            } finally { $relKey.Dispose() }
            try {
                $profilesKey = $prodKey.OpenSubKey('Profiles')
                if (-not $profilesKey) { throw "No 'Profiles' key under $RegistryRoot\$release\$productKeySel" }
                try {
                    $pointerNow = $profilesKey.GetValue('')
                    Assert-ProfilePointerUsable $pointerNow -HasOverride:([bool]$RestoreProfilesDefault)
                    $currentProfile = [string]$pointerNow
                    if (-not $currentProfile) { $currentProfile = $profilesKey.GetValue('CurrentProfile') }
                    if ($RestoreProfilesDefault) { $currentProfile = $RestoreProfilesDefault }
                    if (-not $currentProfile) { throw "Cannot determine current AutoCAD profile (default value of ...\Profiles is empty)" }
                    $srcProfileKey = $profilesKey.OpenSubKey([string]$currentProfile)
                    if (-not $srcProfileKey) { throw "Current profile key '$currentProfile' not found under ...\Profiles" }
                    try { $profileTree = Get-RegTree $srcProfileKey } finally { $srcProfileKey.Dispose() }
                    $targetExists = ($profilesKey.GetSubKeyNames() -icontains $profileName)
                    if ($targetExists) {
                        $tk = $profilesKey.OpenSubKey($profileName)
                        if ($tk) {
                            try { $targetManaged = ($null -ne $tk.GetValue('ForgeManaged')) } finally { $tk.Dispose() }
                        }
                    }
                } finally { $profilesKey.Dispose() }
            } finally { $prodKey.Dispose() }
        } finally { $rootKey.Dispose() }

        # acad install dir from HKLM product key (AcadLocation, fallback Location)
        try {
            $hklmProdPath = Resolve-RegistryPath ($HklmRegistryRoot.TrimEnd('\') + "\$release\$productKeySel")
            $hklmProd = Open-RegKey $hklmProdPath
            if ($hklmProd) {
                try { $acadInstallDir = $hklmProd.GetValue('AcadLocation'); if (-not $acadInstallDir) { $acadInstallDir = $hklmProd.GetValue('Location') } }
                finally { $hklmProd.Dispose() }
            }
        } catch { $acadInstallDir = $null }
    }

    # The pointer recorded into instance.json and restored after start/stop:
    # the declared -RestoreProfilesDefault wins over whatever the key reads now.
    $profilesDefault = if ($RestoreProfilesDefault) { $RestoreProfilesDefault } else { $pointerNow }

    $rootDisplay      = ($RegistryRoot.TrimEnd('\').Replace('/', '\'))
    $profilesKeyPath  = "$rootDisplay\$release\$productKeySel\Profiles"
    $sourceProfileKey = "$profilesKeyPath\$currentProfile"
    $targetProfileKey = "$profilesKeyPath\$profileName"

    # ------------------------------------------------------------ acad.exe resolution

    $acadExeResolved = $null
    $acadExeSource   = $null
    if ($AcadExe) {
        if (-not (Test-Path -LiteralPath $AcadExe)) { throw "-AcadExe not found: $AcadExe" }
        $acadExeResolved = (Resolve-Path -LiteralPath $AcadExe).Path; $acadExeSource = '-AcadExe'
    } elseif ($env:AUTOCAD_2026_ROOT -and (Test-Path (Join-Path $env:AUTOCAD_2026_ROOT 'acad.exe'))) {
        $acadExeResolved = (Join-Path $env:AUTOCAD_2026_ROOT 'acad.exe'); $acadExeSource = 'AUTOCAD_2026_ROOT'
    } elseif ($acadInstallDir -and (Test-Path (Join-Path $acadInstallDir 'acad.exe'))) {
        $acadExeResolved = (Join-Path $acadInstallDir 'acad.exe'); $acadExeSource = 'registry AcadLocation'
    } else {
        throw "acad.exe not found. Pass -AcadExe, set AUTOCAD_2026_ROOT, or install AutoCAD (registry AcadLocation lookup failed)."
    }

    # ------------------------------------------------------------ retarget plan (values under the COPIED profile only)

    $retargets = New-Object System.Collections.Generic.List[object]
    $covered = @{}
    function Add-Retarget([string]$relKey, [string]$valueName, [string]$newData, [string]$kind) {
        $covered["$relKey|$valueName"] = $true
        $retargets.Add([pscustomobject]@{ RelKey = $relKey; Name = $valueName; NewData = $newData; Kind = $kind })
    }
    function Get-SourceValue([string]$relKey, [string]$valueName) {
        $node = $profileTree
        if ($relKey -ne '') {
            foreach ($seg in ($relKey -split '\\')) {
                if (-not $node.subkeys.ContainsKey($seg)) { return $null }
                $node = $node.subkeys[$seg]
            }
        }
        if ($node.values.ContainsKey($valueName)) { return $node.values[$valueName] }
        return $null
    }

    # Known per-session write paths. Created on the copy even when absent
    # upstream, so an AutoCAD default can never land back in the user's folders.
    $known = @(
        @{ RelKey = 'Editor Configuration';  Name = 'LogFilePath';          NewData = "$profileDir\";            DefaultKind = 'String' },
        @{ RelKey = 'Editor Configuration';  Name = 'SaveFilePath';         NewData = "$profileDir\";            DefaultKind = 'ExpandString' },
        @{ RelKey = 'Editor Configuration';  Name = 'XrefLoadPath';         NewData = "$profileDir\";            DefaultKind = 'ExpandString' },
        @{ RelKey = 'General';               Name = 'PlotLogPath';          NewData = "$profileDir\";            DefaultKind = 'ExpandString' },
        @{ RelKey = 'General';               Name = 'ProfileStorage';       NewData = "$profileDir\profile.aws"; DefaultKind = 'ExpandString' },
        @{ RelKey = 'General Configuration'; Name = 'PlotSpoolerDirectory'; NewData = "$profileDir\";            DefaultKind = 'ExpandString' }
    )
    foreach ($k in $known) {
        $src = Get-SourceValue $k.RelKey $k.Name
        $kind = if ($src -and $src.kind -match 'String') { $src.kind } else { $k.DefaultKind }
        Add-Retarget $k.RelKey $k.Name $k.NewData $kind
    }

    # Present-only write paths: retargeted when the source profile has them,
    # but never created - an absent one means the feature is not in use.
    foreach ($k in @(@{ RelKey = 'Variables'; Name = 'XCOMPAREBAKPATH' },
                    @{ RelKey = 'Variables'; Name = 'ACTIVITYINSIGHTSPATH' })) {
        $src = Get-SourceValue $k.RelKey $k.Name
        if ($src -and $src.kind -match 'String' -and (Test-PathLike $src.data)) {
            Add-Retarget $k.RelKey $k.Name "$profileDir\" $src.kind
        }
    }

    # Any other string value under the profile whose name looks like a
    # log/temp/save/spool path and whose data looks like a filesystem path is
    # also per-session: re-point it, preserving the original kind and leaf name.
    foreach ($v in (Get-TreeValues $profileTree)) {
        if ($covered.ContainsKey("$($v.RelKey)|$($v.Name)")) { continue }
        if ($v.Kind -notmatch 'String') { continue }
        if ($v.Name -notmatch '(?i)(log.*path|logfile|profilestorage|temppath|tempdir|scratchpath|savefilepath|loadpath|spooler|bakpath|insightspath)') { continue }
        if (-not (Test-PathLike $v.Data)) { continue }
        $leaf = ([string]$v.Data).TrimEnd('\').Split('\')[-1]
        if (-not $leaf) { $leaf = $v.Name }
        Add-Retarget $v.RelKey $v.Name "$profileDir\$leaf" $v.Kind
    }

    # ------------------------------------------------------------ launch plan

    $argList = "/nologo /p `"$profileName`""
    if ($Drawing) { $argList += " `"$Drawing`"" }
    $commandLine = "`"$acadExeResolved`" $argList"

    $envMap = [ordered]@{
        FORGE_PIPE_NAME  = $pipeName
        FORGE_AUDIT_DIR  = $auditDir
        FORGE_BACKUP_DIR = $backupDir
    }

    $repoRoot  = Split-Path -Parent $PSScriptRoot
    # The live MCP entry runs the deployed server under deploy\server, so
    # the snippet below must point there. The deploy folder is produced by
    # scripts\deploy-server.ps1 (see "Deploying the server" in
    # docs\parallel-acad-instances.md). -ServerExe overrides this default.
    $serverExePath = Join-Path $repoRoot 'deploy\server\Forge.Server.exe'
    if ($ServerExe) { $serverExePath = $ServerExe }
    $serverExeNote = if (Test-Path -LiteralPath $serverExePath) { $null } else { '  (not found on this machine - run scripts\deploy-server.ps1 first or pass -ServerExe)' }

    $mcpEntry = [ordered]@{
        command = $serverExePath
        args    = @()
        env     = [ordered]@{
            FORGE_AUTOCAD_TOKEN = '<same as the existing 765t-forge entry>'
            FORGE_PIPE_NAME     = $pipeName
            FORGE_BACKUP_DIR    = $backupDir
            FORGE_AUDIT_DIR     = $auditDir
            FORGE_ENABLE_UNSAFE_OPS = 'false'
        }
    }

    $tokenSet = [Environment]::GetEnvironmentVariable('FORGE_AUTOCAD_TOKEN') -or
                [Environment]::GetEnvironmentVariable('MCP_AUTOCAD_TOKEN') -or
                ([Environment]::GetEnvironmentVariable('FORGE_DEV_ALLOW_DEFAULT_TOKEN') -eq 'true')

    $tag = 'WOULD'   # the printed plan is always a plan; the real run then executes it
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("=== Forge instance launch $(if ($DryRun) {'plan (DRY RUN - nothing changed)'} else {'plan'} ) ===")
    $lines.Add("Name          : $Name")
    $lines.Add("Pipe          : $pipeName")
    $lines.Add("Profile       : $profileName")
    $lines.Add("Instance dir  : $instanceDir")
    $lines.Add("State file    : $statePath")
    $lines.Add("")
    $lines.Add("Registry ($rootDisplay, release $release, product $productKeySel):")
    $lines.Add("  Source profile key : $sourceProfileKey   (read only)")
    $lines.Add("  Target profile key : $targetProfileKey")
    if ($targetManaged) {
        $lines.Add("[SKIP-COPY]    target profile key already exists (ForgeManaged marker present) - reusing it")
    } else {
        if ($targetExists) {
            $lines.Add("[$tag-RECREATE] $targetProfileKey   (exists without ForgeManaged marker - replaced by a fresh copy)")
        }
        $lines.Add("[$tag-COPY]    $sourceProfileKey")
        $lines.Add("          -> $targetProfileKey   (recursive copy)")
    }
    foreach ($r in $retargets) {
        $lines.Add("[$tag-SET]     $targetProfileKey\$($r.RelKey) :: $($r.Name) = $($r.NewData)  [$($r.Kind)]")
    }
    $lines.Add("[$tag-SET]     $targetProfileKey :: ForgeManaged = 1  [DWord]  (marks a launcher-made copy)")
    $lines.Add("")
    $lines.Add("Current-profile pointer ($profilesKeyPath):")
    $lines.Add("  (Default) now : $(Format-ProfilePointer $pointerNow)")
    $lines.Add("  recorded      : $(Format-ProfilePointer $profilesDefault)   -> instance.json 'profilesDefault'")
    if (([string]$pointerNow) -cne ([string]$profilesDefault)) {
        if ((-not [string]$pointerNow) -or (([string]$pointerNow) -match '^(?i)Forge-')) {
            $lines.Add("[$tag-RESTORE] $profilesKeyPath (Default) : $(Format-ProfilePointer $pointerNow) -> $(Format-ProfilePointer $profilesDefault)")
        } else {
            $lines.Add("             (Default) left as-is (now '$pointerNow', recorded '$profilesDefault') - a real profile name is never overwritten")
        }
    }
    $lines.Add("[$tag-RESTORE] watch after start: if AutoCAD rewrites (Default) to a Forge-* pointer, write it back to $(Format-ProfilePointer $profilesDefault); a real profile name is left as-is (stop-acad-instance.ps1 applies the same rule after exit)")
    $lines.Add("")
    $lines.Add("Process:")
    $lines.Add("[$tag-ENV]     FORGE_PIPE_NAME=$pipeName")
    $lines.Add("[$tag-ENV]     FORGE_AUDIT_DIR=$auditDir")
    $lines.Add("[$tag-ENV]     FORGE_BACKUP_DIR=$backupDir")
    $lines.Add("             (FORGE_AUTOCAD_TOKEN inherited unchanged$(if (-not $tokenSet) {' - WARNING: not set in this shell, child auth will fail unless FORGE_DEV_ALLOW_DEFAULT_TOKEN=true'}))")
    $lines.Add("[$tag-START]   $commandLine   (via $acadExeSource)")
    $lines.Add("")
    $lines.Add("State:")
    $lines.Add("[$tag-WRITE-FILE] $statePath")
    $lines.Add("")
    $lines.Add("MCP server entry (merge into your mcpServers):")
    $mcpJson = '{ "765t-forge-' + $Name + '": ' + ($mcpEntry | ConvertTo-Json -Depth 6 -Compress) + ' }'
    $lines.Add($mcpJson.Replace('\u003c', '<').Replace('\u003e', '>'))
    if ($serverExeNote) { $lines.Add($serverExeNote) }

    # ------------------------------------------------------------ env push/pop (also exercised in DryRun so restore is testable)

    $savedEnv = @{}
    foreach ($k in $envMap.Keys) { $savedEnv[$k] = [Environment]::GetEnvironmentVariable($k) }
    try {
        foreach ($k in $envMap.Keys) { Set-Item -Path "Env:$k" -Value $envMap[$k] }

        $lines | ForEach-Object { Write-Output $_ }

        if ($DryRun) { return }

        # ---- real run ----
        New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
        New-Item -ItemType Directory -Force -Path $auditDir   | Out-Null
        New-Item -ItemType Directory -Force -Path $backupDir  | Out-Null

        $prodRegPath = Resolve-RegistryPath ("$rootDisplay\$release\$productKeySel")
        $prodRegKey = Open-RegKey $prodRegPath -Writable
        if (-not $prodRegKey) { throw "Cannot open $($prodRegPath.Display) for writing" }
        try {
            $profilesW = $prodRegKey.OpenSubKey('Profiles', $true)
            if (-not $profilesW) { throw "Cannot open $($prodRegPath.Display)\Profiles for writing" }
            try {
                if ($targetExists -and -not $targetManaged) {
                    # A key without our marker is either a half-made copy from a
                    # failed run or a profile we did not create; replace it.
                    $profilesW.DeleteSubKeyTree($profileName)
                    $targetExists = $false
                }
                if (-not $targetExists) {
                    $destKey = $profilesW.CreateSubKey($profileName)
                    try { Copy-RegTree $profileTree $destKey }
                    catch {
                        try { $profilesW.DeleteSubKeyTree($profileName) }
                        catch { Write-Warning "Could not remove the partial '$profileName' profile copy at $targetProfileKey - delete it manually." }
                        throw
                    }
                    finally { $destKey.Dispose() }
                }
                $destKey = $profilesW.OpenSubKey($profileName, $true)
                if (-not $destKey) { throw "Cannot open $targetProfileKey for writing" }
                try {
                    foreach ($r in $retargets) {
                        Write-RegValue $destKey $r.RelKey $r.Name $r.NewData $r.Kind
                    }
                    # Stamp only after copy + retargets succeeded: a Forge-<n>
                    # key without this marker is never reused as-is.
                    $destKey.SetValue('ForgeManaged', 1, [Microsoft.Win32.RegistryValueKind]::DWord)
                } finally { $destKey.Dispose() }
            } finally { $profilesW.Dispose() }
        } finally { $prodRegKey.Dispose() }

        # /p falls back to the USER's current profile when the named profile is
        # missing - re-open the target right before start and refuse if absent.
        $targetCheck = Open-RegKey (Resolve-RegistryPath $targetProfileKey)
        if (-not $targetCheck) {
            throw "Target profile key $targetProfileKey is missing - refusing to start (acad /p would fall back to the user's current profile)."
        }
        $targetCheck.Dispose()

        $proc = Start-Process -FilePath $acadExeResolved -ArgumentList $argList -PassThru
        try {
            # Best-effort state first: if anything below fails, the pid is
            # printed and instance.json already points at the right process.
            $state = [ordered]@{
                name                        = $Name
                pipe                        = $pipeName
                profile                     = $profileName
                pid                         = $proc.Id
                processStartTimeUtc         = $null
                processStartTimeUtcFileTime = $null
                commandLine                 = $commandLine
                acadPath                    = $acadExeResolved
                drawing                     = $Drawing
                instanceDir                 = $instanceDir
                auditDir                    = $auditDir
                backupDir                   = $backupDir
                profilesKey                 = $profilesKeyPath
                profilesDefault             = $profilesDefault
                createdUtc                  = (Get-Date).ToUniversalTime().ToString('o')
            }
            try {
                $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
            } catch {
                Write-Warning "Best-effort write of $statePath failed: $($_.Exception.Message)"
            }

            $startUtc = $null
            try { $startUtc = $proc.StartTime.ToUniversalTime() } catch { $startUtc = $null }
            if (-not $startUtc) {
                Start-Sleep -Milliseconds 800
                $proc2 = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
                if ($proc2) { try { $startUtc = $proc2.StartTime.ToUniversalTime() } catch { $startUtc = $null } }
            }
            if (-not $startUtc) { throw "acad.exe started as pid $($proc.Id) but its start time is unavailable - process may have exited immediately. Check the command line: $commandLine" }

            $state.processStartTimeUtc         = $startUtc.ToString('o')
            $state.processStartTimeUtcFileTime = $startUtc.ToFileTimeUtc()
            $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8

            # Post-start watch: AutoCAD may rewrite Profiles (Default) to the /p
            # profile at startup. Poll briefly and put the recorded value back -
            # but only while the pointer is a Forge-* value or empty, which is
            # the failure mode this watch exists to undo. A real, different
            # profile name is the user's own switch: report it once, never
            # overwrite it.
            $watchSeconds = 10
            $restores = 0
            $leftAsIsFor = $null
            $leftAsIsSeen = $false
            $profilesWatch = Open-RegKey (Resolve-RegistryPath $profilesKeyPath) -Writable
            if (-not $profilesWatch) {
                Write-Warning "Cannot open $profilesKeyPath for writing - if AutoCAD rewrites (Default) to '$profileName' it will stay until stop-acad-instance.ps1 restores it."
            } else {
                try {
                    $deadline = (Get-Date).AddSeconds($watchSeconds)
                    while ((Get-Date) -lt $deadline) {
                        $cur = $profilesWatch.GetValue('')
                        $forgeOrEmpty = (-not [string]$cur) -or (([string]$cur) -match '^(?i)Forge-')
                        if ((([string]$cur) -cne ([string]$profilesDefault)) -and $forgeOrEmpty) {
                            $leftAsIsFor = $null
                            try {
                                if ($null -eq $profilesDefault) { $profilesWatch.DeleteValue('', $false) }
                                else { $profilesWatch.SetValue('', [string]$profilesDefault, [Microsoft.Win32.RegistryValueKind]::String) }
                                $restores++
                                Write-Output ("[RESTORED]   {0} (Default): {1} -> {2}" -f $profilesKeyPath, (Format-ProfilePointer $cur), (Format-ProfilePointer $profilesDefault))
                            } catch {
                                Write-Warning "Could not restore Profiles (Default): $($_.Exception.Message)"
                            }
                        } elseif (([string]$cur) -ceq ([string]$profilesDefault)) {
                            $leftAsIsFor = $null
                        } elseif (([string]$cur) -ne $leftAsIsFor) {
                            $leftAsIsFor = [string]$cur
                            $leftAsIsSeen = $true
                            Write-Output ("Profiles (Default) left as-is (now '{0}', recorded '{1}')" -f $cur, $profilesDefault)
                        }
                        Start-Sleep -Milliseconds 250
                    }
                } finally { $profilesWatch.Dispose() }
                if ($restores) {
                    Write-Output "AutoCAD rewrote the current-profile pointer $restores time(s); each was restored to $(Format-ProfilePointer $profilesDefault)."
                }
                if ($leftAsIsSeen) {
                    Write-Output "A real non-Forge profile pointer was seen during the watch and left as-is (a user profile switch is never overwritten)."
                }
                if (-not $restores -and -not $leftAsIsSeen) {
                    Write-Output "Profiles (Default) stayed at $(Format-ProfilePointer $profilesDefault) through the post-start watch."
                }
            }
        } catch {
            throw "Post-start failure for acad.exe pid $($proc.Id): $($_.Exception.Message)`nThe instance may still be running - stop it manually: Stop-Process -Id $($proc.Id) -Force"
        }

        Write-Output ""
        Write-Output "=== Instance state ==="
        $state | ConvertTo-Json -Depth 6 | Write-Output
        Write-Output ""
        Write-Output "Register an MCP server named '765t-forge-$Name' with the env shown above to drive this instance."
    } finally {
        foreach ($k in $savedEnv.Keys) {
            if ($null -eq $savedEnv[$k]) { Remove-Item -Path "Env:$k" -ErrorAction SilentlyContinue }
            else { Set-Item -Path "Env:$k" -Value $savedEnv[$k] }
        }
    }
} finally {
    if ($lockHeld) { $launchMutex.ReleaseMutex() }
    $launchMutex.Dispose()
}
