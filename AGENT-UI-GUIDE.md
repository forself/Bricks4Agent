# AGENT-UI-GUIDE — 元件庫調用入口（給 AI Agent）

> **任務情境**：把一個「重度依賴第三方套件的 React 網站」重製成**只使用本元件庫**的網站。
> 缺的元件 → **補進本元件庫**（而不是引入第三方）。
>
> 本文件是給 AI Agent 的操作入口。開始動手前**必讀本文件**。
> 元件的權威清單以機器可讀檔為準：
> [`packages/javascript/browser/ui_components/metadata/component-catalog.json`](packages/javascript/browser/ui_components/metadata/component-catalog.json)
> （AGENT.md 內的數字是舊敘述，以 catalog 為準。）

---

## 0. 三條鐵則（違反即失敗）

1. **零第三方 runtime**：不得 `import` 任何 npm UI/圖表/地圖/日期/富文本套件。全站只用本元件庫 + 原生瀏覽器 API。唯一例外是 `LeafletMap`（Leaflet 1.9.4 已 **vendored** 於 `ui_components/vendor/leaflet/`，預設本地載入、零外網、嚴格 CSP 可用；本地缺檔才退 CDN 備援）。
2. **樣式只用 theme token**：顏色/圓角/陰影/字體一律用 `var(--cl-*)` CSS 變數，禁止寫死色碼。換膚靠文件根的 `[data-theme="dark"]`，元件不寫 media query。
3. **輸出一律跳脫**：任何把資料塞進 HTML 的地方用 `escapeHtml()`；要放原始 HTML 必須顯式 `raw()`。
4. **嚴格 CSP + SVG 禁用（機器執法）**：禁 `<style>` 注入、禁 innerHTML 模板內 `style=`/`on*=`、禁 eval/`javascript:`（樣式走 CSSOM `cssText`/`setProperty` 或同目錄 `.css` + 同源 `<link>`）；**視覺一律 Canvas、禁新增 SVG**（既有存量按 `tools/scripts/svg-baseline.json` 棘輪只減不增；圖表基底＝`viz/CanvasChart.js`，主題響應靠 `utils/theme-bus.js`，Path2D 可直接吃 SVG path 字串）。守門員：`node tools/scripts/audit-csp.mjs`（六類 CSP 全零 + G 類 SVG 棘輪）、`node tools/scripts/validate-ui-library.mjs`（風格 token 稽核：元件內禁散裝 hex，色回退唯一來源＝theme-bus 的 `FALLBACK_PAINT`）。合規宣稱只認機器判定。

---

## 1. 唯一的匯入入口

所有元件從單一 barrel 匯出：

```js
import {
  TextInput, Dropdown, NumberInput, DatePicker, ToggleSwitch,   // form
  DataTable, TabContainer, SideMenu, PanelManager,              // layout
  BasicButton, Pagination, Notification, Tag, TreeList,         // common
  BarChart, LineChart, PieChart, DrawingBoard,                  // viz
  Avatar, FeedCard, StatCard,                                   // social
  WebTextEditor,                                                // editor
  ComponentFactory, ComponentBinder,                            // binding
  Locale,                                                       // i18n
} from '<相對路徑>/packages/javascript/browser/ui_components/index.js';
```

生成器（把 JSON 定義變成頁面）從另一個 barrel 匯出：

```js
import {
  PageGenerator, FieldTypes, PageTypes, validateDefinition,     // 靜態產碼 / schema
  DynamicPageRenderer, FieldResolver, TriggerEngine,            // 動態渲染
  PageDefinitionAdapter,
} from '<相對路徑>/packages/javascript/browser/page-generator/index.js';
```

---

## 2. 通用元件契約（**每個元件都一樣，先記這個**）

本庫是 **imperative（命令式）Vanilla JS class**，不是 React。沒有 JSX、沒有 virtual DOM。模式固定：

```js
const c = new TextInput({ label: '姓名', required: true, onChange: (v) => {...} });
c.mount(container);   // container 可傳 DOM 元素或 CSS 選擇器字串；回傳 this
// ...操作...
c.destroy();          // 卸載並移除 DOM
```

**共通方法**（依 [TextInput.js](packages/javascript/browser/ui_components/form/TextInput/TextInput.js) 等實作，具值元件皆有）：

| 方法 | 說明 |
|---|---|
| `new X(options)` | 建構；`options` 是純物件，未給的走預設值 |
| `.mount(containerOrSelector)` | 掛載到 DOM，回傳 `this` |
| `.destroy()` | 卸載並移除 |
| `.getValue()` / `.setValue(v)` | 讀/寫值 |
| `.setDisabled(bool)` / `.clear()` | 停用 / 清空 |
| `.show()` / `.hide()` | 顯示 / 隱藏 |
| `.setError(msg)` / `.clearError()` | 表單元件的錯誤狀態 |
| `.snapshot()` / `.send(event, payload)` | 直接操作內部狀態機（進階，見 §7） |

**事件走 callback**，透過 `options` 傳入：`onChange`、`onClick`、`onBlur`、`onFocus` 等（各元件不同，看該元件建構子）。

> ⚠️ 元件建構子的 `options` 欄位**各不相同**。動手用某元件前，先開它的原始碼看 `constructor(options = {...})` 的預設物件，那就是完整可用參數表。例如 [DataTable.js](packages/javascript/browser/ui_components/layout/DataTable/DataTable.js) 支援三種 columns 格式與三種呼叫簽章。

### 用字串名動態建立（重製時很常用）

```js
import { ComponentFactory } from '.../ui_components/index.js';
const dt = ComponentFactory.create('DataTable', { columns, data });   // 查不到回傳 null 並 warn
ComponentFactory.register('MyNewThing', MyNewThingClass);             // 註冊新元件
```

---

## 3. 元件清單（116 個，權威來源＝catalog）

`*` = `generator.usable=false`（`manual_only`：不能靠生成器欄位自動映射，需**手動組合**；仍可正常 `new` 使用）。

- **form (18)**：`BatchUploader, Checkbox, DatePicker, Dropdown, FormField*, MultiSelectDropdown, NumberInput, Radio, SearchForm*, Slider, TextArea, TextInput, TimePicker, ToggleSwitch` + 0626 併入:`CommandComposer, Form, Rating, TagInput`(Textarea 已併入 TextArea,單一實作雙名稱)
- **common (40)**：`ActionButton*, AuthButton*, Badge*, BasicButton, Breadcrumb*, ButtonGroup, ColorPicker, Divider*, DownloadButton*, EditorButton, FeatureCard*, Icon*, ImageViewer, LoadingSpinner*, Notification*, Pagination*, PhotoCard*, Progress*, SimpleDialog*, SortButton*, Tag*, Tooltip*, TreeList*, UploadButton*` + 0626 併入的 atoms/composites:`Alert, CardGrid, CodeBlock, DescriptionList, DropdownMenu, EmptyState, FilterBar, Heading, Link, List, MediaPlayer, ResultList, Skeleton, StatGrid, StepIndicator, Text`
- **layout (13)**：`DataTable*, DocumentWall*, FormRow*, FunctionMenu*, InfoPanel*, PanelManager*, PhotoWall*, SideMenu*, Stepper*, TabContainer*, WorkflowPanel*, EditableTable, FormDesigner`（12 欄表單設計畫布；拖拉、縮放、換元件、改欄位）
- **input (10, 複合輸入)**：`AddressInput, AddressListInput, ChainedInput, DateTimeInput, ListInput, OrganizationInput, PersonInfoList, PhoneListInput, SocialMediaList, StudentInput`
- **viz (23)**：`BarChart*, CanvasMap*, ClusterGraph*, DrawingBoard, FlameChart*, HeatmapChart*, HierarchyChart*, LeafletMap*, LineChart*, MapEditor*, MapEditorV2*, OrgChart*, OSMMapEditor*, PieChart*, RelationChart*, RoseChart*, SankeyChart*, ScatterChart*, Sparkline*, SunburstChart*, TGOSMapEditor*, TimelineChart*, WebPainter`（全數 Canvas 渲染；共同基底 `viz/CanvasChart.js` 非目錄元件、不在 catalog。舊 SVG 基底 BaseChart 已刪除）
- **social (5)**：`Avatar*, ConnectionCard*, FeedCard*, StatCard*, Timeline*`
- **editor (1)**:`WebTextEditor`
- **sections (4,0626 併入)**:`BannerSection, ContentSection, PageFooter, PageHeader`(頁首/頁尾/橫幅/內容區段複合)
- **data (1)**:`RegionMap`(台灣著色地圖,Canvas/Path2D)
- **analytics (1)**:`DataExplorer*`(統計探索複合件:繫結表單/資料 → ChartSpec → 聚合引擎 → 8 種圖型 2D~4D + 聚合表/明細分頁/CSV/PNG 匯出;spec 白名單 fail-closed)

> 想查某元件能不能被生成器直接吃、支援哪些 field type、可綁哪些事件/動作 → 查它在 catalog 的 `generator` / `binding` 區塊，或它資料夾內的 `*.manifest.json`。

---

## 4. 建頁面的三種方式（依情境選）

> **建「新專案」**(而非單頁)用產生器:`node tools/create-project/create-project.mjs --name my-app`(跨平台零依賴)——
> 產出的專案內建「junction 開發(直用活腳手架)+ 發佈快照(自含複本+SNAPSHOT.json 憑證+封閉性驗證)」機制,
> import 一律 `lib/…` 相對路徑。見 [tools/create-project/README.md](tools/create-project/README.md)。

### 方式 A：手動組合（重製任意 React 畫面的主力）
直接 `new` 元件、`mount` 進版面容器。適合非 CRUD 的自訂畫面（儀表板、落地頁、圖表牆）。

```js
const table = new DataTable({ columns, data, options: { selectableRows: 'multiple' } });
table.mount('#list-area');
const chart = ComponentFactory.create('BarChart', { data: series });
chart.mount('#chart-area');
```

### 方式 B：動態渲染（有一份 JSON 定義，執行期畫出來，免產碼）
適合表單/清單/詳情這類「欄位驅動」頁面。

```js
import { DynamicPageRenderer } from '.../page-generator/index.js';
const renderer = new DynamicPageRenderer({
  definition: pageDefinition,          // 見 §4 方式 C 的 PageDefinition 形狀
  mode: 'form',                        // 'form' | 'detail' | 'list'
  data: null,                          // 編輯/詳情時帶入資料物件
  onSave: (values) => {...}, onSearch: (q) => {...}, onEdit: (row) => {...},
});
await renderer.init();
renderer.mount('#app');
// await renderer.switchMode('list', data)  // 切換模式（會重建並重掛）
```
底層：`FieldResolver`（30 種 field type → 元件實例）＋ `TriggerEngine`（8 種動作 clear/setValue/show/hide/setReadonly/setRequired/reload/reloadOptions 做欄位連動）。

### 方式 C：靜態產碼（把 JSON 定義落地成 `.js` 頁面檔）
適合要進版控、成為原始碼的頁面。

```js
import { PageGenerator } from '.../page-generator/index.js';
const { code, errors } = new PageGenerator().generate(pageDefinition);
// errors 為空陣列才算成功；code 是一個完整的 BasePage 子類別原始碼字串
```
或用 CLI：`node tools/page-gen.js --def page.json --mode static --output ./out/`（`--list-types` 看支援型別，`--validate` 只驗證）。

**PageDefinition 形狀**（見 [PageDefinition.js](packages/javascript/browser/page-generator/PageDefinition.js)）：
```jsonc
{
  "name": "EmployeePage",           // PascalCase，靜態格式以 Page 結尾
  "type": "form",                   // form | list | detail | dashboard
  "fields": [
    { "name": "title", "type": "text", "label": "標題", "required": true,
      "validation": { "maxLength": 50 } }
  ],
  "api": { "list": "/api/employee", "get": "...", "create": "...", "update": "...", "delete": "..." },
  "behaviors": { "fieldTriggers": { "city": [{ "target": "district", "action": "reloadOptions" }] } },
  "styles": { "layout": "single" }
}
```

---

## 5. 應用外殼（多頁 App 用這個，取代 react-router / redux）

生成/重製的 SPA 跑在 [templates/spa/frontend/core](templates/spa/frontend/core) 上：

- **`BasePage`**：頁面基底。覆寫 `template()` 回傳 HTML 字串、`events()` 回傳事件委派表 `{ 'click .btn': 'onClick' }`、生命週期 `onInit()/onMounted()/onDestroy()`。`this.data` 是 reactive Proxy（改值自動重繪）。內建 `esc()/escAttr()`、`navigate()`、`showMessage()`、`confirm()`。
- **`Router`**：hash/history 模式、`:param` 動態路由、`beforeEach/afterEach` 守衛。取代 react-router。
- **`Store`**：`get/set/update/subscribe`、dot-notation、可選 localStorage 持久化。取代 redux/zustand。

一鍵產全端 CRUD（前端頁 + C# 後端 Model/Service/API + 自動更新 routes）：
```bash
node templates/spa/scripts/spa-cli.js feature Article --fields "Title:string,Content:text,IsPublic:bool"
```

---

## 6. React → 本庫 對照 playbook

重製時逐一把 React 概念翻譯過來：

| React 生態 | 本庫替代 |
|---|---|
| JSX / function component | `class` + `template()` 字串 或 手動 `new`+`mount` |
| `useState` / props | 元件 `options` + `getValue/setValue`；跨頁共享用 `Store` |
| `useEffect` / 生命週期 | `BasePage.onInit/onMounted/onDestroy` |
| react-router | `core/Router` |
| redux / zustand / context | `core/Store` |
| axios / react-query | 原生 `fetch` + `Store`（或 spa 範本的 `ApiService`）|
| MUI / AntD 表單 | `form/*`（TextInput, Dropdown, DatePicker...）+ `FormField` 包裝 |
| MUI DataGrid / AntD Table | `layout/DataTable` |
| Recharts / ECharts / Chart.js | `viz/*`（Bar/Line/Pie/Sankey/Org...）|
| Leaflet / Google Map React | `viz/LeafletMap` / `CanvasMap` / `MapEditor` |
| TGOS 地圖截圖標記(GIS 標註)| `viz/TGOSMapEditor`(臺灣通用電子地圖;OSM 版為 `OSMMapEditor`,兩者僅地圖來源不同,政網可用 `tileLayers` 指向後端 TGOS 代理)|
| react-jvectormap(區域著色地圖)| `data/RegionMap`(內建台灣地圖,Canvas/Path2D)|
| 民國曆日期(react-datepicker + 民國轉換)| `form/DatePicker` 用 **`format:'taiwan'`**(`useROC` 是舊參數)|
| FontAwesome / MUI icons | `common/Icon`(內建 55 個圖示,可 `Icon.register()` 擴充;現為 SVG 存量、波 3 收斂為 Canvas/Path2D)|
| antd Steps / react-stepzilla | `layout/Stepper` |
| react-sparklines | `viz/Sparkline` |
| react-quill / Draft.js | `editor/WebTextEditor` |
| Ant Modal / Drawer / Tabs | `layout/PanelManager`（Modal/Drawer/Toast...）、`TabContainer` |
| react-i18next | `Locale`（`Locale.t(key)`、`Locale.setLang('en')`）|
| Formik / RHF 驗證 | 元件 `validation` + `setError/clearError`，或動態渲染的 field `validation` |

**流程建議**：
1. 盤點 React 專案的畫面與其第三方元件清單。
2. 每個第三方元件 → 查 §3 / catalog 找對應。**找得到就用**。
3. 找不到對應 → 走 §8 **新增到本庫**（不要引第三方）。
4. CRUD/表單類頁面優先用生成器（方式 B/C）；自訂視覺頁面用手動組合（方式 A）。
5. 用 §9 檢查表逐頁驗收。

---

## 7. 進階：直接操作狀態機（少數情況才需要）

每個元件內部用 [component-state.js](packages/javascript/browser/ui_components/utils/component-state.js) 的不可變狀態機。`snapshot()` 取當前狀態深拷貝，`send(event, payload)` 觸發轉移。一般用 §2 的高階方法即可；只有做自訂衍生元件時才會直接碰 `createComponentState(initialState, transitions)`。

---

## 8. 新增缺漏元件到本庫（**核心任務**，照步驟做）

### 8.1 元件實作契約
在 `ui_components/<category>/<Name>/` 建立：

- **`<Name>.js`**：`export class <Name> { constructor(options={}){...} mount(c){...return this} destroy(){...} }`。
  - 具值元件請實作 `getValue/setValue/setDisabled/clear`（＋表單類的 `setError/clearError`）。
  - 內部狀態建議走 `createComponentState`（與既有元件一致）。
  - 樣式只用 `var(--cl-*)`；輸出用 `escapeHtml`/`raw`（`import ... from '../../utils/security.js'`）。
  - 文案走 i18n：`import Locale from '../../i18n/index.js'`。
- **`index.js`**：`export { <Name> } from './<Name>.js';`
- **`<Name>.manifest.json`**：機器可讀 metadata（見 8.2）。

### 8.2 manifest.json 必填格式
由 [manifest-schema.js](packages/javascript/browser/ui_components/metadata/manifest-schema.js) 驗證，欄位與合法值固定：

```jsonc
{
  "schema_version": 1,
  "component_id": "form.my_widget",           // "<category>.<snake_case>"
  "registry_name": "MyWidget",                // = class 名 = ComponentFactory 註冊名
  "display_name": "MyWidget",
  "category": "form",                          // common|form|input|layout|social|editor|data|viz|utils
  "kind": "atomic",                            // atomic|composite|container|visualizer|service_bridge
  "source_path": "ui_components/form/MyWidget/MyWidget.js",
  "docs_path": "",                             // 有 README 就填，沒有留空字串
  "maturity": "stable",                        // stable|beta|legacy
  "generator": {
    "usable": true,                            // 生成器能否用它
    "usage_mode": "field_direct",              // field_direct|definition_explicit|runtime_only|manual_only
    "supported_field_types": ["text"],         // field_direct 必須非空
    "supported_page_types": ["form","detail","list"],
    "definition_runtime": true
  },
  "composition": { "role": "input", "requires_form_field_wrapper": true, "manual_only": false },
  "binding": {
    "value_io": true,
    "listener_events": ["change","blur"],
    "target_actions": ["clear","setValue","setReadonly","setRequired"]  // 必須是 TriggerEngine 支援的動作
  },
  "styling": { "theme_token_only": true, "style_knobs": ["size","width","disabled"] }
}
```
> 若只是純手動組合的展示/動作元件（如按鈕、卡片），設 `generator.usable=false`、`usage_mode="manual_only"`、`supported_field_types=[]`。

### 8.3 註冊與收尾（缺一不可）
1. 在該 category 的 `index.js` 加 `export`。
2. 若要能被字串名/生成器使用，在 [ComponentFactory.js](packages/javascript/browser/ui_components/binding/ComponentFactory.js) 的 `registry` 加入 `'MyWidget': MyWidget`。
3. 若要被生成器當 field type 用，把 field type → 元件映射補進 [PageDefinition.js](packages/javascript/browser/page-generator/PageDefinition.js) 的 `ComponentMapping` 與 [FieldResolver.js](packages/javascript/browser/page-generator/FieldResolver.js)。
4. **重建 catalog**：
   ```bash
   node packages/javascript/browser/ui_components/metadata/build-metadata.mjs
   ```
   CI/驗證用 `--check`（會校驗每個 registry 元件都有合法 manifest，且 manifest.registry_name 存在於 ComponentFactory）。

### 8.4 不寫 JavaScript 的 JSON 客製元件

若需求是調整既有元件 options 或組合既有元件，不必新增一套 JS 元件。使用 [CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md) 的純 JSON 定義：

1. 開啟 [Theme Studio](tools/theme-studio/) 的「元件組合」頁籤，組裝、預覽並匯出 JSON。舊的 [Custom Component Studio](tools/custom-component-studio/) URL 保留為相容入口，會導向同一頁籤。
2. 將 JSON 放進 `packages/javascript/browser/custom_components/definitions/`。
3. 執行 `npm run custom-components:build` 產生 deterministic `registry.json`。
4. 執行 `npm run custom-components:check` 與 `npm run test:custom-components`。
5. Runtime 以 `CustomComponentRegistry.loadFolder()` 載入；dynamic form 可直接傳 `customComponents: { folder }`。

JSON 客製元件分為 `atomic`、`composite`、`template`，由組合樹自動推導；不得靠手填 `kind` 規避深度規則。客製 JSON 禁止函式、raw HTML、任意 style/import 與 prototype-sensitive key。

### 8.5 Studio 自舉規則

Studio 頁面不得複製或手刻另一套工具 UI。唯一權威定義是 `tools/theme-studio/studio.page.json`，由 `DynamicPageRenderer({ mode: 'tool' })` 交給 `DynamicToolRenderer` 建立。定義中的 `component` 必須由 `ComponentFactory` 解析，事件只能引用 controller 提供的可信 command ID；JSON 不得承載函式或 raw HTML。

若 Studio 需要的互動能力不足，先補正式元件公開 API，再由 state binding 使用。例如 action `List`、父節點可選的 `TreeList`、穩定 file input 與不遞迴觸發的 `NumberInput.setValue()` 都屬這類自舉需求。驗收至少執行 `npm run test:studio:self-host`、`npm run test:studio:browser`，並確認同一 renderer、DOM identity、control provenance、JSON round-trip、CSP 與 SVG hard-zero。

---

## 9. 每頁驗收檢查表

- [ ] 沒有任何第三方 UI/圖表/地圖/日期/富文本 npm import（Leaflet CDN 除外）
- [ ] 所有顏色/尺寸走 `var(--cl-*)`，無寫死色碼；`[data-theme="dark"]` 下正常
- [ ] 所有動態內容經 `escapeHtml`/`esc()`；原始 HTML 皆顯式 `raw()`
- [ ] 元件都有 `mount`/`destroy`，換頁時正確 `destroy` 無殘留 DOM/監聽
- [ ] 文案走 `Locale.t()`，切語言可更新
- [ ] 新增的元件：有 `index.js` export + manifest + ComponentFactory 註冊 + `build-metadata.mjs --check` 通過
- [ ] 路由用 `Router`、共享狀態用 `Store`

---

## 10. 關鍵檔案索引

| 用途 | 路徑 |
|---|---|
| 設計系統(設計說明) | [ui_components/DESIGN-SYSTEM.md](packages/javascript/browser/ui_components/DESIGN-SYSTEM.md) |
| 主題/客製(使用說明) | [ui_components/THEME-USAGE.md](packages/javascript/browser/ui_components/THEME-USAGE.md) |
| 元件客製工作台（樣式客製 + 元件組合） | [tools/theme-studio/](tools/theme-studio/) |
| 元件組合相容入口 | [tools/custom-component-studio/](tools/custom-component-studio/) |
| 客製元件完整契約 | [CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md) |
| 客製 JSON/runtime/registry | [custom_components/](packages/javascript/browser/custom_components/) |
| Schema→表單/API/資料表工作台 | [tools/form-application-studio/](tools/form-application-studio/) |
| 表單應用定義與生成器 | [form-application/](packages/javascript/browser/form-application/) |
| 元件總入口 | [ui_components/index.js](packages/javascript/browser/ui_components/index.js) |
| 元件權威清單 | [metadata/component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) |
| 字串名工廠 | [binding/ComponentFactory.js](packages/javascript/browser/ui_components/binding/ComponentFactory.js) |
| 狀態機 | [utils/component-state.js](packages/javascript/browser/ui_components/utils/component-state.js) |
| 安全工具 | [utils/security.js](packages/javascript/browser/ui_components/utils/security.js) |
| i18n | [i18n/index.js](packages/javascript/browser/ui_components/i18n/index.js) |
| manifest 規則 | [metadata/manifest-schema.js](packages/javascript/browser/ui_components/metadata/manifest-schema.js) |
| catalog 重建 | [metadata/build-metadata.mjs](packages/javascript/browser/ui_components/metadata/build-metadata.mjs) |
| 生成器入口 | [page-generator/index.js](packages/javascript/browser/page-generator/index.js) |
| PageDefinition schema | [page-generator/PageDefinition.js](packages/javascript/browser/page-generator/PageDefinition.js) |
| 動態渲染 | [page-generator/DynamicPageRenderer.js](packages/javascript/browser/page-generator/DynamicPageRenderer.js) |
| App 外殼 | [templates/spa/frontend/core](templates/spa/frontend/core) |
| CLI | [templates/spa/scripts/spa-cli.js](templates/spa/scripts/spa-cli.js) · [tools/page-gen.js](tools/page-gen.js) |

---

## 11. Schema → 表單／API／資料表

[Form Application Studio](tools/form-application-studio/) 接受資料表 schema，先產生可編輯的欄位清單與 12 欄 `FormDesigner` 畫布，再輸出設計 JSON、前端 `PageDefinition`、.NET 10 Minimal API／BaseOrm 程式碼、建表與 rollback SQL。工具頁本身由 [`studio.page.json`](tools/form-application-studio/studio.page.json) 經 `DynamicPageRenderer({ mode: 'tool' })` 產生，`FormDesigner` 也來自正式元件庫，作為自舉驗證。

- 左側欄位可改欄位名、顯示名、圖示／輸入元件，也可新增、刪除欄位。
- 欄位可拖到右側畫布；畫布上的元件可拖曳、以滑鼠或鍵盤調整位置與長寬。
- 未提供連線字串時，目標固定為本地 SQLite `data/<application_id>.db`。
- 有連線字串時，須明確選擇 provider；密碼只留在 Studio controller 記憶體，預設不進 design JSON 或下載 bundle。只有明確勾選時才輸出至 `backend/appsettings.Development.json`。
- Studio 與 CLI 都只生成／預覽，不會連線或寫入資料庫。套用生成的 SQL 前，仍須依資料表、欄位、來源、寫入規則、例外處理及 rollback 計畫另行確認。

CLI 範例：

```bash
node tools/form-application-studio/generate.mjs --schema tools/form-application-studio/sample-schema.json --output ./out/form-app
```

若使用外部資料庫，連線字串必須經環境變數傳入；參數與產物契約見 [工具 README](tools/form-application-studio/README.md)。驗收執行 `npm run test:form-designer:all`。
