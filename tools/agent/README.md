# Ollama AI Agent CLI

讓本機 Ollama 模型成為自主代理，能夠讀寫檔案、執行指令、搜尋程式碼。

**零外部依賴** — 只需 Node.js (v18+) 和 Ollama。

## 快速開始

```bash
# 1. 確認 Ollama 已執行
ollama serve

# 2. 拉取模型（若尚未下載）
ollama pull llama3.1

# 3. 啟動 Agent
node tools/agent/agent.js
```

## 用法

### 互動式對話

```bash
node agent.js
```

啟動 REPL，像聊天一樣與 AI 對話，AI 會自動呼叫工具完成任務。

### 單次執行

```bash
node agent.js --run "讀取 AGENT.md 並總結專案結構"
node agent.js --run "生成一個部落格功能，含 Title, Content, Author 欄位" --no-confirm
```

### 指定模型

```bash
node agent.js --model qwen2.5:14b
node agent.js --model deepseek-v2.5
node agent.js --model llama3.2:3b    # 小型模型，需搭配 --force-react
```

### 列出可用模型

```bash
node agent.js --list-models
```

## 參數

| 參數 | 縮寫 | 說明 | 預設 |
|------|------|------|------|
| `--run "<prompt>"` | `-r` | 單次執行 | (互動模式) |
| `--model <name>` | `-m` | 模型名稱 | `llama3.1` |
| `--host <url>` | `-H` | Ollama URL | `http://localhost:11434` |
| `--no-stream` | | 關閉串流 | 串流開啟 |
| `--force-react` | | 強制 ReAct 模式 | 自動偵測 |
| `--force-native` | | 強制原生 tool calling | 自動偵測 |
| `--max-iterations <n>` | | 迭代上限 | `20` |
| `--no-confirm` | | 跳過確認 | 需確認 |
| `--verbose` | `-v` | 除錯資訊 | 關閉 |

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

支援模型：Llama 3.1+, Qwen 2.5+, Mistral Nemo, Command-R, Granite 4, DeepSeek v2.5+

Agent 自動偵測模型是否支援，不需手動設定。

### ReAct XML 回退

不支援原生 tool calling 的模型（如 Phi-3, Llama 2, Mistral 7B）會自動切換到 XML 標籤模式。模型在文字中輸出 `<tool_call>` 區塊，Agent 解析並執行。

可用 `--force-react` 強制使用此模式（用於測試）。

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
- **指令封鎖**：危險指令（rm -rf /、format、shutdown 等）自動阻擋
- **確認提示**：覆寫既有檔案、破壞性操作前詢問使用者
- **輸出限制**：防止大量輸出造成記憶體溢出

## Bricks4Agent 整合

在 Bricks4Agent 專案中執行時，Agent 會自動偵測並載入 `AGENT.md` 操作手冊，讓模型了解：

- SPA CLI 指令格式
- 欄位類型對應表
- PageGenerator 30 種欄位類型
- 自動路由更新機制
- 常見陷阱

## 架構

```
tools/agent/
├── agent.js              ← CLI 入口
├── lib/
│   ├── ollama-client.js  ← Ollama HTTP 客戶端
│   ├── agent-loop.js     ← 核心代理迴圈
│   ├── tool-registry.js  ← 工具註冊與分派
│   ├── tools/
│   │   ├── file-tools.js ← 檔案操作工具
│   │   ├── dir-tools.js  ← 目錄列表工具
│   │   └── command-tool.js ← 指令執行工具
│   ├── react-parser.js   ← ReAct XML 解析器
│   ├── system-prompt.js  ← 系統提示詞建構
│   ├── safety.js         ← 安全層
│   ├── repl.js           ← REPL 互動介面
│   ├── streaming.js      ← NDJSON 串流解析
│   └── utils.js          ← 共用工具
└── README.md
```
