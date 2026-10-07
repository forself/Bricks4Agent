# 兩層存取權限模型(Tier 1 / Tier 2)

Date: 2026-06-14
Status: **已實作 + 驗證(2026-06-14)** —— 模型/遮罩/註冊閘門/晉升/後台 UI 完成;Unit.Tests 376 全綠、broker-tests 192、方案 0 error。
範圍: 把「註冊核准即給多數權限」改成清楚的兩層存取模型。

> **實作現況（2026-09-26 核對）**
>
> - 本文 §1(L11 自助成為 Tier 1)、§2(自助註冊)、§3 註冊閘門(新使用者 → Basic + Approved)與 §5 驗收(新使用者發訊即可問答)所述「任何人發訊即自動成為 Tier 1」已不成立:在設定 `PortalLineVerificationService` 時,原始 LINE user id(`U` + 32 hex)在完成 Web Portal 註冊並於 LINE 送出 `/verify <user_id> <code>` 之前,一律回 `line_verification_required`,不會進入 Tier 1 問答(`packages/csharp/broker/Services/HighLevelCoordinator.cs:597-605`)。
> - 驗證成功後,LINE id 對應到 Portal user id(`HighLevelCoordinator.cs:590-595`),之後才以該 id 走本文的註冊閘門與 Tier 1 / Tier 2 流程(`HighLevelCoordinator.cs:147,170`)。
> - 設計見 [2026-07-01-line-portal-verification-design.md](../superpowers/specs/2026-07-01-line-portal-verification-design.md)。

## 1. 目標模型(經使用者確認)

| 層級 | 取得方式 | 能做什麼 |
|---|---|---|
| **Tier 1 — 基本註冊者(Basic)** | **自助**:任何人發訊即自動成為 Tier 1(不需審核) | 對話問答 + **唯讀查詢**(交通查詢、受控網路搜尋)。**不碰任何檔案。** |
| **Tier 2 — 會員(Member)** | **人工審核晉升**:管理員核可後升級 | 在 Tier 1 之上,**可被分配**「私有資料夾」類權限:Production 任務(檔案讀寫/文件/報表產生與交付/專案)、使用者授權網站(browser delegated)、佈署。 |

關鍵語意:
- **晉升 ≠ 自動給權**。升 Tier 2 只是「**可以被分配**」私有資料夾權限;實際每項權限仍由管理員逐一指派(預設關)。
- Tier 1 的唯讀查詢(query/transport)預設開,Tier 2 的三項(production/browser/deployment)預設關,且**只有 Member 才生效**。

## 2. 與既有機制的整合

現有:`HighLevelAnonymousRegistrationPolicy`(allow_all/manual_review/deny_all)決定新使用者是否被擋;核准後 `HighLevelUserPermissions` 五個旗標即生效(production 預設 true)。

調整:
- **自助註冊**:新使用者一律自助成為 Tier 1(Basic + Approved)。`manual_review` 不再擋 Tier 1。
- **保留 `deny_all` 為「凍結註冊」kill-switch**:仍可完全關閉註冊(連 Tier 1 都不給),作為安全閥。
- **`ReviewLineUserRegistration` 改為「層級審核」**:`approve`→ 升 Member;`demote`→ 降回 Basic(保留 Tier 1 問答);`reject`→ 封鎖(Rejected,完全擋下)。
- 既有 `PendingReview` 使用者(legacy)不再被擋於 Tier 1(視為 Basic);只有 `Rejected`/`DeniedByPolicy` 才擋。

## 3. 實作要點

### 模型(`HighLevelCoordinator.cs`)

- `HighLevelAccessTier` 常數類:`Basic = "basic"`、`Member = "member"`。

- `HighLevelUserProfile.AccessTier`(預設 Basic)、`HighLevelLineUserSummary.AccessTier`。

- `HighLevelUserPermissions`:`AllowProduction` 預設改 **false**;新增純函式 `EffectiveForTier(accessTier)` —— 回傳遮罩副本:Tier-2 三旗標 `AND isMember`,query/transport 照原值。

### 強制點(用 effective,而非 raw)

- `GetEffectivePermissions(profile) = EnsurePermissions(profile).EffectiveForTier(profile.AccessTier)`。

- 建議查詢閘門(`ShouldSuggestControlledSearch` 區)、`EnforcePermissionGate`、`BuildPermissionSummary` 改用 effective。

- 編輯(`SetLineUserPermissions`)維持寫 raw 旗標(管理員指派的意圖),由 effective 遮罩決定是否生效。

- 2026-10-07 補上:專案訪談的 `/proj` 起手與 `/ok` 也檢查 effective `AllowProduction`,被拒時的回覆與錯誤碼(`production_disabled`)和 `/建立` 相同;先前這兩個指令在權限閘之前處理,Basic 層也能觸發建置。

- 2026-10-07 審查後擴大:effective `AllowProduction` 的檢查共有五個位置——`/proj` 起手、`/ok`、`/revise`、訪談進行中的回答(會產生並交付審查檔),以及確認 draft(回 `y`,在執行前重新檢查,draft 建立後才被降為 Basic 的使用者也會被拒,draft 保留)。`/cancel` 不設閘:訪談中途被降級的使用者仍可結束訪談(連同等待確認的建置 draft 一併撤下)。

### 註冊閘門(`TryHandleRegistrationGate`)

- 新使用者:`deny_all` → 擋;否則 → Basic + Approved,放行。

- 既有使用者:只在 Rejected/DeniedByPolicy 時擋。

### 晉升(`ReviewLineUserRegistration`)

- approve→Member、demote→Basic、reject→Rejected;結果與通知帶 AccessTier。

### 後台 UI(`line-admin.html`)

- 使用者清單/詳情顯示 AccessTier;按鈕:升級會員 / 降為基本 / 封鎖;標註 Tier-2 權限僅 Member 生效。

## 4. 測試(先行)

- `HighLevelUserPermissions.EffectiveForTier`:Basic 遮罩 production/browser/deployment 為 false(即使 raw true);Member 照原值;query/transport 不受層級影響。

- 預設值:`CreateDefault()` 的 production/browser/deployment 為 false、query/transport 為 true。

## 5. 驗收

- 新使用者發訊 → 立即可問答 + 查詢,但不能建 production / 動檔案。

- 管理員升 Member 後,可逐項分配私有資料夾權限;未分配前仍不能用。

- `deny_all` 可完全凍結註冊。
