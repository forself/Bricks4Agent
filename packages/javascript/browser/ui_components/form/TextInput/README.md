# TextInput 文字輸入框

文字輸入元件，支援多種類型（text/password/email/tel）、資安檢查（SQL Injection/Path Traversal）、尺寸、錯誤狀態。

## API

### Constructor

```javascript
import { TextInput } from './TextInput.js';

const input = new TextInput({
    type: 'text',            // 'text' | 'password' | 'email' | 'tel'
    placeholder: '請輸入',    // 提示文字
    value: '',               // 初始值
    label: '名稱',           // 標籤文字
    size: 'medium',          // 'small' | 'medium' | 'large'
    disabled: false,         // 停用
    readonly: false,         // 唯讀
    required: false,         // 必填（label 顯示 *）
    error: '',               // 錯誤訊息
    hint: '',                // 提示訊息
    maxLength: null,         // 最大長度
    width: '100%',           // 寬度
    enableSecurity: true,    // 啟用資安檢查
    onChange: (value) => {},  // 輸入回調
    onBlur: (value) => {}    // 失焦回調
});
```

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件 |
| `getValue()` | 取得目前值 |
| `setValue(value)` | 設定值 |
| `clear()` | 清空值並清除錯誤 |
| `setError(msg)` | 設定錯誤訊息 |
| `clearError()` | 清除錯誤 |
| `focus()` | 聚焦輸入框 |

### 屬性

- `element` — 根 DOM 元素
- `input` — 內部 `<input>` 元素

## 使用範例

```javascript
import { TextInput } from './TextInput.js';

const input = new TextInput({
    label: '使用者名稱',
    placeholder: '請輸入帳號',
    required: true,
    maxLength: 20,
    onChange: (val) => console.log('輸入:', val)
});
input.mount('#username-container');
```

## Demo

`demo.html`（同目錄）
