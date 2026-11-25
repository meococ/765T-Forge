<#
.SYNOPSIS
  Install the already-configured GitHub Actions runner as a Windows service.
  This does not remove or re-register the runner and does not need a GitHub token.
  Must run elevated; when launched non-elevated it relaunches itself through UAC.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/install-runner-service.ps1
#>
[CmdletBinding()]
param(
    [string] $Repo = "meococ/765T-Forge",
    [string] $RunnerRoot = "C:\actions-runner",
    [string] $RunnerName = "AUTOCAD-WIN-01",
    [string] $WindowsLogonAccount = "NT AUTHORITY\NETWORK SERVICE"
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevation required. Relaunching with UAC..."
    $args = @("-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
    foreach ($name in $MyInvocation.BoundParameters.Keys) {
        $args += "-$name"
        $args += "`"$($MyInvocation.BoundParameters[$name])`""
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList ($args -join ' ')
    exit 0
}

$logPath = Join-Path $RunnerRoot "install-runner-service.log"
function Write-SetupLog {
    param([Parameter(Mandatory = $true)] [string] $Message)
    $line = "$(Get-Date -Format o) $Message"
    Add-Content -Path $logPath -Value $line -Encoding utf8
    Write-Host $Message
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Arguments,
        [switch] $AllowFailure
    )

    Write-SetupLog ("sc.exe " + ($Arguments -join ' '))
    & sc.exe @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "sc.exe $($Arguments[0]) failed with exit code $LASTEXITCODE"
    }

    return $LASTEXITCODE
}

try {
    Set-Content -Path $logPath -Value "$(Get-Date -Format o) runner service setup started" -Encoding utf8

    $runnerConfig = Join-Path $RunnerRoot ".runner"
    $runnerCredentials = Join-Path $RunnerRoot ".credentials"
    if (-not (Test-Path $runnerConfig) -or -not (Test-Path $runnerCredentials)) {
        throw "Runner is not configured in $RunnerRoot. Configure it first, then run this script."
    }

    $runnerServiceExe = Join-Path $RunnerRoot "bin\RunnerService.exe"
    if (-not (Test-Path $runnerServiceExe)) {
        throw "RunnerService.exe not found at $runnerServiceExe"
    }

    $repoSlug = ($Repo -replace '/', '-') -replace '[^0-9a-zA-Z._\-]', '-'
    $serviceName = "actions.runner.$repoSlug.$RunnerName"
    $serviceDisplayName = "GitHub Actions Runner ($repoSlug.$RunnerName)"
    Write-SetupLog "Target service: $serviceName"

    Write-SetupLog "Stopping interactive runner process if present..."
    Get-Process Runner.Listener -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 3

    Write-SetupLog "Creating runner-local .NET install dir and granting service access..."
    $dotnetDir = Join-Path $RunnerRoot ".dotnet"
    New-Item -ItemType Directory -Force -Path $dotnetDir | Out-Null
    [Environment]::SetEnvironmentVariable("DOTNET_INSTALL_DIR", $dotnetDir, "Machine")
    & icacls $RunnerRoot /grant "*S-1-5-20:(OI)(CI)(M)" /T | Out-Null

    Write-SetupLog "Initializing Actions Runner event source..."
    & $runnerServiceExe init | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-SetupLog "RunnerService init returned $LASTEXITCODE; continuing because service can still run without event-source registration."
    }

    $existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-SetupLog "Existing service found; replacing service registration only (runner registration is untouched)."
        Invoke-Sc -Arguments @("stop", $serviceName) -AllowFailure | Out-Null
        Start-Sleep -Seconds 2
        Invoke-Sc -Arguments @("delete", $serviceName) | Out-Null
        Start-Sleep -Seconds 2
    }

    Write-SetupLog "Installing Windows service..."
    $scArgs = @(
        "create", $serviceName,
        "binPath=", $runnerServiceExe,
        "start=", "auto",
        "DisplayName=", $serviceDisplayName,
        "obj=", $WindowsLogonAccount
    )

    $wellKnownServiceAccounts = @(
        "NT AUTHORITY\NETWORK SERVICE",
        "NT AUTHORITY\SYSTEM",
        "NT AUTHORITY\LOCAL SERVICE"
    )
    if ($WindowsLogonAccount -notin $wellKnownServiceAccounts) {
        $cred = Get-Credential -UserName $WindowsLogonAccount -Message "Password for runner service account"
        $scArgs += @("password=", $cred.GetNetworkCredential().Password)
    }

    Invoke-Sc -Arguments $scArgs | Out-Null
    Invoke-Sc -Arguments @("failure", $serviceName, "reset=", "86400", "actions=", "restart/5000/restart/10000/restart/30000") -AllowFailure | Out-Null

    Write-SetupLog "Writing .service marker..."
    $serviceMarker = Join-Path $RunnerRoot ".service"
    Set-Content -Path $serviceMarker -Value $serviceName -Encoding ascii
    Set-ItemProperty -Path $serviceMarker -Name Attributes -Value ((Get-Item $serviceMarker).Attributes -bor [IO.FileAttributes]::Hidden)

    Write-SetupLog "Starting service..."
    Invoke-Sc -Arguments @("start", $serviceName) | Out-Null
    Start-Sleep -Seconds 8

    $service = Get-Service -Name $serviceName -ErrorAction Stop
    Write-SetupLog "Runner service installed: $($service.Name) [$($service.Status)]"
    Write-SetupLog "Startup-folder fallback can remain installed; it exits automatically while this service is running."
}
catch {
    Write-SetupLog "ERROR: $($_.Exception.Message)"
    exit 1
}
