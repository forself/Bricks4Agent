# AddressInput

台灣地址輸入元件（縣市 > 行政區 > 詳細地址），繼承 `ChainedInput`，選項透過 mockApi 非同步載入。

## API

### Constructor

```js
new AddressInput(options?)
```

繼承 `ChainedInput` 所有選項（`onChange`、`layout`、`gap`），`fields` 已預設為：

| 欄位 name | type | 說明 |
|-----------|------|------|
| `city` | `select` | 縣市，非同步載入 |
| `district` | `select` | 行政區，依縣市連動 |
| `address` | `text` | 詳細地址 |

### 方法

繼承 `ChainedInput` 所有方法，額外提供：

| 方法 | 說明 |
|------|------|
| `getFullAddress()` | 回傳完整地址字串（自動將縣市代碼轉為中文名稱） |

## 使用範例

```js
import { AddressInput } from './input/AddressInput/index.js';

const addr = new AddressInput({
    onChange: (values) => console.log(values)
});
addr.mount('#container');

// 取得完整地址
const full = addr.getFullAddress(); // "台北市 中正區 忠孝東路一段1號"
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
