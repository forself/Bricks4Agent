# ListInput

列表輸入基底元件，管理可新增、刪除、拖曳排序的項目列表，支援 fields schema 自動產生欄位或自訂 renderItem。

## API

### Constructor

```js
new ListInput(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.title` | `string` | `''` | 標題 |
| `options.minItems` | `number` | `0` | 最小項目數 |
| `options.maxItems` | `number` | `10` | 最大項目數 |
| `options.addButtonText` | `string` | `'新增項目'` | 新增按鈕文字 |
| `options.fields` | `Array` | `null` | 欄位 schema `[{name, type, label, placeholder, flex, width, options, required, min, max, maxLength, accept}]`，支援 type: `text`、`select`、`number`、`checkbox`、`email`、`tel`、`date`、`image`、`file` |
| `options.renderItem` | `Function` | `null` | 自訂渲染回調 `(container, index, value, onChange)`，優先於 fields |
| `options.onItemChange` | `Function` | `null` | 單項變更回調 `(index, value)` |
| `options.onChange` | `Function` | `null` | 列表變更回調 `(items)` |

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至 DOM 容器，回傳 `this` |
| `getValues()` | 回傳項目陣列 |
| `setValues(items)` | 設定項目陣列，重新渲染 |

### 內建功能

- 拖曳排序（drag & drop）
- 上移/下移按鈕
- 項目計數器（`n / maxItems`）
- CSV 範本下載（使用 fields schema 時自動提供）

## 使用範例

```js
import { ListInput } from './input/ListInput/index.js';

// 方式一：使用 fields schema
const list = new ListInput({
    title: '聯絡人',
    minItems: 1,
    maxItems: 5,
    fields: [
        { name: 'name', type: 'text', label: '姓名', flex: 1 },
        { name: 'email', type: 'email', label: 'Email', flex: 1 },
        { name: 'role', type: 'select', label: '角色', options: ['主管', '員工'] }
    ],
    onChange: (items) => console.log(items)
});
list.mount('#container');

// 方式二：自訂 renderItem
const custom = new ListInput({
    renderItem: (container, index, value, onChange) => {
        const input = document.createElement('input');
        input.value = value || '';
        input.addEventListener('input', () => onChange(input.value));
        container.appendChild(input);
    }
});
custom.mount('#container');
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
