# DataTable

資料表格元件，提供排序、分頁、選取、自訂渲染等功能。

## API

### Constructor

```js
new DataTable(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.columns` | `Array` | `[]` | 欄位定義 `[{key, title, width?, sortable?, render?, align?}]` |
| `options.data` | `Array` | `[]` | 資料陣列 |
| `options.rowKey` | `string` | `'id'` | 資料唯一鍵欄位名稱 |
| `options.pagination` | `boolean` | `true` | 是否啟用分頁 |
| `options.pageSize` | `number` | `20` | 每頁筆數 |
| `options.selectable` | `boolean` | `false` | 是否可選取 |
| `options.multiSelect` | `boolean` | `true` | 是否可多選 |
| `options.striped` | `boolean` | `true` | 斑馬紋 |
| `options.bordered` | `boolean` | `false` | 邊框 |
| `options.hoverable` | `boolean` | `true` | 懸停效果 |
| `options.emptyText` | `string` | `'暫無資料'` | 無資料文字 |
| `options.loading` | `boolean` | `false` | 載入中狀態 |
| `options.onSort` | `Function` | `null` | 排序回調 `(key, order)` |
| `options.onSelect` | `Function` | `null` | 選取回調 `(selectedRows)` |
| `options.onRowClick` | `Function` | `null` | 行點擊回調 `(row, index)` |
| `options.onPageChange` | `Function` | `null` | 分頁回調 `(page, pageSize)` |

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `setData(data)` | `this` | 設定資料並重置分頁/選取 |
| `getData()` | `Array` | 取得目前資料 |
| `getSelectedRows()` | `Array` | 取得已選取的資料列 |
| `clearSelection()` | `this` | 清除選取 |
| `setLoading(boolean)` | `this` | 設定載入狀態 |
| `refresh()` | `this` | 重新渲染表格 |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

### 依賴

- `Pagination` — 分頁功能
- `LoadingSpinner` — 載入指示

## 使用範例

```js
import { DataTable } from './DataTable.js';

const table = new DataTable({
    columns: [
        { key: 'name', title: '姓名', sortable: true },
        { key: 'age', title: '年齡', sortable: true, align: 'right' },
        { key: 'action', title: '操作', render: (_, row) => `<button>編輯</button>` }
    ],
    data: [{ id: 1, name: '張三', age: 28 }],
    pageSize: 10,
    selectable: true,
    onRowClick: (row) => console.log(row)
});
table.mount('#app');
```

## XSS 安全協議（renderCell / render）

DataTable 預設對 `render` / `renderCell` 的回傳值進行 HTML 跳脫（escapeHtml），防止 XSS。

若需輸出原始 HTML（如按鈕、圖示），必須使用 `raw()` 包裝：

```js
import { raw } from '../../core/Security.js';

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
- 在 `raw()` 內部使用使用者資料時，務必用 `esc()` / `escAttr()` 跳脫

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
