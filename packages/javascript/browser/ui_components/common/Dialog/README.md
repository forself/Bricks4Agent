# SimpleDialog

輕量級通用對話框，提供非阻塞的 alert / confirm / prompt 功能，取代原生 `window.alert` 等。

## API

SimpleDialog 僅提供靜態方法，無需實例化。

### 靜態方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `SimpleDialog.alert(message, container?)` | `Promise<true>` | 提示訊息對話框 |
| `SimpleDialog.confirm(message, container?)` | `Promise<boolean>` | 確認對話框，確定回傳 `true`，取消回傳 `false` |
| `SimpleDialog.prompt(message, defaultValue?, container?)` | `Promise<string\|null>` | 輸入對話框，確定回傳輸入值，取消回傳 `null` |

### 參數說明

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `message` | `string` | — | 訊息/提示文字 |
| `defaultValue` | `string` | `''` | prompt 預設值 |
| `container` | `HTMLElement` | `document.body` | 掛載容器（對話框使用 fixed 定位，此參數保留） |

### 特性

- 所有方法回傳 `Promise`，支援 `await`
- 內建進場/退場動畫
- prompt 支援 Enter 確認、Escape 取消
- 自動 focus 輸入框

## 使用範例

```js
import { SimpleDialog } from './SimpleDialog.js';

// Alert
await SimpleDialog.alert('操作完成！');

// Confirm
const ok = await SimpleDialog.confirm('確定要刪除嗎？');
if (ok) { /* 執行刪除 */ }

// Prompt
const name = await SimpleDialog.prompt('請輸入名稱', '預設值');
if (name !== null) { /* 使用 name */ }
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
