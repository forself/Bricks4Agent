# §7 應用案例:受治理的多用戶真錢交易 + 家庭資料報表(作者貢獻)— 草稿 scaffold

> 草稿。grounded on TradingEndpoints / AutoTraderService / ExchangeCredentials / PerpetualEndpoints + git blame。
> **IP 邊界**:本章只講「平台治理如何被應用」,**不講交易 alpha / 策略細節**(那是個人研究、主線 B、私有 repo)。

## 為什麼「交易」是最嚴苛的治理試煉場
- **真錢 + 多用戶** = 治理失效代價最大:下錯單、看到別人持倉、用錯別人憑證 = 真實財損 / 隱私外洩。
- 因此「交易應用真的跑起來」= 對平台 principal / capability / auth / audit 模型的**實戰驗證**(不是 demo,是賭真錢)。

## 7.1 多用戶真錢隔離(平台 principal 模型實戰)
- **per-principal 憑證**:每個 caller 用「自己的」交易所憑證(`__credentials` 由 caller principal 解析後注入 payload)→ 平倉/下單只打到**他自己的帳戶**(`AutoTraderService`:對每個 (owner, exchange) 用該 owner 的 BingX credential)。
- **交易歷史/匯出 = 隱私資料**:`owner_principal_id` 過濾;未登入 → 拿不到 principal → **401**(`TradeHistoryOwnerFilter`)。
- **憑證存取 scope**:admin = all、一般 user = self(`ExchangeCredentialsEndpoints`)。
- ⇒ §5c 的 principal claims 一路流進交易層,把「能力模型 / 零信任」在**真錢多用戶**下兌現。

## 7.2 真錢執行的治理閘(整合 §5/§6)
- 敏感真錢操作 → **§5-ext approval 裝飾鏈**(template + time + multi-sig)把關 → 執行 → **audit**。
- **§6 應急**:KillSwitch 可一鍵停 AutoTrader + 所有 watch inactive(真錢出事人能即時撤)。
- **§6 監測**:StrategyHealthMonitor 連虧自動退役(治理自主性的 live 證據)。
- ⇒ 一筆真錢交易穿過作者建的「認證(§5c)→ 審批(§5-ext)→ 監測/應急(§6)」全鏈。

## 7.3 tw-fundflow(多用戶資料 / 報表應用)
- 三大法人資金流資料管線 → 樹狀產業報表 → LINE 投遞給家人。
- **多用戶隱私**:family 公開頁**去個人段**(防 watchlist 外洩給家人)。
- ⇒ 平台被用於「服務真實家人」的非交易資料應用 = 治理 + 多用戶模型的第二個應用見證。

## 7.4 整體論點
交易 + tw-fundflow 共同證明:**平台的治理不是紙上設計,而是在「真錢 + 多用戶 + 真實家人服務」中被強制執行且有效。** 這是平台從「設計可行」到「實戰有效」最有力的證據——而且賭的是作者自己的真錢與家人隱私。

## 貢獻定位
作者用真實高風險應用**驗證了平台治理模型的有效性**;§7 是 §5c/§5-ext/§6 的**整合見證章**(各層在一個真實工作流裡協同)。

---
**待擴**:① 一筆真錢交易穿過認證→審批→監測→應急的 sequence diagram;② 多用戶隔離的威脅模型(若無 owner filter / per-principal credential 會怎樣);③ tw-fundflow 資料管線圖。
