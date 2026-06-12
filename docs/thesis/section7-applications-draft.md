# §7 應用案例:受治理的多用戶真錢交易(平台治理的最嚴苛試煉場)— 草稿

> grounded on AutoTraderService / TradingPerpetualHandler / BingxPerpetualClient / ExchangeCredentials / 風險與韌性層。
> **IP 邊界(組長 2026-06-12 認可此切法)**:本章只講「平台治理如何被應用」,**不講交易 alpha / 策略細節**(那是個人研究、私有 repo)。
> 「自圓其說」的核心 = 能清楚解釋「為什麼平台上有真錢交易、它怎麼體現治理」,而非展示賺錢策略。

## 7.1 為什麼選「真錢交易」當應用案例?——最嚴苛的治理試煉場
受治理自主 AI 的價值,在**失控代價最大**時最能彰顯。真錢交易同時具備三個極端條件:
- **真錢**:決策錯誤 = 真實財損,不能「測試環境再說」。
- **多用戶**:多人共用一套自主系統,隔離失敗 = 看到別人持倉 / 用錯別人憑證。
- **自主 + 高頻**:AutoTrader 自動掃描、自動下單,人不在迴圈中。
→ 若平台的治理(能力、審批、稽核、隔離、韌性)能在這個場景成立,就證明它**不是紙上設計**。這就是本章作為「應用見證」的論證力道。

## 7.2 一筆真錢交易穿過完整治理鏈(本章核心敘事)
一筆自動開倉的真錢單,會依序穿過作者在 §4–§6 建構的每一層:
```
策略信號（strategy-worker、§4）
  → AutoTrader 決策（item.OwnerPrincipalId 綁定發起者)
  → §5c 認證:caller principal 解析 → 取「他自己的」交易所憑證
  → §5-ext 審批:敏感真錢操作過 approval 裝飾鏈（template/time/multi-sig）
  → risk-worker 風險前置檢查（pre_perp_order:槓桿/單筆最大虧損/日內熔斷)
  → trading-worker 執行（帶 per-user __credentials → 打到「該用戶自己的」帳戶)
  → BingX 下單（deterministic clientOrderID 冪等、bracket SL 交易所端硬止損)
  → 稽核鏈記錄 + per-principal 通知投遞
```
**意義**:一筆真錢單就把「能力模型 → 認證 → 審批 → 風控 → 受隔離執行 → 稽核」全部走過一遍。**治理不是某個端點的裝飾,是每一筆真錢都強制穿過的管線。**

## 7.3 多用戶真錢隔離(平台 principal 模型的實戰驗證)
- **per-principal 憑證**:每個 caller 用自己解析出的交易所憑證(`__credentials` 注入)→ 下單/平倉只打到他自己的帳戶;非 admin 拒絕 fallback 到他人。
- **交易歷史 = 隱私資料**:`owner_principal_id` 過濾;未登入 → 拿不到 principal → 401。
- **憑證存取 scope**:admin = all、一般 user = self。
- ⇒ §5c 的 principal claims 一路流進交易層 → 把「零信任 / 能力模型」在**真錢多用戶**下兌現。

## 7.4 真錢安全的縱深防禦(治理層 + 韌性層的具體體現)
治理不只「擋」,還要「就算出事也活」。本應用疊了多層(對應 §5/§6):
- **事前**:approval gate(§5-ext)+ risk-worker 前置檢查 + 有效槓桿/名目上限。
- **執行中**:deterministic clientOrderID 冪等(failover 重送不雙下單)+ 交易所端 bracket SL(broker 掛掉也有硬止損)。
- **持續**:circuit breaker(日內虧損熔斷)+ StrategyHealthMonitor(連虧自動退役,治理自主性的 live 證據)。
- **應急**:EmergencyState KillSwitch(一鍵停 AutoTrader + 所有 watch)。
- **災難**:Litestream PITR + 暖備 broker 選主(§future、自動移轉)。
⇒ 這套縱深防禦本身就是「可治理 + 可運維」在真錢場景的最強示範。

## 7.5 tw-fundflow(第二個多用戶應用、非交易)
- 三大法人資金流資料管線 → 樹狀產業報表 → LINE 投遞給家人。
- **多用戶隱私**:family 公開頁去個人段(防 watchlist 外洩給家人)。
- ⇒ 證明平台的 principal / 隔離 / 通知模型**不只服務交易**,也能服務「真實家人」的非交易資料應用。

## 7.6 IP 邊界與「自圓其說」(誠實、且組長認可)
- 本章講的是**治理機制如何被真實應用驗證**,不展示交易 alpha / 策略。
- 「自圓其說」= 平台上有真錢交易,是因為它是**檢驗治理最嚴苛的場景**;它的存在強化論文論點,而非削弱。
- 策略研究的方法論/結論屬個人 IP,與本平台報告分離(私有 repo)。

## 7.7 本章論點
**平台的治理不是紙上設計,而是在「真錢 + 多用戶 + 自主」這個失控代價最大的場景中被強制執行且有效。** 這是 §3 治理原則 → §4–§6 作者實作 → §7 真實見證的完整閉環:賭的是作者自己的真錢與家人隱私,而平台撐住了。

---
**待擴**:① 7.2 一筆真錢單穿越各層的 sequence diagram;② 7.4 各防禦層的觸發實例(circuit breaker / StrategyHealthMonitor 真實退役紀錄);③ 7.3 多用戶隔離的威脅模型(若無 owner filter / per-principal credential 會怎樣)。
