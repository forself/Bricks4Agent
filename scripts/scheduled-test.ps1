# scheduled-test.ps1 — 排程自動測試 + 產報告
#
# 用法（手動執行）：
#   .\scripts\scheduled-test.ps1
#
# 設定 Windows 排程（每天凌晨 0:00 執行）：
#   .\scripts\scheduled-test.ps1 -Install
#
# 移除排程：
#   .\scripts\scheduled-test.ps1 -Uninstall

param(
    [switch]$Install,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# ── 安裝/移除 Windows 排程任務 ──────────────────────────────────
if ($Install) {
    $taskName = "B4A-ScheduledTest"
    $script = Join-Path $root "scripts\scheduled-test.ps1"
    $action = New-ScheduledTaskAction `
        -Execute "powershell.exe" `
        -Argument "-ExecutionPolicy Bypass -File `"$script`"" `
        -WorkingDirectory $root

    # 每天凌晨 0:00
    $trigger = New-ScheduledTaskTrigger -Daily -At "00:00"

    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -StartWhenAvailable `
        -ExecutionTimeLimit (New-TimeSpan -Minutes 30)

    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Trigger $trigger `
        -Settings $settings `
        -Description "Bricks4Agent 自動測試排程 — 每天凌晨執行測試並產生報告" `
        -Force

    Write-Host "✓ 排程已建立: $taskName (每天 00:00)" -ForegroundColor Green
    Write-Host "  查看: taskschd.msc → 工作排程器程式庫 → $taskName" -ForegroundColor Cyan
    exit 0
}

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName "B4A-ScheduledTest" -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "✓ 排程已移除" -ForegroundColor Green
    exit 0
}

# ── 執行測試 ────────────────────────────────────────────────────
Set-Location $root
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
Write-Host ""
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  B4A Scheduled Test Run — $timestamp" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# 執行測報產生器
dotnet run --project packages/csharp/tests/test-report-generator/TestReportGenerator.csproj

$exitCode = $LASTEXITCODE

# 產出結果
Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "✓ 測試全部通過" -ForegroundColor Green
} else {
    Write-Host "✗ 有測試失敗" -ForegroundColor Red
}

# 列出最新報告
$latestReport = Get-ChildItem "docs/reports/TestReport-*.md" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1
if ($latestReport) {
    Write-Host "  報告: $($latestReport.FullName)" -ForegroundColor Cyan
}
Write-Host "  SQLite: .test-output/test-reports.db" -ForegroundColor Cyan
Write-Host ""

exit $exitCode
