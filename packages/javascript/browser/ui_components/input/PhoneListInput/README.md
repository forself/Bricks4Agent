# PhoneListInput

電話列表輸入元件，繼承 `ListInput`，每筆項目包含電話類型與號碼。

## API

### Constructor

```js
new PhoneListInput(options?)
```

繼承 `ListInput` 所有選項，預設值：

| 參數 | 預設值 | 說明 |
|------|--------|------|
| `title` | `'電話列表'` | 標題 |
| `minItems` | `1` | 最少項目數 |
| `maxItems` | `5` | 最多項目數 |
| `addButtonText` | `'新增電話'` | 新增按鈕文字 |

每筆項目欄位：

| 欄位 name | type | 說明 |
|-----------|------|------|
| `type` | `select` | 電話類型（手機/市話/公司/傳真） |
| `number` | `tel` | 電話號碼 |

### 方法

繼承 `ListInput` 所有方法：`mount(container)`、`getValues()`、`setValues(items)`。

## 使用範例

```js
import { PhoneListInput } from './input/PhoneListInput/index.js';

const phones = new PhoneListInput({
    maxItems: 3,
    onChange: (items) => console.log(items)
});
phones.mount('#container');

// 取得所有電話
const data = phones.getValues();
// [{type: '手機', number: '0912345678'}, ...]
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
