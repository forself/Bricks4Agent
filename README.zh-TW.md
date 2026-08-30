# Bricks4Agent

English version: [README.md](README.md)

## 這是什麼

`Bricks4Agent` 是一套**零 runtime 依賴的 Vanilla JS UI 元件庫**,加上一個把 JSON `PageDefinition` 轉成頁面的**頁面／SPA 生成器**。

- **UI 元件庫** — 116 個元件(form、layout、common、input、viz、social、editor、sections、data、analytics),純 Vanilla JS,以 theme token 上色,內建 XSS 防護與 i18n。

- **頁面生成器** — 一份 `PageDefinition`(JSON)有兩條路變成頁面:**靜態產碼**(產出 `.js` 頁面檔)或**動態渲染**(執行期直接依 JSON 畫出來)。

- **SPA 工具鏈** — CLI 與 Web UI,可一鍵生成全端 CRUD(前端頁面 + 選配的 .NET 10 後端)。

- **表單應用工作台** — 匯入資料表 schema、視覺編排欄位，並生成表單 `PageDefinition`、.NET 10 Minimal API/BaseOrm 程式碼與資料庫 SQL；連線字串留白時以本地 SQLite 為目標。

> 要在這套庫上開發?請先讀 [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) — 那是給人與 AI Agent 的元件調用入口。

## 主要區域

### UI 元件庫

- [ui_components](packages/javascript/browser/ui_components) — 元件本體

- [ui_components/index.js](packages/javascript/browser/ui_components/index.js) — 單一匯入入口 barrel

- [metadata/component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) — 元件權威清單

- [STYLE_CONVENTION.md](packages/javascript/browser/ui_components/STYLE_CONVENTION.md) — 樣式／token 規範

### 頁面生成器

- [page-generator](packages/javascript/browser/page-generator) — 引擎(靜態 + 動態)

- [page-generator/README.md](packages/javascript/browser/page-generator/README.md)

### SPA 鷹架

- [templates/spa](templates/spa) — SPA 專案範本(前端核心 + .NET 10 後端)

- [templates/spa/scripts](templates/spa/scripts) — `spa-cli.js`、`generate-page.js`、`generate-api.js`

- [tools/spa-generator](tools/spa-generator) — 生成器 Web UI(port 3080)

- [tools/page-gen.js](tools/page-gen.js) — 獨立 PageDefinition CLI([說明](tools/page-gen.README.md))

- [tools/static-server](tools/static-server) — 預覽用靜態伺服器

- [tools/form-application-studio](tools/form-application-studio) — JSON 自舉的表單／API／資料庫設計工具

## 快速開始

請使用 Node.js 22 與 .NET 10 SDK；repo 內的 `global.json` 允許使用目前已安裝的最新 .NET 10 feature band。

### 使用元件庫

```js
import { TextInput, DataTable, BarChart } from './packages/javascript/browser/ui_components/index.js';

new TextInput({ label: '姓名', required: true }).mount('#app');
```

每個元件契約一致:`new X(options)` → `.mount(container)` → `.destroy()`。
完整約定與元件清單見 [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)。

### 從定義生成頁面

```bash
# 生成單頁 / 批次生成 DefinitionTemplate 全部頁面 / 列出支援的欄位型別
node tools/page-gen.js --def page.json --mode static --output ./out/
node tools/page-gen.js --def site-definition.json --all --mode static --output ./out/
node tools/page-gen.js --list-types
```

`--validate` 只驗證定義不產檔;`--page <id>` 與 `--pages <id,id>` 可從 DefinitionTemplate 挑選個別頁面。

### 一鍵全端 CRUD

```bash
# 建立專案 —— 互動式;--name/--output 只是預填提示的預設值
node templates/spa/scripts/spa-cli.js new --name my-app --output ./out

# 非互動:先把答案寫進設定檔(參考 scripts/project-config.example.json)
node templates/spa/scripts/create-project.js --config project.json

# 生成一個功能(C# Model/Service + 前端頁面),產出落在 templates/spa 本身
node templates/spa/scripts/spa-cli.js feature Article --fields "Title:string,Content:text,IsPublic:bool"
```

`feature`、`page`、`api` 都以 `templates/spa/` 為基準寫檔,不會寫進 `new` 剛建立的專案——
它們改的是 `new` 用來複製的範本樹。

### 啟動生成器 Web UI

```bash
node tools/spa-generator/server.js   # 前端 + 生成 API,port 3080
npm run serve                        # 只有靜態前端,同樣 port 3080
```

Node 伺服器預設綁 loopback,並拒絕非 loopback 的 `Host`/`Origin` 請求。

## 測試

```bash
npm test                        # 頁面生成器測試
npm run validate:ui-library     # UI 元件庫檢查
npm run audit:ui-styles         # 樣式 token 稽核
npm run test:studio:self-host   # 唯一權威 JSON + 正式元件 provenance
npm run test:studio:browser     # 同頁籤 + Theme/客製元件 JSON round-trip
npm run test:form-designer:all  # 表單應用單元、自舉與瀏覽器驗收
npm run test:form-designer:dotnet # 生成並編譯四種 provider 後端
npm run test:dotnet10           # 強制 net10.0；建置 35 個專案且任何警告都視為錯誤
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj
```

PR 目標為 `main` 或推送至 `main` 時，[GitHub Actions](.github/workflows/ci.yml)
會執行可攜式 JavaScript、政策、metadata、零警告 .NET 10 專案矩陣與生成後端檢查。
真實 Edge 互動 harness 仍是本機驗收守門，因其刻意沿用既有外部
Playwright／Edge runtime，不為 CI 增加 npm dependency。

.NET 10 遷移保留既有 PBKDF2 密碼儲存格式；Broker、MFA 與 SPA 範本都有固定
相容性向量，確保既有雜湊仍可驗證，同時改用目前的靜態 PBKDF2 API。

## 文件

- [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) — 元件調用約定 + React 重製 playbook(給 AI Agent)

- [CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md) — JSON 產生的自舉 Studio、客製元件三層分類與資料夾載入

- [tools/form-application-studio/README.md](tools/form-application-studio/README.md) — Schema→表單/API/資料表工作台與連線政策

- [AGENT.md](AGENT.md) — SPA 生成器操作手冊(給 AI Agent)

- [CLAUDE.md](CLAUDE.md) — 本 repo 的 Claude Code 規則

- [page-generator/README.md](packages/javascript/browser/page-generator/README.md) — 頁面生成器細節

- [templates/spa/README.md](templates/spa/README.md) — SPA 範本
