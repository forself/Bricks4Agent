# AI Agent CLI

讓本機或雲端模型成為自主代理，支援讀寫檔案、執行指令、搜尋程式碼、批次生成 CRUD/服務管線，並可接入 Broker 受控模式。

**零 npm 依賴** — 只需 Node.js (v18+)。本地推理可搭配 Ollama；雲端模式可使用支援的 API provider。

## 快速開始

### 本地 Ollama

```bash
# 1. 確認 Ollama 已執行
ollama serve

# 2. 拉取模型（若尚未下載）
ollama pull llama3.1

# 3. 啟動 Agent
node tools/agent/agent.js
```

### 雲端 Provider

```bash
node tools/agent/agent.js --provider openai --api-key sk-xxx --model gpt-4o
node tools/agent/agent.js --provider gemini --api-key AIza... --model gemini-2.0-flash
```

## 支援的 Provider

| Provider | 說明 |
|----------|------|
| `ollama` | 本地 Ollama (`http://localhost:11434`) |
| `openai` | OpenAI，預設走 Responses API |
| `gemini` | Gemini OpenAI-compatible endpoint |
| `deepseek` | DeepSeek OpenAI-compatible endpoint |
| `groq` | Groq OpenAI-compatible endpoint |
| `mistral` | Mistral OpenAI-compatible endpoint |

> 若未指定 `--provider`，Agent 會優先偵測 API key；有 key 時預設走 `openai`，否則走 `ollama`。

## 用法

### 互動式對話

```bash
node tools/agent/agent.js
```

### 單次執行

```bash
node tools/agent/agent.js --run "讀取 AGENT.md 並總結專案結構"
node tools/agent/agent.js --run "生成一個部落格功能，含 Title, Content, Author 欄位" --no-confirm
```

### 列出模型

```bash
node tools/agent/agent.js --list-models
```

### 自動化生成（讀取 `project.json`）

```bash
node tools/agent/agent.js --generate --project-path projects/PhotoDiary
node tools/agent/agent.js --generate --project-path projects/PhotoDiary --dry-run
node tools/agent/agent.js --generate --project-path projects/PhotoDiary --validate
```

### 手動管線模式

```bash
node tools/agent/agent.js --pipeline crud --entity Product --fields "[{\"name\":\"Name\",\"type\":\"string\"},{\"name\":\"Price\",\"type\":\"decimal\"}]"
```

### Broker 受控模式

```bash
node tools/agent/agent.js --governed --broker-url http://localhost:5000 --broker-pub-key <base64> --principal-id prn_xxx --task-id task_xxx --role-id role_reader --run "讀取 README.md"
```

## 主要參數

| 參數 | 縮寫 | 說明 | 預設 |
|------|------|------|------|
| `--run "<prompt>"` | `-r` | 單次執行 | 互動模式 |
| `--model <name>` | `-m` | 模型名稱 | `llama3.1` |
| `--provider <type>` | `-P` | Provider 類型 | 自動偵測 |
| `--api-key <key>` | `-k` | 雲端 API 金鑰 | 讀環境變數 |
| `--host <url>` | `-H` | 覆蓋 provider 預設端點 | provider 預設值 |
| `--no-stream` | | 關閉串流輸出 | 串流開啟 |
| `--force-react` | | 強制 ReAct XML 模式 | 自動偵測 |
| `--force-native` | | 強制原生 tool calling | 自動偵測 |
| `--max-iterations <n>` | | 最大迭代輪數 | `20` |
| `--no-confirm` | | 跳過確認提示 | 關閉 |
| `--verbose` | `-v` | 顯示除錯資訊 | 關閉 |
| `--generate` | `-g` | 從 `project.json` 批次執行管線 | 關閉 |
| `--pipeline <type>` | | 手動指定管線類型 | 無 |
| `--project-path <path>` | | 專案子路徑 | 無 |
| `--dry-run` | | 顯示計畫但不執行 | 關閉 |
| `--validate` | | 只做 postcondition 驗證 | 關閉 |
| `--force` | | 強制重新生成已存在產物 | 關閉 |
| `--governed` | | 啟用 Broker 受控模式 | 關閉 |

## REPL 指令

| 指令 | 說明 |
|------|------|
| `/help` | 顯示指令說明 |
| `/model <name>` | 切換模型 |
| `/models` | 列出可用模型 |
| `/clear` | 清除對話歷史 |
| `/history` | 對話統計 |
| `/tools` | 列出工具 |
| `/exit` | 退出 |

## 工具呼叫策略

### 原生 Tool Calling（推薦）

支援原生 tool calling 的模型會直接走 provider 原生介面。OpenAI provider 會依 provider 設定自動選擇 `chat` 或 `responses` 格式。

### ReAct XML 回退

不支援原生 tool calling 的模型會自動切換到 XML 標籤模式。模型輸出 `<tool_call>` 區塊後，Agent 會解析並執行。

可用 `--force-react` 或 `--force-native` 強制切換策略。

## 內建工具

| 工具 | 說明 |
|------|------|
| `read_file` | 讀取檔案（含行號，可分頁） |
| `write_file` | 寫入檔案（支援覆寫/追加） |
| `list_directory` | 列出目錄結構 |
| `run_command` | 執行 shell 指令 |
| `search_files` | 搜尋檔名（glob 模式） |
| `search_content` | 搜尋檔案內容（regex） |

## 安全機制

- **路徑沙箱**：所有檔案操作限制在專案根目錄
- **指令封鎖**：危險指令（`rm -rf /`、`format`、`shutdown` 等）自動阻擋
- **確認提示**：覆寫既有檔案、破壞性操作前詢問使用者
- **輸出限制**：防止大量輸出造成記憶體溢出
- **受控模式**：可透過 Broker 進行 capability、session、audit 與 revocation 控制

## Bricks4Agent 整合

在 Bricks4Agent 專案中執行時，Agent 會自動偵測並載入 `AGENT.md` 操作手冊，並可直接搭配：

- SPA CLI 指令格式與欄位型別對應
- `project.json` 實體/服務定義
- PageGenerator 30 種欄位類型
- 自動路由更新與 convention-based 基礎設施
- CRUD / extended service pipeline

## 架構

```text
tools/agent/
├── agent.js                    # CLI 入口
├── lib/
│   ├── agent-loop.js           # 核心代理迴圈
│   ├── repl.js                 # REPL 介面
│   ├── state-machine.js        # 狀態機 / 檢核點執行器
│   ├── tool-registry.js        # 工具註冊與分派
│   ├── governed-executor.js    # Broker 受控執行
│   ├── broker-client.js        # Broker API 客戶端
│   ├── providers/
│   │   ├── provider-factory.js # Provider 選擇與別名
│   │   ├── ollama-provider.js  # Ollama provider
│   │   └── openai-provider.js  # OpenAI-compatible / Responses provider
│   ├── pipelines/
│   │   ├── pipeline-runner.js  # project.json 批次執行
│   │   ├── crud-pipeline.js    # CRUD 生成管線
│   │   └── service-pipeline.js # Extended service 管線
│   ├── tools/
│   │   ├── file-tools.js       # 檔案操作工具
│   │   ├── dir-tools.js        # 目錄列表工具
│   │   └── command-tool.js     # 指令執行工具
│   ├── react-parser.js         # ReAct XML 解析器
│   ├── streaming.js            # 串流回應處理
│   ├── responses-parser.js     # Responses API 事件解析
│   ├── safety.js               # 安全層
│   └── utils.js                # 共用工具
└── README.md
```
