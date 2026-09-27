# MultiSelectDropdown 多選下拉選單

多選下拉選單元件，支援搜尋篩選、選中置頂、展開 Modal 全選操作、最大/最小選取數量限制。

## API

### Constructor

```javascript
import { MultiSelectDropdown } from './MultiSelectDropdown.js';

const msd = new MultiSelectDropdown({
    items: [                        // 選項 [{value, label, disabled?}]
        { value: '1', label: '選項一' },
        { value: '2', label: '選項二', disabled: true }
    ],
    placeholder: '請選擇',          // 預設提示文字
    values: ['1'],                  // 初始已選值陣列
    onChange: (values, items) => {}, // 變更回調
    size: 'medium',                 // 'small' | 'medium' | 'large'
    disabled: false,                // 停用
    width: '100%',                  // 寬度（預設跟隨容器）
    emptyText: '無符合項目',         // 無結果文字
    modalTitle: '選擇項目',          // Modal 標題
    maxCount: Infinity,             // 最大可選數量
    minCount: 0                     // 最小選取數量
});
```

### 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件 |
| `getValues()` | 回傳已選值陣列 |
| `setValues(arr)` | 設定已選值 |
| `setItems(arr)` | 更新選項列表（自動清除無效已選值） |
| `clear()` | 清除所有已選 |
| `open()` / `close()` | 開關下拉選單 |
| `toggle()` | 切換下拉選單開關 |
| `setDisabled(bool)` | 設定停用狀態 |
| `setError(msg, { display })` / `clearError()` | 標示 / 清除欄位錯誤：紅框、`aria-invalid` 與錯誤文字；`display: false` 只標示狀態、不顯示文字 |
| `show()` / `hide()` | 顯示 / 隱藏元件 |

### 屬性

- `element` — 根 DOM 元素

- `selectedValues` — Set，目前已選值

## 使用範例

```javascript
import { MultiSelectDropdown } from './MultiSelectDropdown.js';

const msd = new MultiSelectDropdown({
    items: [
        { value: 'tw', label: '台灣' },
        { value: 'jp', label: '日本' },
        { value: 'kr', label: '韓國' }
    ],
    values: ['tw'],
    maxCount: 2,
    onChange: (vals) => console.log('已選:', vals)
});
msd.mount('#container');
```

## Demo

`demo.html`（同目錄）
