# AddressInput

台灣地址輸入元件（縣市 > 行政區 > 詳細地址），繼承 `ChainedInput`，縣市/行政區選項透過 `loadCities`、`loadDistricts` 載入函式非同步載入。

## API

### Constructor

```js
new AddressInput(options?)
```

繼承 `ChainedInput` 的選項（`onChange`、`layout`），`fields` 已預設，`gap` 固定為 `'8px'`（傳入的 `gap` 會被覆蓋）；另需提供 `loadCities: async () => [...]` 與 `loadDistricts: async (city) => [...]` 資料載入函式（未提供時，載入選項的錯誤會被 ChainedInput 捕捉並以 `console.error` 記錄，下拉選單維持空白）：

| 欄位 name | type | 說明 |
|---|---|---|
| `city` | `select` | 縣市，非同步載入 |
| `district` | `select` | 行政區，依縣市連動 |
| `address` | `text` | 詳細地址 |

### 方法

繼承 `ChainedInput` 所有方法，額外提供：

| 方法 | 說明 |
|---|---|
| `getFullAddress()` | 回傳完整地址字串。目前行為：直接串接 `city`、`district`、`address` 的原始值（即 loader 回傳的 `value`），不會把縣市代碼轉為中文名稱 |

## 使用範例

```js
import { AddressInput } from './index.js';

const addr = new AddressInput({
    loadCities: async () => fetchCities(),
    loadDistricts: async (city) => fetchDistricts(city),
    onChange: (values) => console.log(values)
});
addr.mount('#container');

// 取得完整地址
const full = addr.getFullAddress(); // city/district 的 value + 詳細地址，例如 value 為中文名稱時得到 "台北市中正區忠孝東路一段1號"
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
