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
$configPath = Join-Path $scriptDir "appsettings.json"
$tunnelName = "line$WebhookPort"

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
        } catch {
            Write-Host ""
            Write-Host "Failed to query LINE webhook endpoint." -ForegroundColor Yellow
        }
    }
}
