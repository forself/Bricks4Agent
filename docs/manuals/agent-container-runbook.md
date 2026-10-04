# 受控代理容器操作手冊 (Agent Container Runbook)

Date: 2026-06-13
依據設計: [ControlledAutonomousAISystemTechnicalDesign.md](../designs/ControlledAutonomousAISystemTechnicalDesign.md)
啟用紀錄: [AgentContainerActivation-2026-06-13.md](../reports/AgentContainerActivation-2026-06-13.md)

## 1. 這是什麼

受控代理容器是設計規格 §6.6 的「受控主體執行殼層」:LLM 驅動的 agent 在隔離容器內,**只能**向 broker(控制平面)請領工作、讀授權上下文、呼叫模型、產生結構化執行請求、回報結果;**不可**直接碰工具、資料源、倉庫、部署或模型供應商。所有工具執行都經 broker 領 capability + 裁決,人與 AI 走同一授權路徑。

與 LINE sidecar(`line-sidecar.ps1`)不同:那是 broker + line-worker + tunnel 的常駐服務;受控代理容器是 podman compose 起的一次性治理 stack(broker + worker(s) + agent),用來執行單一受控任務。

## 2. 前置需求

- **podman**(Windows 用 podman machine / WSL backend)。首次需 `podman machine start`(若 `LAST UP: Never`)。

- **node**(跑 stack 啟動腳本)。

- **金鑰**:compose 不附任何預設金鑰。下列 node 測試腳本每次執行都自動產生;手動 `podman compose` 前要先用產生器建立金鑰檔,見 §3.4。

- LLM 後端三選一:

- mock(內建,無需外部)

- 本機 **ollama**(`localhost:11434`,需先 `ollama pull <model>`)

- **商用 API**(OpenAI-compatible 或 Anthropic Claude,需 API key)

## 3. 三條 LLM 路徑(都已實測通過 2026-06-13)

每條都是 `podman compose up` 一個 stack:build 映像 → 起 broker + worker(s) + agent → agent 註冊 session、領 capability、經 broker 裁決執行工具。

每支 node 測試腳本(含 §9 的 execution-adapter 測試)都會在記憶體中產生一組新的 broker 金鑰與 worker 憑證,經環境變數同時傳給 `up` 與 `down`,不寫任何檔案。

> 2026-10-04 起 compose 改為必填金鑰(`${VAR:?...}`)、`WORKER_AUTH_ENFORCE` 預設 `true`、對外埠只綁 `127.0.0.1`;這些變更之後尚未以 podman 重新實測。

### 3.1 mock(最快,離線驗證治理鏈)

```powershell
node tools/agent/tests/test-podman-governed-stack.js
```

驗證 `STACK_OK` + `[governed] read_file`——agent 不直連工具,經 broker 裁決執行 governed `read_file`。

### 3.2 本機 ollama(真實開源模型)

先確認 ollama 有模型(`ollama list`),然後:

```powershell
node tools/agent/tests/test-podman-ollama-host-stack.js
```

自動從 `/api/tags` 選一個可用模型。要指定模型用 `STACK_MODEL`。這條 live-host 驗證會檢查 broker-mediated Ollama round-trip、agent completion、session close 與無 broker/API error；精確 sentinel 文字只由 mock stack 保證，因為不同本機模型可能改寫回覆。

### 3.3 商用 API(OpenAI-compatible / Claude)

broker `LlmProxy` 的 openai provider 支援 `v1/chat/completions`(chat)與 `v1/responses`(responses),帶 Bearer key。把 BaseUrl 指向真實 OpenAI 即可:

```powershell
$env:OPENAI_BASE_URL  = "https://api.openai.com"
$env:OPENAI_API_KEY   = "<你的 OpenAI key>"
$env:OPENAI_API_FORMAT = "responses"   # gpt-5.x 用 responses;gpt-4o 系列用 chat
$env:STACK_MODEL = "gpt-5.4-mini"
node tools/agent/tests/test-podman-openai-compatible-stack.js
```

不設 `OPENAI_BASE_URL` 時預設指向內建 mock-openai，這是目前可離線重跑的 OpenAI-compatible protocol 驗證。若設定 `OPENAI_BASE_URL=https://api.openai.com`、`OPENAI_API_KEY`、`OPENAI_API_FORMAT` 與 `STACK_MODEL`，同一 compose path 會改由 broker `LlmProxy` 打真實 OpenAI；真實模型測試依外部 API key 與模型行為而定。

`LlmProxy` 也支援 Anthropic Claude Messages API (`Provider=anthropic`, `BaseUrl=https://api.anthropic.com`, `ApiFormat=messages`, `DefaultModel=claude-sonnet-4-6`)；這條目前由 broker/sidecar 設定與單元測試覆蓋，不把既有 OpenAI-compatible compose 測試誤稱為 Claude compose stack。

### 3.4 手動執行與金鑰

手動跑 compose 時,先在 repo **以外**產生一份金鑰檔,之後每次 `up` 與 `down` 都帶同一份 `--env-file`(`down` 也會展開必填變數,缺值一樣失敗):

```powershell
node tools/agent/container/gen-stack-secrets.mjs
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
podman compose --env-file "$HOME/.bricks4agent/agent-stack.env" -f tools/agent/container/compose.yml down -v
```

- 輸出位置:`$env:BRICKS4AGENT_SECRETS_DIR/agent-stack.env`;未設定時為 `~/.bricks4agent/agent-stack.env`。產生器會印出實際路徑,但不印出金鑰值。
- 產生器拒絕寫進 repo 內;既有檔案要加 `--force` 才覆寫(等同輪替全部金鑰,之後用 `down -v` 重建 stack)。`--self-test` 只在記憶體中檢查,不寫檔。變數清單見 `tools/agent/container/agent-stack.env.example`(只有名稱)。
- 為什麼不能放進 repo:agent 容器把整個 repo 掛在 `/workspace` 且有讀取授權;compose 也會自動讀取 compose 檔旁的 `.env`。金鑰檔放在 repo 內,等於讓受控 agent 讀得到 broker 私鑰。
- 必填變數:三個 compose 檔都要 `BROKER_SCOPED_TOKEN_SECRET`、`BROKER_MASTER_KEY_BASE64`、`BROKER_ECDH_PRIVATE_KEY_BASE64`、`BROKER_ECDH_PUBLIC_KEY_BASE64`(agent 釘選這把公鑰,必須與私鑰成對);`compose.yml` 另需 LINE、file、execution-adapter 三組 worker 的 `*_AUTH_KEY_ID` 與 `*_AUTH_SHARED_SECRET`。
- broker 啟動驗證:compose 中的 broker 以 Production 執行,啟動時拒絕佔位值(空白、`CHANGE_ME*`、`REPLACE_WITH_*`)與任何曾以 compose 預設值公開過的金鑰,並檢查格式。不要在 compose 加 `ASPNETCORE_ENVIRONMENT=Development` 來繞過。
- `WORKER_AUTH_ENFORCE` 預設 `true`:worker 註冊與 LINE worker 的 HTTP 路由都要驗證憑證;broker 端也登錄了 execution-adapter 的憑證(索引 2,同時蓋掉 `appsettings.json` 該索引的範本憑證)。
- 對外發布的埠(broker、mock LLM、LINE webhook)只綁 `127.0.0.1`;容器之間仍經 compose 網路互通。

> 兩條 LLM 路徑釐清:LINE 高階模型(`HighLevelLlm`)目前 sidecar 會優先讀 `ANTHROPIC_API_KEY` 並設定 `anthropic` / `claude-sonnet-4-6`;沒有該 key 時才走 OpenAI-compatible `Api.txt` fallback。受控代理容器走 broker `LlmProxy`,agent 不直接持有 provider key。

## 4. 用真實商用 API 時的注意

- `OPENAI_API_FORMAT`:responses API 的原始回應把文字放在 `output[].content[].output_text`(頂層 `output_text` 是 SDK 便利欄位,真實 API 不一定有);broker parser 兩者皆支援。gpt-5 系列的 `output[]` 會夾帶 reasoning item,parser 會略過。

- Claude Messages API 需要 `max_tokens`;目前預設 `MaxOutputTokens=4096`。

- key 不應放進 repo;Anthropic 使用 `ANTHROPIC_API_KEY`,OpenAI-compatible fallback 使用 `C:\secure\Bricks4Agent\Api.txt` 或環境變數。broker 金鑰與 worker 憑證同理,用 `gen-stack-secrets.mjs` 產生在 repo 以外(§3.4)。

## 5. FunctionPool 與健康端點

受控代理容器 stack 的 broker 預設 `FunctionPool:Enabled=true`(worker dispatch + container manager 的基礎)。`/api/v1/health/workers`、`/health/score` 等監控端點只在 FunctionPool 啟用時註冊——它們的 handler 依賴 worker registry 服務,關閉時不註冊以免 Minimal API 把未註冊服務推斷成 body。純 LLM 對話的 stack(如 ollama/openai host 測試)可 FunctionPool=false。

## 6. 疑難排解(2026-06-13 通電時實際遇到並修掉的)

| 症狀 | 根因 | 已修 |
|---|---|---|
| broker 容器 build `NETSDK1152` | broker 引用 site-crawler-worker,worker appsettings 流入 publish | broker.csproj publish target 移除重複 |
| broker `FunctionPool=false` 啟動即崩 | HealthScoreService 無條件依賴 IWorkerRegistry | 監控只在 FunctionPool 啟用時註冊 |
| register 回 500 `No data exists` | Linux Sqlite `IsDBNull` edge case | BaseOrm 改用 `GetValue` |
| `GET /api/v1/health` 回 `Body was inferred` | health endpoint 在 FunctionPool=false 仍註冊但服務缺失 | endpoint 註冊也 gate 在 FunctionPool |
| 真實 OpenAI 回 200 但 agent 輸出空 | parser 只認頂層 `output_text` | 從 `output[]` 聚合 message content |
| Claude 回 `400` | Messages API 缺 `max_tokens` 或 request shape 不符 | 確認 `Provider=anthropic` 且使用 broker Claude adapter |

## 7. 網路隔離(§13.1,已實作 2026-06-13)

三條 compose stack 都把 **agent 容器單獨放在 `internal: true` 的 `agent-net`**——該網路無對外閘道,agent 只能連 broker、無法自行對外連網。broker 另接 bridge 網路(`egress`/`control-net`/`worker-net`),保有對外出口(真實 OpenAI/Claude、host ollama)與 host port-publishing;商用 API 仍可用,因為**出口是 broker 不是 agent**(agent 不持金鑰)。agent 無 published port,故放 internal 網路安全(ingress 走 broker)。

為何不直接把共用網路設成 internal:會連帶封住同網路上「有 published port 的 mock LLM」(internal 網路上的 port-publishing 在 docker/podman 行為不可靠)。所以只密封 agent。

驗證(egress 拒絕):

```powershell
# internal 網路的容器連不到外網(bad address / 逾時)
podman network create --internal seal-probe
podman run --rm --network seal-probe alpine wget -T 4 -q -O- https://api.openai.com/v1/models   # 失敗
# bridge 網路的容器連得到(回 401,代表連到了)
podman run --rm alpine wget -T 6 -q -S -O /dev/null https://api.openai.com/v1/models            # 連到(401)
podman network rm seal-probe
```

mock stack 已實測:agent 在 `agent-net` 仍能註冊 session、經 broker 裁決跑 governed `read_file` → `STACK_OK`。ollama/openai stack 的網路拓樸相同(broker 在 `egress`、agent 在 `agent-net`),但因需 GPU/金鑰未在此機離線複驗——使用者跑這兩條時即同時驗證。

## 8. OS 層容器 hardening(§13,已實作 2026-06-13)

三條 compose stack 的 **agent 服務**(受控主體,不受信任)都套上 OS 層沙箱:

| 設定 | 作用 |
|---|---|
| `read_only: true` | rootfs 唯讀;`/workspace` bind mount 仍可寫(唯讀不影響掛載卷) |
| `tmpfs: [/tmp]` | 唯一可寫的 rootfs 路徑放 tmpfs(`os.tmpdir()` 用) |
| `cap_drop: [ALL]` | 丟掉所有 Linux capability(agent 以非 root uid 10001 跑,無需任何 cap) |
| `security_opt: [no-new-privileges:true]` | 擋 setuid/setgid 提權 |
| `pids_limit: 256` | 限制行程數(fork-bomb 防護) |

seccomp 用 runtime 預設 profile(尚未寫客製 profile)。

驗證(以 agent 映像直接驗證強制生效):

```powershell
podman run --rm --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true `
  --entrypoint sh bricks4agent-agent:latest -c `
  'echo uid=$(id -u); touch /app/probe 2>/dev/null && echo rootfs:BAD || echo rootfs:blocked; touch /tmp/probe && echo tmp:ok; grep CapEff /proc/self/status'
# 預期:uid=10001 / rootfs:blocked / tmp:ok / CapEff:0000000000000000
```

mock stack 已實測:套上述 hardening 後 agent 仍能完成 governed `read_file` → `STACK_OK`。

## 9. 執行配接器(§18.1,已實作 + 單元驗證 2026-06-13)

`execution-adapter-worker` 讓受控 agent 能真的做事(不再只讀):agent 產生結構化請求 → broker 裁決(grant/quota/scope/policy)→ adapter worker 執行並附證據。agent 永遠碰不到 adapter,只有 broker 會 dispatch。

兩個能力:

| 能力 / route | 行為 |
|------|------|
| `repo.patch.apply` / `execution.repo.apply_patch` | 驗 patch(非自由 shell)、驗 base_commit==HEAD、限 `scope.allowed_paths`、`git apply --check` 後套用、存 diff 證據、支援 idempotency_key(重放回前次結果不重套) |
| `build.test.run` / `execution.build_test.run` | 只跑白名單命令(npm test / npm run build / dotnet test / dotnet build / pytest;預設見 `packages/csharp/workers/execution-adapter-worker/Handlers/BuildTestRunHandler.cs:23-26`,可由 `Worker:BuildTest:Whitelist` 覆寫)、不經 shell、收 stdout/stderr+exit、截斷大輸出、存 log 證據 |

adapter 是**受信任執行節點**:套 §13.2 OS 加固(非 root uid 10004、read-only rootfs、cap-drop ALL、no-new-privileges、無 docker socket),但與 agent 不同 —— 可寫 workspace(它就是經控制平面中介的寫入路徑)、有出口(build/test restore)。

驗證:

```bash
# broker 單元測試(含 38 條執行配接器斷言,對真實 git 操作)
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
# 設定驗證(compose 接線 + 加固 + 能力 seed + 工具映射)
node tools/agent/tests/test-execution-adapter-config.js
# 端到端:模型驅動 agent 經治理鏈套 patch,斷言檔案真的被改(profile=adapters)
node tools/agent/tests/test-podman-execution-adapter-stack.js
```

compose 中 adapter 服務以 **profile 隔離**(`--profile adapters`),預設不啟動(不影響既有 governed stack 測試);預設掛載 throwaway workspace(`ADAPTER_WORKSPACE` 可覆寫),**不會動到真實 repo**。

**已端到端驗證(2026-06-13)**:stack 測試讓 mock 模型驅動 agent 呼叫 `apply_patch`,經 broker 裁決(grant→policy→pool dispatch)→ adapter `git apply` → fixture 檔案實際被改。這條 e2e 揪出四個單元測試結構上抓不到的整合 bug(mock 環境變數接線、git bind-mount 的 `safe.directory`/`core.fileMode`、能力 route 必須等於 agent 工具名、workspace 根 scope 正規化),皆已修。

> 慣例提醒:agent 送出的 payload `route` 就是「工具名」(如 `apply_patch`),broker policy 要求 `route == capability.Route`。所以新能力的 `Route` 必須設成工具名(`repo.patch.apply`→`apply_patch`、`build.test.run`→`run_build_test`),而非設計文件的 `execution.*` 邏輯名。

尚未做:broker `--integration` HTTP 對新 route 的覆蓋(stack 測試已涵蓋真實 dispatch 路徑)。

## 10. 範圍界線(對照規格 §13/§18;2026-09-26 核對)

**已實作**:
- §18.2 審批服務與風險分級:`PolicyEngine` 依 `RiskLevel` / `approval_policy` 裁決 —— High/Critical 需管理員層審批(Critical 需 2 票)、`auto_if_task_scope_match` 逸出 scope 轉管理員審批(`packages/csharp/broker-core/Services/PolicyEngine.cs:68-117`);`RequireApproval` 時 broker 建審批請求並擱置執行(`packages/csharp/broker-core/Services/BrokerService.cs:256-280`)。放行端點:使用者層 `/api/v1/user/approvals`(`packages/csharp/broker/Endpoints/UserApprovalEndpoints.cs`)、管理員層 `/api/v1/local-admin/approvals`(`packages/csharp/broker/Endpoints/LocalAdminEndpoints.cs:933-953`)。

**尚未實作**:
- agent 客製 seccomp profile(目前用 runtime 預設)

重點:容器「關得住」已做到;危險動作的人工放行也已接上,剩客製 seccomp。
