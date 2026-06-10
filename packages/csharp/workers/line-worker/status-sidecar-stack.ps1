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
$cloudflaredPidFile = Join-Path $runRoot "cloudflared.pid"
$localhostRunPidFile = Join-Path $runRoot "localhostrun.pid"
$webhookSyncPidFile = Join-Path $runRoot "webhook-sync.pid"
$configPath = Join-Path $scriptDir "appsettings.json"
$tunnelName = "line$WebhookPort"
$logDir = Join-Path $runRoot "logs"

function Get-LatestLocalhostRunEndpoint {
    param([string]$Directory)

    $combined = ""
    foreach ($name in @("localhostrun.out.log", "localhostrun.err.log")) {
        $path = Join-Path $Directory $name
        if (Test-Path $path) {
            $combined += "`n" + (Get-Content $path -Raw -ErrorAction SilentlyContinue)
        }
    }

    $matches = [regex]::Matches($combined, "https://[a-zA-Z0-9-]+\.lhr\.life")
    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[$matches.Count - 1].Value.TrimEnd("/") + "/webhook/line/"
}

function Get-RecordedProcessStatus {
    param([string]$PidFile, [string]$Label)

    if (-not (Test-Path $PidFile)) {
        return [pscustomobject]@{
            Label = $Label
            RecordedPid = $null
            Running = $false
            StartTime = $null
        }
    }

    $procId = (Get-Content $PidFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($procId)) {
        return [pscustomobject]@{
            Label = $Label
            RecordedPid = $null
            Running = $false
            StartTime = $null
        }
    }

    $proc = Get-Process -Id ([int]$procId) -ErrorAction SilentlyContinue
    return [pscustomobject]@{
        Label = $Label
        RecordedPid = [int]$procId
        Running = $null -ne $proc
        StartTime = if ($proc) { $proc.StartTime } else { $null }
    }
}

$rows = @(
    Get-RecordedProcessStatus -PidFile $brokerPidFile -Label "broker"
    Get-RecordedProcessStatus -PidFile $workerPidFile -Label "line-worker"
    Get-RecordedProcessStatus -PidFile $ngrokPidFile -Label "ngrok"
    Get-RecordedProcessStatus -PidFile $cloudflaredPidFile -Label "cloudflared"
    Get-RecordedProcessStatus -PidFile $localhostRunPidFile -Label "localhost.run"
    Get-RecordedProcessStatus -PidFile $webhookSyncPidFile -Label "webhook-sync"
)
$rows | Format-Table -AutoSize

try {
    $ngrok = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels"
    $tunnel = $ngrok.tunnels | Where-Object { $_.name -eq $tunnelName }
    if ($tunnel) {
        Write-Host ""
        Write-Host ("ngrok tunnel: {0} -> {1}" -f $tunnel.name, $tunnel.config.addr) -ForegroundColor Cyan
        Write-Host ("public url:   {0}" -f $tunnel.public_url) -ForegroundColor Cyan
    }
} catch {
    Write-Host ""
    Write-Host "ngrok admin API not available." -ForegroundColor Yellow
}

if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    $token = $config.Line.ChannelAccessToken
    if ($token -and $token -notlike "REPLACE_WITH_*") {
        try {
            $endpoint = Invoke-RestMethod -Method Get -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" -Headers @{ Authorization = "Bearer $token" }
            Write-Host ""
            Write-Host ("LINE webhook endpoint: {0}" -f $endpoint.endpoint) -ForegroundColor Cyan
            Write-Host ("LINE webhook active:   {0}" -f $endpoint.active) -ForegroundColor Cyan
            $latestLocalhostRunEndpoint = Get-LatestLocalhostRunEndpoint -Directory $logDir
            if ($latestLocalhostRunEndpoint) {
                Write-Host ("latest localhost.run:  {0}" -f $latestLocalhostRunEndpoint) -ForegroundColor Cyan
                if ($endpoint.endpoint -ne $latestLocalhostRunEndpoint) {
                    Write-Host "LINE webhook does not match latest localhost.run URL; sync watchdog should update it shortly." -ForegroundColor Yellow
                }
            }
        } catch {
            Write-Host ""
            Write-Host "Failed to query LINE webhook endpoint." -ForegroundColor Yellow
        }
    }
}
