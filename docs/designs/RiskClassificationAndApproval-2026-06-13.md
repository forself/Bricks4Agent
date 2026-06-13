# 風險分級與審批定義 (Risk Classification & Approval)

Date: 2026-06-13
Status: **已依 owner 決策定案(2026-06-13)** —— §18.2 審批服務的前置定義
依據: [ControlledAutonomousAISystemTechnicalDesign.md](ControlledAutonomousAISystemTechnicalDesign.md) §3.1、§6.3、§6.4、§18.2、§21
銜接: [CurrentArchitectureAndProgress-2026-06-13.md](../reports/CurrentArchitectureAndProgress-2026-06-13.md)(§18.1 執行配接器已 e2e 完成)

## 0. 目的

規格只說「**高風險行為不由 agent 自決,必須可審批、可撤銷、可重放**」(§3.1/§6.3/§21),但**沒定義什麼叫高風險**。本文補上這個定義,作為 §18.2 審批服務的依據:先有「什麼是高風險」,審批閘門才知道該攔什麼。

## 1. 風險維度(評估一個動作的五個面向)

一個動作的風險不是單一屬性,而是這五個維度的綜合:

| 維度 | 問題 | 高風險訊號 |
|------|------|-----------|
| **可逆性 Reversibility** | 做錯能乾淨還原嗎? | 不可逆(刪除、覆寫無備份、送出訊息、部署) |
| **影響半徑 Blast radius** | 影響多少資源、範圍多大? | 跨多檔/整庫/跨專案/正式環境 |
| **外向性 Externality** | 會影響控制平面**之外**的世界嗎? | 對外發訊息、部署、推 remote、網路安裝、寫外部系統 |
| **資料敏感度 Data sensitivity** | 讀取/暴露敏感資料或密鑰嗎? | 觸及密鑰、憑證、他人資料、可外洩 |
| **權限 Privilege** | 會發放/提升權限或控制其他主體嗎? | 發 capability、spawn/停 agent、改治理設定 |

## 2. 主體與 scope —— 風險的首要判別(owner 決策)

**最重要的判別不是「動作種類」,而是「動作有沒有逸出主體被授權的 scope」。** 主體分兩類:

| 主體 | 授權 scope | 在 scope 內的讀寫 | 逸出 scope |
|------|-----------|------------------|-----------|
| **使用者 User**(經 LINE/Web → 代理容器 → broker) | **自己的私有資料夾** `{AccessRoot}/{channel}/{userId}/...` | **auto**(這就是他的 sandbox,改自己的東西不需審批) | **拒絕**(使用者根本碰不到他資料夾外的東西) |
| **管理員 Admin** | **全域**(目前不細分) | auto(全域讀寫) | n/a(本來就是全域) |
| **AI agent**(自主任務) | 任務 grant 的 scope | scope 內 auto / `auto_if_task_scope_match` | 升 High → 審批 |

含意:
- 同一個 `file.write` / `file.delete` / `repo.patch.apply`,**使用者在自己私有資料夾內做 = Medium/auto**(他的沙箱,可逆與否是他自己的事);**逸出私有資料夾 = High 或直接拒絕**。
- 所以「高風險」的第一道判別 = **scope 逸出**。動作種類(刪除、部署、對外)是第二層,疊加在 scope 之上。
- 審批的「信任錨」是**管理員(全域)**;使用者不審批別人,他只在自己的資料夾內操作(那裡都是 auto)。

## 3. 四級準則(對應現有 `RiskLevel` enum)

風險 = **靜態基準級**(能力本身)＋ **動態升級**(這次請求的情境)。

| 級別 | 準則(任一成立即屬此級) | 預設 `approval_policy` |
|------|------------------------|------------------------|
| **Low** | 唯讀、內部、無副作用、完全可逆 | `auto` |
| **Medium** | 可逆寫入,且**限縮在任務授權的 scope/sandbox 內**;無外向效果 | `auto_if_task_scope_match` |
| **High** | 不可逆 **或** 外向 **或** 逸出任務 scope **或** spawn/控制其他 agent | `require_approval` |
| **Critical** | 權限提升、密鑰/憑證存取、正式環境部署、批量/破壞性操作、自由 shell | `require_approval`(**MVP 單人**;雙人保留給日後),**永不 auto** |

> **「高風險」的定義 = High 或 Critical 級** → 一律走審批(不可 auto、也不該靜默拒絕)。

### 動態升級(關鍵)

同一能力的風險會因情境上升 —— 這正是 `auto_if_task_scope_match` 的精神:

- `repo.patch.apply`:預設 Medium(scope 內套 patch、git 可逆);但**patch 觸及 scope 外路徑、保護路徑、或改寫歷史** → 升 High → 審批。
- `build.test.run`:預設 Medium(白名單命令、隔離);但**命令不在白名單、或需要白名單外的網路安裝** → 升 High → 審批(或直接拒)。
- `file.write`:scope 內 Medium;**寫到 sandbox 外** → 升 High。

審批服務攔的是「**最終風險級 ≥ High**」,而非只看能力的靜態 `risk_level`。

## 4. `approval_policy` 取值(定義列舉,規格只給了一個例子)

| 值 | 意義 |
|----|------|
| `auto` | 直接放行(Low,或使用者在自己私有資料夾內) |
| `auto_if_task_scope_match` | 在授權 scope 內 auto;逸出 scope 則升級為 `require_approval` |
| `require_approval` | 需單一信任錨(管理員)放行 —— **MVP 全部走這個(High 與 Critical 皆單人審批)** |
| `require_dual_approval` | 兩個獨立審批者 —— **MVP 不用**(保留給日後 Critical) |
| `deny` | 一律拒絕(尚未具備安全執行此能力的條件時的暫時值) |

## 5. 現有能力重分級表(依 owner 決策)

重點:檔案/記憶體類的讀寫刪,**使用者在自己私有資料夾內一律 auto**(他的 sandbox);風險來自**逸出 scope**,由 `auto_if_task_scope_match` 動態升 High。對外通訊維持 auto 但加頻率限制。

| 能力 | 現況 risk / policy | 定案 risk / policy | 理由 |
|------|------|------|------|
| file.read / list / search_* | Low / auto | Low / auto | 唯讀 ✓ |
| file.write | Medium / require_approval | Medium / `auto_if_task_scope_match` ⚠️改 | 私有資料夾內 auto;逸出才升 High。原無條件 require_approval 過嚴 |
| file.delete | Medium / auto | Medium / `auto_if_task_scope_match` ⚠️改 | 私有資料夾內刪自己的東西 auto;逸出升 High。**不無條件升 High** |
| repo.patch.apply | Medium / auto_if_task_scope_match | Medium / auto_if_task_scope_match | scope 內可逆 ✓(scope 外升 High) |
| build.test.run | Medium / auto_if_task_scope_match | Medium / auto_if_task_scope_match | 白名單+隔離 ✓(非白名單升 High) |
| command.execute | High / deny | Critical / deny | 自由 shell,最高危;暫時全拒 |
| line.message.send | Medium / auto | Medium / auto **+ rate-limit** ⚠️加 | 維持 auto(發訊是常態);加 quota/頻率限制防濫用,異常量才升 High |
| line.audio.send | Medium / auto | Medium / auto **+ rate-limit** ⚠️加 | 同上 |
| line.notification.send | Low / auto | Low / auto | ✓ |
| line.message.read | Low / auto | Low / auto | 讀取 ✓ |
| line.approval.request | Medium / auto | Medium / auto | 請求審批本身低危 ✓ |
| agent.create | High / require_approval | High / require_approval ✓ | spawn 主體(逸出單一使用者範圍) |
| agent.stop | High / require_approval | High / require_approval ✓ | 控制主體 |
| memory.write | Low / auto | Low / auto(私有 scope) | 使用者私有記憶 ✓ |
| memory.delete | Medium / auto | Medium / `auto_if_task_scope_match` ⚠️改 | 私有 scope 內 auto;逸出升 High |
| rag.import / import_web | Medium / auto | Medium / auto | 攝入外部內容(可逆) ✓ |
| web.search / fetch | Low / auto | Low / auto | 外向**讀取**、低危 ✓ |
| (deploy_azure_vm_iis,tool-spec) | — | Critical / require_approval | 正式環境部署(全域、不可逆、外向) |
| (google drive 上傳,tool-spec) | — | High / require_approval | 對外發佈 |

**定案要點**:檔案/記憶體的寫刪一律 `auto_if_task_scope_match`(私有資料夾 = sandbox,scope 內 auto、逸出升 High);對外通訊維持 auto + 頻率限制;只有跨主體控制(agent.create/stop)、正式部署、自由 shell、對外發佈這類**本質逸出單一使用者私有範圍**的才靜態屬 High/Critical。

## 6. PolicyEngine 需要的改動(§18.2 才實作,本文只定義)

目前 `PolicyEngine.Evaluate` 在 High/Critical 直接 `Deny`(「Only Low and Medium risk are allowed」)—— 等於 `require_approval` 形同虛設。§18.2 要把這條改成:

1. 計算**最終風險級** = 靜態 `risk_level` 經動態升級(scope 逸出、非白名單、保護路徑…)。
2. 依最終級對應 `approval_policy`:
   - Low/Medium 且符合條件 → `Allow`(現狀)。
   - High/Critical 或 scope 逸出 → 回新的決策 `RequireApproval`(不是 Deny),交審批服務。
3. 審批服務(§6.3)建立升權申請、指派審批者、發短時效升權 token、逾時失效、可撤銷;審批通過後該請求才得 dispatch。
4. 全程 audit(誰、何時、基於何權限與**哪個批准**)—— 滿足 §21 可追溯。

`PolicyDecision` enum 需新增 `RequireApproval`(目前只有 Allow/Deny)。

## 6.5 審批層級:兩種信任錨(owner 補充,2026-06-13)

審批面分兩層,各有自己的介面與授權邊界:

| 層級 | 誰批 | 介面 | 授權邊界 |
|------|------|------|---------|
| **使用者層 User** | 動作所屬的**使用者本人** | **使用者自己的介面**(LINE / Web) | **只限該使用者自己的權限範圍**;只能批自己擁有的待審 |
| **管理員層 Admin** | **管理員** | **後台 `line-admin.html` 新增的審批功能** | **全域** |

層級判定(`ApproverTier`):
- 在使用者權限/scope 內、但被設成需確認的動作(`require_approval` policy)→ **User 層**(使用者自己確認)。
- 逸出使用者 scope、或 High/Critical(本質跨使用者:agent.create、部署、自由 shell…)→ **Admin 層**(只有管理員能批)。

授權強制(在 broker 的 approve/reject):
- User 層:`approverId == OwnerPrincipalId` 才行(或管理員)。
- Admin 層:必須是管理員。
- 使用者**不能**批 Admin 層或別人的 User 層。

## 7. 已定案決策(owner,2026-06-13)

1. **對外通訊(line.message.send/audio)維持 auto + 頻率限制** —— 不每次審批;加 quota/rate-limit 防濫用,異常量(批量發送、非授權接收者)才升 High。
2. **檔案/記憶體寫刪改成 scope-aware**(`auto_if_task_scope_match`)—— 使用者在自己私有資料夾內 auto;逸出才升 High。`file.write` 不再無條件 require_approval。
3. **單人審批**:MVP 所有 High 與 Critical 都只需**一個**審批者(暫不雙人)。
4. **審批通道兩個都要,且區分使用者/管理員**:
   - **使用者(User)**:經 **LINE 或 Web** 操作,作用範圍**只限自己的私有資料夾**(`{AccessRoot}/{channel}/{userId}/...`)。在自己資料夾內都是 auto,本來就不需審批;碰不到資料夾外。
   - **管理員(Admin)**:**全域**(目前不細分),是高風險動作的**審批信任錨**。可經 LINE 回 confirm/cancel,或在 `line-admin.html` 的審批佇列按核准/駁回。
   - 即:**使用者 = 被授權的受限主體;管理員 = 全域審批者**。

## 8. §18.2 實作藍圖(依本定義,待實作)

1. `PolicyDecision` enum 加 `RequireApproval`;`PolicyEngine` 不再對 High/Critical 直接 Deny,改:scope 逸出或最終級 ≥ High → `RequireApproval`。
2. 重分級 seed:套 §5 表(file.write/delete/memory.delete 改 `auto_if_task_scope_match`;deploy/command/agent.* 維持/調 High/Critical)。
3. 使用者私有資料夾 scope 強制:確認 broker 對 User 主體的 grant scope 綁 `{AccessRoot}/{channel}/{userId}`;逸出即升級。
4. `ApprovalService`(§6.3):建立升權申請、指派**管理員**、發短時效升權 token、逾時失效、可撤銷。
5. 審批通道:LINE(沿用 line.approval.request / confirm)＋ `line-admin.html` 審批佇列。
6. line.send 頻率限制:per-user quota / rate-limit。
7. 全程 audit(誰、何時、哪個批准)—— 滿足 §21 可追溯。
驗收:高風險動作(逸出 scope、agent.create、部署等)必經管理員審批且可追溯;使用者在私有資料夾內操作不被打擾。

## 9. 範圍界線

本文只**定義**風險分級與審批模型;不含實作。§18.2 依 §8 藍圖實作。
