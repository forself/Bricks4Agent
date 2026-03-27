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
$ngrokPidFile = Join-Path $runRoot "ngrok.pid"
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
        $deadline = (Get-Date).AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 250
            $stillRunning = Get-Process -Id ([int]$procId) -ErrorAction SilentlyContinue
        } while ($stillRunning -and (Get-Date) -lt $deadline)

        if ($stillRunning) {
            Write-Warning ("{0} PID {1} still appears to be running after stop request." -f $Label, $procId)
        } else {
            Write-Host "$Label stopped: PID $procId" -ForegroundColor Green
        }
    } catch {
        Write-Warning ("Failed to stop {0} PID {1}: {2}" -f $Label, $procId, $_.Exception.Message)
    }

    Remove-Item $PidFile -Force -ErrorAction SilentlyContinue
}

function Stop-ProcessByExecutablePath {
    param([string]$ExecutablePath, [string]$Label)

    if (-not (Test-Path $ExecutablePath)) {
        return
    }

    $normalized = [System.IO.Path]::GetFullPath($ExecutablePath)
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $normalized)
        } catch {
            $false
        }
    } | ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
            $deadline = (Get-Date).AddSeconds(20)
            do {
                Start-Sleep -Milliseconds 250
                $stillRunning = Get-Process -Id $_.Id -ErrorAction SilentlyContinue
            } while ($stillRunning -and (Get-Date) -lt $deadline)
            Write-Host "$Label stopped by executable path: PID $($_.Id)" -ForegroundColor Green
        } catch {
            Write-Warning ("Failed to stop {0} PID {1} by executable path: {2}" -f $Label, $_.Id, $_.Exception.Message)
        }
    }
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
Stop-RecordedProcess -PidFile $ngrokPidFile -Label "ngrok"
Stop-ProcessByExecutablePath -ExecutablePath (Join-Path $runRoot "line-worker\\LineWorker.exe") -Label "line-worker"
Stop-ProcessByExecutablePath -ExecutablePath (Join-Path $runRoot "broker\\Broker.exe") -Label "broker"
Remove-NgrokTunnel -Name $tunnelName
