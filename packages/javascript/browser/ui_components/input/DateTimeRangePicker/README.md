# DateTimeRangePicker 日期時間範圍選擇器

選擇「開始日期時間」與「結束日期時間」的範圍輸入元件。每一端由一個 [DatePicker](../../form/DatePicker/README.md) 與一個 [TimePicker](../../form/TimePicker/README.md) 組成，兩端互相約束，並提供驗證、欄位錯誤標示與完整鍵盤操作。

- 值的格式：`{ start: 'YYYY-MM-DDTHH:mm' | null, end: 'YYYY-MM-DDTHH:mm' | null }`（當地時間，不做時區換算，不含秒與時區）
- 沿用既有選項名稱：`format`（DatePicker，`'western'` / `'taiwan'` 民國）、`minuteStep`（TimePicker）、`min` / `max`
- 不修改 DatePicker / TimePicker / DateTimeInput 原始碼

**為什麼不組合 DateTimeInput？** DateTimeInput 沒有 `min` / `max` 選項，內部 DatePicker 建立時不帶可選範圍，也沒有表達「結束必須晚於開始」「最長 N 分鐘」這類跨端限制的方式；它的預設是民國曆（`useROC: true`），日期正規化也不是嚴格解析。因此本元件直接組合 DatePicker ＋ TimePicker，並與 [DateRangePicker](../../form/DateRangePicker/README.md)、[TimeRangePicker](../../form/TimeRangePicker/README.md) 共用鍵盤與約束的輔助函式。

---

## 建構選項

```javascript
const picker = new DateTimeRangePicker({
    value: null,               // 初始值 { start, end }；格式不合或結束不晚於開始時 console.warn 並以空值開始
    min: null,                 // 最早時間：'YYYY-MM-DDTHH:mm' 或 Date；null 不限
    max: null,                 // 最晚時間：'YYYY-MM-DDTHH:mm' 或 Date；null 不限
    format: 'western',         // 日期顯示格式（同 DatePicker）：'western' 西元 / 'taiwan' 民國
    minuteStep: 15,            // 分鐘欄間隔（同 TimePicker；預設與 DateTimeInput 相同為 15），須為 1–60 的整數
    maxSpanMinutes: null,      // 最長時間長度（分鐘，結束 − 開始）；null 不限
    required: false,           // 必填；只影響 isValid() / getValidationError()
    separator: '至',            // 兩端之間的文字；預設 Locale dateTimeRangePicker.separator
    label: '',                 // 可見標籤；有值時群組以 aria-labelledby 指向它
    ariaLabel: '',             // 沒有可見標籤時的群組名稱；空值用 Locale dateTimeRangePicker.groupLabel
    clearable: true,           // 有任何部分已填時顯示「清除」按鈕
    disabled: false,           // 停用
    width: '100%',             // 元件寬度（CSS 長度字串，或數字代表 px）
    onChange: null             // (value) => {}：值真的改變時觸發一次
});
```

四個欄位的提示文字（也是無障礙名稱）來自 Locale：`dateTimeRangePicker.startDate` / `startTime` / `endDate` / `endTime`，可用 `Locale.register()` 覆寫。

---

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `getValue()` | `{ start, end }` | 目前的值（每次回傳新物件）；只選了日期或只選了時間的一端為 `null` |
| `setValue(value)` | `this` | 設定值，**不觸發** `onChange`；`null` / `undefined` / `''` 代表清空。格式不合（非 `YYYY-MM-DDTHH:mm`、日期不存在、含秒或時區）或結束不晚於開始時 `console.warn` 並保持原值 |
| `clear()` | `this` | 清空四個欄位（含只填一半的部分）；值真的改變時才觸發 `onChange` |
| `setDisabled(disabled)` | `this` | 停用／啟用四個欄位與清除按鈕 |
| `isValid()` | `boolean` | 目前的值是否通過驗證 |
| `getValidationError()` | `string` | 驗證失敗的原因（依目前語系）；通過時為空字串 |
| `setError(message, { display = true })` | `this` | 兩端的日期與時間欄都標示紅框與 `aria-invalid`，訊息只顯示一則、位於範圍列下方；空訊息等同 `clearError()`；`display: false` 只標示狀態 |
| `clearError()` | `this` | 清除 `setError` 的標示與文字 |
| `snapshot()` | `object` | 內部狀態快照（`lifecycle`、`availability`、`startDate`、`startTime`、`endDate`、`endTime`） |
| `destroy()` | — | 銷毀四個內部元件（含浮在 `document.body` 的日曆與面板）、移除所有監聽與 DOM；可重複呼叫 |

`startDatePicker` / `startTimePicker` / `endDatePicker` / `endTimePicker` 屬性是內部元件實例，僅供檢查，請勿直接呼叫它們的 `setValue`。

---

## 事件

`onChange(value)`：使用者選定日期或確認時間、按「清除」或呼叫 `clear()`，而**值真的改變**時觸發一次。只選了一端的日期（時間尚未選）時該端仍是 `null`，值沒有改變，所以不觸發；補上時間後才觸發。`setValue()` 與建構時的 `value` 不會觸發。

---

## 規則與設計取捨

**兩端互相約束，已選的部分也納入計算。** 每一端可接受的時間區間由另一端、`min` / `max`、`maxSpanMinutes` 決定（結束必須**晚於**開始，不可相等）：

- 日期欄：該端已選時間時，只開放「搭配這個時間會落在區間內」的日期（例如開始是 03-10 22:00、結束時間已選 08:00，結束日期最早只能選 03-11）；還沒選時間時，開放區間涵蓋到的日期。
- 時間欄：該端已選日期時，只開放該日期上落在區間內的時間（小時全部不可選時變淡且點不了）；還沒選日期時不限制。
- 另一端只選了日期時，以該日的 00:00 ～ 23:59 估算。

**順序錯誤一律拒絕，不自動對調。** 介面上選不到會讓結束不晚於開始的日期或時間；時間面板中的草稿時間不合規則時「確認」會被擋下，面板內以 `role="alert"` 說明原因。`setValue()` 收到結束不晚於開始的值時 `console.warn` 並保持原值。

**`setValue()` 對 `min` / `max` / `maxSpanMinutes` 不設防。** 格式與順序正確的既有資料會照樣顯示，由 `isValid()` / `getValidationError()` 回報。

**未完成的一端。** 只選了日期或只選了時間的一端，`getValue()` 回傳 `null`，驗證回報缺少的部分（例如「請選擇開始時間」）。`required: false` 時兩端全空或只有一端完整都視為有效。

**驗證順序與訊息**（`dateTimeRangePicker.errors.*`）：`required` → `startDateRequired` / `startTimeRequired` / `endDateRequired` / `endTimeRequired`（一端只填一半）→ `startRequired` / `endRequired`（必填時整端空白）→ `order` 結束必須晚於開始 → `beforeMin` 不可早於 {min} → `afterMax` 不可晚於 {max} → `maxSpan` 範圍不可超過 {duration}。`{min}` / `{max}` 依 `format` 顯示（例 `2026/03/10 08:00`、民國 `115/03/10 08:00`），`{duration}` 以「1 天 2 小時 30 分鐘」（英文 `1 d 2 h 30 min`）顯示。

---

## 鍵盤與無障礙

| 位置 | 按鍵 | 行為 |
|---|---|---|
| 觸發框 | `Tab` | 依序到達開始日期、開始時間、結束日期、結束時間、清除按鈕 |
| 觸發框 | `Enter` / `Space` / `↓` | 開啟日曆或時間面板，焦點移入 |
| 觸發框 | `Esc` | 關閉已開啟的日曆或面板（不讓外層對話框一起關閉） |
| 日曆 | `←` `→` `↑` `↓` / `Home` `End` / `PageUp` `PageDown` | 移動日期／月初月底／換月（加 `Shift` 換年） |
| 日曆 | `Enter` / `Space` | 選取日期，焦點回到觸發框 |
| 時間面板 | `↑` `↓` / `Home` `End` / `PageUp` `PageDown` | 在可選的小時或分鐘間移動（跳過不可選的時間） |
| 時間面板 | `←` / `→` | 切換小時欄／分鐘欄 |
| 時間面板 | `Enter` | 確認，焦點回到觸發框 |
| 日曆／時間面板 | `Esc` | 關閉並回到觸發框 |
| 日曆／時間面板 | `Tab` / `Shift+Tab` | 在面板內的控制項間循環 |

- 外層 `role="group"`，以 `aria-labelledby`（有 `label` 時）或 `aria-label` 命名。
- 四個觸發框皆為 `role="combobox"`、`aria-haspopup="dialog"`、`aria-expanded`、`aria-controls`、`aria-label`，必填時 `aria-required`，停用時 `tabindex="-1"` 與 `aria-disabled`。
- 日曆與時間面板為 `role="dialog"`；日期按鈕有完整日期的 `aria-label`、`aria-disabled`、`aria-pressed`；時間欄為 `role="listbox"`，項目為 `role="option"`，以 `aria-selected`、`aria-activedescendant`、`aria-disabled` 表示狀態。細節見 [DateRangePicker](../../form/DateRangePicker/README.md) 與 [TimeRangePicker](../../form/TimeRangePicker/README.md)。

---

## 範例

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>DateTimeRangePicker 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="maintenance"></div>
    <button type="button" id="check">檢查</button>
    <p id="result"></p>

    <script type="module">
        import { DateTimeRangePicker } from './DateTimeRangePicker.js';

        const result = document.getElementById('result');
        const picker = new DateTimeRangePicker({
            label: '系統維護時段',
            required: true,
            min: new Date(),
            maxSpanMinutes: 8 * 60,
            minuteStep: 30,
            onChange: (value) => {
                picker.clearError();
                result.textContent = `${value.start ?? '—'} ~ ${value.end ?? '—'}`;
            }
        }).mount('#maintenance');

        document.getElementById('check').addEventListener('click', () => {
            if (picker.isValid()) picker.clearError();
            else picker.setError(picker.getValidationError());
        });
    </script>
</body>
</html>
```
