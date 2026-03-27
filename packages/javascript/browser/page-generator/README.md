# Page Generator

頁面生成器模組 - 根據頁面定義 (PageDefinition) 自動生成頁面程式碼，或在瀏覽器端動態渲染頁面。

## 目前狀態與邊界

這個模組目前同時承擔兩條路徑：

- 靜態程式碼生成
- 執行期動態渲染

但兩條路徑的成熟度與行為並不完全相同。

根據目前程式碼：

- `PageGenerator.js` 仍以字串模板產生頁面原始碼，不是 AST-based generator
- `ComponentPaths` 仍是明確寫在程式中的路徑映射
- `FieldResolver.js` 在遇到未知 `fieldType` 時會 `console.warn` 並回退成 text input
- `PageGenerator.js` 對某些未知型別則會輸出 TODO 註記

因此比較準確的理解是：

- 動態渲染路徑是目前較完整、較實際可用的執行期路徑
- 靜態生成路徑仍是實用工具，但不是通用程式編譯器
- 新舊格式與靜態/動態雙軌仍在同一模組內並存

閱讀這份 README 時，請把它當成「目前 API 與能力清單」，不要把它誤讀成「所有路徑都已同等成熟」。

## 模組目錄總覽

| 檔案 | 說明 |
|------|------|
| `PageDefinition.js` | 頁面定義規格（FieldTypes、PageTypes、驗證函式） |
| `PageGenerator.js` | 靜態程式碼生成器，從定義產生 BasePage 子類別原始碼 |
| `PageDefinitionAdapter.js` | 新舊格式雙向轉換器（AI 格式 ⟷ PageGenerator 格式） |
| `FieldResolver.js` | 欄位推論引擎，將 30 種 fieldType 映射為 UI 元件實例 |
| `TriggerEngine.js` | 聯動行為引擎，8 個內建原子行為 + 自訂擴充 |
| `DynamicFormRenderer.js` | 動態表單渲染器，組合 FormField + FormRow + TriggerEngine |
| `DynamicDetailRenderer.js` | 動態明細渲染器，以唯讀方式顯示 label + formatted value |
| `DynamicListRenderer.js` | 動態列表渲染器，組合 SearchForm + DataTable + Pagination |
| `DynamicPageRenderer.js` | 統一入口，依 mode 委派給 Form / Detail / List 渲染器 |
| `index.js` | 模組匯出入口 |

## 架構概念

```
┌─ 靜態生成（建置期）─────────────────────────────┐
│  PageDefinition (WHAT) + PageGenerator (HOW)    │
│  → 生成 BasePage 子類別 .js 原始碼               │
└─────────────────────────────────────────────────┘

┌─ 動態渲染（執行期）─────────────────────────────┐
│  PageDefinition JSON（新格式）                   │
│    ↓                                            │
│  PageDefinitionAdapter（格式轉換，可選）          │
│    ↓                                            │
│  DynamicPageRenderer (統一入口)                  │
│    ├─ form   → DynamicFormRenderer              │
│    │            ├─ FieldResolver (30 種推論)     │
│    │            └─ TriggerEngine (聯動行為)      │
│    ├─ detail → DynamicDetailRenderer            │
│    └─ list   → DynamicListRenderer              │
│                 ├─ SearchForm                    │
│                 ├─ DataTable                     │
│                 └─ Pagination                    │
└─────────────────────────────────────────────────┘

┌─ CLI 工具 ──────────────────────────────────────┐
│  tools/page-gen.js                              │
│  --mode static|dynamic|both                     │
│  --def <json-file>                              │
│  --validate / --list-types                      │
└─────────────────────────────────────────────────┘
```

## 安裝

此模組為 Bricks4Agent 的一部分，直接引用即可：

```javascript
// 靜態生成 API
import {
    PageGenerator,
    FieldTypes,
    PageTypes,
    validateDefinition,
    createDefaultDefinition
} from '@component-library/page-generator';

// 格式轉換
import { PageDefinitionAdapter } from '@component-library/page-generator';

// 動態渲染 API
import {
    DynamicPageRenderer,
    DynamicFormRenderer,
    DynamicDetailRenderer,
    DynamicListRenderer,
    FieldResolver,
    TriggerEngine
} from '@component-library/page-generator';
```

---

## 兩種頁面定義格式

本系統支援兩種頁面定義格式，透過 `PageDefinitionAdapter` 相互轉換：

### 新格式（AI 生成格式）

用於 `DynamicPageRenderer` 動態渲染引擎與 CLI 工具。支援完整的佈局控制、聯動觸發、搜尋/列表設定。

```json
{
  "page": {
    "pageName": "員工管理",
    "entity": "employee",
    "view": "adminList"
  },
  "fields": [
    {
      "fieldName": "name",
      "label": "姓名",
      "fieldType": "text",
      "formRow": 1,
      "formCol": 6,
      "listOrder": 2,
      "isRequired": true,
      "isReadonly": false,
      "isSearchable": true,
      "validation": { "maxLength": 50 }
    }
  ]
}
```

### 舊格式（PageGenerator 格式）

用於 `PageGenerator` 靜態程式碼生成器。

```javascript
{
    name: 'EmployeePage',
    type: 'form',
    fields: [
        { name: 'name', type: 'text', label: '姓名', required: true }
    ],
    api: { get: '/api/employee', create: '/api/employee' }
}
```

### 格式對照表

| 新格式 | 舊格式 | 說明 |
|--------|--------|------|
| `page.pageName` | `description` | 頁面中文名稱 |
| `page.entity` | 推斷為 `name`（加 "Page" 後綴） | 如 employee → EmployeePage |
| `page.view` | 推斷為 `type`（list/detail/form） | 如 adminList → list |
| `field.fieldName` | `field.name` | 欄位技術名稱 |
| `field.fieldType` | `field.type` | 欄位類型 |
| `field.isRequired` | `field.required` | 是否必填 |
| `field.defaultValue` | `field.default` | 預設值（新格式為字串） |
| `field.optionsSource.items` | `field.options` | 下拉選項 |
| `field.triggers` | `behaviors.fieldTriggers` | 聯動行為 |

---

## 各模組 API

### PageDefinition

頁面定義規格，包含常數與驗證工具函式。

**匯出常數：**

| 常數 | 說明 |
|------|------|
| `FieldTypes` | 欄位類型列舉（30 種，含基本、進階、複合輸入） |
| `PageTypes` | 頁面類型列舉（form, list, detail, dashboard） |
| `ComponentMapping` | fieldType → 元件名稱映射表 |
| `AvailableComponents` | 可用元件清單（spa / packages） |

**匯出函式：**

| 函式 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `validateDefinition(definition)` | `PageDefinition` | `{ valid, errors }` | 驗證頁面定義是否有效 |
| `inferComponents(fields)` | `FieldDef[]` | `string[]` | 從欄位推斷需要的元件 |
| `createDefaultDefinition(name, type)` | `string, string` | `PageDefinition` | 建立預設頁面定義 |

---

### PageDefinitionAdapter

新舊格式雙向轉換器。所有方法均為靜態方法，無需實例化。

```javascript
import { PageDefinitionAdapter } from './PageDefinitionAdapter.js';

// 新格式 → 舊格式（供 PageGenerator 使用）
const oldDef = PageDefinitionAdapter.toOldFormat(newDefinition);

// 舊格式 → 新格式（供 DynamicPageRenderer 使用）
const newDef = PageDefinitionAdapter.toNewFormat(oldDefinition);
```

**靜態方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `toOldFormat(newDef)` | `Object` | `Object` | 新格式 → 舊格式 |
| `toNewFormat(oldDef)` | `Object` | `Object` | 舊格式 → 新格式 |

**轉換細節：**

- `page.entity` → PascalCase + "Page" 作為 `name`（例如 employee → EmployeePage）
- `page.view` 含 "list" → `type: 'list'`，含 "detail" → `type: 'detail'`，其餘 → `type: 'form'`
- `fieldType: 'multiselect'` 在舊格式中映射為 `type: 'select'`（舊 PageGenerator 不區分）
- `optionsSource.items` → `options` 陣列（static 型）
- `triggers` 從各欄位收集至 `behaviors.fieldTriggers`
- `defaultValue` 自動進行字串 ⟷ 原生型別轉換（"true" ⟷ `true`）

---

### PageGenerator

靜態程式碼生成器，從頁面定義產出完整的 BasePage 子類別原始碼。

```javascript
const generator = new PageGenerator(options);
```

**Constructor 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `baseImportPath` | `string` | `'../core/BasePage.js'` | BasePage 的 import 路徑 |

**公開方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `generate(definition)` | `PageDefinition` | `{ code: string, errors: string[] }` | 生成頁面程式碼 |

---

### TriggerEngine

聯動行為執行引擎。管理欄位間的互動邏輯，每個 action 為原子操作，可自由組合。

```javascript
const engine = new TriggerEngine();
```

**Constructor 參數：** 無

**內建 8 個原子行為：**

| Action | 說明 | params |
|--------|------|--------|
| `clear` | 清空目標值 | - |
| `setValue` | 設定目標值 | `{ value }` 或 `{ fromField }` |
| `show` | 顯示目標欄位 | - |
| `hide` | 隱藏目標欄位 | - |
| `setReadonly` | 設定唯讀狀態 | `{ value: boolean }` |
| `setRequired` | 設定必填狀態 | `{ value: boolean }` |
| `reload` | 觸發目標元件重新載入 | - |
| `reloadOptions` | 重新載入目標欄位的下拉選項（API） | - |

**公開方法：**

| 方法 | 參數 | 說明 |
|------|------|------|
| `registerAction(name, handler)` | `string, Function` | 註冊自訂原子行為 |
| `bind(fieldDefinitions, fieldInstances)` | `Array, Map` | 綁定欄位定義與元件實例，啟動觸發監聽 |
| `execute(actionName, sourceFieldName, targetFieldName, params)` | `string, string, string, Object` | 手動執行單一原子行為 |
| `unbind()` | - | 解除所有綁定 |
| `destroy()` | - | 銷毀引擎（unbind + 清除註冊） |

**觸發時機（trigger.on）：** `change`、`check`、`uncheck`、`upload`

---

### FieldResolver

欄位推論引擎。將 fieldType 映射為實際 UI 元件實例，並包裝為 FormField。

```javascript
const resolver = new FieldResolver();
```

**Constructor 參數：** 無

**支援的 30 種 fieldType 映射：**

| 分類 | fieldType | 對應元件 |
|------|-----------|----------|
| **基本** | `text` | TextInput (type=text) |
| | `email` | TextInput (type=email) |
| | `password` | TextInput (type=password) |
| | `number` | NumberInput |
| | `textarea` | 原生 textarea 包裝 |
| **日期時間** | `date` | DatePicker |
| | `time` | TimePicker |
| | `datetime` | DateTimeInput |
| **選擇** | `select` | Dropdown (searchable) |
| | `multiselect` | MultiSelectDropdown |
| | `checkbox` | Checkbox |
| | `toggle` | ToggleSwitch |
| | `radio` | Radio.createGroup |
| **進階** | `color` | ColorPicker |
| | `image` | ImageViewer |
| | `file` | BatchUploader |
| | `richtext` | WebTextEditor |
| | `canvas` | DrawingBoard |
| **服務** | `geolocation` | GeolocationService |
| | `weather` | WeatherService |
| **複合輸入** | `address` | AddressInput（縣市/鄉鎮/地址聯動） |
| | `addresslist` | AddressListInput（多筆地址） |
| | `chained` | ChainedInput（通用聯動下拉） |
| | `list` | ListInput（動態列表輸入） |
| | `personinfo` | PersonInfoList（人員資訊列表） |
| | `phonelist` | PhoneListInput（電話列表） |
| | `socialmedia` | SocialMediaList（社群媒體列表） |
| | `organization` | OrganizationInput（組織層級輸入） |
| | `student` | StudentInput（學生資訊輸入） |
| **隱藏** | `hidden` | 原生 hidden input 包裝 |

**公開方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `preload()` | - | `Promise<void>` | 預載入所有元件模組（必須在 resolve 前呼叫） |
| `registerComponent(name, factory)` | `string, Function` | - | 註冊自訂元件工廠 |
| `resolve(fieldDef)` | `Object` | `{ component, formField }` | 解析單一欄位定義為元件 + FormField |
| `resolveAll(fieldDefs)` | `Array` | `Map<string, { component, formField }>` | 批次解析所有欄位 |

---

### DynamicFormRenderer

動態表單渲染器。從頁面定義 JSON 自動建構表單，含欄位解析、行列排版、聯動觸發、驗證。

```javascript
const form = new DynamicFormRenderer(options);
await form.init();
```

**Constructor 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `definition` | `Object` | `null` | 頁面定義 JSON（含 page + fields） |
| `onSave` | `Function` | `null` | 儲存回調 `(values) => void` |
| `onCancel` | `Function` | `null` | 取消回調 `() => void` |
| `showButtons` | `boolean` | `true` | 是否顯示底部按鈕 |

**公開方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `init()` | - | `Promise<this>` | 初始化（預載入元件 + 建構 DOM） |
| `getValues()` | - | `Object` | 取得所有欄位值 `{ fieldName: value }` |
| `setValues(data)` | `Object` | - | 設定欄位值 |
| `validate()` | - | `boolean` | 驗證所有必填欄位 |
| `mount(container)` | `string \| Element` | `this` | 掛載到容器 |
| `destroy()` | - | - | 銷毀渲染器 |

---

### DynamicDetailRenderer

動態明細渲染器。從頁面定義 JSON 產生唯讀明細檢視，不實例化 form 元件，直接渲染 label + 格式化值。

```javascript
const detail = new DynamicDetailRenderer(options);
```

**Constructor 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `definition` | `Object` | `null` | 頁面定義 JSON |
| `data` | `Object` | `{}` | 資料物件 `{ fieldName: value }` |
| `onBack` | `Function` | `null` | 返回按鈕回調 |
| `onEdit` | `Function` | `null` | 編輯按鈕回調 |

**公開方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `setData(data)` | `Object` | - | 設定/更新資料（自動重新渲染） |
| `mount(container)` | `string \| Element` | `this` | 掛載到容器 |
| `destroy()` | - | - | 銷毀渲染器 |

**支援的格式化類型：**

| fieldType | 格式化方式 |
|-----------|-----------|
| `date` | YYYY/MM/DD 日期字串 |
| `datetime` | YYYY/MM/DD HH:MM |
| `checkbox` / `toggle` | 是/否 色彩標籤 |
| `select` / `radio` | 顯示 label（非 value） |
| `multiselect` | 多個藍色標籤 |
| `color` | 色塊 + 色碼 |
| `image` | 縮圖預覽 |
| `password` | 遮罩（••••••••） |
| `richtext` | HTML 內容截斷顯示 |
| `canvas` | 文字提示（繪圖內容） |
| `geolocation` | 地址短名或座標 |
| `weather` | 圖示 + 溫度 + 描述 |
| `address` | 縣市 + 鄉鎮 + 地址 |
| `addresslist` / `phonelist` / `socialmedia` / `personinfo` | 編號列表 |
| `organization` | 層級路徑（/ 分隔） |
| `student` | 學生/非學生 + 學校名 |
| `chained` / `list` | 編號列表 |

---

### DynamicListRenderer

動態列表渲染器。從頁面定義 JSON 組合 SearchForm + DataTable + Pagination 三件元件。

```javascript
const list = new DynamicListRenderer(options);
await list.init();
```

**Constructor 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `definition` | `Object` | `null` | 頁面定義 JSON |
| `onSearch` | `Function` | `null` | 搜尋回調 `(filters, page, pageSize) => void` |
| `onAction` | `Function` | `null` | 操作回調 `(action, row) => void`（action: view/edit/delete） |
| `pageSize` | `number` | `20` | 每頁筆數 |

**公開方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `init()` | - | `Promise<this>` | 初始化（載入依賴元件 + 建構 DOM） |
| `setData(rows, total)` | `Array, number` | - | 設定列表資料與總筆數 |
| `mount(container)` | `string \| Element` | `this` | 掛載到容器 |
| `destroy()` | - | - | 銷毀渲染器 |

**欄位定義中的列表相關屬性：**
- `isSearchable: true` — 欄位出現在搜尋區
- `listOrder: number` — 欄位在表格中的排序（> 0 才顯示）

---

### DynamicPageRenderer

統一入口渲染器。依 mode 自動委派給對應的 Form / Detail / List 渲染器。

```javascript
const page = new DynamicPageRenderer(options);
await page.init();
```

**Constructor 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `definition` | `Object` | `null` | 頁面定義 JSON |
| `mode` | `string` | `'form'` | 渲染模式：`'form'` / `'detail'` / `'list'` |
| `data` | `Object` | `null` | 資料（detail/form 編輯時使用） |
| `onSave` | `Function` | `null` | 儲存回調（form 模式） |
| `onCancel` | `Function` | `null` | 取消回調（form 模式） |
| `onSearch` | `Function` | `null` | 搜尋回調（list 模式） |
| `onAction` | `Function` | `null` | 操作回調（list 模式） |
| `onBack` | `Function` | `null` | 返回回調（detail 模式） |
| `onEdit` | `Function` | `null` | 編輯回調（detail 模式） |
| `pageSize` | `number` | `20` | 每頁筆數（list 模式） |

**公開方法：**

| 方法 | 參數 | 回傳 | 說明 |
|------|------|------|------|
| `init()` | - | `Promise<this>` | 初始化並建構渲染器 |
| `getRenderer()` | - | `Renderer` | 取得內部渲染器實例 |
| `switchMode(mode, data)` | `string, Object` | `Promise<this>` | 切換模式（銷毀舊渲染器，建立新的） |
| `mount(container)` | `string \| Element` | `this` | 掛載到容器 |
| `destroy()` | - | - | 銷毀渲染器 |

---

## 使用範例

### 靜態生成頁面程式碼

```javascript
import { PageGenerator, validateDefinition, FieldTypes, PageTypes } from './index.js';

const definition = {
    name: 'ContactFormPage',
    type: PageTypes.FORM,
    description: '聯絡表單',
    components: ['DatePicker', 'ToastPanel'],
    fields: [
        { name: 'title', type: FieldTypes.TEXT, label: '標題', required: true },
        { name: 'date', type: FieldTypes.DATE, label: '日期' },
        { name: 'content', type: FieldTypes.TEXTAREA, label: '內容', required: true }
    ],
    api: { create: '/api/contacts', get: '/api/contacts' }
};

const validation = validateDefinition(definition);
if (!validation.valid) {
    console.error('定義無效:', validation.errors);
} else {
    const generator = new PageGenerator({ baseImportPath: '../../core/BasePage.js' });
    const result = generator.generate(definition);
    console.log(result.code);
}
```

### 使用 PageDefinitionAdapter 轉換格式

```javascript
import { PageDefinitionAdapter } from './PageDefinitionAdapter.js';

// AI 生成的新格式定義
const aiDefinition = {
    page: { pageName: '員工管理', entity: 'employee', view: 'adminList' },
    fields: [
        { fieldName: 'name', label: '姓名', fieldType: 'text', formRow: 1, formCol: 6,
          listOrder: 1, isRequired: true, isReadonly: false, isSearchable: true },
        { fieldName: 'email', label: '信箱', fieldType: 'email', formRow: 1, formCol: 6,
          listOrder: 2, isRequired: true, isReadonly: false, isSearchable: true }
    ]
};

// 轉為舊格式 → 交給 PageGenerator 生成靜態 .js
const oldDef = PageDefinitionAdapter.toOldFormat(aiDefinition);
// oldDef.name === 'EmployeePage'
// oldDef.type === 'list'

// 反向轉換
const roundTrip = PageDefinitionAdapter.toNewFormat(oldDef);
```

### 動態渲染（統一入口）

```javascript
import { DynamicPageRenderer } from './index.js';

// 頁面定義 JSON（通常從 API 或 JSON 檔案載入）
const definition = {
    fields: [
        { fieldName: 'name', fieldType: 'text', label: '姓名', isRequired: true, formRow: 1, formCol: 6 },
        { fieldName: 'email', fieldType: 'email', label: '信箱', isRequired: true, formRow: 1, formCol: 6 },
        { fieldName: 'role', fieldType: 'select', label: '角色', formRow: 2, formCol: 6,
          optionsSource: { type: 'static', items: [
            { value: 'admin', label: '管理員' },
            { value: 'user', label: '使用者' }
          ]}
        },
        { fieldName: 'isActive', fieldType: 'toggle', label: '啟用', formRow: 2, formCol: 6,
          triggers: [
            { on: 'uncheck', target: 'role', action: 'hide' },
            { on: 'check', target: 'role', action: 'show' }
          ]
        }
    ]
};

// 表單模式
const page = new DynamicPageRenderer({
    definition,
    mode: 'form',
    onSave: (values) => console.log('儲存:', values),
    onCancel: () => history.back()
});
await page.init();
page.mount('#app');

// 切換到明細模式
await page.switchMode('detail', { name: '王小明', email: 'wang@example.com' });

// 切換到列表模式
await page.switchMode('list');
```

### 動態渲染（個別使用）

```javascript
import { DynamicFormRenderer } from './index.js';

const form = new DynamicFormRenderer({
    definition,
    onSave: (values) => fetch('/api/save', {
        method: 'POST',
        body: JSON.stringify(values)
    })
});
await form.init();
form.mount('#form-container');

// 編輯模式：填入既有資料
form.setValues({ name: '王小明', email: 'wang@example.com' });

// 取值
const data = form.getValues();

// 驗證
if (form.validate()) {
    // 通過驗證
}
```

### 使用複合輸入元件

```javascript
const definition = {
    fields: [
        // 地址輸入（含縣市/鄉鎮聯動）
        { fieldName: 'homeAddress', fieldType: 'address', label: '住家地址',
          formRow: 1, isRequired: true },

        // 多筆電話
        { fieldName: 'phones', fieldType: 'phonelist', label: '聯絡電話',
          formRow: 2, validation: { maxItems: 5 } },

        // 組織層級
        { fieldName: 'org', fieldType: 'organization', label: '所屬組織',
          formRow: 3 },

        // 學生資訊
        { fieldName: 'student', fieldType: 'student', label: '學生資訊',
          formRow: 4 },

        // 社群媒體列表
        { fieldName: 'social', fieldType: 'socialmedia', label: '社群帳號',
          formRow: 5 }
    ]
};
```

### 自訂 TriggerEngine 行為

```javascript
import { TriggerEngine } from './index.js';

const engine = new TriggerEngine();

// 註冊自訂行為
engine.registerAction('highlight', (source, target, params) => {
    target.formField.element.style.backgroundColor = params?.color || '#fff3cd';
});
```

---

## CLI 工具（page-gen）

提供命令列介面操作頁面定義。詳見 `tools/page-gen.js`。

```bash
# 驗證定義
node tools/page-gen.js --validate --def employee.json

# 生成靜態 .js 頁面
node tools/page-gen.js --def employee.json --mode static --output ./output/

# 輸出動態渲染用 JSON
node tools/page-gen.js --def employee.json --mode dynamic --output ./output/

# 兩者都生成
node tools/page-gen.js --def employee.json --mode both --output ./output/

# 列出所有 fieldType
node tools/page-gen.js --list-types

# stdin 管道模式（AI 代理用）
cat employee.json | node tools/page-gen.js --mode static --output ./output/
```

所有輸出皆為 JSON 格式（stdout），錯誤訊息輸出至 stderr。

---

## Demo 進入點

```
demos/page-generator/DynamicPage.html           — 動態渲染三模式展示
demos/page-generator/AdapterDemo.html           — 格式轉換展示
demos/page-generator/CompositeFieldsDemo.html   — 複合元件展示
```

## 範例

查看 `examples/` 目錄：

- `EmployeeDefinition.js` — 員工管理（新格式，完整功能展示）
- `PersonnelDefinition.js` — 人事系統（新格式，複合輸入元件展示）
- `DiaryEditorDefinition.js` — 日記編輯器（舊格式）
- `ContactFormDefinition.js` — 聯絡表單（舊格式）
- `test-generator.js` — 靜態生成測試
- `test-all.js` — 完整測試腳本

執行測試：
```bash
node examples/test-all.js
```

---

## 欄位類型 (FieldTypes) — 完整 30 種

### 基本類型

| fieldType | 說明 | 對應元件 |
|-----------|------|----------|
| `text` | 單行文字 | TextInput |
| `email` | 電子郵件 | TextInput (type=email) |
| `password` | 密碼 | TextInput (type=password) |
| `number` | 數字 | NumberInput |
| `textarea` | 多行文字 | 原生 textarea |

### 日期時間

| fieldType | 說明 | 對應元件 |
|-----------|------|----------|
| `date` | 日期 | DatePicker |
| `time` | 時間 | TimePicker |
| `datetime` | 日期時間 | DateTimeInput |

### 選擇類型

| fieldType | 說明 | 對應元件 |
|-----------|------|----------|
| `select` | 單選下拉 | Dropdown |
| `multiselect` | 多選下拉 | MultiSelectDropdown |
| `checkbox` | 核取方塊 | Checkbox |
| `toggle` | 開關 | ToggleSwitch |
| `radio` | 單選按鈕組 | Radio |

### 進階類型

| fieldType | 說明 | 對應元件 |
|-----------|------|----------|
| `color` | 顏色選擇 | ColorPicker |
| `image` | 圖片顯示 | ImageViewer |
| `file` | 檔案上傳 | BatchUploader |
| `richtext` | 富文本 | WebTextEditor |
| `canvas` | 畫布 | DrawingBoard |

### 服務類型

| fieldType | 說明 | 對應元件 |
|-----------|------|----------|
| `geolocation` | 地理位置 | GeolocationService |
| `weather` | 天氣 | WeatherService |

### 複合輸入元件

| fieldType | 說明 | 對應元件 | 繼承關係 |
|-----------|------|----------|----------|
| `address` | 地址（縣市/鄉鎮/地址聯動） | AddressInput | ← ChainedInput |
| `addresslist` | 多筆地址列表 | AddressListInput | ← ListInput |
| `chained` | 通用聯動下拉 | ChainedInput | 基底 |
| `list` | 動態列表輸入 | ListInput | 基底 |
| `personinfo` | 人員資訊列表 | PersonInfoList | ← ListInput |
| `phonelist` | 電話列表 | PhoneListInput | ← ListInput |
| `socialmedia` | 社群媒體列表 | SocialMediaList | ← ListInput |
| `organization` | 組織層級（四級下拉） | OrganizationInput | ← ChainedInput |
| `student` | 學生資訊（含學校） | StudentInput | ← ChainedInput |

### 隱藏

| fieldType | 說明 | 對應元件 |
|-----------|------|----------|
| `hidden` | 隱藏欄位 | 原生 hidden input |

---

## 頁面類型 (PageTypes)

| 類型 | 說明 |
|------|------|
| `FORM` | 表單頁面（新增/編輯） |
| `LIST` | 列表頁面 |
| `DETAIL` | 詳情頁面（唯讀） |
| `DASHBOARD` | 儀表板 |

---

## triggers 格式

每個欄位可定義 `triggers` 陣列，組合原子行為實現聯動：

```json
[
  { "on": "change", "target": "district", "action": "reloadOptions" },
  { "on": "change", "target": "district", "action": "clear" }
]
```

**合法的 on 值：** `change`、`check`、`uncheck`、`upload`

**合法的 action 值：** `reloadOptions`、`show`、`hide`、`setReadonly`、`setRequired`、`reload`、`setValue`、`clear`

### 常見聯動模式

**連動下拉（縣市→鄉鎮）：**
```json
{ "fieldName": "city", "triggers": [
  { "on": "change", "target": "district", "action": "reloadOptions" },
  { "on": "change", "target": "district", "action": "clear" }
]}
```

**勾選後顯示/必填：**
```json
{ "fieldName": "hasDeadline", "triggers": [
  { "on": "check", "target": "deadline", "action": "show" },
  { "on": "check", "target": "deadline", "action": "setRequired", "params": { "value": true } },
  { "on": "uncheck", "target": "deadline", "action": "hide" },
  { "on": "uncheck", "target": "deadline", "action": "setRequired", "params": { "value": false } }
]}
```

---

## optionsSource 格式

```json
// 靜態選項
{ "type": "static", "items": [{"value": "hr", "label": "人力資源部"}] }

// API 動態載入
{ "type": "api", "endpoint": "/api/data", "params": { "action": "options", "entity": "department" } }

// API 連動載入（需搭配 dependsOn）
{ "type": "api", "endpoint": "/api/data", "params": { "action": "options", "entity": "district" }, "parentField": "city" }
```

---

## 注意事項

1. 動態渲染的 `DynamicFormRenderer` 和 `DynamicListRenderer` 須呼叫 `await init()` 才能使用（需預載入元件模組）
2. `DynamicDetailRenderer` 為同步建構，不需 `init()`
3. 生成的靜態程式碼包含 TODO 註解，表示需要手動實作的部分
4. 所有元件必須來自 Bricks4Agent
5. 使用 `esc()` 和 `escAttr()` 防止 XSS
6. `PageDefinitionAdapter.toOldFormat()` 會自動從 `page.entity` 推導 PascalCase 頁面名稱
7. 複合輸入元件（address, phonelist 等）支援 `validation.maxItems` / `validation.minItems` 限制筆數
