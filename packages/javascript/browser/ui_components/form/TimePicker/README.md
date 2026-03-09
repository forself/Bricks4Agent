# TimePicker 時間選擇器

時間選擇器元件，下拉面板選擇小時/分鐘，支援分鐘間隔設定。

## API

### Constructor

```javascript
import { TimePicker } from './TimePicker.js';

const picker = new TimePicker({
    label: '開始時間',           // 標籤文字
    placeholder: '請選擇時間',   // 提示文字
    value: '09:30',             // 初始值（HH:MM 格式，null=空）
    disabled: false,            // 停用
    required: false,            // 必填（label 顯示 *）
    size: 'medium',             // 'small' | 'medium' | 'large'
    minuteStep: 1,              // 分鐘間隔（1/5/10/15/30）
    className: '',              // 自訂 CSS 類別
    onChange: (timeStr, {hour, minute}) => {} // 變更回調
});
```

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件 |
| `getValue()` | 取得時間字串（HH:MM）或空字串 |
| `setValue(str)` | 設定時間（'HH:MM' 格式） |
| `clear()` | 清除已選時間 |
| `open()` / `close()` / `toggle()` | 開關面板 |

### 屬性

- `element` — 根 DOM 元素
- `hour` / `minute` — 目前選中的小時/分鐘（number 或 null）

## 使用範例

```javascript
import { TimePicker } from './TimePicker.js';

const picker = new TimePicker({
    label: '會議時間',
    value: '14:00',
    minuteStep: 15,
    onChange: (time, {hour, minute}) => console.log(`選擇: ${time}`)
});
picker.mount('#time-container');
```

## Demo

`demo.html`（同目錄）
