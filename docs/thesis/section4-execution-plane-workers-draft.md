# §4 執行平面與 worker 生態(作者貢獻:7/11 worker)— 草稿 scaffold

> 草稿。grounded on workers/*/Handlers + broker-core/Contracts + git blame。
> **誠實分工**:執行契約 = Benson 核心;worker 實作 = 作者(7 個)。

## 4.1 執行平面契約(Benson 核心)
- `ExecutionRequest` / `ApprovedRequest` / `PipelineExceptions`(git blame = Benson):worker **只收「已驗證、能力範圍內」的結構化意圖**(非原始對話)。
- 核心原則(§3):worker **不能自我擴權、不能直接碰資料源 / 工具 / 部署 / 模型**;只在 broker 派發的 `ApprovedRequest` 範圍內動作。
- ⇒ 執行平面是「被控制平面餵食」的,本身無自主權 = 「零信任 + 能力模型」在架構上的落實。

## 4.2 作者實作的 worker 生態(11 個建 7 個)
| worker | handler | 能力域 |
|---|---|---|
| **quote** | QuoteOhlcv/Indicator/Prices/History/BatchFetch/FetchNow(6) | 市場資料(最豐富) |
| **strategy** | StrategySignal | 策略評估 + 回測引擎 |
| **risk** | RiskCheck | 下單前風險檢查 |
| **trading** | TradingOrder / TradingAccount / TradingPerpetual | 真錢執行(spot + 永續) |
| **telemetry** | TelemetryHistory | 遙測歷史 |
| **code-exec** | CodeExec | **沙箱碼執行**(timeout/ulimit/non-root、require_approval、full-audit) |
| **agent** | (runtime,非 handler 式) | 代理執行 |
| *(Benson)* | — | browser / file / line / transport-tdx |

## 4.3 worker 如何尊重契約(治理在執行層的延續)
- 每個 worker 消費的是**已過 §5c 認證 + §5 審批**的 `ApprovedRequest` → 只在授權能力面動作。
- **code-exec 範例**:對「任意碼執行」這種最高風險能力,用 **require_approval + 沙箱(timeout/ulimit/non-root)+ full-audit** 達成「**受控的危險能力**」——示範平台「能力越危險、治理越厚」的設計。

## 4.4 貢獻定位
- Benson 設計**執行契約**;作者用該契約建了 **7 個 worker** = 把「空的執行平面」填成「實用的能力生態」(市場資料 / 策略 / 風險 / 執行 / 遙測 / 碼執行)。
- 展示作者**理解並善用平台擴充點**(worker 註冊 / `IExecutionDispatcher`),且新 worker **一律遵守 Benson 的契約**(不繞過治理)= 呼應「擴充點順勢加 > 推倒重來」。

---
**待擴**:① 一個 worker 從註冊→收 ApprovedRequest→執行→回報的 lifecycle 圖;② quote-worker 多 handler 的能力切分;③ code-exec 沙箱的威脅模型(為什麼 require_approval + 沙箱 + audit 三件齊全)。
