# B4A bot-node

Discord bot in Node.js — an alternative to the Claude Code `--channels` plugin
approach (which has been blocked on upstream regressions). Uses discord.js
to receive messages, dispatches via the existing broker capability system to
keep all governance (ACL / Approval / Risk rules / Audit chain) intact.

## Phase roadmap

| Phase | What | Status |
| --- | --- | --- |
| 1 | Echo bot — Discord connect + access.json filter, no LLM | done |
| 2 | Wire `claude --print` headless for chat | done |
| 3 | Tool calling via text protocol — read-only capabilities | active |
| 4 | Trading capabilities + approval workflow integration | pending |

## Phase 3 setup

Phase 3 connects bot ↔ broker via shared secret. Both sides need
`BOT_INTERNAL_TOKEN` set to the SAME random string.

1. Generate a random token (any random hex/base64 string is fine):

   ```powershell
   # PowerShell: 32-char hex
   -join ((48..57) + (97..102) | Get-Random -Count 32 | ForEach-Object { [char]$_ })
   ```

2. Add to `bot-node/.env`:

   ```text
   BOT_INTERNAL_TOKEN=<paste here>
   ```

3. Add the SAME value to `tools/.env.trading` (broker side):

   ```text
   BOT_INTERNAL_TOKEN=<paste here>
   ```

4. Rebuild + restart broker AND bot-node so both pick up the env:

   ```powershell
   # broker side
   docker compose -f tools\compose.trading.yml --env-file tools\.env.trading up -d --build broker
   # bot side (after running setup once already)
   docker compose -f docker\compose.sandboxed.yml --env-file .env up -d --build
   ```

5. Test in Discord:
   - `@bot 現在 BTC 多少錢` → bot calls `quote.prices`, replies with current price
   - `@bot 我的 perp 帳戶餘額` → bot calls `trading.account`, replies
   - `@bot 你能幫我下單嗎` → bot says no (ACL denies trading.order, by design)

## Setup

```bash
# 1. Discord bot 同 token 跟既有 claude bot 共用
cp .env.example .env
# 編輯 .env、填 DISCORD_BOT_TOKEN

# 2. 設定 access.json 白名單（user / channel / guild ID）
cp access.json.example access.json
# 編輯 access.json

# 3. 確認 b4a-trading-net 已建（從 trading 堆疊）
docker network ls | grep b4a-trading-net

# 4. 啟動
cd docker
docker compose -f compose.sandboxed.yml --env-file ../.env up -d --build

# 5. 看 log
docker compose -f compose.sandboxed.yml logs -f
```

**重要**：同時只能有一個 bot 連同個 Discord token。啟動 bot-node 之前先 stop claude bot：

```bash
docker compose -f ../../claude/docker/compose.sandboxed.yml down
```

## Sandbox 設定

跟既有 claude bot 同套 hardening：

- `cap_drop: [ALL]` + `no-new-privileges`
- `b4a-trading-net` only → 進不了 host LAN
- `host.docker.internal` 改指 127.0.0.1 → 防 host port escape
- access.json mount `:ro` → bot 不能改自己的權限白名單
- 容器內非 root user（`claude-user:10001`）
- 不開 `read_only`：claude code 會寫 `~/.cache` 等位置、全 readonly 會 silent fail；上面四項已等同既有 claude bot 防護等級

## 跟既有 claude bot 的差異

| 維度 | claude bot | bot-node |
| --- | --- | --- |
| LLM 入口 | claude code `--channels` plugin（blocked） | `claude --print` subprocess（phase 2+） |
| Tool calling | MCP（接不通） | text protocol via broker dispatch（phase 3+） |
| Discord token | 同一個（不能同時跑） | 同一個 |
| Sandbox | 同套 | 同套 |
| Governance | 透過 broker capability | 透過 broker capability（同） |
| 維護成本 | Anthropic 修才會動 | 自己改 |

## Architecture（phase 4 final）

```text
Discord 訊息（access.json 過濾）
  ↓
discord.js client（容器內）
  ↓ spawn `claude --print` subprocess（吃 Max 訂閱）
LLM response（text or {action: ..., args: ...} JSON）
  ↓
若是 tool_call → HTTP POST broker /api/v1/{capability}
  → broker PoolDispatcher：ACL → Approval → Risk → Audit chain（governance）
  ↓
result 餵回 claude --print 下一輪
  ↓
最終文字 → Discord reply
```
