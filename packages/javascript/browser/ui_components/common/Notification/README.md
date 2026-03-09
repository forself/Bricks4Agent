# Notification

通知訊息元件，提供 Toast 風格通知，支援 success/error/warning/info 四種類型和六種位置。

## API

### Constructor

```js
new Notification(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.type` | `string` | `'info'` | 類型：`'success'`/`'error'`/`'warning'`/`'info'` |
| `options.title` | `string` | `''` | 標題 |
| `options.message` | `string` | `''` | 訊息內容 |
| `options.duration` | `number` | `4000` | 顯示時間 (ms)，`0` 為不自動關閉 |
| `options.position` | `string` | `'top-right'` | 位置（見靜態常數） |
| `options.closable` | `boolean` | `true` | 是否可手動關閉 |
| `options.onClose` | `Function` | `null` | 關閉回調 |
| `options.icon` | `string` | `null` | 自訂圖示 |

### 靜態常數

- `Notification.TYPES` — `{ SUCCESS, ERROR, WARNING, INFO }`
- `Notification.POSITIONS` — `{ TOP_RIGHT, TOP_LEFT, TOP_CENTER, BOTTOM_RIGHT, BOTTOM_LEFT, BOTTOM_CENTER }`

### 實例方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `show()` | `this` | 顯示通知 |
| `close()` | `void` | 關閉通知（含動畫） |

### 靜態方法（建立並立即顯示）

| 方法 | 說明 |
|------|------|
| `Notification.success(message, options?)` | 成功通知 |
| `Notification.error(message, options?)` | 錯誤通知（預設不自動關閉） |
| `Notification.warning(message, options?)` | 警告通知 |
| `Notification.info(message, options?)` | 資訊通知 |
| `Notification.closeAll()` | 關閉所有通知 |

## 使用範例

```js
import { Notification } from './Notification.js';

// 靜態方法（最常用）
Notification.success('儲存成功');
Notification.error('操作失敗，請稍後再試');

// 自訂選項
Notification.warning('即將到期', {
    duration: 8000,
    position: 'top-center'
});
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
