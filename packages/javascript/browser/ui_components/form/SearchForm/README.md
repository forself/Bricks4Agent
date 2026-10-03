# SearchForm 搜尋表單

自動產生搜尋表單，支援多種欄位類型（text/number/select/multiselect/date/dateRange/checkbox）、展開收合、驗證。

## API

### Constructor

```javascript
import { SearchForm } from './SearchForm.js';

const form = new SearchForm({
    fields: [                       // 欄位定義陣列
        { key: 'name', label: '姓名', type: 'text', placeholder: '搜尋...', required: false, width: '' },
        { key: 'status', label: '狀態', type: 'select', options: [{value:'1',label:'啟用'}] },
        { key: 'tags', label: '標籤', type: 'multiselect', options: [{value:'a',label:'A'}] },
        { key: 'amount', label: '金額', type: 'number' },
        { key: 'date', label: '日期', type: 'date', format: 'western', min: '2024-01-01', max: '2024-12-31' }, // format / min / max 傳給 DatePicker
        { key: 'period', label: '期間', type: 'dateRange' },   // 值為 period_start / period_end 兩個 key
        { key: 'active', label: '啟用', type: 'checkbox', placeholder: '僅顯示啟用' }
        // 任一欄位可加 required: true 與 requiredMessage: '自訂必填訊息'
    ],
    values: {},                     // 初始值 { key: value }
    columns: 4,                     // 每行欄位數
    collapsible: true,              // 是否可收合
    visibleRows: 1,                 // 收合時顯示行數
    showReset: true,                // 顯示重設按鈕
    searchText: '搜尋',             // 搜尋按鈕文字
    resetText: '重設',              // 重設按鈕文字
    requiredMark: '*',              // 必填標記文字
    onSearch: (values) => {},       // 搜尋回調
    onReset: () => {},              // 重設回調
    onChange: (key, val, all) => {}, // 值變更回調
    onValidationError: (field) => {}, // 必填驗證失敗時回調（收到該欄位定義）
    markInvalidFields: false        // true 時驗證失敗也標示欄位元件（紅框、aria-invalid）
});
```

必填驗證失敗時，錯誤文字固定顯示在欄位下方。預設只有原本就有 `setError` 的元件（文字欄位的 TextInput）會收到錯誤，畫面與先前版本相同；設 `markInvalidFields: true` 時，每個欄位元件都以 `setError(msg, { display: false })` 標示錯誤狀態，文字不重複顯示。

### 欄位類型常數

`SearchForm.FIELD_TYPES`: `TEXT`, `NUMBER`, `SELECT`, `MULTISELECT`, `DATE`, `DATE_RANGE`, `CHECKBOX`

`dateRange` 欄位不會產生 `<key>` 本身的值，而是 `<key>_start` 與 `<key>_end` 兩個值。

### 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件（含子元件） |
| `getValues()` | 取得所有值物件 |
| `setValues(obj)` | 批次設定值 |
| `getValue(key)` | 取得單一值 |
| `setValue(key, value)` | 設定單一值 |
| `reset()` | 重設為 `options.values` 的初始值（不會套用欄位的 `defaultValue`；未在 `values` 中的欄位清為空） |
| `submit()` | 觸發搜尋 |

### 屬性

- `element` — 根 DOM 元素（`<form>`）

## 使用範例

```javascript
import { SearchForm } from './SearchForm.js';

const form = new SearchForm({
    fields: [
        { key: 'keyword', label: '關鍵字', type: 'text', placeholder: '搜尋...' },
        { key: 'status', label: '狀態', type: 'select', options: [
            { value: '1', label: '啟用' },
            { value: '0', label: '停用' }
        ]}
    ],
    columns: 3,
    onSearch: (values) => console.log('搜尋:', values)
});
form.mount('#search-container');
```

## Demo

`demo.html`（同目錄）
