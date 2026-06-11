# §6 運維韌性層(作者的平台級貢獻)— 草稿 scaffold

> 草稿。作者貢獻章節,grounded on git blame + 實際 code。2026-06-11 起草。
> 全章元件作者 = AnthonyLee(git blame 佐證);Benson 寫受治理核心、近一個多月幾乎未動(最後實質 commit ~2026-05-01)。

## 6.1 動機與定位
- Benson 的核心提供「**可治理**」(control plane / capability model / approval / audit)。但**「可治理」≠「可在生產運行」**。
- 生產運行還需要三件:**可觀測**(看得到系統狀態)、**可自癒**(故障自動恢復)、**可緊急控制**(人能即時介入)。
- 本層 = 作者在 Benson 受治理核心**之上**擴充的運維韌性層,使平台從「可治理的原型」變成「**可運維的生產系統**」。
- 貢獻佐證(git blame):dashboard.html、HealthCheck/Worker/Dashboard endpoints、WorkerAutoRestart/AutoScale/StrategyHealthMonitor、EmergencyState — 皆作者。

## 6.2 觀測層
- **三面**:worker 生命週期(`/workers/containers`)、worker stats(`/workers/stats`)、健康分布(`/health/score`:healthy/degraded/critical/worker_count)+ 拓樸(`/dashboard/network-status`)+ 診斷(`/diagnostics/scan`)。
- **介面**(dashboard.html):單頁 vanilla JS、輪詢控制平面 endpoint、**語義化 KPI 上色**(critical>0=red、degraded>0=amber、healthy=green)。
- **取捨**:輪詢 vs 推播(輪詢簡單、可接受延遲);唯讀總覽 vs 可操作。

## 6.3 治理可視化(把「可審批/可撤權」落地成介面)
- 面板:待審批佇列(`/admin/approvals?status=pending`)+ ACL override(`/admin/acl/overrides`)+ 緊急狀態(`/emergency/status`)。
- **設計巧思(展示理解治理語義)**:
  - **安全感知**:403/非-admin 時不誤判成「0 待審、治理暢通」→ 區分「沒待審」vs「沒權限看」(不假綠燈)。
  - **語義告警**:`override>0` = 「治理被繞過中」→ amber(override 雖被允許、但代表治理被繞過、需提醒 = 治理衛生)。
- 後端:`IApprovalService`(approve / reject / approve-and-dispatch 審批→執行)、`EmergencyEndpoints`(stop-all=KillSwitch / lockdown=ReadOnlyMode / clear)、全程 audit(`resourceRef: broker.emergency`)= 「可審核」落實。

## 6.4 容錯層(自癒)
- **WorkerAutoRestart**(容器級自癒):30s tick 掃 Stopped/Failed → **指數退避**(0→30s→2m→10m→30m→放棄、標 unrecoverable 等 admin reset)+ Running>5min reset 嘗試計數 + startup 20s 避 race。**誠實限制**:不處理 application-level flapping(容器仍 Running 的 broker 斷連)— 明寫那需 heartbeat protocol 校正。
- **WorkerAutoScale**:自動擴容。
- **StrategyHealthMonitor**:策略級自動退役(連虧/低勝率自動 pause)。
- **三層**:容器自動重啟 + 自動擴容 + 策略自動退役 +(人工)緊急 KillSwitch。

## 6.5 設計原則對應(呼應專題工作原則)
- **可解釋性**:每個 service 有清楚「為什麼這樣做」(退避階梯、reset 邏輯、race 避免、語義上色)。
- **誠實記錄限制**:AutoRestart 明寫不處理 application-level flapping;儀表板明示「需 admin 才看得到治理」。
- **最小侵入既有檔**:擴充 BackgroundService / endpoint / 單頁 UI、不改 Benson 受治理核心。

## 6.6 貢獻定位
作者貢獻 = **把 Benson「可治理的核心」擴成「可運維的生產系統」**(觀測 + 自癒 + 應急)。平台級工程貢獻、git blame 背書、且為平台近期演進的主體(Benson 近月幾乎未動)。交易延伸(§7)則是「這套受治理+可運維平台被真的用起來」的應用案例。

---
**待擴**:6.2-6.4 各補一段 code 片段 + 架構圖;6.6 補「與相關工作(k8s self-healing / service mesh observability)的對比 = 本平台把治理+觀測+容錯統一在單一控制平面」。
