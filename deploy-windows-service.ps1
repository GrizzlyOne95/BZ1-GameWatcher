<#
.SYNOPSIS
    Atomically deploys a CI-built BZ1 Game Watcher Windows package and rolls back on failure.

.DESCRIPTION
    This script is intended to run on the production Windows host from a dedicated GitHub Actions
    self-hosted runner. It never builds source code on the production machine.

    The package is staged under C:\Services\BZ1GameWatcher\releases, the existing protected
    appsettings.Production.json is carried forward, the service is stopped, and the staged release
    is swapped into C:\Services\BZ1GameWatcher\current. The service is then started and /api/health
    is polled. If startup or health validation fails, the previous release is restored automatically.

    Activity history remains outside the release tree under C:\Services\BZ1GameWatcher\data and is
    therefore untouched by deployments.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{7,40}$')]
    [string]$ReleaseId,

    [string]$ServiceName = 'BZ1GameWatcher',
    [string]$InstallRoot = 'C:\Services\BZ1GameWatcher',
    [string]$HealthUrl = 'http://127.0.0.1:5283/api/health',
    [int]$HealthAttempts = 20,
    [int]$HealthDelaySeconds = 2,
    [int]$RetainedRollbackCount = 3
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-SuccessfulNativeCommand {
    param([string]$Description)
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Grant-ServiceReadAccess {
    param([string]$Path, [string]$ServiceAccount)

    & icacls $Path /grant "${ServiceAccount}:(OI)(CI)(RX)" /T /C /Q | Out-Null
    Assert-SuccessfulNativeCommand "Granting read access to $ServiceAccount"
}

function Protect-SecretFile {
    param([string]$Path, [string]$ServiceAccount)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    & icacls $Path /inheritance:r /Q | Out-Null
    Assert-SuccessfulNativeCommand 'Disabling secret-file ACL inheritance'

    & icacls $Path /grant:r 'SYSTEM:(R)' 'Administrators:(F)' "${ServiceAccount}:(R)" /Q | Out-Null
    Assert-SuccessfulNativeCommand 'Applying secret-file ACLs'
}

function Wait-ForHealthyService {
    param([string]$Url, [int]$Attempts, [int]$DelaySeconds)

    $lastError = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                Write-Host "Health check passed on attempt $attempt ($($response.StatusCode))."
                return
            }

            $lastError = "HTTP $($response.StatusCode)"
        } catch {
            $lastError = $_.Exception.Message
        }

        if ($attempt -lt $Attempts) {
            Start-Sleep -Seconds $DelaySeconds
        }
    }

    throw "Health check did not succeed after $Attempts attempts. Last error: $lastError"
}

if (-not (Test-IsAdministrator)) {
    throw 'Deployment runner must be running with local administrator rights to stop/start the service and maintain ACLs.'
}

$package = (Resolve-Path -LiteralPath $PackagePath).Path
$service = Get-Service -Name $ServiceName -ErrorAction Stop

$packageExe = Join-Path $package 'BZAPI.exe'
$packageIndex = Join-Path $package 'wwwroot\index.html'
$packageMarker = Join-Path $package '_deployment_commit.txt'
$packageSecret = Join-Path $package 'appsettings.Production.json'

if (-not (Test-Path -LiteralPath $packageExe)) {
    throw "Deployment package is missing BZAPI.exe: $packageExe"
}
if (-not (Test-Path -LiteralPath $packageIndex)) {
    throw "Deployment package is missing the Angular production bundle: $packageIndex"
}
if (-not (Test-Path -LiteralPath $packageMarker)) {
    throw "Deployment package is missing commit marker: $packageMarker"
}
if (Test-Path -LiteralPath $packageSecret) {
    throw 'Refusing to deploy: the CI artifact unexpectedly contains appsettings.Production.json.'
}

$artifactReleaseId = (Get-Content -LiteralPath $packageMarker -Raw).Trim()
if ($artifactReleaseId -ne $ReleaseId) {
    throw "Artifact commit mismatch. Expected $ReleaseId but package reports $artifactReleaseId."
}

$currentPath = Join-Path $InstallRoot 'current'
$dataPath = Join-Path $InstallRoot 'data'
$releasesPath = Join-Path $InstallRoot 'releases'
$rollbackPath = Join-Path $InstallRoot 'rollback'
$serviceAccount = "NT SERVICE\$ServiceName"
$releasePath = Join-Path $releasesPath $ReleaseId
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$shortReleaseId = $ReleaseId.Substring(0, [Math]::Min(8, $ReleaseId.Length))
$previousPath = Join-Path $rollbackPath "previous-$timestamp-$shortReleaseId"
$failedPath = Join-Path $releasesPath "failed-$timestamp-$shortReleaseId"

New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
New-Item -ItemType Directory -Path $releasesPath -Force | Out-Null
New-Item -ItemType Directory -Path $rollbackPath -Force | Out-Null

if (Test-Path -LiteralPath $releasePath) {
    Remove-Item -LiteralPath $releasePath -Recurse -Force
}
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
Copy-Item -Path (Join-Path $package '*') -Destination $releasePath -Recurse -Force

$currentSecret = Join-Path $currentPath 'appsettings.Production.json'
$releaseSecret = Join-Path $releasePath 'appsettings.Production.json'
if (Test-Path -LiteralPath $currentSecret) {
    Copy-Item -LiteralPath $currentSecret -Destination $releaseSecret -Force
    Write-Host 'Carried forward the existing protected production configuration.'
} else {
    Write-Warning 'No existing appsettings.Production.json was found; Steam avatar enrichment may remain unavailable.'
}

Write-Host "Prepared release $ReleaseId at $releasePath"

$hadCurrentRelease = Test-Path -LiteralPath $currentPath
$serviceWasRunning = $service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
$swapped = $false

try {
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Write-Host "Stopping $ServiceName..."
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
    }

    if ($hadCurrentRelease) {
        Move-Item -LiteralPath $currentPath -Destination $previousPath
        Write-Host "Previous release moved to $previousPath"
    }

    Move-Item -LiteralPath $releasePath -Destination $currentPath
    $swapped = $true

    Grant-ServiceReadAccess -Path $currentPath -ServiceAccount $serviceAccount
    Protect-SecretFile -Path (Join-Path $currentPath 'appsettings.Production.json') -ServiceAccount $serviceAccount

    Write-Host "Starting $ServiceName on release $ReleaseId..."
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))

    Wait-ForHealthyService -Url $HealthUrl -Attempts $HealthAttempts -DelaySeconds $HealthDelaySeconds

    $deployedMarker = Join-Path $currentPath '_deployment_commit.txt'
    $deployedRelease = (Get-Content -LiteralPath $deployedMarker -Raw).Trim()
    if ($deployedRelease -ne $ReleaseId) {
        throw "Post-deploy commit verification failed. Current release reports $deployedRelease."
    }

    Write-Host "Deployment succeeded: $ReleaseId"
} catch {
    $deploymentError = $_
    Write-Warning "Deployment failed: $($deploymentError.Exception.Message). Attempting rollback."

    try {
        $currentService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($currentService -and $currentService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name $ServiceName -Force
            (Get-Service -Name $ServiceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
        }

        if ($swapped -and (Test-Path -LiteralPath $currentPath)) {
            Move-Item -LiteralPath $currentPath -Destination $failedPath
            Write-Warning "Failed release retained at $failedPath"
        }

        if ($hadCurrentRelease -and (Test-Path -LiteralPath $previousPath)) {
            Move-Item -LiteralPath $previousPath -Destination $currentPath
            Grant-ServiceReadAccess -Path $currentPath -ServiceAccount $serviceAccount
            Protect-SecretFile -Path (Join-Path $currentPath 'appsettings.Production.json') -ServiceAccount $serviceAccount

            Start-Service -Name $ServiceName
            (Get-Service -Name $ServiceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
            Wait-ForHealthyService -Url $HealthUrl -Attempts $HealthAttempts -DelaySeconds $HealthDelaySeconds
            Write-Warning 'Rollback succeeded; the previous release is live again.'
        } elseif ($serviceWasRunning) {
            Write-Warning 'There was no previous release directory available for rollback.'
        }
    } catch {
        Write-Warning "Rollback also failed: $($_.Exception.Message)"
    }

    throw $deploymentError
}

# Keep a few previous releases for emergency/manual rollback without allowing the directory to grow forever.
$oldRollbacks = Get-ChildItem -LiteralPath $rollbackPath -Directory -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip ([Math]::Max(0, $RetainedRollbackCount))

foreach ($oldRollback in $oldRollbacks) {
    Remove-Item -LiteralPath $oldRollback.FullName -Recurse -Force
}
