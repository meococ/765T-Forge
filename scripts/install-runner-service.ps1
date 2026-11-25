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
    $args = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"")
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

$transcriptPath = Join-Path $RunnerRoot "install-runner-service.log"
try { Stop-Transcript | Out-Null } catch { }
Start-Transcript -Path $transcriptPath -Force | Out-Null

Write-Host "Stopping interactive runner process if present..."
Get-Process Runner.Listener -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3

Write-Host "Removing existing runner registration locally (token not printed)..."
$removeToken = (gh api --method POST "repos/$Repo/actions/runners/remove-token" --jq .token).Trim()
if ($removeToken) {
    & $configCmd remove --token $removeToken 2>$null | Out-Host
}

Write-Host "Configuring runner-local .NET install dir for service identity..."
$dotnetDir = Join-Path $RunnerRoot ".dotnet"
New-Item -ItemType Directory -Force -Path $dotnetDir | Out-Null
[Environment]::SetEnvironmentVariable("DOTNET_INSTALL_DIR", $dotnetDir, "Machine")
& icacls $dotnetDir /grant "*S-1-5-20:(OI)(CI)(M)" /T | Out-Null

Write-Host "Registering runner as Windows service..."
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

if ($WindowsLogonAccount -notmatch '^NT AUTHORITY\(NETWORK SERVICE|SYSTEM|LOCAL SERVICE)$') {
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

Write-Host "Runner service installed: $($service.Name) [$($service.State)]"
Write-Host "Startup-folder fallback can remain installed; it exits automatically while this service is running."
Stop-Transcript | Out-Null
