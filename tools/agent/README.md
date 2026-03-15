# AI Agent CLI

`tools/agent` 是 Bricks4Agent 的本地代理與生成入口，支援三種模式：

- 本地模式：Agent 直接連 Ollama 或 OpenAI-compatible provider。
- Pipeline 模式：Agent 驅動 CRUD / `project.json` 生成流程。
- Governed 模式：Agent 只向 broker 發送 `POST + JSON` 請求；工具執行與 LLM 對話都由 broker 轉發與裁決。

## 需求

- Node.js 18+
- 本地模式可搭配 Ollama
- 雲端模式可用 OpenAI-compatible API provider
- Governed 模式需搭配 broker

## Provider

支援的 provider alias：

| Provider | 說明 |
|---|---|
| `ollama` | 本地 Ollama，預設 `http://localhost:11434` |
| `openai` | OpenAI，預設使用 Responses API |
| `gemini` | Gemini 的 OpenAI-compatible endpoint |
| `deepseek` | DeepSeek 的 OpenAI-compatible endpoint |
| `groq` | Groq 的 OpenAI-compatible endpoint |
| `mistral` | Mistral 的 OpenAI-compatible endpoint |

若未指定 `--provider`，CLI 會優先看 API key；有 key 時預設用 `openai`，否則用 `ollama`。

## 快速開始

### 本地 Ollama

```bash
ollama serve
ollama pull llama3.1
node tools/agent/agent.js
```

### 雲端 provider

```bash
node tools/agent/agent.js --provider openai --api-key sk-xxx --model gpt-5
node tools/agent/agent.js --provider gemini --api-key AIza... --model gemini-2.0-flash
```

### 單次執行

```bash
node tools/agent/agent.js --run "閱讀 AGENT.md 並摘要限制"
```

### 列出模型

```bash
node tools/agent/agent.js --list-models
```

### 生成 `project.json`

```bash
node tools/agent/agent.js --generate --project-path projects/PhotoDiary
node tools/agent/agent.js --generate --project-path projects/PhotoDiary --dry-run
node tools/agent/agent.js --generate --project-path projects/PhotoDiary --validate
```

### CRUD pipeline

```bash
node tools/agent/agent.js --pipeline crud --entity Product --fields "[{\"name\":\"Name\",\"type\":\"string\"},{\"name\":\"Price\",\"type\":\"decimal\"}]"
```

## Governed 模式

Governed 模式不是只有「工具經 broker」，而是：

- 工具請求走 broker
- LLM health / models / chat 也走 broker
- agent 不應直接持有 provider API key 作為正式執行路徑
- broker 依 session / role / grant / capability / scope / policy 決定是否允許

### 啟動

```bash
node tools/agent/agent.js \
  --governed \
  --broker-url http://localhost:5000 \
  --broker-pub-key <base64> \
  --principal-id prn_xxx \
  --task-id task_xxx \
  --role-id role_reader \
  --run "讀取 README.md"
```

也可用環境變數：

```bash
set BROKER_URL=http://localhost:5000
set BROKER_PUB_KEY=MFkwEwYH...
set BROKER_PRINCIPAL_ID=prn_xxx
set BROKER_TASK_ID=task_xxx
set BROKER_ROLE_ID=role_reader
node tools/agent/agent.js --governed
```

### Broker 契約

Governed agent 只能透過 broker 的 `POST` 路由提出請求：

- `POST /api/v1/sessions/register`
- `POST /api/v1/execution-requests/submit`
- `POST /api/v1/sessions/heartbeat`
- `POST /api/v1/sessions/close`
- `POST /api/v1/capabilities/list`
- `POST /api/v1/grants/list`
- `POST /api/v1/runtime/spec`
- `POST /api/v1/llm/health`
- `POST /api/v1/llm/models`
- `POST /api/v1/llm/chat`

Governed prompt 會注入：

- 當前 session 資訊
- 目前 granted capability 清單
- `scope.paths` / `scope.routes`
- broker URL 與完整路由
- 每個 POST 路由的標準 JSON body 格式
- LLM runtime spec，例如 default model / override policy / tool-calling 能力

### 功能層與範圍層

- 功能層：`capability_id`
- 範圍層：`scope.routes`、`scope.paths`

也就是說，不是只有「能不能讀檔」，而是「能不能以某個 capability 對某些 route / path 發請求」。

### Governed 模式的重要行為

- `--provider`、`--api-key`、`--host` 在 governed 模式下會被忽略
- `--list-models` 在 governed 模式下會改走 broker `/api/v1/llm/models`
- AgentLoop 會把 governed executor 當成 provider 使用
- LLM 連線是 broker-mediated，不是 agent 直連 upstream provider

## Podman 容器

最小 governed agent 容器定義在 `tools/agent/Containerfile`。

### 建置

```bash
podman build -f tools/agent/Containerfile -t bricks4agent-agent:dev .
```

### 執行

```bash
podman run --rm -it \
  -v %CD%:/workspace \
  -e BROKER_URL=http://host.containers.internal:5000 \
  -e BROKER_PUB_KEY=<base64> \
  -e BROKER_PRINCIPAL_ID=prn_xxx \
  -e BROKER_TASK_ID=task_xxx \
  -e BROKER_ROLE_ID=role_reader \
  -e AGENT_MODEL=llama3.1 \
  -e AGENT_RUN="讀取 README.md 並摘要重點" \
  bricks4agent-agent:dev
```

容器入口只接受 governed 路徑：

- 必須提供 `BROKER_URL`、`BROKER_PUB_KEY`、`BROKER_PRINCIPAL_ID`、`BROKER_TASK_ID`
- 不接受 direct provider API key 作為正式執行模式
- 預設 workspace 掛載點是 `/workspace`
- 預設會加上 `--governed` 與 broker/session 參數
- `AGENT_RUN` 有值時跑單次任務；沒有時會進 REPL

## 驗證

```bash
npm run validate:agent-governed
npm run validate:broker-scope
npm run validate:broker-llm-proxy
```

目前驗證覆蓋：

- governed prompt 是否包含 broker route 與標準 POST JSON contract
- governed agent 初始化是否不再觸碰本地 direct provider
- broker-mediated `health/models/chat` 是否可被 agent 使用
- live broker + fake upstream LLM 是否真的能完成 governed session 與 chat
- tool visibility 是否依 grant 過濾
- 未授權 capability 是否會在本地 governed executor 直接拒絕
- broker policy 是否同時驗證 capability 與 `scope.paths/scope.routes`

## 主要參數

| 參數 | 短參數 | 說明 |
|---|---|---|
| `--run "<prompt>"` | `-r` | 單次執行 |
| `--model <name>` | `-m` | 模型名稱 |
| `--provider <type>` | `-P` | 本地模式 provider |
| `--api-key <key>` | `-k` | 本地模式 API key |
| `--host <url>` | `-H` | 覆蓋 provider base URL |
| `--list-models` |  | 列出模型 |
| `--no-stream` |  | 關閉串流輸出 |
| `--force-react` |  | 強制 ReAct XML |
| `--force-native` |  | 強制 native tool calling |
| `--max-iterations <n>` |  | 最大迭代數 |
| `--generate` | `-g` | 執行 `project.json` 生成 |
| `--pipeline <type>` |  | 執行指定 pipeline |
| `--project-path <path>` |  | 目標專案路徑 |
| `--dry-run` |  | 只驗證不寫檔 |
| `--validate` |  | 只驗證 pipeline |
| `--force` |  | 覆蓋既有生成內容 |
| `--governed` |  | 啟用 broker 受控模式 |
| `--broker-url <url>` |  | broker URL |
| `--broker-pub-key <base64>` |  | broker public key |
| `--principal-id <id>` |  | principal id |
| `--task-id <id>` |  | task id |
| `--role-id <id>` |  | role id |

## REPL 命令

| 命令 | 說明 |
|---|---|
| `/help` | 顯示說明 |
| `/model <name>` | 切換模型 |
| `/models` | 列出模型 |
| `/clear` | 清除歷史 |
| `/history` | 顯示歷史 |
| `/tools` | 顯示目前可用工具 |
| `/exit` | 離開 |

## 目錄

```text
tools/agent/
├── agent.js
├── README.md
├── lib/
│   ├── agent-loop.js
│   ├── repl.js
│   ├── state-machine.js
│   ├── tool-registry.js
│   ├── governed-executor.js
│   ├── broker-client.js
│   ├── providers/
│   │   ├── provider-factory.js
│   │   ├── ollama-provider.js
│   │   └── openai-provider.js
│   ├── pipelines/
│   ├── react-parser.js
│   ├── streaming.js
│   ├── responses-parser.js
│   ├── safety.js
│   └── utils.js
└── tests/
    └── test-governed-mode.js
```
