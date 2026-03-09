# AddressListInput

多筆地址列表輸入元件，繼承 `ListInput`，每筆項目為一個 `AddressInput`。

## API

### Constructor

```js
new AddressListInput(options?)
```

繼承 `ListInput` 所有選項，預設值：

| 參數 | 預設值 | 說明 |
|------|--------|------|
| `title` | `'地址列表'` | 標題 |
| `minItems` | `1` | 最少項目數 |
| `maxItems` | `3` | 最多項目數 |
| `addButtonText` | `'新增地址'` | 新增按鈕文字 |

### 方法

繼承 `ListInput` 所有方法：`mount(container)`、`getValues()`、`setValues(items)`。

## 使用範例

```js
import { AddressListInput } from './input/AddressListInput/index.js';

const list = new AddressListInput({
    maxItems: 5,
    onChange: (items) => console.log(items)
});
list.mount('#container');

// 取得所有地址
const addresses = list.getValues();
// [{city, district, address}, ...]
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
