# AddressListInput

多筆地址列表輸入元件，繼承 `ListInput`，每筆項目為一個 `AddressInput`。

## API

### Constructor

```js
new AddressListInput(options?)
```

繼承 `ListInput` 所有選項，並**必須**提供每筆 `AddressInput` 使用的資料載入函式：

| 參數 | 說明 |
|---|---|
| `loadCities` | `async () => [...]`，載入縣市選項（必填） |
| `loadDistricts` | `async (city) => [...]`，依縣市載入行政區選項（必填） |

未提供時，各筆地址的縣市/行政區下拉選單不會有資料（錯誤由 ChainedInput 以 `console.error` 記錄）。

預設值：

| 參數 | 預設值 | 說明 |
|---|---|---|
| `title` | `'地址列表'` | 標題 |
| `minItems` | `1` | 最少項目數 |
| `maxItems` | `3` | 最多項目數 |
| `addButtonText` | `'新增地址'` | 新增按鈕文字 |

### 方法

繼承 `ListInput` 所有方法：`mount(container)`、`getValues()`、`setValues(items)`。

## 使用範例

```js
import { AddressListInput } from './index.js';

const list = new AddressListInput({
    loadCities: async () => fetchCities(),
    loadDistricts: async (city) => fetchDistricts(city),
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
