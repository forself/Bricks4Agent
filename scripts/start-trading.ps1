# B4A Trading System — Auto-start script
# 用法：加入 Windows 工作排程器，登入時自動執行
#
# 設定方式：
# 1. Win+R → taskschd.msc
# 2. 建立基本工作 → 觸發：登入時 → 動作：啟動程式
# 3. 程式：powershell.exe
# 4. 引數：-ExecutionPolicy Bypass -File "C:\Users\USER\Desktop\AI_Project\scripts\start-trading.ps1"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

# 等 Docker Desktop 啟動
Write-Host "Waiting for Docker Desktop..."
$maxWait = 120
$waited = 0
while ($waited -lt $maxWait) {
    try {
        $version = docker version --format '{{.Server.Version}}' 2>$null
        if ($version) { break }
    } catch {}
    Start-Sleep -Seconds 5
    $waited += 5
}

if ($waited -ge $maxWait) {
    Write-Host "Docker not available after ${maxWait}s. Starting Docker Desktop..."
    Start-Process "C:\Program Files\Docker\Docker\Docker Desktop.exe"
    Start-Sleep -Seconds 30
}

# 啟動交易系統
Set-Location $projectRoot
Write-Host "Starting B4A Trading System..."
docker compose -f tools/compose.trading.yml --env-file tools/.env.trading up -d

Write-Host "B4A Trading System started. Dashboard: http://localhost:5100/trading.html"
