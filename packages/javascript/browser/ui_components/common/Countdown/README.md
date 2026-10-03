# Countdown 倒數計時

顯示距離目標時間的剩餘時間，例如「會議 5 分鐘後開始」、「訂單保留時間」。

- 可注入時鐘（`now`）與伺服器時差（`serverOffsetMs`），不受使用者電腦時間不準影響
- 每次都從時鐘重算，計時器對齊整秒邊界，不累積誤差
- 分頁隱藏時暫停，回到前景立即重新同步
- 門檻（`thresholds`）依剩餘時間切換顏色，每個門檻只觸發一次 `onThreshold`
- 睡眠或背景分頁越過目標時間時，`onComplete` 也只觸發一次
- 螢幕閱讀器只在開始、跨過門檻、完成時公告，不逐秒朗讀

## 建構子

```javascript
import { Countdown } from './Countdown.js';

const countdown = new Countdown({
    target: null,                 // 目標時間：Date、ISO 字串或 epoch 毫秒
    now: () => new Date().getTime(), // 可注入的時鐘（回傳毫秒或 Date）；預設為系統時鐘，值同 Date.now()
    serverOffsetMs: 0,            // 伺服器時差（毫秒），加到 now() 上
    format: 'auto',               // 'auto' | 'dhms' | 'hms' | (parts) => string
    thresholds: [],               // [{ seconds, variant: 'info' | 'warning' | 'danger' }]
    completedText: null,          // 完成時顯示的文字；null 使用語系預設「時間到」
    autoStart: true,              // mount() 時自動開始
    ariaLive: 'polite',           // 公告的 aria-live：'polite' | 'assertive' | 'off'
    onTick: null,                 // (parts) 顯示的秒數改變時
    onThreshold: null,            // (threshold) 跨過門檻時，每個門檻一次
    onComplete: null              // () 倒數結束時，只觸發一次
});
```

`parts` 為 `{ days, hours, minutes, seconds, totalMs }`：`totalMs` 是剩餘毫秒（不為負）；天、時、分、秒以**無條件進位**的整秒計算——剩 0.4 秒時仍顯示 1 秒，顯示 0 的那一刻就是完成。

目標無效（無法解析）時顯示「—」、不會開始倒數，也不會觸發 `onComplete`。

### 顯示格式

| format | 範例（剩 1 天 2 小時 3 分 4 秒） | 說明 |
|---|---|---|
| `'auto'` | `1 天 02 小時 03 分 04 秒` / en `1d 02h 03m 04s` | 省略前導為零的單位（剩 4 秒時顯示 `4 秒`）；第一個單位不補零，其後補成兩位數 |
| `'dhms'` | `1 天 02 小時 03 分 04 秒` | 固定顯示四個單位，時、分、秒補成兩位數 |
| `'hms'` | `26:03:04` | 時:分:秒，天數併入小時 |
| 函式 | 自訂 | `(parts) => string`，回傳值以純文字顯示；拋錯時退回 `'auto'` |

單位文字走 `Locale.t('countdown.units.*')`。

### 門檻

剩餘時間 ≤ `seconds` 時套用該門檻的 `variant`（同時符合多個時取 `seconds` 最小者），元素的 `data-variant` 同步為 `default` / `info` / `warning` / `danger`，並以對應的 `--cl-*` 色彩 token 著色。每個門檻在跨過時觸發一次 `onThreshold(threshold)`（傳回原本的門檻物件）；開始時已在門檻內的，依 `seconds` 由大到小立即各觸發一次。`setTarget()` 會重設。

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載到容器（元素或選擇器）；`autoStart` 為 true 時開始倒數。回傳 `this` |
| `start()` | 開始，或從 `stop()` 後繼續；已完成時不動作（先 `setTarget()`） |
| `stop()` | 停止（保留目標與已觸發的門檻） |
| `setTarget(target)` | 更換目標並重設門檻與完成狀態；原本在倒數或已完成時以新目標重新開始，原本停止時只更新顯示 |
| `getRemaining()` | 剩餘毫秒數，永不為負；沒有有效目標時為 0 |
| `getParts()` | `{ days, hours, minutes, seconds, totalMs }` |
| `destroy()` | 清除計時器與 `visibilitychange` 監聽並移除 DOM；可重複呼叫，之後呼叫其他方法不會拋錯 |

## 事件回呼

| 回呼 | 時機 |
|---|---|
| `onTick(parts)` | 開始時，以及之後每次顯示的秒數改變時 |
| `onThreshold(threshold)` | 每個門檻跨過時一次 |
| `onComplete()` | 剩餘時間歸零時一次 |

回呼拋錯只記錄到 console，不會中斷計時。

## 計時細節

- 每次觸發都從 `now() + serverOffsetMs` 重算剩餘時間；下一次 `setTimeout` 的延遲是「到剩餘時間下一個整秒邊界的時間」（目標為整秒時，即調整後時鐘的下一個整秒）。計時器晚到時下一次延遲會縮短補回，不會逐漸落後。
- `document.hidden` 為 true 時不排任何計時器；`visibilitychange` 回到前景時立即重算，補觸發期間跨過的門檻與完成。
- `visibilitychange` 監聽只在倒數進行中掛載；完成、`stop()`、`destroy()` 時移除。

## 無障礙

- 顯示元素為 `role="timer"`（`aria-live` 預設 off）：畫面每秒更新，但不逐秒朗讀。
- 另有視覺隱藏的 live region（`aria-live` 依 `ariaLive`、`aria-atomic="true"`），只在開始（例如「剩餘 2 分鐘 30 秒」）、跨過門檻、完成（`completedText`）時公告；公告延後約 100ms 寫入，同一時刻的多次公告合併為一次。`ariaLive: 'off'` 完全不公告。
- 狀態不只靠顏色：`danger` 時字重加粗；元素的 `data-state` 為 `running` / `stopped` / `completed`。
- 所有文字走 `Locale.t('countdown.*')`，提供 zh-TW 與 en；完成文字預設在顯示當下取語系字串。

## 範例

```javascript
import { Countdown } from './ui_components/common/Countdown/index.js';

// 伺服器時間（例如取自 API 回應）：計算與本機的時差，避免使用者電腦時間不準
const serverNow = Date.now() + 42_000;          // 假設伺服器比本機快 42 秒
const serverOffsetMs = serverNow - Date.now();

const countdown = new Countdown({
    target: serverNow + 5 * 60 * 1000,          // 會議 5 分鐘後開始
    serverOffsetMs,
    thresholds: [
        { seconds: 120, variant: 'warning' },
        { seconds: 30, variant: 'danger' }
    ],
    completedText: '會議已開始',
    onThreshold: (threshold) => console.log(`剩不到 ${threshold.seconds} 秒`),
    onComplete: () => console.log('開始')
}).mount('#host');

// 離開頁面或不再需要時
// countdown.destroy();
```
