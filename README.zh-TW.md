# Bricks4Agent

English version:
- [README.md](/d:/Bricks4Agent/README.md)

## 專案定位

`Bricks4Agent` 是以 broker 為中心的受治理 AI operations 平台原型。核心原則是：

- broker 是控制平面，不是自主 planner。
- 高階模型提出意圖，broker 驗證、記錄並轉成結構化 intent。
- 執行層消耗 broker 核准後的結構化 intent，不直接吃原始對話。
- 使用者工作區位於 `{AccessRoot}/{channel}/{userId}/{conversations|documents|projects}`。

目前 canonical LINE 路徑是：

```text
LINE webhook -> public tunnel -> line-worker -> broker /api/v1/high-level/line/process
```

`agent --line-listen` 只保留作為 legacy/development-only 路徑。

## 主要區塊

- Broker / control plane: `packages/csharp/broker`, `packages/csharp/broker-core`
- LINE ingress: `packages/csharp/workers/line-worker`
- Governed agent runtime: `tools/agent`, `tools/agent/container`
- UI library / generation: `packages/javascript/browser/ui_components`, `packages/javascript/browser/page-generator`
- SPA generator / template: `tools/spa-generator`, `templates/spa`
- 文件: `docs/reports`, `docs/designs`, `docs/manuals`

## 快速啟動

Broker 與 LINE sidecar 的常用本機埠：

- broker: `http://127.0.0.1:5361`
- line-worker webhook: `http://127.0.0.1:5357`
- admin console: `http://127.0.0.1:5361/line-admin.html`

啟動 sidecar：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 start
```

檢查狀態：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

送出 canonical 驗證訊息：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -Message "hello"
```

## 目前高階回覆模型

LINE sidecar 的 high-level responder 會優先讀取 `ANTHROPIC_API_KEY`，並使用：

- provider: `anthropic`
- model: `claude-sonnet-4-6`

若沒有 `ANTHROPIC_API_KEY`，則回到既有 OpenAI-compatible `Api.txt` fallback 路徑。

## Agent Container

Agent container 是 Podman-based governed stack：LLM-driven agent 在隔離容器中執行，只能透過 broker claim work、要求 governed tool execution、回報結果；它不直接碰工具、資料或 model provider。

目前有三條 LLM 驗證路徑：

```powershell
# mock：離線驗證 governed chain 與 deterministic sentinel
node tools/agent/tests/test-podman-governed-stack.js

# host Ollama：透過 broker LlmProxy 做 live round trip
# 需將 STACK_MODEL 設為本機 Ollama 已有的模型
node tools/agent/tests/test-podman-ollama-host-stack.js

# OpenAI-compatible protocol path：預設使用內建 mock-openai
node tools/agent/tests/test-podman-openai-compatible-stack.js

# 可選：改成透過 broker LlmProxy 打真實 OpenAI endpoint
$env:OPENAI_BASE_URL="https://api.openai.com"; $env:OPENAI_API_KEY="<key>"
$env:OPENAI_API_FORMAT="responses"; $env:STACK_MODEL="<model>"
node tools/agent/tests/test-podman-openai-compatible-stack.js
```

Mock 路徑與 OpenAI-compatible 預設 mock 可驗證固定 sentinel；host Ollama 是 live model round trip，不應文件化成「必須回傳某個固定字串」。真實 OpenAI 需額外設定 `OPENAI_BASE_URL`、`OPENAI_API_KEY` 與模型。broker `LlmProxy` 目前支援 Ollama、OpenAI chat/responses 與 Anthropic Claude Messages 格式。

## UI 與 Determinism 現況

目前已清理並驗證的是 site-generator / metadata 驅動路徑的 UI state 與穩定輸出約束。整個 `ui_components` 目錄尚未宣稱全庫 deterministic；部分 demo、viz、map、social、download 類或範例仍可能使用 `Date.now()` / `Math.random()` 作為展示或 runtime 行為。

相關驗證：

```powershell
npm --prefix packages/javascript/browser run metadata:check
npm --prefix packages/javascript/browser test
npm test
npm run validate:ui-state
npm run audit:ui-styles
npm run validate:ui-library
```

## 建置與測試

```powershell
dotnet build packages/csharp/ControlPlane.slnx
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
npm run validate:baseorm
npm run validate:baseorm-sync
npm run validate:broker-scope
npm run validate:backend-governance
npm run validate:agent-governed
npm run validate:broker-llm-proxy
npm run validate:anthropic-provider-smoke
```

整合測試需要先啟動 broker：

```powershell
dotnet run --project packages/csharp/broker/Broker.csproj -- --urls http://127.0.0.1:{port}
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj -- --integration http://localhost:{port}
```

測試後請清理 `packages/csharp/broker/broker.db*` 與 `.test-output/`。

## 目前強項

- broker-centered governed workflow 方向明確。
- LINE ingress canonical path 已收斂到 `line-worker -> broker high-level coordinator`。
- command grammar、workflow gate、promotion gate 與 execution intent 持續收斂。
- BaseOrm transaction、scope validation、LLM proxy 與 agent container 路徑已有驗證腳本。
- broker POST API 具備基本 exception JSON 化、body size cap 與單節點 IP rate limit；LINE `message/audio` outbound 具備 worker-local rate limit。
- UI generator 的 metadata / site-gen 相關 determinism 規則已建立。

## 目前限制

- 各子系統成熟度仍不一致。
- broker 是必要中心節點，需要保持窄而清楚的責任邊界。
- browser governance 仍是基礎建設，不是完整 browser automation platform。
- host Ollama / OpenAI-compatible / Anthropic 驗證依賴本機模型、外部服務與環境變數。
- Critical dual approval 已具備 broker 持久化與兩個不同 approver id 門檻；local admin 身分仍是 session 型，不是完整 named operator account。LINE 分散式配額與 `line.notification.send` rate limit 尚未完成。
- UI 元件庫尚未全庫 deterministic；文件應只描述已驗證範圍。

## 推薦閱讀順序

1. [目前使用手冊](/d:/Bricks4Agent/docs/manuals/current-user-manual.zh-TW.md)
2. [目前技術手冊](/d:/Bricks4Agent/docs/manuals/current-technical-manual.zh-TW.md)
3. [CurrentArchitectureAndProgress-2026-06-13.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-06-13.md)
4. [Agent Container Runbook](/d:/Bricks4Agent/docs/manuals/agent-container-runbook.md)
5. [LINE Sidecar Runbook](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)
6. `docs/designs/` 內的子系統設計文件
