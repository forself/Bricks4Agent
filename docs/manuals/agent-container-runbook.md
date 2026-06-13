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
- LLM 後端三選一:
  - mock(內建,無需外部)
  - 本機 **ollama**(`localhost:11434`,需先 `ollama pull <model>`)
  - **商用 API**(OpenAI/ChatGPT,需 API key)

## 3. 三條 LLM 路徑(都已實測通過 2026-06-13)

每條都是 `podman compose up` 一個 stack:build 映像 → 起 broker + worker(s) + agent → agent 註冊 session、領 capability、經 broker 裁決執行工具。

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

自動選 `/api/tags` 第一個模型。要指定模型用 `STACK_MODEL`。實測:qwen3.6(23GB,native tool calling)→ `OLLAMA_STACK_OK`。

### 3.3 商用 API(ChatGPT)

broker `LlmProxy` 的 openai provider 支援 `v1/chat/completions`(chat)與 `v1/responses`(responses),帶 Bearer key。把 BaseUrl 指向真實 OpenAI 即可:

```powershell
$env:OPENAI_BASE_URL  = "https://api.openai.com"
$env:OPENAI_API_KEY   = "<你的 OpenAI key>"
$env:OPENAI_API_FORMAT = "responses"   # gpt-5.x 用 responses;gpt-4o 系列用 chat
$env:STACK_MODEL = "gpt-5.4-mini"
node tools/agent/tests/test-podman-openai-compatible-stack.js
```

不設 `OPENAI_BASE_URL` 時預設指向內建 mock-openai。實測:真實 gpt-5.4-mini(responses)→ broker `GET /v1/models` + `POST /v1/responses` 皆 200,agent 收到回應經治理鏈回 `STACK_OK`。

> 兩條 LLM 路徑釐清:LINE 高階模型(`HighLevelLlm`,appsettings)早已配真實 OpenAI(api.openai.com / gpt-5.4-mini);受控代理容器走 broker `LlmProxy`,本節補的是它也能用商用 API。

## 4. 用真實 OpenAI 時的注意

- `OPENAI_API_FORMAT`:responses API 的原始回應把文字放在 `output[].content[].output_text`(頂層 `output_text` 是 SDK 便利欄位,真實 API 不一定有);broker parser 兩者皆支援。gpt-5 系列的 `output[]` 會夾帶 reasoning item,parser 會略過。
- key 不應放進 repo;放 `C:\secure\Bricks4Agent\Api.txt` 或環境變數。

## 5. FunctionPool 與健康端點

受控代理容器 stack 的 broker 預設 `FunctionPool:Enabled=true`(worker dispatch + container manager 的基礎)。`/api/v1/health/workers`、`/health/score` 等監控端點只在 FunctionPool 啟用時註冊——它們的 handler 依賴 worker registry 服務,關閉時不註冊以免 Minimal API 把未註冊服務推斷成 body。純 LLM 對話的 stack(如 ollama/openai host 測試)可 FunctionPool=false。

## 6. 疑難排解(2026-06-13 通電時實際遇到並修掉的)

| 症狀 | 根因 | 已修 |
|------|------|------|
| broker 容器 build `NETSDK1152` | broker 引用 site-crawler-worker,worker appsettings 流入 publish | broker.csproj publish target 移除重複 |
| broker `FunctionPool=false` 啟動即崩 | HealthScoreService 無條件依賴 IWorkerRegistry | 監控只在 FunctionPool 啟用時註冊 |
| register 回 500 `No data exists` | Linux Sqlite `IsDBNull` edge case | BaseOrm 改用 `GetValue` |
| `GET /api/v1/health` 回 `Body was inferred` | health endpoint 在 FunctionPool=false 仍註冊但服務缺失 | endpoint 註冊也 gate 在 FunctionPool |
| 真實 OpenAI 回 200 但 agent 輸出空 | parser 只認頂層 `output_text` | 從 `output[]` 聚合 message content |

## 7. 範圍界線(尚未做,對照規格 §13/§18)

受控代理容器目前是「通電 + governed 工具執行」的 MVP 骨架。**尚未實作**:
- §13 完整容器安全 hardening(read-only rootfs、`cap-drop=ALL`、`no-new-privileges`、seccomp、tmpfs)
- §13.1 嚴格網路隔離(容器只能連控制平面、禁直接對外)— 模型呼叫已走 broker(容器不持金鑰、商用 API 靠 broker 出口),但尚未*強制*容器無法自行對外連網
- §18.2 審批服務、風險分級
- repo-adapter / build-test-adapter 等執行配接器

這些是後續階段。
