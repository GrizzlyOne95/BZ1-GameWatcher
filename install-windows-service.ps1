<#
.SYNOPSIS
    Installs BZ1 Game Watcher as a Windows Service. MUST be run elevated.

.DESCRIPTION
    Creates the BZ1GameWatcher service from an already-published Release build,
    running under the least-privilege virtual account NT SERVICE\BZ1GameWatcher.

    The service listens on 127.0.0.1:5283 only. No firewall rule is created and
    none is needed: loopback traffic is never filtered, and nothing outside this
    machine can reach the port. Public access is expected to arrive through an
    outbound tunnel (cloudflared), never through an inbound port forward.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-windows-service.ps1
#>
[CmdletBinding()]
param(
    [string]$ServiceName  = 'BZ1GameWatcher',
    [string]$DisplayName  = 'BZ1 Game Watcher',
    [string]$InstallRoot  = 'C:\Services\BZ1GameWatcher',
    [string]$BindUrl      = 'http://127.0.0.1:5283',
    [string]$SteamApiKey  = ''
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run from an elevated (Run as Administrator) PowerShell session.'
}

$exePath  = Join-Path $InstallRoot 'current\BZAPI.exe'
$dataPath = Join-Path $InstallRoot 'data'

if (-not (Test-Path $exePath)) {
    throw "Published build not found at $exePath. Publish first, then re-run."
}
if (-not (Test-Path $dataPath)) {
    New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
}

# The virtual account is created implicitly by the SCM the first time it is named
# as a service identity. It gets no interactive rights and no profile.
$account = "NT SERVICE\$ServiceName"

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
# tracked configuration stays host-agnostic (the Linux/Render deployment needs to
# bind 0.0.0.0 and must not inherit this).
$environment = @(
    'ASPNETCORE_ENVIRONMENT=Production'
    "ASPNETCORE_URLS=$BindUrl"
    "Activity__PersistencePath=$dataPath\activity-history.json"
    'Activity__PersistenceIsDurable=true'
)
if ($SteamApiKey) { $environment += "Steam__ApiKey=$SteamApiKey" }

$serviceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $serviceKey -Name 'Environment' -PropertyType MultiString -Value $environment -Force | Out-Null
Write-Host "Service environment written ($($environment.Count) entries)."

# Least privilege on disk: read/execute the binaries, write only the data directory.
& icacls "$InstallRoot\current" /grant "${account}:(OI)(CI)(RX)" /T /C /Q | Out-Null
& icacls $dataPath              /grant "${account}:(OI)(CI)(M)"  /T /C /Q | Out-Null
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
