# B4A Trading Bot — 一鍵啟動
#
# 做的事：
#   1. 確保 Docker Desktop 在跑
#   2. 確保交易堆疊 (broker/quote/strategy/risk/trading) 在 localhost:5100
#   3. 從 bot workspace 啟動 claude --channels（載入 B4A Trading Bot persona）
#      使用 --permission-mode bypassPermissions 避免每次按 yes
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File "C:\Users\USER\discord-bots\claude\workspace\start-bot.ps1"

$ErrorActionPreference = "Stop"

# 路徑自動從腳本位置推導，repo 內外都能跑
$botWorkspace = $PSScriptRoot
$claudeRoot   = Split-Path -Parent $PSScriptRoot                     # .../discord-bots/claude
$botsRoot     = Split-Path -Parent $claudeRoot                        # .../discord-bots
$aiProjectGuess = Split-Path -Parent $botsRoot                        # .../AI_Project (if inside repo)
if (Test-Path (Join-Path $aiProjectGuess "tools\compose.trading.yml")) {
    $aiProject = $aiProjectGuess
} else {
    $aiProject = "C:\Users\USER\Desktop\AI_Project"
}
$composeFile  = Join-Path $aiProject "tools\compose.trading.yml"
$envFile      = Join-Path $aiProject "tools\.env.trading"
$brokerUrl    = "http://localhost:5100/api/v1/health/workers"
$channelsArg  = "plugin:discord@claude-plugins-official"

function Write-Step($msg) {
    Write-Host ""
    Write-Host "=== $msg ===" -ForegroundColor Cyan
}

function Test-BrokerUp {
    try {
        $resp = Invoke-WebRequest -Uri $brokerUrl -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
        return $resp.StatusCode -eq 200
    } catch {
        return $false
    }
}

# --- 1. Docker Desktop ---
Write-Step "Checking Docker Desktop"
$dockerUp = $false
try {
    docker version --format '{{.Server.Version}}' 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $dockerUp = $true }
} catch {}

if (-not $dockerUp) {
    Write-Host "Docker not running. Starting Docker Desktop..."
    $dockerExe = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    if (Test-Path $dockerExe) {
        Start-Process $dockerExe
    } else {
        Write-Host "Docker Desktop executable not found at $dockerExe" -ForegroundColor Yellow
    }

    Write-Host "Waiting for Docker daemon (up to 120s)..."
    $waited = 0
    while ($waited -lt 120) {
        Start-Sleep -Seconds 5
        $waited += 5
        try {
            docker version --format '{{.Server.Version}}' 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { $dockerUp = $true; break }
        } catch {}
        Write-Host "  ... $waited s"
    }
    if (-not $dockerUp) {
        Write-Host "Docker did not become ready in time. Abort." -ForegroundColor Red
        exit 1
    }
}
Write-Host "Docker OK." -ForegroundColor Green

# --- 2. Trading stack ---
Write-Step "Checking trading stack (localhost:5100)"
if (Test-BrokerUp) {
    Write-Host "Broker already responsive at $brokerUrl" -ForegroundColor Green
} else {
    Write-Host "Broker not responding. Bringing stack up..."
    if (-not (Test-Path $envFile)) {
        Write-Host "Missing $envFile - create it from tools/.env.trading.example first." -ForegroundColor Red
        exit 1
    }
    Push-Location $aiProject
    try {
        docker compose -f $composeFile --env-file $envFile up -d
        if ($LASTEXITCODE -ne 0) {
            Write-Host "docker compose up failed." -ForegroundColor Red
            exit 1
        }
    } finally {
        Pop-Location
    }

    Write-Host "Waiting for broker to become healthy (up to 90s)..."
    $waited = 0
    while ($waited -lt 90) {
        Start-Sleep -Seconds 3
        $waited += 3
        if (Test-BrokerUp) { break }
        Write-Host "  ... $waited s"
    }
    if (-not (Test-BrokerUp)) {
        Write-Host "Broker still not responsive after 90s. Check: docker compose -f `"$composeFile`" logs" -ForegroundColor Yellow
    } else {
        Write-Host "Broker is up." -ForegroundColor Green
    }
}

# --- 3. Launch claude from bot workspace ---
Write-Step "Launching Claude Code with Discord channel"
Write-Host "Workspace:        $botWorkspace"
Write-Host "Channels:         $channelsArg"
Write-Host "Permission mode:  bypassPermissions (no yes/no prompts)"
Write-Host ""

Set-Location $botWorkspace
& claude --channels $channelsArg --permission-mode bypassPermissions
