# bot-node helper script (English-only to avoid PS5.1 UTF-8 BOM issues)
# Usage: PowerShell -ExecutionPolicy Bypass -File .\setup.ps1
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $here)
$claudeEnv = Join-Path $projectRoot "discord-bots\claude\.env"
$myEnv = Join-Path $here ".env"
$myAccess = Join-Path $here "access.json"
$myCompose = Join-Path $here "docker\compose.sandboxed.yml"
$claudeCompose = Join-Path $projectRoot "discord-bots\claude\docker\compose.sandboxed.yml"

Write-Host "==> bot-node setup" -ForegroundColor Cyan

# 1. Docker check
Write-Host "[1/6] check docker daemon..." -ForegroundColor Yellow
docker version --format "{{.Server.Version}}" 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "    FAIL docker daemon not reachable. Open Docker Desktop." -ForegroundColor Red
    exit 1
}
Write-Host "    OK" -ForegroundColor Green

# 2. trading network check
Write-Host "[2/6] check b4a-trading-net..." -ForegroundColor Yellow
$net = docker network ls --format "{{.Name}}" | Select-String -Pattern "^b4a-trading-net$"
if (-not $net) {
    Write-Host "    FAIL b4a-trading-net missing. Start trading stack first:" -ForegroundColor Red
    Write-Host "    docker compose -f tools\compose.trading.yml --env-file tools\.env.trading up -d" -ForegroundColor Yellow
    exit 1
}
Write-Host "    OK" -ForegroundColor Green

# 3. token from claude bot env
Write-Host "[3/6] copy DISCORD_BOT_TOKEN from claude bot env..." -ForegroundColor Yellow
if (-not (Test-Path $myEnv)) {
    if (-not (Test-Path $claudeEnv)) {
        Write-Host "    FAIL $claudeEnv not found, manual edit needed" -ForegroundColor Red
        exit 1
    }
    Get-Content $claudeEnv | Where-Object { $_ -match "^DISCORD_BOT_TOKEN=" } | Set-Content $myEnv -Encoding UTF8
    Write-Host "    OK copied" -ForegroundColor Green
} else {
    Write-Host "    OK already exists" -ForegroundColor Green
}

# 4. access.json
Write-Host "[4/6] ensure access.json..." -ForegroundColor Yellow
if (-not (Test-Path $myAccess)) {
    Copy-Item (Join-Path $here "access.json.example") $myAccess
    Write-Host "    OK created from example" -ForegroundColor Green
} else {
    Write-Host "    OK already exists" -ForegroundColor Green
}

# 5. stop existing claude bot
Write-Host "[5/6] stop claude bot if running..." -ForegroundColor Yellow
$claudeBot = docker ps --filter "name=b4a-discord-bot" --format "{{.Names}}"
if ($claudeBot) {
    docker compose -f $claudeCompose --env-file $claudeEnv down 2>$null | Out-Null
    Write-Host "    OK claude bot stopped" -ForegroundColor Green
} else {
    Write-Host "    OK not running" -ForegroundColor Green
}

# 6. build + up
Write-Host "[6/6] build + start bot-node (first build ~2-3 min)..." -ForegroundColor Yellow
docker compose -f $myCompose --env-file $myEnv up -d --build
if ($LASTEXITCODE -ne 0) {
    Write-Host "    FAIL see error above" -ForegroundColor Red
    exit 1
}
Write-Host "    OK started" -ForegroundColor Green

Write-Host ""
Write-Host "==> tailing logs (Ctrl+C to stop tailing, container keeps running)" -ForegroundColor Cyan
docker compose -f $myCompose --env-file $myEnv logs -f
