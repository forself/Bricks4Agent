#Requires -Version 5.1
param(
    [int]$WebhookPort = 5357
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..\..\..\..")
$runRoot = Join-Path $repoRoot ".run\line-sidecar"
$brokerPidFile = Join-Path $runRoot "broker.pid"
$workerPidFile = Join-Path $runRoot "line-worker.pid"
$tunnelName = "line$WebhookPort"

function Stop-RecordedProcess {
    param([string]$PidFile, [string]$Label)

    if (-not (Test-Path $PidFile)) {
        Write-Host "$Label PID file not found: $PidFile" -ForegroundColor Yellow
        return
    }

    $procId = (Get-Content $PidFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($procId)) {
        Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
        Write-Host "$Label PID file was empty." -ForegroundColor Yellow
        return
    }

    try {
        Stop-Process -Id ([int]$procId) -Force -ErrorAction Stop
        Write-Host "$Label stopped: PID $procId" -ForegroundColor Green
    } catch {
        Write-Warning ("Failed to stop {0} PID {1}: {2}" -f $Label, $procId, $_.Exception.Message)
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

function Remove-NgrokTunnel {
    param([string]$Name)

    try {
        Invoke-RestMethod -Method Delete -Uri "http://127.0.0.1:4040/api/tunnels/$Name" | Out-Null
        Write-Host "ngrok tunnel removed: $Name" -ForegroundColor Green
    } catch {
        Write-Host "ngrok tunnel not removed: $Name" -ForegroundColor Yellow
    }
}

Stop-RecordedProcess -PidFile $workerPidFile -Label "line-worker"
Stop-RecordedProcess -PidFile $brokerPidFile -Label "broker"
Remove-NgrokTunnel -Name $tunnelName
