# FormField 表單欄位包裝器

為任何 form 元件提供 label、必填標記、錯誤/提示訊息的外殼容器。

## API

### Constructor

```javascript
import { FormField } from './FormField.js';

const field = new FormField({
    fieldName: 'username',      // 欄位技術名稱
    label: '使用者名稱',         // 標籤文字
    required: true,             // 是否必填（顯示 * 標記）
    error: '',                  // 錯誤訊息
    hint: '請輸入 3-20 字元',    // 提示文字
    component: textInputInstance, // 內部元件實例（需有 mount/destroy）
    col: 6                      // CSS grid span 欄寬 1-12，null=不設定
});
```

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器（selector 或 DOM） |
| `destroy()` | 銷毀元件（含內部 component） |
| `setError(msg)` | 設定錯誤訊息，空字串清除 |
| `clearError()` | 清除錯誤，恢復 hint |
| `setRequired(bool)` | 設定必填狀態 |
| `setLabel(text)` | 設定標籤文字 |
| `setCol(n)` | 設定欄寬 |
| `setReadonly(bool)` | 轉傳 readonly 給內部元件 |
| `show()` / `hide()` | 顯示/隱藏欄位 |
| `isVisible()` | 回傳是否可見 |
| `getComponent()` | 取得內部元件實例 |

### 屬性

- `element` — 根 DOM 元素

## 使用範例

```javascript
import { FormField } from './FormField.js';
import { TextInput } from '../TextInput/TextInput.js';

const input = new TextInput({ placeholder: '請輸入' });
const field = new FormField({
    fieldName: 'name',
    label: '姓名',
    required: true,
    hint: '必填欄位',
    component: input,
    col: 6
});
field.mount('#form-container');
```

## Demo

`demo.html`（同目錄）
