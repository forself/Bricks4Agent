# bot-node 一鍵設定 + 啟動腳本
#
# 用法：右鍵此檔 → "用 PowerShell 執行"，或在 PowerShell 裡：
#   cd C:\Users\USER\Desktop\AI_Project\discord-bots\bot-node
#   .\setup.ps1
#
# 它會：
#   1. 檢查 Docker Desktop 在跑
#   2. 檢查 trading 堆疊在跑（需要 b4a-trading-net）
#   3. 自動從 ..\claude\.env 抄 DISCORD_BOT_TOKEN（同個 token、不重新申請）
#   4. 自動建 access.json（預設你的 owner ID）
#   5. 停掉既有 claude bot（搶 token 衝突）
#   6. build + 起 bot-node 容器
#   7. 跟 log

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $here)
$claudeEnv = Join-Path $projectRoot "discord-bots\claude\.env"
$myEnv = Join-Path $here ".env"
$myAccess = Join-Path $here "access.json"
$myCompose = Join-Path $here "docker\compose.sandboxed.yml"
$claudeCompose = Join-Path $projectRoot "discord-bots\claude\docker\compose.sandboxed.yml"

Write-Host "==> bot-node 設定 / 啟動腳本" -ForegroundColor Cyan
Write-Host "    project root: $projectRoot" -ForegroundColor DarkGray

# Step 1: Docker 在跑嗎
Write-Host "`n[1/7] 檢查 Docker Desktop ..." -ForegroundColor Yellow
try {
    docker version --format '{{.Server.Version}}' | Out-Null
    Write-Host "    ✓ Docker daemon 連得上" -ForegroundColor Green
} catch {
    Write-Host "    ✗ Docker daemon 連不上、請先打開 Docker Desktop。" -ForegroundColor Red
    exit 1
}

# Step 2: trading 堆疊在跑嗎（要 b4a-trading-net）
Write-Host "`n[2/7] 檢查 b4a-trading-net 網路 ..." -ForegroundColor Yellow
$net = docker network ls --format "{{.Name}}" | Select-String -Pattern "^b4a-trading-net$"
if (-not $net) {
    Write-Host "    ✗ b4a-trading-net 不存在、trading 堆疊沒在跑。" -ForegroundColor Red
    Write-Host "    請先跑：" -ForegroundColor Yellow
    Write-Host "      cd $projectRoot" -ForegroundColor Yellow
    Write-Host "      docker compose -f tools\compose.trading.yml --env-file tools\.env.trading up -d" -ForegroundColor Yellow
    exit 1
}
Write-Host "    ✓ b4a-trading-net 在線" -ForegroundColor Green

# Step 3: 抄 token from claude/.env
Write-Host "`n[3/7] 取得 DISCORD_BOT_TOKEN ..." -ForegroundColor Yellow
if (-not (Test-Path $myEnv)) {
    if (-not (Test-Path $claudeEnv)) {
        Write-Host "    ✗ 找不到 $claudeEnv —— 你之前的 claude bot 沒 token？" -ForegroundColor Red
        Write-Host "    請手動複製 .env.example → .env、填入 DISCORD_BOT_TOKEN" -ForegroundColor Yellow
        exit 1
    }
    $tokenLine = Get-Content $claudeEnv | Select-String -Pattern "^DISCORD_BOT_TOKEN="
    if (-not $tokenLine) {
        Write-Host "    ✗ claude/.env 沒有 DISCORD_BOT_TOKEN 那行" -ForegroundColor Red
        exit 1
    }
    Set-Content -Path $myEnv -Value $tokenLine.ToString() -Encoding UTF8
    Write-Host "    ✓ 已從 claude/.env 抄到 $myEnv" -ForegroundColor Green
} else {
    Write-Host "    ✓ $myEnv 已存在、不覆蓋" -ForegroundColor Green
}

# Step 4: 建 access.json
Write-Host "`n[4/7] 確認 access.json ..." -ForegroundColor Yellow
if (-not (Test-Path $myAccess)) {
    Copy-Item (Join-Path $here "access.json.example") $myAccess
    Write-Host "    ✓ 已從 example 建立 access.json（預設你的 owner ID）" -ForegroundColor Green
} else {
    Write-Host "    ✓ access.json 已存在、不覆蓋" -ForegroundColor Green
}

# Step 5: 停掉既有 claude bot（搶 token）
Write-Host "`n[5/7] 停掉既有 claude bot 容器（如果在跑）..." -ForegroundColor Yellow
$claudeBot = docker ps --filter "name=b4a-discord-bot" --format "{{.Names}}"
if ($claudeBot) {
    Write-Host "    偵測到 claude bot 在跑、stopping ..." -ForegroundColor DarkGray
    docker compose -f $claudeCompose --env-file $claudeEnv down 2>&1 | Out-Null
    Write-Host "    ✓ claude bot 已停" -ForegroundColor Green
} else {
    Write-Host "    ✓ claude bot 沒在跑" -ForegroundColor Green
}

# Step 6: build + start
Write-Host "`n[6/7] Build + 啟動 bot-node container（首次 ~2-3 分鐘）..." -ForegroundColor Yellow
docker compose -f $myCompose --env-file $myEnv up -d --build
if ($LASTEXITCODE -ne 0) {
    Write-Host "    ✗ docker compose up 失敗、看上面錯誤訊息" -ForegroundColor Red
    exit 1
}
Write-Host "    ✓ bot-node 已啟動" -ForegroundColor Green

# Step 7: 跟 log
Write-Host "`n[7/7] 跟 log（Ctrl+C 退出 log、容器繼續跑）..." -ForegroundColor Yellow
Write-Host "    預期看到："
Write-Host "      [bot] B4A bot-node starting, phase=2" -ForegroundColor DarkGray
Write-Host "      [access] loaded from /app/access.json: 1 users, ..." -ForegroundColor DarkGray
Write-Host "      [bot] logged in as <bot_name>#XXXX" -ForegroundColor DarkGray
Write-Host ""
docker compose -f $myCompose --env-file $myEnv logs -f
