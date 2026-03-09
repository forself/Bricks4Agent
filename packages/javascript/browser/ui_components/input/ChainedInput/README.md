# ChainedInput

相依輸入基底元件，前一個欄位有值後下一個欄位才能操作，支援非同步載入選項。

## API

### Constructor

```js
new ChainedInput(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.fields` | `Array` | `[]` | 欄位定義陣列，每項含 `{name, type, label, placeholder, required, flex, minWidth, loadOptions, hideWhenDisabled, hideWhenEmpty, options, checkboxLabel, maxLength, min, max}` |
| `options.onChange` | `Function` | `null` | 值變更回調，參數為 `getValues()` 結果 |
| `options.layout` | `string` | `'horizontal'` | 布局方式：`'horizontal'` / `'vertical'` |
| `options.gap` | `string` | `'12px'` | 欄位間距 |

支援的欄位 type：`select`、`text`、`number`、`date`、`time`、`roc-date`、`checkbox`。

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至 DOM 容器（可傳選擇器字串或 Element），回傳 `this` |
| `getValues()` | 回傳所有欄位值的物件 `{fieldName: value}` |
| `setValues(values)` | 設定欄位值，`values` 為 `{fieldName: value}` 物件，非同步 |
| `validate()` | 驗證必填欄位，回傳錯誤陣列 `[{field, message}]` |
| `reset()` | 重置所有欄位 |
| `destroy()` | 移除元件 |

### 靜態方法

| 方法 | 說明 |
|------|------|
| `ChainedInput.bindDependency({source, target, condition?})` | 綁定兩個外部元素的相依關係，回傳 `{unbind}` |

## 使用範例

```js
import { ChainedInput } from './input/ChainedInput/index.js';

const input = new ChainedInput({
    fields: [
        { name: 'country', type: 'select', label: '國家', options: ['TW', 'US'] },
        { name: 'city', type: 'select', label: '城市', loadOptions: async (country) => fetchCities(country) },
        { name: 'detail', type: 'text', label: '地址' }
    ],
    onChange: (values) => console.log(values)
});
input.mount('#container');
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
