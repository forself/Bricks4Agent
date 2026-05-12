# LINE Bot LLM Integration — 設計與待選路徑

**寫於**：2026-05-10 深夜（連續 14h debug session 結尾）
**目的**：把 Discord bot-node「自然語言提需求 → tool calling → broker」的能力複製到 LINE OA、讓使用者手機 LINE 就能查行情/下單/審核
**狀態**：研究完、選路、未動工（明天接續）

---

## 1. 現況盤點

### 1.1 已存在的 LINE 基礎建設

```
┌──────────────────────────────────────────────────────────────────────┐
│ LINE Platform (api.line.me)                                          │
└──────┬───────────────────────────────────────────────────────────────┘
       │ POST /webhook/line/  (HTTPS, X-Line-Signature)
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│ ?? 缺：公網 HTTPS 反向代理 / cloudflared tunnel                       │
│        VPS 上目前 **沒有** tunnel running（pgrep cloudflared = 空）  │
└──────┬───────────────────────────────────────────────────────────────┘
       │ HTTP (容器內)
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│ b4a-line-worker container (port 5357, 內部)                          │
│   Program.cs 同時跑 3 個 task：                                       │
│     - WebhookReceiver.RunAsync   listen 5357、簽章驗證、enqueue       │
│     - InboundDispatcher.RunAsync 消費 queue、處理 approval reply、    │
│                                  普通訊息 POST /api/v1/high-level/   │
│                                  line/process                        │
│     - host.RunAsync              連 broker、註冊 5 個 capability     │
└──────┬───────────────────────────────────────────────────────────────┘
       │ POST /api/v1/high-level/line/process
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│ broker · HighLevelEndpoints.cs:14                                    │
│   line.MapPost("/process", coordinator.ProcessLineMessageAsync(...)) │
└──────┬───────────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────────────────────────────────────┐
│ HighLevelCoordinator.ProcessLineMessageAsync                         │
│   3700+ 行的 state machine、處理：                                    │
│     - registration gate                                              │
│     - profile commands                                               │
│     - help / draft / project interview                               │
│     - RAG-augmented LLM reply（透過 LLM proxy）                       │
│   **沒有 tool calling**、不會 call quote.* / trading.* / strategy.*  │
└──────────────────────────────────────────────────────────────────────┘
```

**outbound 路徑**（broker → LINE）跑得起來、且已驗證：
- `line.notification.send` capability → broker LineNotificationService 推 governance alert
- `line.message.send` capability → broker 主動推任意訊息

**inbound 路徑（user → LINE OA → bot）目前 dead**：
- WebhookReceiver code 在容器內、但 compose 沒 expose 5357
- VPS 也沒 cloudflared / nginx / Caddy 把公網 HTTPS 接進去
- LINE Developer Console 的 webhook URL 推測指向「使用者本機 dev 環境的 cloudflared tunnel」（看 [start-with-tunnel.ps1](packages/csharp/workers/line-worker/start-with-tunnel.ps1)）、那個 URL 在 user 沒開 dev 環境時不通

### 1.2 Discord bot-node 對照

```
Discord Gateway (WSS, bot 主動連) ←→ b4a-bot-node container
                                         │
                                         │ spawn claude --print（吃 Max 訂閱、無 token cost）
                                         │
                                         │ tool_call JSON 文字協議
                                         ▼
                                       broker /api/v1/* (X-Internal-Bot-Token)
                                         │
                                         │ approval gate / ACL / dispatch
                                         ▼
                                       worker handlers
```

**關鍵差異**：

| 維度 | bot-node (Discord) | 現有 LINE 路徑 (HighLevelCoordinator) |
|---|---|---|
| LLM 來源 | claude --print subprocess (Max 訂閱) | broker LLM proxy (API token、付費) |
| 工具 | quote.*/strategy.*/trading.*/health.*/audit.* via 文字協議 | **無工具** |
| 訊息協議 | LLM 輸出 ` ```json {call,args} ``` ` | 內建 state machine + LLM free-form reply |
| 持久化 | history.js per (user, channel) | broker shared_context_entries / drafts |
| ACL | access.json deny-by-default + privileged_user_ids | 自有 allowedUserIds CSV |
| Trading 能力 | 完整（含 approval flow） | 完全沒有 |

---

## 2. 三條候選路徑

### Path A：把 tool calling 接進 HighLevelCoordinator

**做什麼**：在 `HighLevelCoordinator.ProcessLineMessageAsync` 的 LLM reply 階段加 tool_call 文字協議偵測 + dispatch。共用既有 LINE 基礎建設、也不影響 Discord 那條。

**Pros**：
- LINE 基建幾乎沒變、只擴 coordinator 內部
- 既有 RAG / drafts / project interview 等高階功能不丟、跟新 trading 工具共存
- 一條 LINE 路徑兩種能力（生活助理 + 交易助理）

**Cons**：
- HighLevelCoordinator 是 3700+ 行 state machine、要動內部 LLM call 的回包路徑風險中等
- LLM 來源不同：用 broker LLM proxy 等於要付 API token 費、不像 Discord 吃 Max 訂閱
- Tool catalog 跟 Discord 那邊要兩份維護（除非也抽 broker 端共用）

**估工**：4-6h

### Path B：平行做 bot-line（鏡像 bot-node）

**做什麼**：在容器內跑一個新 service `bot-line`、訂閱 LINE webhook 直接呼叫 `claude --print`、tool 邏輯複製 bot-node。**棄用** InboundDispatcher → HighLevelCoordinator 那條。

**Pros**：
- LLM 來源一致（吃 Max 訂閱）、無 token 費用
- Tool 行為跟 Discord 完全一致、UX 對齊
- 不動 HighLevelCoordinator、降低既有功能 regression 風險

**Cons**：
- 既有 RAG / project interview 等功能丟掉（LINE 變成純 trading bot）
- 兩個 bot service 各自維護（bot-node + bot-line）、共用邏輯複製貼上
- 需要新建 LINE webhook receiver（因為要 bypass line-worker 的 InboundDispatcher）
- `claude --print` subprocess 要在 line-worker 或 bot-line 容器內安裝、跟 bot-node 同套 dockerfile pattern

**估工**：5-8h

### Path C：bot-core 共用模組重構（理想終態）

**做什麼**：抽出 `discord-bots/bot-core/` 共用 multi-turn / tool dispatch / access / approval polling，`bot-discord` + `bot-line` 兩個薄殼共用。Path B 的 bot-line + 把 bot-node 也重構掉。

**Pros**：
- 沒有重複程式碼
- 加 Slack / Telegram / 任何新 transport 都只是寫薄殼

**Cons**：
- 大工：要先把 bot-node 切成 core + transport、然後再做 bot-line transport
- 過程中 bot-node 可能短暫不穩
- 今晚不可能完工

**估工**：10-15h

---

## 3. 推薦：**B（先求有，明天做）→ C（後續重構，下下次）**

理由：

1. **目標明確**：使用者要 LINE 能像 DC 一樣下單。bot-node 已驗證可用、設計清楚、最快能複製。
2. **HighLevelCoordinator 不丟可惜、但不擋**：那條 RAG/project interview 路徑不是這次的 scope；可以先跟 trading bot 並存，使用者到 LINE OA 講話時兩邊都收到、看哪邊先回（或路由切分）。
3. **C 的乾淨抽象**等 bot-line 做完、知道哪些是真正共通的、再重構不晚。先重構容易過度設計。

⚠️ Caveat：路由衝突要想清楚——一個 LINE OA 一個 webhook URL、訊息該餵 HighLevelCoordinator 還是新 bot-line？需要在 webhook 入口加路由邏輯（例如關鍵字偵測、或加前綴 `/trade ...`）。建議先**完全切換**到 bot-line（HighLevelCoordinator 暫時擱置）、之後重新引入時再加路由。

---

## 4. Path B 具體實作步驟（明天動工順序）

### 4.1 Public HTTPS 對外（30-60 分）

選一：

- **Cloudflare Tunnel**（推薦、免費、不用網域）
  ```bash
  ssh b4a 'curl -L --output cloudflared.deb https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64.deb && sudo dpkg -i cloudflared.deb'
  ssh b4a 'cloudflared tunnel --url http://127.0.0.1:5357'
  # 輸出 https://random.trycloudflare.com → 設進 LINE Developer Console webhook
  ```
  - quick tunnel URL 每次重啟會變、production 應該綁 named tunnel + 你自己的 domain
- **VPS 直接用 Caddy + Let's Encrypt**：要有自己的 domain
- **既有的 start-with-tunnel.ps1**：已經有 Update-LineWebhook function 自動把 URL 推到 LINE API、但是用本機 windows 跑、不適合 VPS

### 4.2 改 line-worker compose 把 webhook port expose（5 分）

`tools/compose.trading.yml`（或 line-worker 自己的 compose）：
```yaml
line-worker:
  ports:
    - "127.0.0.1:5357:5357"  # 給 cloudflared 接、不對外裸露
```

### 4.3 在 line-worker 容器內裝 claude code + 改 InboundDispatcher（2-3h）

最簡單 path：直接改 [InboundDispatcher.ProcessMessage](packages/csharp/workers/line-worker/InboundDispatcher.cs#L124)：

- 不再 POST 到 `/api/v1/high-level/line/process`
- 改 spawn `claude --print` subprocess、模仿 [bot-node llm.js](discord-bots/bot-node/src/llm.js) 的呼叫
- LLM 回應裡偵測 `tool_call` JSON、dispatch 同一套 broker capabilities（with X-Internal-Bot-Token header）
- 結果包成 reply 透過 `lineApi.PushMessageAsync` 回給 user

**chunky 的部分**：
- C# 比 JS 多一些 boilerplate、要寫 ClaudeCliService 處理 stdin/stdout
- Tool catalog 要在 C# 重寫一份（或讓 LineWorker 用 HTTP 呼 bot-node 的 dispatchTool？太繞）

**或者反向 design**：
- LINE webhook URL 直接打到 **bot-node** 容器（bot-node 加 webhook endpoint）
- bot-node 用 JS 處理（JS 已有 dispatchTool）
- line-worker 退化成純 outbound capability provider

這個倒過來的 design 更合 bot-core 重構的方向、推薦這條：

```
LINE Platform → cloudflared → bot-node:NEW_PORT/webhook/line
                               │
                               │ 同 dispatchTool 邏輯（共用 tools.js）
                               ▼
                             broker (X-Internal-Bot-Token)
                               │
                               │ outbound reply 透過 line.message.send capability
                               ▼
                             line-worker（沒變、繼續做 outbound）
```

### 4.4 bot-node 加 LINE webhook handler

新檔：[discord-bots/bot-node/src/line.js](discord-bots/bot-node/src/line.js)
- HTTP server 聽 `POST /webhook/line/`、簽章驗證（用 LINE_CHANNEL_SECRET）
- parse events、抽 text message
- 走 runMultiTurn(userId, channelId="line:DM", text)（新加 channel scheme `line:`）
- final reply 透過 broker.callBroker('POST', '/api/v1/workers/line/messages/send', {...}) ——但 broker 端沒這 route、要改用 `dispatchCapability` 或直接呼叫 line.message.send capability
- access.json 加 `line_allowed_user_ids`：LINE userId 跟 Discord userId 不同名稱空間

### 4.5 LINE Developer Console 設 webhook URL

開 https://developers.line.biz/console/、選你的 OA channel、Messaging API 分頁、Webhook URL 貼 cloudflared 給的 URL（記得加 `/webhook/line/` 後綴）、Verify、Use webhook 打開。

---

## 5. 開放議題（明天討論）

1. **路由策略**：HighLevelCoordinator 的工作流功能（registration / project interview）跟新 trading bot 並存？還是先全切到 trading？
2. **LINE Channel ID**：trading bot 要不要用獨立 LINE OA？避免跟生活助理混（建議獨立、多 OA 沒額外成本）
3. **Public URL 持久度**：cloudflared quick tunnel 每次重啟換 URL、要不要綁 named tunnel + 自己 domain？
4. **Tool catalog 重複**：bot-node tools.js 裡的 tool 描述要不要從 broker 取（單一真相源）？短期不重要、長期跟 bot-core 重構一起做
5. **LINE 對應 privileged_user_ids**：access.json 結構要分 platform→user list 還是 user 跟 platform 都一個全域 list？

---

## 6. 今晚已研究 / 不要重做的部分

- WebhookReceiver.cs 簽章驗證已寫好、不用重寫
- LineApiClient 的 push / signature validate / audio download 都可用
- broker 端 line.message.send capability 已驗證能 outbound push
- InboundDispatcher 內的 audio download / approval reply 流程可參考重用
- compose / 容器網路架構已穩定

## 7. 不該重做（避免明天踩雷）

- 不要動 HighLevelCoordinator 內部除非真的選 Path A——3700 行的 state machine 動一行可能毀別的 LINE 流程
- 不要在 line-worker 容器內裝 claude code subprocess——line-worker 是 .NET 容器、加 nodejs runtime 又重又奇怪、改在 bot-node 容器處理
- 不要假設 LINE userId 跟 Discord userId 可互通——兩個是不同 namespace
