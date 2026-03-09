# FormRow

表單列佈局元件，使用 12 欄 CSS Grid 系統管理同一列的多個 FormField。

## API

### Constructor

```js
new FormRow(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.fields` | `Array` | `[]` | FormField 實例陣列 |
| `options.gap` | `string` | `'16px'` | 欄間距 |

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `addField(formField)` | `this` | 新增 FormField |
| `getField(fieldName)` | `FormField\|null` | 依 fieldName 取得 FormField |
| `getFields()` | `FormField[]` | 取得所有 FormField |
| `mount(container)` | `this` | 掛載至容器（CSS 選擇器或 DOM 元素） |
| `destroy()` | `void` | 銷毀元件與所有子 FormField |

### 屬性

| 屬性 | 說明 |
|------|------|
| `element` | 根 DOM 元素（`div.form-row`） |

## 使用範例

```js
import { FormRow } from './FormRow.js';

const row = new FormRow({
    fields: [field1, field2, field3],
    gap: '12px'
});
row.mount('#form-container');
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
