# §8 限制與未來工作 — 草稿 scaffold

> 草稿。本章誠實揭露平台已知限制 + 未來方向(呼應工作原則「誠實記錄限制」)。
> 多數限制有對應評估/設計文件或實作註解佐證(非空談)。

## 8.1 高可用 / 自動 failover 的根本限制
- **broker = 有狀態單例**(控制平面 + SQLite document store)→ **無法安全地「自動」failover**。
- 三個驗證過的結構洞:① 副作用路徑(真錢下單/Inbox push)非全冪等 → 切換可能重放;② 跨機孤兒容器(舊節點容器未被 fence);③ 第三方(交易所)無法被 token-fence。
- 結論:2 節點不能安全自動切(需第三方見證避免 split-brain);k3s 只解一半(解容器調度、不解有狀態單例 + 副作用)。
- **現況**:暖備 + 半自動(關鍵切換仍要人按一下)。**未來**:見證節點 + fencing + 半自動;副作用路徑補端到端冪等。
- 佐證:`docs/designs/HA-Failover-Evaluation-2026-05-30.md`。

## 8.2 子 LLM 的稽核灰區
- Arbitrator 與 bot-node 內的 LLM(claude subprocess)會做**推理層決策**;broker 稽核鏈看得到「**結果/意圖**」,但看不到 LLM 的「**reasoning 過程**」。
- 這與核心「可審核、可重放」原則有張力(推理不可完全重放)。
- **現況**:灰區已識別 + 範圍限制(LLM 只提議、broker 仍驗證+記錄結構化意圖)。**未來**:把 reasoning trace 也納入稽核證據鏈。

## 8.3 安全雙模的取捨(§5c 延伸)
- 完整「信封加密 + scoped-token」主要用於正規 agent↔broker 通道;**admin/dashboard + trading-internal 路徑走 cookie-session + plain-JSON**(務實取捨)。
- **代價**:plain-JSON 路徑不享端到端加密 / replay 防護,倚賴 TLS + cookie 認證 + (A1 後)fail-closed。
- **未來**:統一到完整信封,或對各路徑做正式威脅模型分級。

## 8.4 容錯的已知缺口
- **WorkerAutoRestart 不處理 application-level flapping**(容器仍 Running、但 broker 端斷連重連)→ 需 heartbeat protocol + disconnect-reason logging(已在程式註解明列)。
- **真錢失效路徑**:交易所端 bracket SL 已開(broker 掛也有硬止損),但缺口=無外部 dead-man switch、單一 Discord 告警通道、操作者睡眠時段。**未來**:分層 fallback(外部 watchdog 已有、dead-man 待補)。

## 8.5 架構偏離(誠實揭露)
- **bot-node** 偏離 Benson canonical 的 `line-worker → broker` 路徑(開發期為打通 Discord 而走的捷徑,W13 已加 audit mitigation)。
- **未來**:realign 回 canonical 高階協調路徑,或正式把 bot-node 納入 worker 契約。

## 8.6 研究延伸的範圍限制(主線 B)
- 量化交易研究為個人延伸、與本平台專題分離(IP 隔離);本報告僅以其作為「平台治理被應用」的案例(§7),**不展開策略/alpha 細節**。
- 該延伸自身的限制(策略容量、非平穩性、衰退監測)記錄於私有研究 repo,非本平台報告範圍。

## 8.7 總結
平台已達「可治理 + 可運維 + 可被真實多用戶應用」的生產級狀態;主要未竟之處集中在**真 HA(有狀態單例的根本難題)**與**推理層稽核**——兩者皆為「受控自主 AI 系統」往更高保證等級演進的自然前沿,且本報告已給出評估與分層路徑,而非迴避。

---
**待擴**:① 8.1 補 split-brain 時序圖 + 見證機制設計;② 8.2 補「結果可審 vs 過程可審」的學理討論;③ 各限制標注「現況 mitigation vs 未來」對照表。
