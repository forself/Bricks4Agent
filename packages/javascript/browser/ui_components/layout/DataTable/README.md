# DataTable

資料表格元件，提供排序、分頁、選取、自訂渲染等功能。

## API

### Constructor

```js
new DataTable(config?)
// 或兩參數模式：
new DataTable(containerElement, config)
```

| 參數 | 型別 | 預設值 | 說明 |
|---|---|---|---|
| `config.columns` | `Array` | `[]` | 欄位定義，支援三種格式：標準 `[{name, label, options: {customBodyRender, display, setCellProps}}]`、Audit `[{key, title, width?, sortable?, render?, hidden?, html?}]`（data 為物件陣列）、Search `[{title, visible?, width?}]`（data 為 2D 陣列） |
| `config.data` | `Array` | `[]` | 資料陣列（物件陣列或 2D 陣列，依 columns 格式） |
| `config.container` | `HTMLElement` | `null` | 容器元素，省略時可稍後 `mount()` |
| `config.title` | `string` | `''` | 表格標題 |
| `config.variant` | `string` | `'default'` | 主題：`'default'` / `'search'` |
| `config.pagination` | `boolean` | `true` | 設為 `false` 停用分頁 |
| `config.pageSize` | `number` | `10` | 每頁筆數（未指定時取 `rowsPerPageOptions` 第一項） |
| `config.pageSizeOptions` | `number[]` | `[10,20,100,500,1000]` | 每頁筆數選項（同 `rowsPerPageOptions`） |
| `config.emptyText` | `string` | Locale 文字 | 無資料文字 |
| `config.striped` | `boolean` | — | 斑馬紋（未設定時依主題 CSS） |
| `config.hoverable` | `boolean` | — | 懸停效果（未設定時依主題 CSS） |
| `config.selectableRows` | `string` | `'multiple'` | 行選取模式：`'multiple'` / `'single'` / `'none'`（也可放在 `config.options` 內） |
| `config.sortOrder` | `Object` | `null` | 初始排序 `{name, direction}` |
| `config.options` | `Object` | `{}` | 進階設定：`textLabels`、`customToolbar`、`customToolbarSelect`、`rowsPerPageOptions`、`onRender(element, dt)`、`onRowSelectionChange(_, allSelected, selectedIndices)` |

### 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `setData(data)` | `void` | 設定資料並重置分頁/選取，重新渲染 |
| `getData()` | `Array` | 取得目前資料（正規化後的 2D 陣列） |
| `getSelectedRows()` | `Array` | 取得已選取列的 dataIndex 陣列 |
| `setSelectedRows(indices)` | `void` | 設定已選取列並重新渲染 |
| `render()` | `void` | 重新渲染表格至 container |
| `mount(container)` | `this` | 掛載至容器（CSS 選擇器或 DOM 元素） |
| `destroy()` | `void` | 銷毀元件 |

### 選取變更的更新方式

使用者點擊行 checkbox 或表頭全選時，DataTable **只定點同步選取相關 DOM**（列的 `--selected` / `--even` class、行 checkbox、表頭全選狀態），不重建整張表：

- `onRender` **不會**因為純選取變更而重發

- 儲存格內既有的元件實例與事件監聽器不會被銷毀重建，外部加在 `<tr>` 上的其他 class 也會保留

- `onRowSelectionChange` 仍照常發出

以下情況維持完整重繪（工具列內容依賴選取狀態，`onRender` 會再次發出）：

- 設定了 `options.customToolbarSelect`

- `options.customToolbar` 傳入的是函式（可能讀取選取狀態）

- 由程式呼叫 `setSelectedRows(indices)`

排序結果在單次渲染流程內共用（工具列、事件綁定重複取用時免重算），流程結束即清除；渲染流程外的呼叫維持即時重算。

### 具名匯出

- `linkCell(text, href, options?)` — 產生連結儲存格（內部以 `Link` 元件 hydrate）

- `badgeCell(text, options?)` — 產生徽章儲存格（內部以 `Badge` 元件 hydrate）

### 依賴

- `Link`、`Badge` — `linkCell()` / `badgeCell()` 儲存格渲染

- `utils/security.js` — `escapeHtml` / `escapeAttr` / `raw`

## 使用範例

```js
import { DataTable } from './DataTable.js';

const table = new DataTable({
    columns: [
        { key: 'name', title: '姓名' },
        { key: 'age', title: '年齡' },
        { key: 'action', title: '操作', render: (_, row) => `<button>編輯</button>` }
    ],
    data: [{ id: 1, name: '張三', age: 28 }],
    pageSize: 10,
    options: {
        selectableRows: 'multiple',
        onRowSelectionChange: (_, allSelected, indices) => console.log(indices)
    }
});
table.mount('#app');
```

## XSS 安全協議（render / customBodyRender）

DataTable 預設對 `render` / `customBodyRender` 的回傳值進行 HTML 跳脫（escapeHtml），防止 XSS。

若需輸出原始 HTML（如按鈕、圖示），必須使用 `raw()` 包裝：

```js
import { raw, escapeAttr } from '../../utils/security.js';

const table = new DataTable({
    columns: [
        // 純文字 — 自動跳脫，安全
        { key: 'name', title: '姓名' },

        // 需要 HTML — 必須用 raw() 明確標記
        { key: 'action', title: '操作',
          render: (_, row) => raw(`<button data-id="${escapeAttr(row.id)}">編輯</button>`)
        }
    ]
});
```

**規則**：
- `render` 回傳 `string` → 自動 HTML 跳脫（safe-by-default）
- `render` 回傳 `raw(html)` → 原樣輸出（開發者負責確保安全）
- 在 `raw()` 內部使用使用者資料時，務必用 `escapeHtml()` / `escapeAttr()` 跳脫
- 只有 `raw()` 產生的物件會被認可：標記以 `Symbol.for('bricks4agent.rawHtml')` 品牌識別，手寫的 `{ __html: '...' }`（或 API 回傳的同名 JSON 欄位）一律當成一般值跳脫

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
