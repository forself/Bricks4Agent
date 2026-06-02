# 6/29 朋友 onboarding checklist

> 2026-06-02 建。新增使用者(朋友)到 B4A 平台的標準流程 + **隔離驗收**。
> 多用戶基建已 live(風險隔離 + Gap 1/1.5/2 交易隱私 + 推播 MVP + fail-open 漏洞修復)。
> 認證現況:你透過 **Cloudflare Access(email 白名單)** 進儀錶板;朋友也走同一個 Access(需把他 email 加進 policy)。

---

## Phase 0 — 開始前的決策

- [ ] **paper 還是真錢?** → **建議先 paper**(零真錢風險、隔離已端到端驗過;真錢還缺 Gap 2b,見 Phase 5)。
- [ ] 朋友清單(2 人)+ 各自要用的交易所(paper=Alpaca / 真錢=他自己的 BingX)。
- [ ] 把朋友的 email 加進 **Cloudflare Access policy**(否則他連儀錶板都進不來)。見 [[reference_cloudflare_tunnel_setup]]。

## Phase 1 — 建朋友帳號(你 as admin)

- [ ] `trading-manage.html` → 用戶管理 → **新增用戶**:`principal_id`(如 `prn_friend_alice`)、密碼、**`role=user`**。
- [ ] ⚠️ **一定要 `role=user`、不是 admin**。admin 會看到全部、繞過所有隔離(`TradeHistoryOwnerFilter` admin→不過濾)。
- [ ] (API 等價:`POST /api/v1/admin/users {principal_id, password, role:"user", display_name}`)

## Phase 2 — 朋友註冊「自己的」憑證(關鍵,Gap 1.5)

- [ ] 朋友登入 → 交易所憑證 → 新增**他自己的** API key(paper=Alpaca paper / 真錢=他自己的 BingX,**絕不是你的**)。
- [ ] ⚠️ **沒註冊 = 被 deny**:非 admin 無自有憑證查 `/account`/`/positions` 會被擋(Gap 1.5「系統不會用共用/預設帳戶代查」)——這是**設計、不是 bug**,正是防止他掉進你的帳戶。
- [ ] (API 等價:朋友登入後 `POST /api/v1/exchange-credentials {exchange, api_key, api_secret, is_demo}`)

## Phase 3 — 朋友註冊推播頻道(可選;目前 API only、無 UI)

- [ ] 朋友登入後 `POST /api/v1/notification-channels {channel_type:"discord", target:"他的 webhook URL", label}` → 他收**自己的**每日 PnL 彙整到自己頻道。
- [ ] (TODO 小 follow-on:在 trading-manage.html 加推播頻道 UI;目前只能 API/curl。)

## Phase 4 — 隔離驗收(authed 朋友,**本次併進來的步驟**)

用朋友帳號登入(建議無痕視窗,避免跟你的 admin session 衝突),確認:
- [ ] `/trading/account?exchange=<他的>` → 顯示**他自己的**帳戶(或他沒設憑證時 deny)——**絕不是你的真錢帳戶**。
- [ ] `/trading/pnl-summary?exchange=bingx` → **0 筆**(或只他自己的),**看不到你的成交**。
- [ ] `/trading/trades/export.csv` → 只有表頭 / 他自己的列。
- [ ] `/notification-channels` → 只他自己的。
- [ ] **控制組**:你 admin 登入 → `/pnl-summary` 仍看得到全部成交(確認 filter 沒把功能弄壞)。
- [ ] 自動化版:`ADMIN_PW='你的admin密碼' bash tools/test-multiuser-isolation.sh`(若你有 principal+密碼登入法;Cloudflare-only 的話走上面瀏覽器肉眼驗)。
- [ ] ✅ 已知**未認證** path 安全(commit 832dcac 修掉 fail-open、live 驗證 401)。

## Phase 5 — 真錢專屬(只在朋友要用真金才需要)

- [ ] 🔴 **Gap 2b(必補)**:`FillPoller` 目前只輪詢 env 單帳戶 → **朋友自有帳戶的真實成交不會被記錄/歸屬**。真錢上線前要把 FillPoller 多憑證化(逐 cred 輪詢 + tag owner)。
- [ ] **per-user 申報資金/風險上限**:`ResolveDeclaredCapital` 已支援 per-(owner,exchange);用 `UpdateDeclaredCapital(owner, exchange, anchor)` 設朋友的資金上限(env `Risk__DeclaredCapital__{ex}` 是全域 fallback 預設)。
- [ ] **武裝真錢那一步永遠你手按**;真錢循 [[feedback_effective_leverage_is_real_risk]](有效槓桿壓 ~1-3×、名目壓 ~1×權益)。
- [ ] r16 當日虧損熔斷已 per-(owner,exchange) 隔離(commit 76a7f75),朋友互不影響彼此基準。

## 離場 / 回滾

- [ ] `trading-manage.html` → 停用或刪除朋友 principal(或 `POST /admin/users/{id}/disable`)。
- [ ] 從 Cloudflare Access policy 移除其 email。

---

**相關記憶**:[[project_multiuser_risk_isolation]](技術全貌 + 漏洞修復史)、[[project_multiuser_notifications]](推播)、[[feedback_real_money_idempotency]]、[[project_real_money_live_2026_05_23]]。
**驗證腳本**:`tools/test-multiuser-isolation.sh`。
