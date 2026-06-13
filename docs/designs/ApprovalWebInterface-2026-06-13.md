# 審批 Web 介面:分析・評估・規劃 (§18.2-C2)

Date: 2026-06-13
Status: **規劃,待審** —— §18.2 審批引擎(A/B/C1)已完成,本文規劃 web 介面層
依據: [RiskClassificationAndApproval-2026-06-13.md](RiskClassificationAndApproval-2026-06-13.md) §6.5(兩層審批)
銜接引擎: PolicyEngine RequireApproval(+tier)、BrokerService approve/reject/list、`ApprovalRequest` 持久化

## 1. 現況分析

| 面向 | 現況 |
|------|------|
| **管理員 web 後台** | `line-admin.html`(單檔 vanilla JS,~1700 行,深色主題,sidebar+tabs)。認證 `LocalAdminAuthService`:**僅限 localhost**、單一共享密碼、cookie 12h。加分頁的 pattern 清楚(HTML section + nav 鈕 + state + load/render + 路由)。 |
| **使用者 web 前台** | **不存在**。系統 API-first;使用者只經 **LINE** 或加密 API 互動。無使用者 web 登入/入口。 |
| **既有 LINE 審批** | `line.approval.request` + InboundDispatcher:**記憶體內、易失**(worker 重啟即丟)、純文字 approve/deny、一次一筆、無上下文。與新的 `ApprovalRequest`(DB 持久化)是**兩套**。 |
| **新審批引擎** | 已完成:決策含 tier、`ApprovalRequest` 持久化、approve/reject/list/授權。**只差介面**。 |

## 2. 評估:Web vs LINE(為何 web 能做得更好)

| 能力 | LINE | Web |
|------|------|-----|
| 待審清單 | 一次一則訊息,無總覽 | **整個佇列**,可篩選(tier/能力/時間/發起者)、待審數徽章 |
| 請求上下文 | 只有一段文字描述 | **完整細節**:intent、能力、風險原因、tier、owner、scope |
| **看實際內容** | 做不到 | repo.patch.apply → **渲染 patch diff(逐行 +/-)**;build.test.run → 顯示命令;file.write → 顯示路徑/內容 |
| 批次 | 不行 | **批次核准/駁回** |
| 決策理由 | 難輸入 | 文字框,記入 audit |
| 歷史/稽核 | 無 | **誰何時批了什麼**的稽核檢視 |
| 持久性 | 易失(重啟丟) | DB-backed,不丟 |

結論:**LINE 適合「輕量即時確認」(一鍵 approve/deny),web 適合「需要看內容才能決定」的審批**(尤其 patch diff、批次、稽核)。兩者互補,但 web 是主力。

## 3. 規劃

### Phase 1 — 管理員審批後台(可立即做,高價值)

在既有 `line-admin.html` 加「**審批**」分頁(全域,管理員 = §6.5 Admin 層信任錨):

**後端端點**(`LocalAdminEndpoints.cs`,沿用 `auth.TryRequireAuthenticated` + `IBrokerService`):
- `GET /api/v1/local-admin/approvals` → 待審清單(含關聯 ExecutionRequest 細節:intent、payload、policy_reason、tier、owner)
- `POST /api/v1/local-admin/approvals/{id}/approve` body `{reason}` → `broker.ApproveExecutionAsync(id, adminId, reason, isAdmin:true)`
- `POST /api/v1/local-admin/approvals/{id}/reject` body `{reason}` → `broker.RejectExecution(..., isAdmin:true)`

**前端**(新分頁,左清單 + 右細節/動作,沿用 .panel/.list/.item/.json/.button):
- 左:待審佇列,每筆顯示 能力 / tier 徽章 / 發起者 / 時間;可篩選;nav 上待審數徽章。
- 右:選中後顯示完整細節 + **依能力渲染 payload**:
  - `repo.patch.apply` → patch 以 diff 樣式渲染(綠加紅刪)。
  - `build.test.run` → 命令 + 預期。
  - 其他 → 結構化 JSON。
- 動作:核准 / 駁回(帶理由框)、批次。
- 自動刷新(沿用既有 5s 輪詢機制)。

安全:localhost-only 後台已是強邊界;管理員 isAdmin:true。

### Phase 2 — 使用者審批前台(較大,但你要的「web 比 LINE 好」主要在此)

**問題**:目前沒有使用者 web 認證(後台是 localhost-only,不適用使用者)。使用者要在「自己的介面」批自己權限內的(§6.5 User 層),選項:

- **2a(建議):輕量使用者 web 入口 + 連結式認證**。使用者在 LINE 收到一則「有待審動作,點此查看」→ 開啟帶**短時效簽章 token** 的 web 頁(綁該 userId),頁面只列**他自己的 User 層待審**,可看內容(例如 agent 想在他資料夾寫什麼)再 approve/deny。這把「LINE 一鍵」升級成「web 看了內容再決定」,正是你要的。需新增:使用者 web 認證(簽章連結,非 localhost)、`/api/v1/user/approvals/*`(`isAdmin:false`,broker 已支援 owner 授權)、`user-approvals.html`。
- 2b(暫行):User 層先沿用 LINE confirm/deny,web 先做管理員(Phase 1)。

建議:**先 Phase 1(管理員後台,快又高價值),再 Phase 2a(使用者連結式 web 審批)**。LINE 仍保留為「通知 + 輕量確認」管道,但「需看內容」的決策導向 web。

### Phase 3 — line.send 頻率限制(獨立小項)

per-user quota / rate-limit,異常量才升 High。與審批 UI 無關,可並行。

## 4. 建議落地順序

1. **Phase 1 管理員審批分頁 + 端點**(test-first:端點整合測 + UI 手動驗)。← 最高 CP 值,先做。
2. Phase 2a 使用者連結式 web 審批(新使用者 web 認證 + 頁面)。
3. Phase 3 line.send rate-limit。

## 5. 待你拍板

- Phase 1 先做管理員後台審批分頁 —— 同意?
- Phase 2 使用者面:走 **2a(連結式 web,能看內容)** 還是 **2b(先 LINE)**?
- patch diff 等「看內容」渲染是這次 web 的賣點,優先度高 —— 確認?
