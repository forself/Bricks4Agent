# PersonInfoList

個人基本資料列表輸入元件，繼承 `ListInput`，每筆項目包含姓名、性別、年齡、身分證號、其他證號。

## API

### Constructor

```js
new PersonInfoList(options?)
```

繼承 `ListInput` 所有選項，預設值：

| 參數 | 預設值 | 說明 |
|------|--------|------|
| `title` | `'個人基本資料'` | 標題 |
| `minItems` | `1` | 最少項目數 |
| `maxItems` | `5` | 最多項目數 |
| `addButtonText` | `'新增人員'` | 新增按鈕文字 |

每筆項目欄位：

| 欄位 name | type | 說明 |
|-----------|------|------|
| `name` | `text` | 姓名 |
| `gender` | `select` | 性別（男/女/其他） |
| `age` | `number` | 年齡（0-150） |
| `id` | `text` | 身分證號（最長 20 碼） |
| `otherId` | `text` | 其他證號 |

### 方法

繼承 `ListInput` 所有方法：`mount(container)`、`getValues()`、`setValues(items)`。

## 使用範例

```js
import { PersonInfoList } from './input/PersonInfoList/index.js';

const people = new PersonInfoList({
    maxItems: 10,
    onChange: (items) => console.log(items)
});
people.mount('#container');

// 取得所有人員資料
const data = people.getValues();
// [{name, gender, age, id, otherId}, ...]
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
