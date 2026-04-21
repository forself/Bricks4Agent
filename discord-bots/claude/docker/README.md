# B4A Discord Bot — Sandboxed Docker 版

## 目的

把 `claude --channels ... --dangerously-skip-permissions` 裝進容器。
Bot 就算被 prompt injection 誘導去執行 bash，也只能：

- 看 `/home/claude-user/workspace`（唯讀 persona）
- 讀寫 `/home/claude-user/.claude`（自己的 plugin 狀態 / 憑證）
- 走 `b4a-trading-net` 打到 `broker:5000` 這個唯一能走的服務

**碰不到 host 檔案系統、碰不到 host 上其他 port、碰不到 LAN 其他機器。**

這和直接在 host 上跑 `claude --permission-mode bypassPermissions` 的差別：
host 版本有等同使用者權限，可以改 `AI_Project` 的任何檔案、發任何 HTTP、
跑任何程式。容器版沒有這些能力。

## 檔案

| 檔案 | 用途 |
|------|------|
| `compose.sandboxed.yml` | 加了 trading-net + cap_drop 的收緊版 compose |
| `../Dockerfile` | 共用（既有） |
| `../entrypoint.sh` | 共用（既有） |
| `../workspace-docker/CLAUDE.md` | persona，URL 改為 `http://broker:5000` |
| `../.env` | `DISCORD_BOT_TOKEN=...`（既有） |

## 啟動前先決條件

1. 交易堆疊要起來（`b4a-trading-net` 這個 external 網路才會存在）：
   ```powershell
   cd C:\Users\USER\Desktop\AI_Project
   docker compose -f tools/compose.trading.yml --env-file tools/.env.trading up -d
   ```
   或重開機（`B4A Trading Stack` 排程會自動跑）。

2. 確認 broker healthy：
   ```powershell
   curl http://localhost:5100/api/v1/health/workers
   ```

## 啟動 bot 容器

```powershell
cd C:\Users\USER\discord-bots\claude\docker
docker compose -f compose.sandboxed.yml --env-file ../.env up -d --build
```

初次 build 會花一點時間裝 Node + Bun + claude-code。

## 看 log / 互動

```powershell
# tail 看 claude 在做什麼
docker compose -f compose.sandboxed.yml logs -f

# 接進容器（真有互動需求時才用）
docker exec -it b4a-discord-bot bash
```

## 停止 / 移除

```powershell
docker compose -f compose.sandboxed.yml down
```

## 驗證 sandbox 真的在

容器內跑下列指令應該全部失敗：

```bash
# 不該能到 host
curl http://host.docker.internal:5100  # 沒加 extra_hosts，解不到
curl http://10.0.0.1:5100              # 應被 b4a-trading-net 擋掉

# 不該能打其他服務
curl https://google.com                # 看網路策略；至少不該能 escape 到 host 服務
```

該能通的：

```bash
curl http://broker:5000/api/v1/health/workers   # 200 OK
```

## 跟既有 `docker-compose.yml` 差異

`../docker-compose.yml`（昨天做的）：
- 用 `host.docker.internal:host-gateway` → bot 能打 host 任何 port
- 沒 `cap_drop`
- mount `./workspace`（persona URL = `localhost:5100`，實際打不通）
- 適合「只做網路對 host + 開發用」

`compose.sandboxed.yml`（這個）：
- 不給 host 存取路徑
- `cap_drop: ALL` + `no-new-privileges`
- mount `../workspace-docker`（persona URL = `broker:5000`，真的通）
- 生產用 / 你要的 sandbox 情境

兩個 compose 可共存，需要哪個就用哪個。
