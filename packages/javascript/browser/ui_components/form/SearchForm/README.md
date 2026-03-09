# SearchForm 搜尋表單

自動產生搜尋表單，支援多種欄位類型（text/number/select/date/dateRange/checkbox）、展開收合、驗證。

## API

### Constructor

```javascript
import { SearchForm } from './SearchForm.js';

const form = new SearchForm({
    fields: [                       // 欄位定義陣列
        { key: 'name', label: '姓名', type: 'text', placeholder: '搜尋...', required: false, width: '' },
        { key: 'status', label: '狀態', type: 'select', options: [{value:'1',label:'啟用'}] },
        { key: 'amount', label: '金額', type: 'number' },
        { key: 'date', label: '日期', type: 'date' },
        { key: 'period', label: '期間', type: 'dateRange' },
        { key: 'active', label: '啟用', type: 'checkbox', placeholder: '僅顯示啟用' }
    ],
    values: {},                     // 初始值 { key: value }
    columns: 4,                     // 每行欄位數
    collapsible: true,              // 是否可收合
    visibleRows: 1,                 // 收合時顯示行數
    showReset: true,                // 顯示重設按鈕
    searchText: '搜尋',             // 搜尋按鈕文字
    resetText: '重設',              // 重設按鈕文字
    onSearch: (values) => {},       // 搜尋回調
    onReset: () => {},              // 重設回調
    onChange: (key, val, all) => {}  // 值變更回調
});
```

### 欄位類型常數

`SearchForm.FIELD_TYPES`: `TEXT`, `NUMBER`, `SELECT`, `DATE`, `DATE_RANGE`, `CHECKBOX`

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件（含子元件） |
| `getValues()` | 取得所有值物件 |
| `setValues(obj)` | 批次設定值 |
| `getValue(key)` | 取得單一值 |
| `setValue(key, value)` | 設定單一值 |
| `reset()` | 重設為初始值 |
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
