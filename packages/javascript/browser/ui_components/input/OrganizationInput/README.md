# OrganizationInput

多層級組織單位選擇元件（最多四級），繼承 `ChainedInput`，逐級連動載入子單位。

## API

### Constructor

```js
new OrganizationInput(options?)
```

繼承 `ChainedInput` 所有選項（`onChange`、`layout`、`gap`），`fields` 已預設為：

| 欄位 name | type | 說明 |
|-----------|------|------|
| `level1` | `select` | 一級單位，非同步載入 |
| `level2` | `select` | 二級單位，依上級連動，無子單位時自動隱藏 |
| `level3` | `select` | 三級單位，同上 |
| `level4` | `select` | 四級單位，同上 |

### 方法

繼承 `ChainedInput` 所有方法，額外提供：

| 方法 | 說明 |
|------|------|
| `getSelectedUnit()` | 回傳最底層已選單位 `{level, id}`，無選擇時回傳 `null` |

## 使用範例

```js
import { OrganizationInput } from './input/OrganizationInput/index.js';

const org = new OrganizationInput({
    onChange: (values) => console.log(values)
});
org.mount('#container');

// 取得最底層選定單位
const unit = org.getSelectedUnit();
// { level: 'level3', id: 'unit-123' }
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
