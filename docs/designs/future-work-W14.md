# 平台未來工作（W14 後續）

> 本文件用於 2026-05-16 答辯之「未來工作」章節。
> 寫作時點：2026-05-15。
> 已完成（W14 + G/H 系列）見 §0；本文重點在 §1 「論文等級之未來工作」。

---

## §0 已完成基線（截至 2026-05-15）

| 系列 | 完工項目 |
|------|---------|
| **W14 安全加固** | P1 緊急停機 / P2 bot rate limit / P3 approval risk hint / P4 file integrity agent / P5 read-only lockdown |
| **G 系列 demo 加分** | G1 PolicyEngine sandbox replay / G2 live audit event WebSocket stream / G3 capability registry graph |
| **H 系列治理深化** | H1 per-principal LLM token + dispatch quota（in-memory、daily reset、soft-mode 預設） |
| **既有平台基底** | 14 個 inbox-driven agent / 14 條 AutoTrader risk gate / W13 LLM reasoning audit pipeline / 兩層 ACL / hash chain audit / Cloudflare Tunnel + Access |

---

## §1 論文「未來工作」候選

每條附三段：**問題陳述 / 設計輪廓 / 預期成本與風險**。
按「對 Benson 設計貢獻度」排序。

### I1. Multi-sig approval —— 多方簽核（**minimal 已落地**）

**問題陳述**
現行 approval gate 是單人決策（一個 admin 按 approve 就放行）。
真錢額度上升後（> 1000 USDT 單）、單人決策的內部威脅與失誤代價過高。
參考：DeFi treasury multi-sig、傳統金融兩人覆核制。

**已實作（minimal）**
- `multi_sig_rules`（capability_id, min_approvers, enabled, description）— per-capability N-of-M
- `approval_decisions`（decision_id, approval_id, approver_pid, decision, reason, decided_at）— 每位簽核者紀錄
- `MultiSigApprovalService` 是 4 層裝飾鏈最外層、Approve() 累積票數、達門檻才呼 inner.Approve
- 同一 admin 重按 idempotent（不算第二票）
- 任一 admin Reject 立刻終止
- endpoint `/api/v1/multi-sig` (GET/PUT/DELETE) 管規則、`/multi-sig/decisions/{aid}` 查歷史

**未做的進階版**
- 按金額分級（> 1000 USDT 才 multi-sig、小單照常單人）— 需要 capability payload 解析整合（H3 PayloadMatcher 重用）
- Dashboard 多人視圖 — admin 看到「目前 2/3 簽核完成」狀態列
- 撤回票（admin 反悔在門檻達成前撤回自己的 approve）

**對 Benson 設計**
把 PolicyEngine 從「規則式」延伸到「治理式」、是 governance framework 的自然成長。

---

### I2. Plugin marketplace —— 第三方 agent / capability

**問題陳述**
目前 14 個 inbox agent 全部是 broker 內 hardcoded `BackgroundService`。
論文主軸「治理框架」的價值在於「能讓不可信第三方在受控環境跑」、但目前無法驗證此假說。

**設計輪廓**
- Capability manifest 格式（YAML）：宣告 capability_id、route、param_schema、risk_level、approval_policy
- Worker plugin 介面：標準 worker SDK 規範、註冊時上交 manifest
- broker 啟動時掃 `plugins/` 目錄、驗 schema、載進 CapabilityCatalog
- 每個 plugin 跑獨立 sandbox（OS process 或 container、依信任等級）
- ACL/Approval/Audit 三層強制適用所有 plugin

**預期成本與風險**
- 工時：1-2 週（manifest spec、plugin loader、sandbox isolation、文件）
- 風險：sandbox 是個大坑（記憶體 / CPU / 檔案 / 網路 quota；Linux namespace + cgroup or 直接用 Docker）
- 對 Benson 設計：把「broker 是 control plane」這句話從 demo 推到 production；這是 platform play

---

### I3. Time-travel debugging —— 從 hash chain 重建任意時刻 state

**問題陳述**
audit_events 已是 append-only event log（具備 event sourcing 形式），
但目前只用來「查某 trace 發生過什麼」、沒利用它「能重建任意時刻完整 state」的潛在價值。
事後 forensics、合規舉證、incident replay 都需要這個能力。

**設計輪廓**
- 把 audit_events 視為 source-of-truth、broker 內所有 mutable state（`approval_requests`、`capability_grants`、`auto_trade_watchlist` …）視為 projection
- 為每張 mutable 表寫 reducer：`(prevState, AuditEvent) -> nextState`
- 新 endpoint `GET /api/v1/timetravel?at=<utc-timestamp>` 回該時刻的完整 snapshot（grant 表 / approval 表 / watchlist 表）
- 以 read-only 物化視圖呈現、不影響 live state
- dashboard 加「時光機」滑桿：拉時間軸看當時 dashboard 長什麼樣

**預期成本與風險**
- 工時：3-5 天（reducer 數量 = mutable 表數 × 幾個 event_type）
- 風險：reducer 寫不全會造成 projection 與 live state 不一致（要對齊測試）
- 對 Benson 設計：直接驗證「hash chain 是真實 source-of-truth」這個架構選擇

---

### I4. Compliance report generator —— 一鍵 PDF 給審計

**問題陳述**
治理框架的價值要可被「外部稽核」驗證、不能只給工程師看。
目前 audit_events / approval_requests 散落在 SQLite、要 SQL 查或翻 dashboard、外部審計人員無法直接消化。

**設計輪廓**
- 模板系統（Razor 或 Handlebars）：定義「期間總覽」「異常事件」「approval 決策清單」「PolicyEngine 觸發紀錄」「KILL_SWITCH/READONLY 事件」五個段落
- 新 endpoint `POST /api/v1/compliance/report`：input 期間 + 範圍 → 出一份 PDF（用 Puppeteer / wkhtmltopdf）
- PDF 內嵌 hash chain 摘要：每章節最後附該段 audit_events 的 root hash + 範例驗證步驟
- 加密簽章選項：用 broker 私鑰簽 PDF、外部驗章

**預期成本與風險**
- 工時：2-3 天（模板 + PDF render + 簽章）
- 風險：PDF render 對中文字型 / 表格折行敏感
- 對 Benson 設計：把 audit chain 從「技術設計」變「合規工具」、進入金融科技 / SaaS 採購評估視野

---

## §2 平台延伸 — 不在 Benson 設計核心、但增益明顯

| # | 項目 | 工時 | 備註 |
|---|------|------|------|
| H2 | Time-based ACL（`trading.order` 只在 09–17 自動放行） | 1d | 低風險、policy DSL 雛形 |
| H3 | Approval template（命中規則自動放行、不彈窗） | 1d | 解 P3 之後的「approval 疲勞」 |
| H4 | Audit RAG（自然語言查 audit_events） | 1-2d | W13 + RAG ingest 兩元件相乘 |
| H5 | Policy DSL（YAML 寫 ACL/Approval rule） | 2-3d | 從 hardcode 升級到設定檔 |

> 這 4 條與 I1-I4 的差別：H 系列偏「現有元件深化」、I 系列偏「新方向探索」。論文編排建議 H 列在「短期可達」、I 列在「長期方向」。

---

## §3 對齊 Benson 原架構的判斷準則

每條未來工作條目通過以下三問才寫進論文：

1. **是不是延伸 broker 是 control plane 的設計？**（不是延伸到 worker / 不是繞過 broker）
2. **能不能用既有抽象表達？**（用 capability / grant / audit / policy；不要新增同層次概念）
3. **論文方法論能否描述？**（formalizable as gate / decision / state；不是 ad-hoc UX）

I1-I4 全部三問通過。H2-H5 通過 1+2、第 3 視 framing 而定。

---

## §4 不在本文件的事項

以下功能對使用體驗有價值、但**不屬於 Benson 治理框架的延伸**、不應寫進論文未來工作：

- 多語系（i18n 第三語）
- 行動端 native app
- 介面美化 / dashboard 視覺改版
- 交易策略本身的優化（lookahead bias 修復、新指標）
- Discord/LINE/Slack bot 的功能加強
- 既有 worker 的效能優化

這些屬於「應用層工作」、應寫在「實作系統」章節，不在「未來工作」。
