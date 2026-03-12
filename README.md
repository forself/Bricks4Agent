# Bricks4Agent

全端元件庫與專案生成工具集，涵蓋 **80 個可直接使用的前端 UI 元件**、**6 個前端支援模組**、**21 個 C# 後端模組**、**頁面生成引擎**與 **SPA 生成器**。

前端元件零外部依賴、純 Vanilla JavaScript，專為 AI Agent 驅動的應用開發而設計。

### 依賴說明

| 層級 | 依賴 | 用途 |
|------|------|------|
| 前端元件 | 無 | 所有 UI 元件皆為純 Vanilla JS，瀏覽器原生運行 |
| 開發工具 | Playwright、Puppeteer | Demo 截圖、自動化 UI 測試 |
| 後端 | .NET 8 SDK | C# 模組編譯、SPA Generator 後端 |

主要模組：

- `packages/javascript/browser/ui_components/`：原生 JavaScript UI 元件庫
- `packages/javascript/browser/page-generator/` + `templates/spa/scripts/` + `tools/spa-generator/`：從頁面定義到完整 SPA 的生成工具鏈

---

## 統計總覽

| 項目 | 數量 |
|------|------|
| 前端 UI 元件 | 80（form 12 / common 23 / layout 10 / input 10 / viz 18 / social 5 / editor 1 / data 1） |
| 前端支援模組 | 6（binding 2 / utils 4） |
| C# 後端模組 | 21（api 7 / database 4 / security 6 / logging 1 / utils 3） |
| 頁面生成器模組 | 9（支援 30 種欄位類型與 8 種 trigger action） |
| CSS 主題變數 | 140+ |

> `viz` 的 18 個數量不包含 `BaseChart` 這個抽象基底類別。

### 技術棧

| 層級 | 技術 |
|------|------|
| 前端 | Vanilla JavaScript (ES6+), CSS3, HTML5 |
| 後端 | .NET 8 Minimal API |
| 資料庫 | SQLite（預設），可配置 SQL Server / MySQL / PostgreSQL |
| 認證 | JWT + HttpOnly Cookie |
| 主題 | CSS Custom Properties（`--cl-*`，支援深色主題） |

---

## 快速開始

```bash
# 安裝開發工具依賴（截圖、UI 測試用；元件本身不需要）
npm install

# 啟動 SPA 生成器 Web UI
cd tools/spa-generator
node server.js
# 瀏覽器開啟 http://localhost:3080

# 使用範本 CLI（互動式）
node templates/spa/scripts/spa-cli.js new

# 使用範本 CLI（非互動）
node templates/spa/scripts/spa-cli.js new --name my-app --output ./projects
node templates/spa/scripts/spa-cli.js feature User --fields "Name:string,Email:string"

# Demo 截圖（需先 npm install）
node tools/screenshot-demos.js

# 執行測試
npm test
```

**環境需求**：Node.js 18+、.NET 8 SDK、Git

---

## 使用範例

### 表單元件

```javascript
import { TextInput } from './packages/javascript/browser/ui_components/form/TextInput/TextInput.js';

const input = new TextInput({ label: '姓名', required: true });
input.mount(document.getElementById('container'));
input.getValue();         // 取值
input.setValue('王小明'); // 設值
input.destroy();          // 銷毀
```

### 主題切換

```javascript
// 深色主題
document.documentElement.setAttribute('data-theme', 'dark');

// 淺色主題
document.documentElement.removeAttribute('data-theme');
```

### 動態頁面生成

```javascript
import { DynamicPageRenderer } from './packages/javascript/browser/page-generator/index.js';

const page = new DynamicPageRenderer({
  definition,
  mode: 'form',
  onSave: async (values) => {
    await api.post('/users', values);
  }
});

await page.init();
page.mount(document.getElementById('app'));
```

---

## 目錄結構

```
Bricks4Agent/
├── packages/
│   ├── javascript/browser/
│   │   ├── ui_components/                  # 前端 UI 元件與支援模組
│   │   └── page-generator/                # 動態/靜態頁面生成引擎
│   └── csharp/                            # C# 後端模組
├── templates/spa/                         # SPA 專案範本
│   └── scripts/                           # 範本 CLI（spa-cli.js）
├── tools/
│   ├── spa-generator/                     # Web UI 生成器
│   └── page-gen.js                        # PageDefinition CLI
└── docs/manuals/                          # 中英文手冊
```

---

## 文件

### 給非技術人員

| 文件 | 說明 |
|------|------|
| [使用者手冊](docs/manuals/user-guide.md) | 功能總覽、操作說明、完整元件清單 |

### 給技術人員

| 文件 | 說明 |
|------|------|
| [工程師手冊（繁中）](docs/manuals/engineer-guide.md) | 完整元件 API、主題系統、生成器使用指南 |
| [Engineer Guide (EN)](docs/manuals/engineer-guide-en.md) | Full component API, theme system, and generator guide |
| [頁面生成器 README](packages/javascript/browser/page-generator/README.md) | `PageGenerator`、`DynamicPageRenderer`、`PageDefinitionAdapter` |
| [page-gen CLI](tools/page-gen.README.md) | PageDefinition CLI 使用方式與 30 種 fieldType |
| [SPA Generator Web UI](tools/spa-generator/README.md) | Web UI 啟動方式與工具結構 |
| [SPA 範本 CLI](templates/spa/scripts/README.md) | `spa-cli.js` 指令與輸出結果 |
| [安全性檢查清單](docs/security/SecurityReviewChecklist.md) | 程式碼安全審查項目 |
| [BaseOrm 文件](packages/csharp/database/BaseOrm/README.md) | ORM 套件使用手冊 |
