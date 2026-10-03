# Bricks4Agent — AI Agent 操作手冊（SPA 生成器）

> 本手冊專為 AI Agent 設計（適用於任何語言模型：GPT、Claude、Llama、Qwen、DeepSeek、Gemini 等，含離線地端模型）。
> 內容是**頁面／SPA 生成器**的指令格式、欄位對應表與操作流程。

> **要在元件庫上手刻頁面（呼叫元件、缺件補庫）請優先讀 [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)；本文件講的是用生成器批次產頁。**

### AI Agent 框架入口

- 通用 / 其他框架：`.agentrc`（指向 [AGENTS.md](AGENTS.md)，再連到本手冊與 AGENT-UI-GUIDE.md）

- Claude Code：[CLAUDE.md](CLAUDE.md)

---

## 1. 專案概觀

Bricks4Agent 是一套**零 runtime 依賴的 Vanilla JS UI 元件庫**，加上一個把 JSON `PageDefinition` 轉成頁面的**頁面／SPA 生成器**。

組成：

- **前端 UI 元件庫**（Vanilla JS，零外部 runtime dependency，116 個元件）

- **頁面生成引擎**（PageGenerator，`PageDefinition.FieldTypes` 共 37 種欄位類型，`tools/page-gen.js` 接受其中 34 種；靜態產碼 + 動態渲染）

- **SPA 生成器**（CLI + Web UI，一鍵產生全端 CRUD）

- **C# 後端範本**（.NET 10 Minimal API，供生成器產出後端；ORM 為輕量 BaseOrm）

AI agent 動手前的優先閱讀：手刻頁面／補元件看 [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)；用生成器批次產頁看本文件；規則看 [CLAUDE.md](CLAUDE.md)。

### 1.1 核心子系統位置

- UI 元件庫：`packages/javascript/browser/ui_components`（權威清單：`metadata/component-catalog.json`）

- 頁面生成引擎：`packages/javascript/browser/page-generator`

- SPA 範本：`templates/spa`（前端核心 `frontend/core` + 後端 `backend/SpaApi.csproj`）

- 生成器 CLI：`templates/spa/scripts`、`tools/page-gen.js`

- 生成器 Web UI：`tools/spa-generator`（port 3080）

- Schema→表單/API/資料表工作台：`tools/form-application-studio`（未給連線字串時生成本地 SQLite；只產碼、不連線）

- 表單應用定義與生成器：`packages/javascript/browser/form-application`

### 1.2 Build 與 test 入口

```powershell
npm --prefix packages/javascript/browser install   # 首次：安裝 Vitest/jsdom（npm test 的最後一段需要）
npm test                        # test-all.js 生成器範例 + test:ui-components + test:custom-components + 元件 Vitest 套件
npm run validate:ui-library     # UI 元件庫檢查
npm run audit:ui-styles         # 樣式 token 稽核
npm run test:form-designer:dotnet # 生成並編譯 SQLite/SQL Server/PostgreSQL/MySQL 後端
npm run test:dotnet10           # 35 個 net10.0 專案；任何建置警告都視為錯誤
node tools/spa-generator/server.js # 生成器 Web UI + 生成 API（port 3080）
npm run serve                   # 只以 StaticServer 提供生成器靜態前端（port 3080，無 /api）

# 生成器產出的 .NET 10 後端（SPA 範本）
dotnet build templates/spa/backend/SpaApi.csproj
dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj
```

### 核心工具鏈

```
spa-cli.js feature → generate-api.js (C# Model/Service；--fields 只決定後端型別)
                    → generate-page.js ×2 (前端頁面)
                        └── 一律輸出原始 BasePage 模板 + 自動登錄 pages/generated/routes.generated.js

元件庫頁面：PageDefinition JSON → PageGenerator（靜態）／DynamicPageRenderer（動態）／tools/page-gen.js（§7）
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
frontend/pages/diarys/DiaryListPage.js   ← 原始 BasePage 列表模板頁（不依欄位產生元件）
frontend/pages/diarys/DiaryDetailPage.js ← 原始 BasePage 詳情模板頁
frontend/pages/generated/routes.generated.js ← 自動登錄（import + 路由）
```

**後續步驟：**
`generate-api.js` 預設會以 `// --- BRICKS:* ---` 標記自動修補 `Program.cs`／`AppDbContext.cs`，但這些標記已於 `9093509` 從範本移除，因此只會印出 `Marker not found`、不會修補。建議加上 `--no-patch`（`spa-cli.js feature` 會把它轉給 `generate-api.js`），取得終端印出的服務註冊與 API 端點程式碼，再手動更新 `AppDbContext.cs` 的 `EnsureCreated()`（建表 SQL）與 AppDb 方法、`Program.cs`（服務註冊 + API 端點）。`spa-cli.js` 結尾列出的第 3 步「更新 routes.js」已由 `generate-page.js` 的自動登錄完成（除非加了 `--no-register`）。

---

### 2.2 僅生成頁面

```bash
node templates/spa/scripts/generate-page.js <路徑/名稱> [--detail] [--no-register]
```

**範例：**

```bash
node templates/spa/scripts/generate-page.js orders/OrderList
node templates/spa/scripts/generate-page.js orders/OrderDetail --detail
node templates/spa/scripts/generate-page.js SimplePage --no-register
```

`generate-page.js` 一律輸出原始 `BasePage` 模板頁（檔名含 `detail`／`view` 或加 `--detail` 時用詳情模板），沒有 `--fields`／`--api-path`，也不使用 PageGenerator；要依欄位產生元件庫頁面請用 PageGenerator／DynamicPageRenderer／`tools/page-gen.js`（§7）。預設會把新頁登錄到 `frontend/pages/generated/routes.generated.js`（`--no-register` 略過）；目標檔已存在時報錯結束、不覆蓋。

---

### 2.3 僅生成 API

```bash
node templates/spa/scripts/generate-api.js <名稱> --fields "<欄位>"
```

---

### 2.4 建立新專案

```bash
# 互動模式（--name / --output 只是提示的預設值，之後仍會逐項詢問）
node templates/spa/scripts/spa-cli.js new --name my-app --output ./projects

# 非互動模式：唯一不會停下來問的路徑是 --config
node templates/spa/scripts/create-project.js --config templates/spa/scripts/project-config.example.json
```

---

## 3. 欄位類型對應表

### 3.1 CLI `--fields` 類型只決定後端型別

`spa-cli.js feature`／`generate-api.js` 的 `--fields` 類型**只決定 C# 後端型別**（§3.3）。`spa-cli.js` 呼叫 `generate-page.js` 時不傳 `--fields`（spa-cli.js:116-117），前端一律是原始 `BasePage` 模板頁，不會依欄位挑選 UI 元件。

### 3.2 要依欄位產生元件庫頁面

請直接使用 PageDefinition JSON 格式（37 種欄位類型，如 `select`、`multiselect`、`color`、`image`、`richtext`、`canvas` 等，見 §7.1），搭配 PageGenerator（靜態產碼）、DynamicPageRenderer（動態渲染）或 `tools/page-gen.js`。

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

`templates/spa/frontend/components/` 中的檔案會隨 `spa new` 一起複製到新專案，實際內容如下（`BasePage` 使用其中的 `Panel/ModalPanel.js`、`Panel/ToastPanel.js` 與 `CanvasIcon.js`）：

| 元件 | 路徑 | 用途 |
|---|---|---|
| CanvasIcon | `components/CanvasIcon.js` | Canvas 圖示 |
| ColorPicker | `components/ColorPicker/ColorPicker.js` | 顏色選擇器 |
| DatePicker | `components/DatePicker/DatePicker.js` | 日期選擇器 |
| ImageViewer | `components/ImageViewer/ImageViewer.js` | 圖片檢視器 |
| Panel | `components/Panel/{BasePanel,ModalPanel,PanelManager,ToastPanel}.js` | 面板／對話框／Toast |
| services | `components/services/{GeolocationService,WeatherService}.js` | 定位／天氣服務 |
| utils | `components/utils/security.js` | 跳脫與安全檢查工具 |

注意：PageGenerator 生成的頁面**不會** import 這個資料夾；元件 import 一律指向元件庫本體 `@component-library/ui_components/…`（見 PageGenerator.js 的 `ComponentPaths`）。

---

## 5. 自動路由更新

`generate-page.js` 預設會把新頁登錄到 `frontend/pages/generated/routes.generated.js`（`routes.js` 會併入其中的 `generatedRoutes`）：

1. 檔案不存在時先建立空的 `generatedRoutes` 陣列

2. 在最後一個 `import` 後插入新的 import 語句

3. 在 `];` 前插入新的路由條目

4. 自動處理逗號分隔

5. 重複檢查：若相同 path 或 className 已存在則跳過

**加 `--no-register`** 時不改路由檔，需自行把終端輸出的 import 與路由條目加進路由設定。

---

## 6. 目錄結構

```
Bricks4Agent/
├── AGENT.md                              ← 本手冊
├── packages/
│   ├── javascript/browser/
│   │   ├── ui_components/                ← 116 個 UI 元件（完整版）
│   │   │   ├── form/                     ← 表單 (18)
│   │   │   ├── common/                   ← 通用 (40)
│   │   │   ├── layout/                   ← 佈局 (13)
│   │   │   ├── input/                    ← 進階輸入 (10)
│   │   │   ├── viz/                      ← 視覺化 (23)
│   │   │   ├── social/                   ← 社群 (5)
│   │   │   ├── sections/                 ← 區塊 (4)
│   │   │   ├── editor/                   ← 編輯器 (1)
│   │   │   ├── analytics/                ← 分析 (1)
│   │   │   └── data/                     ← 資料展示 (1)
│   │   └── page-generator/              ← PageGenerator 引擎
│   │       ├── PageGenerator.js          ← 靜態產碼：PageDefinition → 頁面程式碼
│   │       ├── PageDefinition.js         ← 定義格式、驗證、欄位→元件映射
│   │       ├── FieldResolver.js          ← 欄位型別 → 元件實例
│   │       ├── TriggerEngine.js          ← 欄位連動（8 種動作）
│   │       └── DynamicPageRenderer.js    ← 動態渲染（執行期依 JSON 畫出）
│   └── csharp/                           ← C# 後端模組（生成器產出的後端所用）
├── templates/spa/                        ← SPA 專案範本
│   ├── scripts/
│   │   ├── spa-cli.js                    ← CLI 入口（new/feature/page/api）
│   │   ├── generate-page.js              ← 原始模板頁面生成
│   │   ├── generate-api.js               ← C# API 生成
│   │   └── create-project.js             ← 專案建立
│   ├── frontend/
│   │   ├── core/                         ← 框架核心（BasePage, Router, Store）
│   │   ├── pages/                        ← 頁面範本
│   │   │   ├── routes.js                 ← 路由配置（併入 generated/ 的自動登錄路由）
│   │   │   └── generated/routes.generated.js ← generate-page.js 自動登錄
│   │   └── components/                   ← SPA 範本元件
│   └── backend/                          ← .NET 10 後端範本（SpaApi.csproj）
└── tools/
    ├── spa-generator/                    ← SPA 生成器 Web UI（port 3080）
    ├── page-gen.js                       ← PageDefinition CLI（獨立工具）
    └── static-server/                    ← 預覽用靜態伺服器
```

---

## 7. PageGenerator 進階用法

CLI 的 `--fields` 只決定後端型別（§3）；要依欄位產生元件庫頁面，請直接使用 PageGenerator 的 37 種欄位類型（`PageDefinition.FieldTypes`）：

```javascript
import { PageGenerator } from './packages/javascript/browser/page-generator/PageGenerator.js';

const definition = {
    name: 'MyFormPage',
    type: 'form',           // form | list | detail | dashboard | tool
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

**命名限制**：`name`、每個 `fields[].name`，以及 `behaviors.onInit/onSave/onDelete` 與 `behaviors.fieldTriggers[欄位名]` 的方法名，都會被寫成生成檔中的**裸 JavaScript 識別字**，無法跳脫，因此生成前會先驗證是否為合法的 JS `IdentifierName`（依 Unicode `ID_Start`/`ID_Continue`，中文欄位名如 `姓名` 合法；`a-b`、`2col`、含空白或引號者不合法）。保留字只在繫結位置（`name` → class 名稱）被擋。不合法時 `generate()` 回傳 `{ code: null, errors: [...] }`——**產碼後務必先檢查 `result.errors` 是否為空**。

### 7.1 完整 37 種欄位類型

元件欄以動態渲染（[FieldResolver.js](packages/javascript/browser/page-generator/FieldResolver.js)）為準；［］內為靜態產碼（PageGenerator）的差異。

| 類型 | 元件 |
|---|---|
| text / email / password | TextInput |
| tel / url | 未註冊，退回 TextInput |
| number | NumberInput |
| textarea / memo | TextArea［靜態：memo 生成錯誤］ |
| select | Dropdown |
| multiselect | MultiSelectDropdown［靜態：無模板，不渲染任何控制項］ |
| radio | Radio.createGroup |
| checkbox | Checkbox |
| toggle | ToggleSwitch |
| date / rocDate | DatePicker（rocDate 帶 `format: 'taiwan'`） |
| time | TimePicker |
| datetime | DateTimeInput |
| richtext | WebTextEditor |
| canvas | DrawingBoard |
| color | ColorPicker |
| image | ImageViewer |
| file | BatchUploader |
| geolocation / weather | GeolocationService / WeatherService |
| address / addresslist / chained / list / personinfo / phonelist / socialmedia / organization / student | AddressInput / AddressListInput / ChainedInput / ListInput / PersonInfoList / PhoneListInput / SocialMediaList / OrganizationInput / StudentInput |
| slider | Slider［靜態：生成錯誤］ |
| rating / tags | 未註冊，退回 TextInput［靜態：原生星等 radio／標籤清單］ |
| hidden | `<input type="hidden">` |

靜態產碼對 text、email、password、tel、url、number、textarea、select、radio、checkbox、toggle、time、file、hidden 輸出原生 HTML 控制項，只有 date、rocDate、datetime、richtext、canvas、color、image、geolocation、weather 與九個複合輸入會 import 元件庫元件。`tools/page-gen.js` 只接受其中 34 種（不含 rocDate、slider、memo），並在靜態生成前把 multiselect 轉成 select。

### 7.2 用 `tools/page-gen.js` 批次產頁

單頁：

```bash
node tools/page-gen.js --def employee.json --mode static --output ./output/
```

CLI 的輸入須為 page-gen 格式 `{ page: {...}, fields: [{ fieldName, fieldType, ... }] }` 或 DefinitionTemplate；上面 §7 那種 PageDefinition 物件（`{ name, type, fields }`）直接餵給 CLI 會被拒（`缺少 page 區塊`）。

輸入若是 DefinitionTemplate（一份定義內含多個 pages），可在**同一個 process 內**一次產出多頁（不必每頁開一個 process）：

```bash
# 指定 page id（逗號分隔）
node tools/page-gen.js --def site-definition.json --pages products-list,orders-form --mode static --output ./output/

# 模板內所有 pages
node tools/page-gen.js --def site-definition.json --all --mode static --output ./output/
```

| 旗標 | 說明 |
|---|---|
| `--def <path>` | 定義 JSON 路徑（省略則從 stdin 讀取） |
| `--page <id>` | 從 DefinitionTemplate 取單一 page |
| `--pages <ids>` | 從 DefinitionTemplate 批次處理指定 page id（逗號分隔，不可重複） |
| `--all` | 批次處理 DefinitionTemplate 內所有 pages |
| `--mode <mode>` | `static` \| `dynamic` \| `both`（預設 `static`） |
| `--output <dir>` | 輸出目錄 |
| `--validate` | 只驗證不生成（批次模式下逐頁驗證，錯誤訊息帶 page id 前綴） |
| `--list-types` | 列出欄位類型、觸發器事件與動作 |

批次模式輸出彙總 JSON `{ success, results: [{ pageId, files }], errors? }`；模板只解析驗證一次，並且**先驗證全部選取的 pages 才開始生成**。`--pages`/`--all` 只適用於 DefinitionTemplate 輸入，用在單頁定義上會直接報錯。

---

## 8. 常見陷阱

### 8.1 MSYS 路徑轉換（Windows Git Bash）

在 Git Bash 中，以 `/` 開頭的命令列參數（如 `/api/xxx`）會被自動轉換為 `C:/Program Files/Git/api/xxx`，加引號也無法避免。目前的生成器 CLI 都沒有 API 路徑參數（`generate-page.js` 沒有 `--api-path`，repo 內也沒有 `sanitizeApiPath()`）；API 路徑請寫在 PageDefinition JSON 的 `api.*`。若必須在 Git Bash 傳遞此類參數，請在指令前加 `MSYS_NO_PATHCONV=1`。

### 8.2 檔案已存在

只有 `generate-page.js` 會拒絕覆蓋已存在的檔案（報錯結束）。`generate-api.js` 會直接覆寫 `backend/Models/<名稱>.cs` 與 `backend/Services/<名稱>Service.cs`；`tools/page-gen.js` 也會覆寫輸出目錄內的同名檔案。重新生成前請先確認或備份目標檔案。

### 8.3 CJS 與 ESM

- `tools/page-gen.js`、`generate-page.js`、`spa-cli.js`：CommonJS（使用 `require`）

- `PageGenerator.js`、所有元件：ESM（使用 `import/export`）

- 橋接方式（見 `tools/page-gen.js`）：`const { pathToFileURL } = require('node:url')` + `await import(pathToFileURL(path).href)`

### 8.4 import 路徑深度

PageGenerator 輸出的 `BasePage` import 預設為 `'../core/BasePage.js'`（可用 `new PageGenerator({ baseImportPath })` 覆寫）；元件 import 一律是元件庫路徑 `@component-library/ui_components/…`（見 `ComponentPaths`），部署時需自行提供對應的 alias 或 import map。`generate-page.js` 不使用 PageGenerator，只依子資料夾深度調整原始模板中 `core/BasePage.js` 的相對路徑（如 `pages/orders/OrderPage.js` → `../../core/BasePage.js`）。

### 8.5 欄位名／頁面名不是合法識別字

看到 `... is emitted as a bare JavaScript identifier and must be a valid IdentifierName` 或 `... must not be a reserved word`，表示定義裡的名稱無法寫成生成檔中的識別字（詳見 §7 的命名限制）。改名即可，`errors` 非空時 `code` 為 `null`，不會有半成品檔案。

另外 `behaviors.fieldTriggers[欄位名]` 的值是**方法名字串**（如 `"reloadDistricts"`），不是動作物件陣列；後者是欄位層級 `field.triggers` 的形狀，放錯位置會被上述識別字檢查擋下。

---

## 9. 操作流程範例

### 範例：生成部落格功能

```bash
# 1. 建立新專案
node templates/spa/scripts/spa-cli.js new --name my-blog --output ./projects

# 2. 生成 Article 功能
#    注意：feature / page / api 是寫進 repo 的 templates/spa/ 樹，不是寫進剛建立的專案；
#    create-project.js 複製時會排除 scripts/，生成的專案內沒有這支 CLI 可用。
node templates/spa/scripts/spa-cli.js feature Article --fields "Title:string,Content:text,Author:string,PublishedAt:datetime,IsPublished:bool"

# 3. 手動更新 AppDbContext.cs 與 Program.cs（預設 patch 找不到標記，見 §2.1 後續步驟）

# 4. 啟動
dotnet run              # 後端
# 前端用任意靜態伺服器開啟 frontend/
```

**生成結果：**
- `Article.cs`：C# Model，含 Title(string), Content(string), Author(string), PublishedAt(DateTime), IsPublished(bool)
- `ArticleService.cs`：CRUD 服務
- `ArticleListPage.js`：原始 `BasePage` 列表模板頁（不依欄位產生元件）
- `ArticleDetailPage.js`：原始 `BasePage` 詳情模板頁
- `pages/generated/routes.generated.js`：自動加入 `/articles/article-list` 與 `/articles/article-detail` 路由

---

## 10. 延伸閱讀

- 手刻頁面／呼叫元件／缺件補庫：[AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)

- JSON 客製元件、三層分類、資料夾載入與 DynamicPageRenderer 接線：[CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md)

- JSON 產生的工具頁、可信 commands、state bindings 與 self-host Studio：[page-generator/README.md](packages/javascript/browser/page-generator/README.md#tool-pagedefinition-與自舉工具頁)

- 頁面生成器細節：[page-generator/README.md](packages/javascript/browser/page-generator/README.md)

- 獨立 PageDefinition CLI：[tools/page-gen.README.md](tools/page-gen.README.md)

- SPA 範本：[templates/spa/README.md](templates/spa/README.md)
