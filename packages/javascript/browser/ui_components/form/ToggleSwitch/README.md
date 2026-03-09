# ToggleSwitch 開關滑桿

布林值輸入元件，滑桿式外觀，API 對齊 Checkbox 可互換使用。

## API

### Constructor

```javascript
import { ToggleSwitch } from './ToggleSwitch.js';

const toggle = new ToggleSwitch({
    label: '啟用通知',       // 標籤文字
    checked: false,          // 是否開啟
    disabled: false,         // 停用
    size: 'medium',          // 'small' | 'medium' | 'large'
    onChange: (checked) => {} // 變更回調
});
```

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件 |
| `toggle()` | 切換開關狀態 |
| `isChecked()` | 回傳目前狀態 |
| `setChecked(bool)` | 設定狀態（不觸發 onChange） |
| `getValue()` | 同 `isChecked()` |
| `setValue(value)` | 同 `setChecked()` |
| `clear()` | 重置為 false |

### 屬性

- `element` — 根 DOM 元素
- `checked` — 目前布林值

## 使用範例

```javascript
import { ToggleSwitch } from './ToggleSwitch.js';

const toggle = new ToggleSwitch({
    label: '深色模式',
    size: 'medium',
    onChange: (checked) => console.log('dark mode:', checked)
});
toggle.mount('#container');
```

## Demo

`demos/form/ToggleSwitch.html`（專案根目錄）
