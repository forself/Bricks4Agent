# DateTimeInput

日期時間整合輸入元件，內部組合 `DatePicker` + `TimePicker`，支援民國年。

## API

### Constructor

```js
new DateTimeInput(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.label` | `string` | `''` | 整體標籤 |
| `options.useROC` | `boolean` | `true` | 使用民國年格式 |
| `options.showTime` | `boolean` | `true` | 是否顯示時間選擇 |
| `options.minuteStep` | `number` | `15` | 分鐘間隔 |
| `options.dateValue` | `string` | `''` | 預設日期（YYYY-MM-DD 或民國年格式） |
| `options.timeValue` | `string` | `''` | 預設時間（HH:MM） |
| `options.onChange` | `Function` | `null` | 值變更回調，參數為 `{date, time, combined}` |

### 方法

| 方法 | 說明 |
|------|------|
| `mount(container)` | 掛載至 DOM 容器，回傳 `this` |
| `getValue()` | 回傳 `{date, time}` |
| `setValue(date?, time?)` | 設定日期和/或時間 |
| `destroy()` | 移除元件及內部子元件 |

## 使用範例

```js
import { DateTimeInput } from './input/DateTimeInput/index.js';

const dt = new DateTimeInput({
    label: '事件時間',
    useROC: true,
    minuteStep: 30,
    onChange: ({ date, time, combined }) => console.log(combined)
});
dt.mount('#container');

// 程式設定值
dt.setValue('2024-03-15', '14:30');
```

## Demo

`packages/javascript/browser/ui_components/input/demo.html`
