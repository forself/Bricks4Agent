<#
.SYNOPSIS
  把「去相關精選 4 支」一鍵組合(decorr4_ls 淨加權 ensemble)佈署到 AutoTrader 的 12 個 symbol。
  預設 SHADOW 模式(評估訊號、絕不下真單)。要真上線必須顯式加 -Live 並二次確認。

.DESCRIPTION
  decorr4_ls = dual_mom_ls 38% + dual_thrust 32% + bb_revert_ls 19% + fib_retrace_ls 10% 的淨加權曝險。
  搭配 AUTOTRADER_CONFIDENCE_SIZING_ENABLED=true,部位會隨「淨曝險強度」縮放(分歧縮量)→
  單一 watch 即復刻風險加權組合(回測 Sharpe~0.59 / maxDD~51%,對照真組合 0.62/46%)。

  本腳本只呼叫既有 /api/v1/auto-trader/watch(upsert、冪等),不改任何程式。先 shadow 跑數週對帳、
  確認 live 訊號≈回測,再考慮 -Live。

.PARAMETER Broker     broker base URL(預設 http://localhost:5100)
.PARAMETER Token      scoped_token 或 session cookie 值(授權用;本機 dev 可留空走 admin fallback)
.PARAMETER Leverage   perp 槓桿(預設 2;真錢建議低槓桿)
.PARAMETER Live       ★危險★ 關閉 shadow、真實下單。需再輸入 'YES' 確認。

.EXAMPLE
  # 安全:shadow 佈署(只觀察)
  ./deploy-decorr4.ps1 -Broker http://localhost:5100 -Token $env:B4A_TOKEN

  # 真上線(會二次確認)
  ./deploy-decorr4.ps1 -Broker https://your-broker -Token $env:B4A_TOKEN -Live
#>
param(
    [string]$Broker = "http://localhost:5100",
    [string]$Token = "",
    [int]$Leverage = 2,
    [switch]$Live
)

$ErrorActionPreference = "Stop"

$symbols = @("BTCUSDT","ETHUSDT","SOLUSDT","BNBUSDT","XRPUSDT","ADAUSDT",
             "DOGEUSDT","AVAXUSDT","LINKUSDT","LTCUSDT","DOTUSDT","ATOMUSDT")

$shadow = -not $Live
if ($Live) {
    Write-Host "⚠  你選了 -Live:這會讓 decorr4_ls 在 12 個 symbol 真實下單(BingX 真錢)。" -ForegroundColor Red
    $confirm = Read-Host "   確定請輸入大寫 YES"
    if ($confirm -ne "YES") { Write-Host "已取消。" -ForegroundColor Yellow; exit 1 }
    Write-Host "   已確認真實下單模式。" -ForegroundColor Red
} else {
    Write-Host "✅ SHADOW 模式:只評估訊號、絕不下單(對帳用)。要真上線請加 -Live。" -ForegroundColor Green
}

$headers = @{ "Content-Type" = "application/json" }
if ($Token -ne "") { $headers["Authorization"] = "Bearer $Token"; $headers["Cookie"] = "scoped_token=$Token" }

Write-Host "`n建議先設這些環境變數(讓淨加權曝險真正縮放部位 + 控風險):" -ForegroundColor Cyan
Write-Host "  AUTOTRADER_CONFIDENCE_SIZING_ENABLED=true   # 部位隨淨曝險強度縮放(分歧縮量)"
Write-Host "  AUTOTRADER_MIN_CONFIDENCE=0.55              # decorr4_ls hold=0.5、進場 0.6+,0.55 放行"
Write-Host "  AUTOTRADER_DYNAMIC_RISK_PCT=2              # 每筆最大虧損 % of balance"
Write-Host "  AUTOTRADER_MAX_PORTFOLIO_RISK_PCT=6        # 組合總風險上限"
Write-Host "  AUTOTRADER_MAX_OPEN_POSITIONS=12           # 12 檔都能同時持倉`n"

$ok = 0; $fail = 0
foreach ($sym in $symbols) {
    $body = @{
        symbol   = $sym
        exchange = "bingx"
        strategy = "decorr4_ls"
        mode     = "perp_both"     # 多空都開
        leverage = $Leverage
        shadow   = $shadow
    } | ConvertTo-Json -Compress
    try {
        $resp = Invoke-RestMethod -Uri "$Broker/api/v1/auto-trader/watch" -Method Post -Headers $headers -Body $body
        $tag = if ($shadow) { "SHADOW" } else { "LIVE" }
        Write-Host ("  [{0}] {1,-9} decorr4_ls perp_both x{2}  → OK" -f $tag, $sym, $Leverage) -ForegroundColor Green
        $ok++
    } catch {
        Write-Host ("  {0,-9} → 失敗: {1}" -f $sym, $_.Exception.Message) -ForegroundColor Red
        $fail++
    }
}

Write-Host "`n完成:$ok 成功 / $fail 失敗。" -ForegroundColor Cyan
if ($shadow) {
    Write-Host "下一步:dashboard 看 [SHADOW] 日誌對帳數週,確認 live 訊號≈回測後再 -Live。" -ForegroundColor Yellow
} else {
    Write-Host "已真上線。務必盯 dashboard + 確認每筆風險上限(RISK_MAX_LOSS_PER_TRADE_PCT)生效。" -ForegroundColor Red
}
Write-Host "移除:對每個 symbol 呼叫 DELETE /api/v1/auto-trader/watch?symbol=...&exchange=bingx"
