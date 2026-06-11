# §5c 安全中介管線:加密 + 認證(作者貢獻)— 草稿 scaffold

> 草稿。grounded on EncryptionMiddleware.cs / BrokerAuthMiddleware.cs + git blame(皆 AnthonyLee、各 ~21 commit)。

## 動機
- 核心原則(零信任 / 可撤權 / 能力模型)**必須在 wire 層被強制執行**,否則只是設計文件。
- 作者建了兩層 middleware 管線(**加密 → 認證 → 稽核**),把抽象原則落地成「每個請求都過的強制閘」。

## 加密層(EncryptionMiddleware,管線第一層)
- **AES-256-GCM** 加解密 POST request/response body。
- **per-session 金鑰**經 **ECDH 交握**派生(`ISessionKeyStore`、`IEnvelopeCrypto`)。
- **Replay 防護**:`seq > last_seen_seq` 才接受。
- 流程:解密 → 明文注入 `HttpContext.Items` → endpoint 處理 → 回傳時加密信封寫出。
- 排除:`/api/v1/health`(LB 探測)、非 POST。

## 認證層(BrokerAuthMiddleware,第二層)
- 從解密明文讀 **Scoped Token**(或 admin JWT)→ 驗**簽章 + 時效**。
- **Epoch 閘道**:`token.epoch < current_epoch → 401` ⇒ **bump current_epoch = 全域所有舊 token 立即失效 = 大規模即時撤權**。
- Session 狀態檢查(active、未過期)。
- 驗證後 claims(principal/role/task/session/epoch)注入 `HttpContext.Items` 供 endpoint 用。

## 對應核心原則(這層 = 原則的「執行者」)
| 核心原則 | 本層如何強制執行 |
|---|---|
| **零信任** | 每請求都驗;scoped token 限定能力面 |
| **可撤權** | **Epoch 閘道 = bump epoch 即全域撤權**(最優雅的一點) |
| **能力模型** | scoped token 攜帶能力範圍、endpoint 依 claims 授權 |
| **可審核** | 管線第三層 audit 接續(解密後明文可稽核) |
| **機密性** | AES-256-GCM + per-session ECDH 金鑰 + replay 防護 |

## 誠實的設計取捨(雙模)
- 完整「加密 + scoped-token」管線主要用於**正規 agent↔broker 通道**。
- **admin/dashboard + trading-internal 路徑**(`/admin/`、`/trading/`、`/perpetual/` 等)走較輕的 **cookie-session 認證 + plain-JSON**(`IsTrustedInternalPlainJsonPath`)——務實取捨:這些是人類操作/內部可信路徑,不套完整信封。
- **安全代價需誠實記載**:plain-JSON 路徑不享 replay 防護/端到端加密,倚賴 TLS + cookie 認證 + (A1 修復後)fail-closed。
- 配套:**§3b A1 安全修復**(真錢控制端點 fail-open → fail-closed)補上「驗證失敗時拒絕而非放行」。

## 貢獻定位
作者建的是**「把核心治理原則在 wire 層強制執行的安全管線」= 治理思想的執行層**(設計→§3/§5a 是 Benson 的思想;強制執行→§5c 是作者的實作)。git blame:兩 middleware 皆 AnthonyLee。

---
**待擴**:① ECDH 交握 sequence diagram;② epoch 撤權 vs 傳統 token blacklist 的對比(epoch 是 O(1) 大規模撤權);③ 雙模路徑的威脅模型分析。
