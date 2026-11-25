<#
.SYNOPSIS
  Reconfigure the existing GitHub Actions runner as a Windows service.
  Must run elevated. Uses gh CLI only to mint short-lived remove/registration tokens;
  token values are never printed.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts/install-runner-service.ps1
#>
[CmdletBinding()]
param(
    [string] $Repo = "meococ/765T-Forge",
    [string] $RunnerRoot = "C:\actions-runner",
    [string] $RunnerName = "AUTOCAD-WIN-01",
    [string] $Labels = "autocad-2026",
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

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "gh CLI is required and must be authenticated as a repo admin."
}

$configCmd = Join-Path $RunnerRoot "config.cmd"
if (-not (Test-Path $configCmd)) {
    throw "config.cmd not found at $configCmd"
}

$logPath = Join-Path $RunnerRoot "install-runner-service.log"
function Write-SetupLog {
    param([Parameter(Mandatory = $true)] [string] $Message)
    $line = "$(Get-Date -Format o) $Message"
    Add-Content -Path $logPath -Value $line -Encoding utf8
    Write-Host $Message
}

Set-Content -Path $logPath -Value "$(Get-Date -Format o) runner service setup started" -Encoding utf8
trap {
    Write-SetupLog "ERROR: $($_.Exception.Message)"
    exit 1
}

Write-SetupLog "Stopping interactive runner process if present..."
Get-Process Runner.Listener -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3
Write-SetupLog "Interactive runner process stopped."

Write-SetupLog "Requesting remove token via gh..."
$removeToken = (gh api --method POST "repos/$Repo/actions/runners/remove-token" --jq .token).Trim()
Write-SetupLog "Remove token acquired: $(-not [string]::IsNullOrWhiteSpace($removeToken))"
if ($removeToken) {
    Write-SetupLog "Removing existing runner registration locally..."
    & $configCmd remove --token $removeToken 2>$null | Out-Host
    Write-SetupLog "Remove step exited with code $LASTEXITCODE."
}

Write-SetupLog "Configuring runner-local .NET install dir for service identity..."
$dotnetDir = Join-Path $RunnerRoot ".dotnet"
New-Item -ItemType Directory -Force -Path $dotnetDir | Out-Null
[Environment]::SetEnvironmentVariable("DOTNET_INSTALL_DIR", $dotnetDir, "Machine")
& icacls $dotnetDir /grant "*S-1-5-20:(OI)(CI)(M)" /T | Out-Null

Write-SetupLog "Registering runner as Windows service..."
$registrationToken = (gh api --method POST "repos/$Repo/actions/runners/registration-token" --jq .token).Trim()
if (-not $registrationToken) {
    throw "Could not obtain runner registration token."
}

$configArgs = @(
    "--unattended",
    "--url", "https://github.com/$Repo",
    "--token", $registrationToken,
    "--name", $RunnerName,
    "--labels", $Labels,
    "--replace",
    "--runasservice",
    "--windowslogonaccount", $WindowsLogonAccount
)

$wellKnownServiceAccounts = @(
    "NT AUTHORITY\NETWORK SERVICE",
    "NT AUTHORITY\SYSTEM",
    "NT AUTHORITY\LOCAL SERVICE"
)
if ($WindowsLogonAccount -notin $wellKnownServiceAccounts) {
    $cred = Get-Credential -UserName $WindowsLogonAccount -Message "Password for runner service account"
    $configArgs += @("--windowslogonpassword", $cred.GetNetworkCredential().Password)
}

Push-Location $RunnerRoot
try {
    & $configCmd @configArgs
    if ($LASTEXITCODE -ne 0) { throw "Runner service configuration failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

Start-Sleep -Seconds 5
$service = Get-CimInstance Win32_Service | Where-Object {
    $_.Name -like "actions.runner.*" -and $_.PathName -like "*$RunnerRoot*"
} | Select-Object -First 1

if (-not $service) {
    throw "Runner service was configured but no actions.runner.* service rooted at $RunnerRoot was found."
}

if ($service.State -ne "Running") {
    Start-Service -Name $service.Name
    Start-Sleep -Seconds 5
    $service = Get-CimInstance Win32_Service -Filter "Name='$($service.Name)'"
}

Write-SetupLog "Runner service installed: $($service.Name) [$($service.State)]"
Write-SetupLog "Startup-folder fallback can remain installed; it exits automatically while this service is running."
