<#
.SYNOPSIS
  Open AutoCAD with Forge plugin, create a synthetic lab DWG, run smoke-lab pipe checks.
#>
[CmdletBinding()]
param(
    [string] $Token = "",
    [string] $PipeName = "765T.Forge.AutoCAD",
    [int] $AcadWaitSeconds = 90,
    [int] $AcadYear = 2026,
    [string] $AccoreConsolePath = "",
    [switch] $SkipLaunchAcad,
    [switch] $KeepAcadOpen
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# Pipe token resolution (deterministic precedence, no well-known shared constant):
#   1. explicit -Token
#   2. FORGE_AUTOCAD_TOKEN already set in the process environment
#   3. a per-run random token
if (-not [string]::IsNullOrEmpty($Token)) {
    # operator supplied -Token explicitly
} elseif (-not [string]::IsNullOrEmpty($env:FORGE_AUTOCAD_TOKEN)) {
    $Token = $env:FORGE_AUTOCAD_TOKEN
} else {
    $Token = [guid]::NewGuid().ToString("n")
}
Write-Host "Smoke-lab pipe token for this run (pair the AutoCAD plugin with this exact value): $Token"

# Exact path construction only. No globbing, no discovery, no guessing.
$acadRoot = "C:\Program Files\Autodesk\AutoCAD $AcadYear"
if ([string]::IsNullOrEmpty($AccoreConsolePath)) {
    $accore = Join-Path $acadRoot "accoreconsole.exe"
} else {
    $accore = $AccoreConsolePath
}
$acad = Join-Path $acadRoot "acad.exe"

$labRoot = Join-Path $env:LOCALAPPDATA "765T-Forge\smoke-lab"
$pluginInstall = Join-Path $env:LOCALAPPDATA "765T-Forge\plugin"
$bundleRoot = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\765T-Forge.bundle"
$resultsPath = Join-Path $labRoot "smoke-results.json"
$dwgPath = Join-Path $labRoot "forge-smoke-lab.dwg"
$pdfPath = Join-Path $labRoot "forge-smoke-publish.pdf"
$contractPath = Join-Path $labRoot "issue-set-contract.json"
$scrCreate = Join-Path $labRoot "create-lab.scr"
$scrNetload = Join-Path $labRoot "startup-netload.scr"

New-Item -ItemType Directory -Force -Path $labRoot | Out-Null

function Write-Step([string]$msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

function Send-ForgeTool {
    param(
        [string]$Tool,
        [hashtable]$Args = @{},
        [bool]$DryRun = $false,
        [int]$TimeoutSec = 120
    )

    $cmd = [ordered]@{
        id = [guid]::NewGuid().ToString("N")
        tool = $Tool
        args = $Args
        dryRun = $DryRun
        unsafeAcknowledged = $false
        authToken = $Token
    }
    $json = ($cmd | ConvertTo-Json -Compress -Depth 12)

    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $swConnect = [diagnostics.stopwatch]::StartNew()
        while (-not $pipe.IsConnected -and $swConnect.Elapsed.TotalSeconds -lt 15) {
            try { $pipe.Connect(1000) } catch { Start-Sleep -Milliseconds 200 }
        }
        if (-not $pipe.IsConnected) {
            return @{ ok = $false; error = @{ code = "plugin_connect_timeout"; message = "Could not connect to pipe $PipeName" } }
        }

        $writer = New-Object System.IO.StreamWriter($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $writer.AutoFlush = $true
        $reader = New-Object System.IO.StreamReader($pipe, [Text.Encoding]::UTF8, $true, 1024, $true)
        $writer.WriteLine($json)

        $cts = [System.Threading.CancellationTokenSource]::new()
        $cts.CancelAfter([TimeSpan]::FromSeconds($TimeoutSec))
        $task = $reader.ReadLineAsync()
        while (-not $task.IsCompleted) {
            if ($cts.IsCancellationRequested) {
                return @{ ok = $false; error = @{ code = "plugin_response_timeout"; message = "Timed out after ${TimeoutSec}s waiting for $Tool" } }
            }
            Start-Sleep -Milliseconds 50
        }
        $line = $task.Result
        if ([string]::IsNullOrWhiteSpace($line)) {
            return @{ ok = $false; error = @{ code = "pipe_empty_response"; message = "Empty response for $Tool" } }
        }
        return ($line | ConvertFrom-Json)
    }
    finally {
        if ($pipe) { $pipe.Dispose() }
    }
}

Write-Step "1) Ensure plugin installed"
$dll = Join-Path $pluginInstall "Forge.Plugin.dll"
if (-not (Test-Path $dll)) {
    & (Join-Path $PSScriptRoot "install-plugin.ps1") -Configuration Release -SkipBuild:(Test-Path (Join-Path $root "src\Forge.Plugin\bin\Release\net8.0-windows\Forge.Plugin.dll"))
}
if (-not (Test-Path $dll)) { throw "Plugin DLL missing at $dll" }
Write-Host "Plugin: $dll"

Write-Step "2) Install Autoloader bundle to APPDATA"
$contentsWin = Join-Path $bundleRoot "Contents\Windows"
New-Item -ItemType Directory -Force -Path $contentsWin | Out-Null
Copy-Item (Join-Path $root "plugin-bundle\765T-Forge.bundle\PackageContents.xml") $bundleRoot -Force
Copy-Item (Join-Path $pluginInstall "*") $contentsWin -Recurse -Force
Write-Host "Bundle: $bundleRoot"

Write-Step "3) Create synthetic lab DWG via AccoreConsole"
@"
FILEDIA 0
BACKGROUNDPLOT 0
_LAYOUT _N LabSheet1
_LAYOUT _N LabSheet2
_QSAVE
_QUIT
"@ | Set-Content -LiteralPath $scrCreate -Encoding ascii

if (Test-Path $dwgPath) { Remove-Item $dwgPath -Force }

# Start from a blank drawing template if available
$template = Join-Path $acadRoot "Template\acad.dwt"
if (-not (Test-Path $template)) { $template = Join-Path $acadRoot "Template\acadiso.dwt" }

# AccoreConsole: create new drawing, run script that creates layouts then saveas
$createScr2 = Join-Path $labRoot "create-lab2.scr"
@"
FILEDIA 0
BACKGROUNDPLOT 0
_LAYOUT _N LabSheet1
_LAYOUT _N LabSheet2
_.SAVEAS  
$dwgPath
_QUIT
"@ | Set-Content -LiteralPath $createScr2 -Encoding ascii

# Use a seed DWG: copy template isn't a DWG. Create via acad - or use NEW in console.
# AccoreConsole /i requires existing DWG. Create minimal via copying a system sample if any.
$seed = $null
Get-ChildItem (Join-Path $acadRoot "Sample") -Recurse -Filter "*.dwg" -ErrorAction SilentlyContinue |
    Select-Object -First 5 |
    ForEach-Object { if (-not $seed) { $seed = $_.FullName } }

if (-not $seed) {
    Write-Warning "No sample DWG found under AutoCAD Sample. Will try creating via AutoCAD GUI NEW."
} else {
    Write-Host "Seed DWG: $seed"
    Copy-Item $seed $dwgPath -Force
    $p = Start-Process -FilePath $accore -ArgumentList @("/i", $dwgPath, "/s", $createScr2) -Wait -PassThru -NoNewWindow
    Write-Host "AccoreConsole exit=$($p.ExitCode)"
    if (-not (Test-Path $dwgPath)) { throw "Failed to prepare lab DWG" }
}

# Issue set contract pointing at a missing layout (for step 6)
@{
    contractId = "smoke-lab"
    projectId = "smoke"
    rev = "A"
    sheets = @(
        @{ layout = "LabSheet1"; drawingNo = "MTR-SMOKE-001"; rev = "A" }
        @{ layout = "DoesNotExistLayout"; drawingNo = "MTR-SMOKE-999"; rev = "A" }
    )
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $contractPath -Encoding utf8

# Placeholder PDF for overwrite-ack test
"placeholder" | Set-Content -LiteralPath $pdfPath -Encoding ascii

Write-Step "4) Launch AutoCAD with Forge env"
$env:FORGE_AUTOCAD_TOKEN = $Token
$env:FORGE_DEV_ALLOW_DEFAULT_TOKEN = "true"
$env:FORGE_PIPE_NAME = $PipeName

$acadProc = Get-Process acad -ErrorAction SilentlyContinue
if (-not $SkipLaunchAcad) {
    if ($acadProc) {
        Write-Host "AutoCAD already running (pid=$($acadProc.Id)). Will use existing session - ensure plugin loaded with matching token."
    } else {
        $argList = @("/nologo")
        if (Test-Path $dwgPath) { $argList += $dwgPath }
        Write-Host "Starting AutoCAD..."
        Start-Process -FilePath $acad -ArgumentList $argList
        Start-Sleep -Seconds 5
    }
}

Write-Step "5) Wait for named pipe $PipeName (do NOT empty-connect probe)"
# Empty Connect/Dispose without a JSON line stalls the single-instance pipe server.
$deadline = (Get-Date).AddSeconds($AcadWaitSeconds)
$connected = $false
while ((Get-Date) -lt $deadline) {
    try {
        # Wake command context
        try {
            $acadCom = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")
            if ($acadCom.ActiveDocument) { $acadCom.ActiveDocument.SendCommand("MCP_STATUS ") }
        } catch { }

        $probeCmd = [ordered]@{
            id = [guid]::NewGuid().ToString("N")
            tool = "forge_system_health"
            args = @{}
            dryRun = $false
            unsafeAcknowledged = $false
            authToken = $Token
        } | ConvertTo-Json -Compress -Depth 5

        $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(".", $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
        $pipe.Connect(3000)
        $w = New-Object System.IO.StreamWriter($pipe, [Text.UTF8Encoding]::new($false), 1024, $true)
        $w.AutoFlush = $true
        $r = New-Object System.IO.StreamReader($pipe, [Text.Encoding]::UTF8, $true, 1024, $true)
        $w.WriteLine($probeCmd)
        $lineTask = $r.ReadLineAsync()
        $sw = [diagnostics.stopwatch]::StartNew()
        while (-not $lineTask.IsCompleted -and $sw.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 50 }
        $pipe.Dispose()
        if ($lineTask.IsCompleted -and $lineTask.Result) {
            $connected = $true
            break
        }
    } catch {
        Start-Sleep -Seconds 2
        Write-Host ("  waiting... {0:n0}s left" -f ($deadline - (Get-Date)).TotalSeconds)
    }
}
if (-not $connected) {
    throw "Named pipe $PipeName not available after ${AcadWaitSeconds}s. Check Autoloader/TRUSTEDPATHS/MCP_STATUS."
}
Write-Host "Pipe is up."

Write-Step "6) Run smoke checks"
$results = [ordered]@{
    startedUtc = [DateTimeOffset]::UtcNow.ToString("o")
    pipe = $PipeName
    dwg = $dwgPath
    steps = @()
}

function Add-Result($name, $result, $pass, $notes) {
    $script:results.steps += [ordered]@{
        name = $name
        pass = [bool]$pass
        ok = [bool]$result.ok
        errorCode = $(if ($result.error) { $result.error.code } else { $null })
        notes = $notes
        raw = $result
    }
    $color = if ($pass) { "Green" } else { "Red" }
    Write-Host ("[{0}] {1} - {2}" -f $(if ($pass) { "PASS" } else { "FAIL" }), $name, $notes) -ForegroundColor $color
}

# Step 1: health
$r = Send-ForgeTool -Tool "forge_system_health" -Args @{}
$pass = $r.ok -and ($r.data.pipe -eq $PipeName -or $r.data.status -eq "ok" -or ($r.data | ConvertTo-Json -Compress) -match $PipeName)
Add-Result "forge_system_health" $r $pass ("pipe/status check; data=$($r.data | ConvertTo-Json -Compress)")

# Open lab DWG if needed
if (Test-Path $dwgPath) {
    $r = Send-ForgeTool -Tool "forge_doc_open" -Args @{ path = $dwgPath }
    Add-Result "forge_doc_open" $r ([bool]$r.ok) "open lab dwg"
}

# Step 3: preflight with missing required tag
$r = Send-ForgeTool -Tool "forge_qa_preflight" -Args @{ requiredTitleblockTags = @("DWG_NO_SMOKE_MISSING"); titleblockBlockName = $null; expectedLayers = @() }
$findings = @()
if ($r.data -and $r.data.findings) { $findings = @($r.data.findings) }
elseif ($r.data -and $r.data.Findings) { $findings = @($r.data.Findings) }
$hasMissing = $findings | Where-Object { $_.code -eq "titleblock_tag_missing" -or $_.Code -eq "titleblock_tag_missing" }
$pass = $r.ok -and ($r.data.passed -eq $false -or $r.data.Passed -eq $false -or $hasMissing)
Add-Result "forge_qa_preflight_missing_tag" $r $pass "expect passed=false / titleblock_tag_missing"

# ---------------------------------------------------------------------------
# Phase: overwrite-guard
# Deterministic gate: force = $false and NO overwrite acknowledgement.
# ForgeToolRunner refuses force=true unless FORGE_ALLOW_FORCE_PUBLISH=true, so
# this phase must NOT set force. Expected error code is an exact literal string.
# ---------------------------------------------------------------------------
$r = Send-ForgeTool -Tool "forge_plot_publish" -Args @{
    outputPath = $pdfPath
    layouts = @("LabSheet1")
    singlePdf = $true
    overwriteAcknowledged = $false
    requirePreflight = $false
    force = $false
}
$pass = (-not $r.ok) -and ($r.error.code -eq "publish_overwrite_not_acknowledged")
Add-Result "forge_plot_publish_overwrite_guard" $r $pass "expect publish_overwrite_not_acknowledged"

# ---------------------------------------------------------------------------
# Phase: forced-publish
# FORGE_ALLOW_FORCE_PUBLISH is set ONLY for the force = $true calls below and is
# always removed in the finally block, whatever happens inside the try.
# ---------------------------------------------------------------------------
$previousForcePublish = $env:FORGE_ALLOW_FORCE_PUBLISH
try {
    $env:FORGE_ALLOW_FORCE_PUBLISH = "true"

    # Dry-run publish (existing coverage kept)
    $r = Send-ForgeTool -Tool "forge_plot_publish" -Args @{
        outputPath = (Join-Path $labRoot "forge-smoke-publish-real.pdf")
        layouts = @("LabSheet1")
        singlePdf = $true
        overwriteAcknowledged = $true
        requirePreflight = $false
        force = $true
    } -DryRun $true
    Add-Result "forge_plot_publish_dryrun" $r ([bool]$r.ok) "dry-run publish"

    # Real forced publish (may fail if layout/plot device issues - still record)
    $outPdf = Join-Path $labRoot "forge-smoke-publish-real.pdf"
    if (Test-Path $outPdf) { Remove-Item $outPdf -Force }
    $r = Send-ForgeTool -Tool "forge_plot_publish" -Args @{
        outputPath = $outPdf
        layouts = @("LabSheet1")
        singlePdf = $true
        overwriteAcknowledged = $true
        requirePreflight = $false
        force = $true
    } -TimeoutSec 180

    # Documented success gate: exact literal strings only, never a pattern.
    $documentedOk = $false
    if ($r.ok -eq $true) { $documentedOk = $true }
    if ($r.status -eq "queued") { $documentedOk = $true }
    if ($r.status -eq "completed") { $documentedOk = $true }
    if ($r.data) {
        if ($r.data.status -eq "queued") { $documentedOk = $true }
        if ($r.data.status -eq "completed") { $documentedOk = $true }
    }

    # Existing probe/verify coverage kept: PDF exists + non-empty + verification.
    $pdfExists = [bool](Test-Path $outPdf)
    $pdfNonEmpty = $false
    if ($pdfExists) { $pdfNonEmpty = ((Get-Item $outPdf).Length -gt 0) }
    $verificationPassed = $null
    if ($r.verification) { $verificationPassed = ($r.verification.passed -eq $true) }

    Add-Result "forge_plot_publish_real" $r $documentedOk ("forced publish documented success/queued/completed; pdfExists=$pdfExists pdfNonEmpty=$pdfNonEmpty verificationPassed=$verificationPassed")
}
finally {
    if ($null -eq $previousForcePublish) {
        Remove-Item -Path Env:FORGE_ALLOW_FORCE_PUBLISH -ErrorAction SilentlyContinue
    } else {
        $env:FORGE_ALLOW_FORCE_PUBLISH = $previousForcePublish
    }
}

# Step 6: issue set validate
$r = Send-ForgeTool -Tool "forge_issue_set_validate" -Args @{ contractPath = $contractPath }
$findings = @()
if ($r.data -and $r.data.findings) { $findings = @($r.data.findings) }
$hasLayoutMissing = $findings | Where-Object { $_.code -eq "issue_set_layout_missing" -or $_.Code -eq "issue_set_layout_missing" }
$pass = $r.ok -and ($r.data.passed -eq $false -or $hasLayoutMissing)
Add-Result "forge_issue_set_validate" $r $pass "expect issue_set_layout_missing for DoesNotExistLayout"

# Layouts list for diagnostics
$r = Send-ForgeTool -Tool "forge_doc_list_layouts" -Args @{}
Add-Result "forge_doc_list_layouts" $r ([bool]$r.ok) (($r.data | ConvertTo-Json -Compress))

$results.finishedUtc = [DateTimeOffset]::UtcNow.ToString("o")
$results.passedCount = @($results.steps | Where-Object { $_.pass }).Count
$results.totalCount = @($results.steps).Count
$results.allCriticalPassed = (
    ($results.steps | Where-Object { $_.name -eq "forge_system_health" }).pass -and
    ($results.steps | Where-Object { $_.name -eq "forge_qa_preflight_missing_tag" }).pass -and
    ($results.steps | Where-Object { $_.name -eq "forge_plot_publish_overwrite_guard" }).pass
)

$results | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $resultsPath -Encoding utf8
Write-Step "DONE"
Write-Host "Results: $resultsPath"
Write-Host ("Passed {0}/{1}; critical={2}" -f $results.passedCount, $results.totalCount, $results.allCriticalPassed)

if (-not $KeepAcadOpen -and -not $SkipLaunchAcad) {
    Write-Host "Leaving AutoCAD open for inspection (use -KeepAcadOpen explicitly; default keep open)."
}

if (-not $results.allCriticalPassed) { exit 2 }
exit 0
