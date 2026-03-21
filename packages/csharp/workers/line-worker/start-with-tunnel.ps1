#Requires -Version 5.1
<#
.SYNOPSIS
    ?? cloudflared Quick Tunnel + LINE Worker嚗???LINE Webhook URL??

.DESCRIPTION
    1. ?岫?? cloudflared quick tunnel嚗??荔?
    2. ??Cloudflare API 銝?剁?fallback ?唬?甈⊥??? URL ???撓??
    3. ?澆 LINE API ?湔 webhook endpoint
    4. ?? LINE Worker嚗??荔?
    5. Ctrl+C ??????cloudflared

.PARAMETER WebhookPort
    本地 webhook 監聽埠，預設 5357

.PARAMETER TunnelUrl
    ???? tunnel URL嚗歲??cloudflared ??

.PARAMETER ConfigPath
    appsettings.json 頝臬?嚗?閮剔?單?????appsettings.json
#>
param(
    [int]$WebhookPort = 5357,
    [string]$TunnelUrl = "",
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $scriptDir "appsettings.json"
}

# 銝活????URL 敹怠?瑼?
$lastUrlFile = Join-Path $scriptDir ".last-tunnel-url"

# ?? 霈?身摰???
$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
$channelAccessToken = $config.Line.ChannelAccessToken
$channelSecret = $config.Line.ChannelSecret

if (-not $channelAccessToken -or -not $channelSecret) {
    Write-Error "ChannelAccessToken and ChannelSecret are required in $ConfigPath"
    exit 1
}

# 敺?config 霈??port嚗?芷????嚗?
if ($WebhookPort -eq 5357 -and $config.Line.WebhookPort) {
    $WebhookPort = [int]$config.Line.WebhookPort
}

# ?? ??cloudflared ??
$cloudflared = $null
foreach ($p in @(
    "cloudflared",
    "C:\Program Files (x86)\cloudflared\cloudflared.exe",
    "C:\Program Files\cloudflared\cloudflared.exe",
    "$env:USERPROFILE\cloudflared.exe"
)) {
    if (Get-Command $p -ErrorAction SilentlyContinue) {
        $cloudflared = $p
        break
    }
    if (Test-Path $p) {
        $cloudflared = $p
        break
    }
}

Write-Host "=== Bricks4Agent LINE Worker Launcher ===" -ForegroundColor Cyan
Write-Host "Config:      $ConfigPath"
Write-Host "Webhook port: $WebhookPort"
Write-Host ""

$tunnelProcess = $null
$tunnelUrl = $TunnelUrl

# ?? Step 1: Tunnel ??
if ($tunnelUrl) {
    Write-Host "[1/3] Using provided tunnel URL: $tunnelUrl" -ForegroundColor Yellow
} else {
    if (-not $cloudflared) {
        Write-Host "[1/3] cloudflared not found, skipping tunnel" -ForegroundColor Red
    } else {
        Write-Host "[1/3] Starting cloudflared quick tunnel..." -ForegroundColor Yellow
        Write-Host "  Cloudflared: $cloudflared"

        $maxAttempts = 3
        $attempt = 0

        while ($attempt -lt $maxAttempts -and -not $tunnelUrl) {
            $attempt++
            $tunnelLogFile = Join-Path $env:TEMP "cloudflared-tunnel-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
            $tunnelProcess = Start-Process -FilePath $cloudflared `
                -ArgumentList "tunnel", "--url", "http://localhost:$WebhookPort" `
                -RedirectStandardError $tunnelLogFile `
                -PassThru -NoNewWindow

            Write-Host "  Attempt $attempt/$maxAttempts (PID: $($tunnelProcess.Id))..." -NoNewline

            $maxWait = 20
            $waited = 0
            $failed = $false

            while ($waited -lt $maxWait) {
                Start-Sleep -Seconds 1
                $waited++
                Write-Host "." -NoNewline

                if (Test-Path $tunnelLogFile) {
                    $logContent = Get-Content $tunnelLogFile -Raw -ErrorAction SilentlyContinue
                    if ($logContent -match 'https://[a-z0-9-]+\.trycloudflare\.com') {
                        $tunnelUrl = $Matches[0]
                        break
                    }
                    if ($logContent -match 'failed to unmarshal quick Tunnel') {
                        $failed = $true
                        break
                    }
                }
            }

            Write-Host ""

            if ($failed -or (-not $tunnelUrl)) {
                Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
                $tunnelProcess = $null
                if ($attempt -lt $maxAttempts) {
                    $backoff = $attempt * 10
                    Write-Host "  Cloudflare API unavailable, waiting ${backoff}s before retry..." -ForegroundColor Red
                    Start-Sleep -Seconds $backoff
                }
            }
        }
    }

    # Fallback: ?岫銝活????URL ?閰?LINE ?桀?閮剖?
    if (-not $tunnelUrl) {
        Write-Host ""
        Write-Host "  Could not create new tunnel." -ForegroundColor Red

        # ?岫霈??LINE ?桀?閮剖???webhook
        $currentWebhook = $null
        try {
            $headers = @{
                "Authorization" = "Bearer $channelAccessToken"
                "Content-Type"  = "application/json"
            }
            $whInfo = Invoke-RestMethod -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" -Method GET -Headers $headers
            if ($whInfo.endpoint -match 'trycloudflare\.com') {
                $currentWebhook = $whInfo.endpoint
            }
        } catch {}

        # ?岫銝活敹怠?
        $lastUrl = $null
        if (Test-Path $lastUrlFile) {
            $lastUrl = (Get-Content $lastUrlFile -Raw).Trim()
        }

        # ???賊?
        Write-Host ""
        Write-Host "  Options:" -ForegroundColor Yellow
        if ($currentWebhook) {
            Write-Host "    [1] Use current LINE webhook: $currentWebhook" -ForegroundColor DarkGray
        }
        if ($lastUrl -and $lastUrl -ne $currentWebhook) {
            Write-Host "    [2] Use last cached URL: $lastUrl" -ForegroundColor DarkGray
        }
        Write-Host "    [M] Manually start cloudflared in another terminal and paste URL" -ForegroundColor DarkGray
        Write-Host "    [S] Skip tunnel, start worker only (webhook won't work from outside)" -ForegroundColor DarkGray
        Write-Host ""

        $choice = Read-Host "  Choose"

        switch ($choice.ToUpper()) {
            "1" {
                if ($currentWebhook) {
                    if ($currentWebhook -match '(https://[a-z0-9-]+\.trycloudflare\.com)') {
                        $tunnelUrl = $Matches[1]
                    }
                }
            }
            "2" { $tunnelUrl = $lastUrl }
            "M" {
                Write-Host ""
                Write-Host "  In another terminal, run:" -ForegroundColor Cyan
                Write-Host "    cloudflared tunnel --url http://localhost:$WebhookPort" -ForegroundColor White
                Write-Host ""
                $tunnelUrl = Read-Host "  Paste the tunnel URL (https://xxx.trycloudflare.com)"
            }
            "S" {
                Write-Host "  Skipping tunnel setup." -ForegroundColor Yellow
            }
            default {
                Write-Host "  Skipping tunnel setup." -ForegroundColor Yellow
            }
        }
    }
}

# ?? 敹怠?????URL ??
if ($tunnelUrl) {
    $tunnelUrl | Out-File -FilePath $lastUrlFile -Encoding utf8 -NoNewline
}

# ?? Step 2: ?湔 LINE Webhook ??
if ($tunnelUrl) {
    $webhookUrl = if ($tunnelUrl -match '/webhook/line') { $tunnelUrl } else { "$tunnelUrl/webhook/line/" }

    Write-Host ""
    Write-Host "  Tunnel URL: $tunnelUrl" -ForegroundColor Green
    Write-Host "  Webhook URL: $webhookUrl" -ForegroundColor Green
    Write-Host ""
    Write-Host "[2/3] Updating LINE webhook endpoint..." -ForegroundColor Yellow

    $updateBody = "{`"endpoint`":`"$webhookUrl`"}"
    $headers = @{
        "Authorization" = "Bearer $channelAccessToken"
        "Content-Type"  = "application/json"
    }

    try {
        Invoke-RestMethod -Uri "https://api.line.me/v2/bot/channel/webhook/endpoint" `
            -Method PUT -Body ([System.Text.Encoding]::UTF8.GetBytes($updateBody)) -Headers $headers | Out-Null
        Write-Host "  LINE webhook updated successfully" -ForegroundColor Green
    } catch {
        Write-Host "  Warning: Failed to update LINE webhook: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Please update manually in LINE Developers Console" -ForegroundColor Red
    }

    # 撽?
    try {
        $testResult = Invoke-RestMethod -Uri "https://api.line.me/v2/bot/channel/webhook/test" `
            -Method POST -Body "{`"endpoint`":`"$webhookUrl`"}" -Headers $headers
        if ($testResult.success) {
            Write-Host "  LINE webhook verify: OK" -ForegroundColor Green
        } else {
            Write-Host "  LINE webhook verify: $($testResult.reason)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Webhook verify skipped: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "[2/3] No tunnel URL, skipping LINE webhook update" -ForegroundColor Yellow
}

Write-Host ""

# ?? Step 3: ?? LINE Worker ??
Write-Host "[3/3] Starting LINE Worker..." -ForegroundColor Yellow
Write-Host "  Press Ctrl+C to stop" -ForegroundColor DarkGray
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$cleanup = {
    Write-Host ""
    Write-Host "Shutting down..." -ForegroundColor Yellow
    if ($tunnelProcess -and -not $tunnelProcess.HasExited) {
        Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host "  Cloudflared stopped" -ForegroundColor DarkGray
    }
}

try {
    $dotnetArgs = @("run", "--project", $scriptDir)
    $workerProcess = Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs `
        -NoNewWindow -PassThru -Wait

    Write-Host "LINE Worker exited with code: $($workerProcess.ExitCode)"
} finally {
    & $cleanup
}
