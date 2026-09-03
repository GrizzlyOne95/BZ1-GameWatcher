<#
.SYNOPSIS
    Installs BZ1 Game Watcher as a Windows Service. MUST be run elevated.

.DESCRIPTION
    Creates the BZ1GameWatcher service from an already-published Release build,
    running under the least-privilege virtual account NT SERVICE\BZ1GameWatcher.

    The service listens on 127.0.0.1:5283 only. No firewall rule is created and
    none is needed: loopback traffic is never filtered, and nothing outside this
    machine can reach the port. Public access is expected to arrive through an
    outbound tunnel (Tailscale Funnel / cloudflared), never an inbound port forward.

.PARAMETER SteamApiKey
    Optional. Written to appsettings.Production.json in the publish directory and
    locked down with file ACLs. Omit it on a re-run to keep the existing key.

.PARAMETER ProtectOnly
    Re-applies the secret-file ACLs and restarts the service, without touching the
    service registration. Run this after every publish: 'dotnet publish' rewrites
    the publish directory and resets the ACLs on appsettings.Production.json.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-windows-service.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-windows-service.ps1 -ProtectOnly
#>
[CmdletBinding()]
param(
    [string]$ServiceName  = 'BZ1GameWatcher',
    [string]$DisplayName  = 'BZ1 Game Watcher',
    [string]$InstallRoot  = 'C:\Services\BZ1GameWatcher',
    [string]$BindUrl      = 'http://127.0.0.1:5283',
    [string]$SteamApiKey  = '',
    [switch]$ProtectOnly
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run from an elevated (Run as Administrator) PowerShell session.'
}

$currentPath = Join-Path $InstallRoot 'current'
$exePath     = Join-Path $currentPath 'BZAPI.exe'
$dataPath    = Join-Path $InstallRoot 'data'
$secretPath  = Join-Path $currentPath 'appsettings.Production.json'

if (-not (Test-Path $exePath)) {
    throw "Published build not found at $exePath. Publish first, then re-run."
}
if (-not (Test-Path $dataPath)) {
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
}

# The virtual account is created implicitly by the SCM the first time it is named
# as a service identity. It gets no interactive rights and no profile.
$account = "NT SERVICE\$ServiceName"

<#
    The Steam key lives in a file rather than the service's registry Environment
    value, because HKLM\SYSTEM\CurrentControlSet\Services\<name> grants read to
    Authenticated Users by default, so any local account could read the key there.
    A file can be restricted to only the accounts that need it.

    'dotnet publish' rewrites this directory and resets the file's inherited ACLs,
    so re-run with -ProtectOnly after every publish to restore them.
#>
function Protect-SecretFile {
    param([string]$Path, [string]$ServiceAccount)

    if (-not (Test-Path $Path)) { return $false }

    & icacls $Path /inheritance:r /Q | Out-Null
    & icacls $Path /grant 'SYSTEM:(R)' /Q | Out-Null
    & icacls $Path /grant 'Administrators:(F)' /Q | Out-Null
    & icacls $Path /grant "${ServiceAccount}:(R)" /Q | Out-Null
    return $true
}

if ($SteamApiKey) {
    $secretConfig = [ordered]@{ Steam = [ordered]@{ ApiKey = $SteamApiKey } }
    [System.IO.File]::WriteAllText(
        $secretPath,
        ($secretConfig | ConvertTo-Json -Depth 5),
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host 'Steam API key written to appsettings.Production.json.'
} elseif (Test-Path $secretPath) {
    Write-Host 'Existing appsettings.Production.json kept (no key supplied).'
}

if (Protect-SecretFile -Path $secretPath -ServiceAccount $account) {
    Write-Host 'Secret file ACLs applied: SYSTEM (R), Administrators (F), service account (R).'
} else {
    Write-Host 'No appsettings.Production.json present; Steam avatar enrichment will be skipped.'
}

if ($ProtectOnly) {
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        throw "-ProtectOnly was used but the $ServiceName service does not exist yet."
    }

    Write-Host 'Restarting service to pick up configuration...'
    Restart-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 12
    Write-Host ('Status: ' + (Get-Service -Name $ServiceName).Status)
    return
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Service $ServiceName already exists; stopping and removing it first."
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 5
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 3
}

Write-Host "Creating service $ServiceName..."
# binPath is quoted so a path containing spaces is passed as one argument.
& sc.exe create $ServiceName `
    binPath= "`"$exePath`"" `
    DisplayName= "`"$DisplayName`"" `
    start= delayed-auto `
    obj= "$account" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed with exit code $LASTEXITCODE." }

& sc.exe description $ServiceName "Maintains the outbound websocket to the Battlezone 98 Redux lobby service and serves the sanitized read-only lobby API on 127.0.0.1:5283." | Out-Null

# Restart on the first and second unexpected failure, then give up and leave the
# state visible rather than looping forever. Counter resets after a clean day.
& sc.exe failure $ServiceName reset= 86400 actions= restart/30000/restart/60000/none/0 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

# Service-scoped environment. Kept in the service's own registry key rather than
# machine-wide, so nothing else on the host inherits it. ASPNETCORE_URLS is what
# holds Kestrel to loopback; it is set here rather than in appsettings so the
# tracked configuration stays host-agnostic (the Linux/container deployment needs
# to bind 0.0.0.0 and must not inherit this).
#
# Nothing secret goes here: the Steam key is in the ACL-protected file above,
# because this registry key is readable by any authenticated local account.
$environment = @(
    'ASPNETCORE_ENVIRONMENT=Production'
    "ASPNETCORE_URLS=$BindUrl"
    "Activity__PersistencePath=$dataPath\activity-history.json"
    'Activity__PersistenceIsDurable=true'
)

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $serviceKey -Name 'Environment' -PropertyType MultiString -Value $environment -Force | Out-Null
Write-Host "Service environment written ($($environment.Count) entries)."

# Least privilege on disk: read/execute the binaries, write only the data directory.
& icacls $currentPath /grant "${account}:(OI)(CI)(RX)" /T /C /Q | Out-Null
& icacls $dataPath    /grant "${account}:(OI)(CI)(M)"  /T /C /Q | Out-Null
# That recursive grant re-broadened the secret file, so lock it back down.
Protect-SecretFile -Path $secretPath -ServiceAccount $account | Out-Null
Write-Host 'Filesystem ACLs applied.'

# Registering the Event Log source up front means the service can write startup
# and failure records without needing rights to create the source itself.
if (-not [System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    New-EventLog -LogName Application -Source $ServiceName
    Write-Host "Event Log source '$ServiceName' registered."
}

Write-Host "Starting $ServiceName..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 12

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "Service : $($svc.Name)"
Write-Host "Status  : $($svc.Status)"
Write-Host "Startup : $((Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartMode) (delayed auto)"
Write-Host "Account : $((Get-CimInstance Win32_Service -Filter "Name='$ServiceName'").StartName)"

$listeners = Get-NetTCPConnection -State Listen -LocalPort 5283 -ErrorAction SilentlyContinue
Write-Host ""
Write-Host 'Listening endpoints on 5283:'
$listeners | ForEach-Object { Write-Host "  $($_.LocalAddress):$($_.LocalPort)" }
if ($listeners | Where-Object { $_.LocalAddress -notin @('127.0.0.1', '::1') }) {
    Write-Warning 'A non-loopback listener is present. Investigate before exposing any tunnel.'
} else {
    Write-Host '  OK - loopback only.'
}

try {
    $health = Invoke-RestMethod "$BindUrl/api/health" -TimeoutSec 20
    Write-Host ""
    Write-Host "Health  : status=$($health.status) lobbies=$($health.lobbyCount) websocket=$($health.lobbyConnection.state)"
} catch {
    Write-Warning "Health check failed: $($_.Exception.Message)"
    Write-Host 'Check Event Viewer > Windows Logs > Application, source BZ1GameWatcher.'
}
