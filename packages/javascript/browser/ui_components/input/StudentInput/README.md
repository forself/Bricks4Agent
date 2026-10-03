# StudentInput

學籍身份輸入元件，繼承 `ChainedInput`，勾選「在學學生」後才顯示學校名稱欄位。

## API

### Constructor

```js
new StudentInput(options?)
```

繼承 `ChainedInput` 的選項（如 `onChange`），`fields` 已預設為下表；`layout` 固定為 `'horizontal'`、`gap` 固定為 `'20px'`（傳入的值會被覆蓋）：

| 欄位 name | type | 說明 |
|---|---|---|
| `isStudent` | `checkbox` | 是否為在學學生 |
| `schoolName` | `text` | 學校名稱，僅勾選時顯示（`hideWhenDisabled: true`） |

### 方法

繼承 `ChainedInput` 所有方法：`mount(container)`、`getValues()`、`setValues(values)`、`validate()`、`reset()`、`destroy()`。

## 使用範例

```js
import { StudentInput } from './index.js';

const student = new StudentInput({
    onChange: (values) => console.log(values)
});
student.mount('#container');

// 取得值
const data = student.getValues();
// { isStudent: true, schoolName: '台灣大學' }
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
