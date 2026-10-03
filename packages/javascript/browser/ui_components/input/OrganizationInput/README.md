# OrganizationInput

多層級組織單位選擇元件（最多四級），繼承 `ChainedInput`，逐級連動載入子單位。

## API

### Constructor

```js
new OrganizationInput(options?)
```

繼承 `ChainedInput` 的選項（`onChange`、`layout`），`fields` 已預設為下表，`gap` 固定為 `'8px'`（傳入的 `gap` 會被覆蓋）；另需提供 `loadUnits` 資料載入函式 `async (parentId) => [{value|id, label|name}]`（未提供時，載入錯誤會被 ChainedInput 捕捉並以 `console.error` 記錄，下拉選單維持空白）：

| 欄位 name | type | 說明 |
|---|---|---|
| `level1` | `select` | 一級單位，非同步載入 |
| `level2` | `select` | 二級單位，依上級連動，無子單位時自動隱藏 |
| `level3` | `select` | 三級單位，同上 |
| `level4` | `select` | 四級單位，同上 |

### 方法

繼承 `ChainedInput` 所有方法，額外提供：

| 方法 | 說明 |
|---|---|
| `getSelectedUnit()` | 回傳最底層已選單位 `{level, id}`，無選擇時回傳 `null` |

## 使用範例

```js
import { OrganizationInput } from './index.js';

const org = new OrganizationInput({
    loadUnits: async (parentId) => fetchUnits(parentId),
    onChange: (values) => console.log(values)
});
org.mount('#container');

// 取得最底層選定單位
const unit = org.getSelectedUnit();
// { level: 'level3', id: 'unit-123' }
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
