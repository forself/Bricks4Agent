# Radio 單選按鈕

單選按鈕元件，支援群組建立、方向排列、自訂尺寸。

## API

### Constructor（單一 Radio）

```javascript
import { Radio } from './Radio.js';

const radio = new Radio({
    name: 'gender',          // 群組名稱（同群組共用）
    label: '男',             // 標籤文字
    value: 'male',           // 值
    checked: false,          // 是否選中
    disabled: false,         // 停用
    size: 'medium',          // 'small' | 'medium' | 'large'
    onChange: (value) => {}   // 變更回調
});
```

### 單一 Radio 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至容器 |
| `destroy()` | 銷毀元件 |
| `isChecked()` | 回傳是否選中 |
| `setChecked(bool)` | 設定選中狀態 |

### 靜態方法 `Radio.createGroup(config)` （推薦）

```javascript
const group = Radio.createGroup({
    name: 'color',
    items: [
        { label: '紅色', value: 'red' },
        { label: '藍色', value: 'blue' },
        { label: '綠色', value: 'green', disabled: true }
    ],
    value: 'red',                    // 初始選中值
    direction: 'horizontal',         // 'vertical' | 'horizontal'
    size: 'medium',                  // 傳遞至各 Radio
    onChange: (value) => {}          // 選中值變更回調
});
```

### Group 方法

| 方法 | 說明 |
|------|------|
| `group.mount(container)` | 掛載群組至容器 |
| `group.getValue()` | 取得選中值 |
| `group.setValue(value)` | 設定選中值 |
| `group.clear()` | 清除選取（重置為 null） |

## 使用範例

```javascript
import { Radio } from './Radio.js';

const group = Radio.createGroup({
    name: 'priority',
    items: [
        { label: '高', value: 'high' },
        { label: '中', value: 'medium' },
        { label: '低', value: 'low' }
    ],
    value: 'medium',
    direction: 'horizontal',
    onChange: (val) => console.log('優先級:', val)
});
group.mount('#priority-container');
```

## Demo

`demo.html`（同目錄）
