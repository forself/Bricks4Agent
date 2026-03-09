# NumberInput 數字輸入框

數字輸入元件，支援加減按鈕、鍵盤方向鍵、範圍限制、精度控制。

## API

### Constructor

```javascript
import { NumberInput } from './NumberInput.js';

const input = new NumberInput({
    label: '數量',           // 標籤文字
    value: null,             // 初始值（null=空）
    min: 0,                  // 最小值
    max: 100,                // 最大值
    step: 1,                 // 每次增減量
    precision: 0,            // 小數位數
    disabled: false,         // 停用
    showButtons: true,       // 顯示 +/- 按鈕
    placeholder: '',         // 提示文字
    width: '100%',           // 寬度
    size: 'medium',          // 'small' | 'medium' | 'large'
    onChange: (value) => {},  // 變更回調
    className: ''            // 自訂 CSS 類別
});
```

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件 |
| `getValue()` | 取得目前數值（number 或 null） |
| `setValue(value)` | 設定數值（觸發 onChange） |
| `clear()` | 清空為 null |

### 屬性

- `element` — 根 DOM 元素
- `value` — 目前數值

## 使用範例

```javascript
import { NumberInput } from './NumberInput.js';

const qty = new NumberInput({
    label: '訂購數量',
    min: 1,
    max: 999,
    step: 1,
    value: 1,
    onChange: (v) => console.log('數量:', v)
});
qty.mount('#qty-container');
```

## Demo

`demo.html`（同目錄）
