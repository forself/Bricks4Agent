# Bricks4Agent — AI Agent 操作手冊

> 本手冊專為 AI Agent 設計（適用於任何語言模型：GPT、Claude、Llama、Qwen、DeepSeek、Gemini 等，含離線地端模型）。
> 提供結構化的指令格式、欄位對應表與操作流程。
> 人類工程師請參閱 `docs/manuals/engineer-guide.md`。
>
> **AI Agent 進入本專案後，請優先閱讀本文件（AGENT.md）。**

### 支援的 AI Agent 框架入口

本專案為以下 AI 程式助手框架提供了入口檔案，所有入口都指向本手冊：

| 框架 | 入口檔案 |
|------|----------|
| Cursor IDE | `.cursorrules` |
| Windsurf (Codeium) | `.windsurfrules` |
| GitHub Copilot | `.github/copilot-instructions.md` |
| Cline | `.clinerules` |
| Claude Code | `.claude/CLAUDE.md` |
| Aider | `.aider.conf.yml` |
| 通用 / 其他 | `.agentrc` |

---

## 1. 專案概觀

Bricks4Agent 是全端元件庫與程式碼生成工具集：

- **75 個前端 UI 元件**（純 Vanilla JS，零外部依賴）
- **21 個 C# 後端模組**（.NET 8 Minimal API）
- **頁面生成引擎**（PageGenerator，支援 30 種欄位類型）
- **SPA 生成器**（CLI + Web UI，一鍵產生全端 CRUD）

### 核心工具鏈

```
spa-cli.js feature → generate-api.js (C# 後端)
                    → generate-page.js (前端頁面)
                        ├── 無 --fields → 原始 HTML 模板
                        └── 有 --fields → PageGenerator → 元件庫頁面 + 自動更新 routes.js
```

---

## 2. 指令格式

### 2.1 生成完整功能（前端 + 後端）

```bash
node templates/spa/scripts/spa-cli.js feature <名稱> --fields "<欄位定義>"
```

**參數：**
- `<名稱>`：功能名稱，PascalCase（如 `Article`、`PhotoDiary`）
- `--fields`：欄位定義字串，格式為 `"欄位名:類型,欄位名:類型,..."`

**範例：**
```bash
node templates/spa/scripts/spa-cli.js feature Diary --fields "Title:string,Content:text,Date:date,Mood:string,IsPublic:bool"
```

**產出：**
```
backend/Models/Diary.cs              ← C# Model + DTO
backend/Services/DiaryService.cs     ← CRUD Service
frontend/pages/diarys/DiaryListPage.js   ← 元件庫列表頁
frontend/pages/diarys/DiaryDetailPage.js ← 元件庫詳情頁
frontend/pages/routes.js             ← 自動更新（import + 路由）
```

**後續步驟（僅需 1 步）：**
依照終端輸出的指示，將 API 端點程式碼貼入 `Program.cs`。

---

### 2.2 僅生成頁面

```bash
# 元件庫頁面（推薦）
node templates/spa/scripts/generate-page.js <路徑/名稱> --fields "<欄位>" --api-path "<API路徑>"

# 原始模板頁面（向下相容）
node templates/spa/scripts/generate-page.js <路徑/名稱>
node templates/spa/scripts/generate-page.js <路徑/名稱> --detail
```

**範例：**
```bash
# 元件庫表單頁
node scripts/generate-page.js orders/OrderList --fields "Name:string,Price:decimal" --api-path "/api/orders"

# 元件庫詳情頁
node scripts/generate-page.js orders/OrderDetail --detail --fields "Name:string,Price:decimal" --api-path "/api/orders"

# 原始模板（無 --fields）
node scripts/generate-page.js SimplePage
```

---

### 2.3 僅生成 API

```bash
node templates/spa/scripts/generate-api.js <名稱> --fields "<欄位>"
```

---

### 2.4 建立新專案

```bash
# 互動模式
node templates/spa/scripts/spa-cli.js new

# 非互動模式
node templates/spa/scripts/spa-cli.js new --name my-app --output ./projects
```

---

## 3. 欄位類型對應表

### 3.1 CLI 類型 → PageDefinition 類型 → UI 元件

| CLI `--fields` 類型 | PageDefinition 類型 | 生成的 UI 元件 | 說明 |
|---|---|---|---|
| `string` | `text` | TextInput | 單行文字 |
| `text` | `textarea` | WebTextEditor | 多行富文本 |
| `int`, `integer` | `number` | NumberInput | 整數 |
| `long` | `number` | NumberInput | 長整數 |
| `decimal`, `float`, `double` | `number` | NumberInput | 小數 |
| `bool`, `boolean` | `toggle` | ToggleSwitch | 開關 |
| `date` | `date` | DatePicker | 日期選擇器 |
| `datetime` | `datetime` | DateTimeInput | 日期時間 |
| `guid` | `text` | TextInput | GUID 文字 |

### 3.2 未對應的類型（預設為 TextInput）

未列於上表的 CLI 類型會預設映射為 `text` → TextInput。若需要其他 30 種欄位類型（如 `select`、`multiselect`、`color`、`image`、`richtext`、`canvas` 等），請直接使用 PageDefinition JSON 格式搭配 PageGenerator API 或 `tools/page-gen.js`。

### 3.3 C# 類型對應（generate-api.js）

| CLI 類型 | C# 類型 |
|---|---|
| `string` | `string` |
| `text` | `string` |
| `int` | `int` |
| `long` | `long` |
| `decimal` | `decimal` |
| `float` | `float` |
| `double` | `double` |
| `bool` | `bool` |
| `date` | `DateTime` |
| `datetime` | `DateTime` |
| `guid` | `Guid` |

---

## 4. SPA 範本元件清單

`templates/spa/frontend/components/` 中的元件會隨 `spa new` 一起複製到新專案。PageGenerator 生成的頁面會自動 import 這些元件：

| 元件 | 路徑 | 用途 |
|------|------|------|
| TextInput | `components/TextInput/TextInput.js` | 文字輸入（含 XSS 防護） |
| NumberInput | `components/NumberInput/NumberInput.js` | 數字輸入（含 +/- 按鈕） |
| Dropdown | `components/Dropdown/Dropdown.js` | 下拉選單（含搜尋） |
| ToggleSwitch | `components/ToggleSwitch/ToggleSwitch.js` | 開關元件 |
| WebTextEditor | `components/WebTextEditor/WebTextEditor.js` | 簡易富文本編輯器 |
| DatePicker | `components/DatePicker/DatePicker.js` | 日期選擇器 |
| ColorPicker | `components/ColorPicker/ColorPicker.js` | 顏色選擇器 |
| ImageViewer | `components/ImageViewer/ImageViewer.js` | 圖片檢視器 |
| Panel | `components/Panel/Panel.js` | 面板佈局 |

---

## 5. 自動路由更新

使用 `--fields` 生成頁面時，`routes.js` 會自動更新：

1. 在最後一個 `import` 後插入新的 import 語句
2. 在 `];` 前插入新的路由條目
3. 自動處理逗號分隔
4. 重複檢查：若 className 已存在則跳過
5. 保持原始換行符號（CRLF/LF）

**不使用 `--fields`**（原始模板）時，需手動更新 routes.js，終端會輸出所需的程式碼。

---

## 6. 目錄結構

```
Bricks4Agent/
├── AGENT.md                              ← 本手冊
├── packages/
│   ├── javascript/browser/
│   │   ├── ui_components/                ← 75 個 UI 元件（完整版）
│   │   │   ├── form/                     ← 表單 (12)
│   │   │   ├── common/                   ← 通用 (18)
│   │   │   ├── layout/                   ← 佈局 (10)
│   │   │   ├── input/                    ← 進階輸入 (10)
│   │   │   ├── viz/                      ← 視覺化 (18)
│   │   │   ├── social/                   ← 社群 (5)
│   │   │   ├── editor/                   ← 編輯器 (1)
│   │   │   └── data/                     ← 資料展示 (1)
│   │   └── page-generator/              ← PageGenerator 引擎
│   │       ├── PageGenerator.js          ← 核心：PageDefinition → 頁面程式碼
│   │       ├── PageDefinition.js         ← 定義格式與驗證
│   │       ├── ComponentMapping.js       ← 30 種欄位 → 元件映射
│   │       └── FieldRenderers.js         ← 各元件的 mount 渲染邏輯
│   └── csharp/                           ← 21 個 C# 後端模組
├── templates/spa/                        ← SPA 專案範本
│   ├── scripts/
│   │   ├── spa-cli.js                    ← CLI 入口（new/feature/page/api）
│   │   ├── generate-page.js              ← 頁面生成（整合 PageGenerator）
│   │   ├── generate-api.js               ← C# API 生成
│   │   └── create-project.js             ← 專案建立
│   ├── frontend/
│   │   ├── core/                         ← 框架核心（BasePage, Router, Store）
│   │   ├── pages/                        ← 頁面範本
│   │   │   └── routes.js                 ← 路由配置（自動更新）
│   │   └── components/                   ← 9 個 SPA 範本元件
│   └── backend/                          ← .NET 8 後端範本
└── tools/
    ├── agent/                            ← Ollama AI Agent CLI（本機模型代理化）
    │   ├── agent.js                     ← CLI 入口
    │   └── lib/                         ← 核心模組（13 個檔案）
    ├── spa-generator/                    ← SPA 生成器 Web UI（port 3080）
    └── page-gen.js                       ← PageDefinition CLI（獨立工具）
```

---

## 7. PageGenerator 進階用法

若 CLI 的 13 種欄位類型不夠用，可直接使用 PageGenerator 的 30 種欄位類型：

```javascript
import { PageGenerator } from './packages/javascript/browser/page-generator/PageGenerator.js';

const definition = {
    name: 'MyFormPage',
    type: 'form',           // form | detail | list
    description: '自訂表單',
    fields: [
        { name: 'Color', type: 'color', label: '顏色' },
        { name: 'Avatar', type: 'image', label: '頭像' },
        { name: 'Tags', type: 'multiselect', label: '標籤',
          options: ['A', 'B', 'C'] },
        { name: 'Category', type: 'select', label: '分類',
          options: ['類別1', '類別2'] },
        { name: 'Bio', type: 'richtext', label: '自傳' }
    ],
    api: {
        create: '/api/my-form',
        update: '/api/my-form',
        get: '/api/my-form',
        delete: '/api/my-form',
        list: '/api/my-form'
    },
    styles: { layout: 'single', theme: 'default' }
};

const generator = new PageGenerator();
const result = generator.generate(definition);
// result.code → 完整頁面 JS 程式碼
// result.errors → 錯誤陣列（空陣列表示成功）
```

### 7.1 完整 30 種欄位類型

| 類型 | 元件 | 類型 | 元件 |
|------|------|------|------|
| text | TextInput | number | NumberInput |
| textarea | WebTextEditor | toggle | ToggleSwitch |
| date | DatePicker | datetime | DateTimeInput |
| select | Dropdown | multiselect | MultiSelect |
| checkbox | CheckboxGroup | radio | RadioGroup |
| color | ColorPicker | slider | Slider |
| rating | StarRating | image | ImageUploader |
| file | FileUploader | password | TextInput(password) |
| email | TextInput(email) | url | TextInput(url) |
| tel | TextInput(tel) | richtext | RichTextEditor |
| canvas | CanvasBoard | address | AddressInput |
| location | LocationPicker | weather | WeatherWidget |
| avatar | AvatarUploader | tags | TagInput |
| signature | SignaturePad | percentage | PercentageInput |
| currency | CurrencyInput | hidden | (隱藏欄位) |

---

## 8. 常見陷阱

### 8.1 MSYS 路徑轉換（Windows Git Bash）

在 Git Bash 中，`/api/xxx` 會被自動轉換為 `C:/Program Files/Git/api/xxx`。generate-page.js 已內建 `sanitizeApiPath()` 修復此問題，但若你手動傳遞路徑，請注意：

```bash
# Git Bash 中安全的寫法（加引號）
node scripts/generate-page.js orders/OrderList --api-path "/api/orders"
```

### 8.2 檔案已存在

生成器不會覆蓋已存在的檔案。若需重新生成，請先手動刪除目標檔案。

### 8.3 CJS 與 ESM

- `generate-page.js`、`spa-cli.js`：CommonJS（使用 `require`）
- `PageGenerator.js`、所有元件：ESM（使用 `import/export`）
- 橋接方式：`const { pathToFileURL } = require('url')` + `await import(path)`

### 8.4 import 路徑深度

PageGenerator 輸出的 import 路徑為 depth=1（`../components/X/X.js`）。若頁面位於子目錄（如 `pages/orders/OrderPage.js`），generate-page.js 會自動調整為 `../../components/X/X.js`。

---

## 9. 操作流程範例

### 範例：生成部落格功能

```bash
# 1. 建立新專案
node templates/spa/scripts/spa-cli.js new --name my-blog --output ./projects

# 2. 進入專案目錄
cd projects/my-blog

# 3. 生成 Article 功能
node scripts/spa-cli.js feature Article --fields "Title:string,Content:text,Author:string,PublishedAt:datetime,IsPublished:bool"

# 4. 依終端輸出指示，更新 Program.cs

# 5. 啟動
dotnet run              # 後端
# 前端用任意靜態伺服器開啟 frontend/
```

**生成結果：**
- `Article.cs`：C# Model，含 Title(string), Content(string), Author(string), PublishedAt(DateTime), IsPublished(bool)
- `ArticleService.cs`：CRUD 服務
- `ArticleListPage.js`：使用 TextInput, WebTextEditor, DateTimeInput, ToggleSwitch
- `ArticleDetailPage.js`：同上元件
- `routes.js`：自動加入 `/articles/article-list` 與 `/articles/article-detail` 路由

---

## 10. Ollama AI Agent CLI

若你不是透過商業 AI 工具（Claude Code、Cursor 等）進入本專案，可以使用內建的 Ollama Agent CLI：

```bash
# 啟動互動式對話
node tools/agent/agent.js

# 單次執行
node tools/agent/agent.js --run "生成一個部落格功能"

# 指定模型
node tools/agent/agent.js --model qwen2.5:14b
```

**零外部依賴**，只需 Node.js + Ollama。自動偵測並載入本手冊（AGENT.md）。

詳見 `tools/agent/README.md`。
