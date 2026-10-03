# DateRangePicker 日期範圍選擇器

以兩個 [DatePicker](../DatePicker/README.md) 組成的日期範圍輸入元件：選擇開始日與結束日，兩端互相約束，並提供驗證、快速選擇、欄位錯誤標示與完整鍵盤操作。

- 值的格式：`{ start: 'YYYY-MM-DD' | null, end: 'YYYY-MM-DD' | null }`（當地日期，不做時區換算）
- 沿用 DatePicker 的選項名稱：`format`（`'western'` 西元 / `'taiwan'` 民國）、`min`、`max`
- 不修改 DatePicker 原始碼；鍵盤與 ARIA 由本元件從外部補上

---

## 建構選項

```javascript
const range = new DateRangePicker({
    value: null,               // 初始值 { start, end }；格式不合或順序顛倒時 console.warn 並以空值開始
    min: null,                 // 最早可選日期：'YYYY-MM-DD' 或 Date；null 不限
    max: null,                 // 最晚可選日期：'YYYY-MM-DD' 或 Date；null 不限
    format: 'western',         // 顯示格式（同 DatePicker）：'western' 西元 / 'taiwan' 民國
    allowSameDay: true,        // 開始與結束可為同一天；false 時結束日至少是開始日隔天
    maxSpanDays: null,         // 最長天數，含開始與結束兩天（7 → 09-01 ~ 09-07）；null 不限
    required: false,           // 必填；只影響 isValid() / getValidationError()
    presets: [],               // 快速選擇：[{ label, range: () => ({ start, end }) }]，非空時顯示成小按鈕
    startPlaceholder: '開始日期', // 預設 Locale dateRangePicker.startPlaceholder；也是開始端的無障礙名稱
    endPlaceholder: '結束日期',   // 預設 Locale dateRangePicker.endPlaceholder；也是結束端的無障礙名稱
    separator: '至',            // 兩端之間的文字；預設 Locale dateRangePicker.separator
    label: '',                 // 可見標籤；有值時群組以 aria-labelledby 指向它
    ariaLabel: '',             // 沒有可見標籤時的群組名稱；空值用 Locale dateRangePicker.groupLabel
    clearable: true,           // 有值時顯示「清除」按鈕
    disabled: false,           // 停用
    width: '100%',             // 元件寬度（CSS 長度字串，或數字代表 px）
    onChange: null             // (value) => {}：使用者確定變更時觸發一次
});
```

---

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `getValue()` | `{ start, end }` | 目前的值（每次回傳新物件） |
| `setValue(value)` | `this` | 設定值，**不觸發** `onChange`；`null` / `undefined` / `''` 代表清空。格式不合或結束早於開始時 `console.warn` 並保持原值 |
| `clear()` | `this` | 清空兩端；值真的改變時才觸發 `onChange` |
| `setDisabled(disabled)` | `this` | 停用／啟用兩端、清除按鈕與快速選擇 |
| `isValid()` | `boolean` | 目前的值是否通過驗證 |
| `getValidationError()` | `string` | 驗證失敗的原因（依目前語系）；通過時為空字串 |
| `setError(message, { display = true })` | `this` | 兩端都標示紅框與 `aria-invalid`，訊息只顯示一則、位於範圍列下方；空訊息等同 `clearError()`；`display: false` 只標示狀態 |
| `clearError()` | `this` | 清除 `setError` 的標示與文字 |
| `snapshot()` | `object` | 內部狀態快照（`lifecycle`、`availability`、`start`、`end`） |
| `destroy()` | — | 銷毀兩個 DatePicker（含浮在 `document.body` 的日曆）、移除所有監聽與 DOM；可重複呼叫 |

`startPicker` / `endPicker` 屬性是內部的 DatePicker 實例，僅供檢查，請勿直接呼叫它們的 `setValue`。

---

## 事件

`onChange(value)`：使用者在日曆選定日期、按下快速選擇、按「清除」或呼叫 `clear()` 而**值真的改變**時觸發一次；`setValue()` 與建構時的 `value` 不會觸發。

---

## 規則與設計取捨

**兩端互相約束。** 選了開始日後，結束端日曆只開放「開始日（`allowSameDay: false` 時為隔天）之後、且不超過 `maxSpanDays`」的日期；選了結束日後，開始端也反向受限；兩端都再與 `min` / `max` 取交集。一端有值、另一端空白時，空白端的日曆會自動跳到同一個月份。

**順序顛倒一律拒絕，不自動對調。** 介面上選不到早於開始日的結束日；`setValue()` 或快速選擇回傳結束早於開始的值時，`console.warn` 並保持原值。自動對調會讓使用者看到的欄位內容與自己輸入的不同，因此不採用。

**`setValue()` 對限制條件不設防。** 只要格式正確、順序正確，即使違反 `min` / `max` / `maxSpanDays` / `allowSameDay` 也會顯示（保留既有資料），由 `isValid()` / `getValidationError()` 回報原因。快速選擇則必須完全通過驗證，否則 `console.warn` 並忽略。

**必填與半開區間。** `required: false` 時只填一端視為有效（例如「某日之後」）；`required: true` 時兩端都要填。

**驗證順序與訊息**（`dateRangePicker.errors.*`）：`required` 請選擇日期範圍 → `startRequired` / `endRequired` → `order` 結束日期不可早於開始日期 → `sameDay` 結束日期必須晚於開始日期 → `beforeMin` 日期不可早於 {min} → `afterMax` 日期不可晚於 {max} → `maxSpan` 日期範圍不可超過 {days} 天。`{min}` / `{max}` 依 `format` 顯示（民國格式為 `115/03/01`）。

---

## 鍵盤與無障礙

| 位置 | 按鍵 | 行為 |
|---|---|---|
| 觸發框 | `Tab` | 依序到達開始端、結束端、清除按鈕、快速選擇按鈕 |
| 觸發框 | `Enter` / `Space` / `↓` | 開啟日曆並把焦點移到已選日期（或今天、或第一個可選日期） |
| 觸發框 | `Esc` | 關閉日曆 |
| 日曆 | `←` `→` / `↑` `↓` | 前後一天 / 前後一週（跨月時自動換月） |
| 日曆 | `Home` / `End` | 當月第一天 / 最後一天 |
| 日曆 | `PageUp` / `PageDown` | 上個月 / 下個月（加 `Shift` 為上一年 / 下一年） |
| 日曆 | `Enter` / `Space` | 選取日期（不可選的日期不動作），關閉後焦點回到觸發框 |
| 日曆 | `Esc` | 關閉日曆，焦點回到觸發框 |
| 日曆 | `Tab` / `Shift+Tab` | 在「上個月、年、月、下個月、目前日期」之間循環 |

- 外層 `role="group"`，以 `aria-labelledby`（有 `label` 時）或 `aria-label` 命名。
- 觸發框：`role="combobox"`、`aria-haspopup="dialog"`、`aria-expanded`、`aria-controls`、`aria-label`（= 提示文字）、必填時 `aria-required`，停用時 `tabindex="-1"` 與 `aria-disabled`；取得焦點時以 box-shadow 顯示外框（不會蓋掉 `setError` 使用的 outline）。
- 日曆：`role="dialog"`；日期按鈕補上完整日期的 `aria-label`（依語系，民國格式用民國曆）、不可選日期 `aria-disabled="true"`、已選日期 `aria-pressed="true"`。
- 以 `Tab` 從觸發框移到其他元素時，已開啟的日曆會自動關閉；以滑鼠選好日期後焦點也回到觸發框。

---

## 範例

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>DateRangePicker 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="booking"></div>
    <button type="button" id="check">檢查</button>
    <p id="result"></p>

    <script type="module">
        import { DateRangePicker } from './DateRangePicker.js';

        const toIso = (date) => [
            date.getFullYear(),
            String(date.getMonth() + 1).padStart(2, '0'),
            String(date.getDate()).padStart(2, '0')
        ].join('-');
        const daysFromToday = (offset) => {
            const date = new Date();
            date.setDate(date.getDate() + offset);
            return toIso(date);
        };

        const result = document.getElementById('result');
        const range = new DateRangePicker({
            label: '會議室借用期間',
            required: true,
            min: new Date(),
            maxSpanDays: 14,
            presets: [
                { label: '今天', range: () => ({ start: daysFromToday(0), end: daysFromToday(0) }) },
                { label: '未來 7 天', range: () => ({ start: daysFromToday(0), end: daysFromToday(6) }) }
            ],
            onChange: (value) => {
                range.clearError();
                result.textContent = `${value.start ?? '—'} ~ ${value.end ?? '—'}`;
            }
        }).mount('#booking');

        // 送出前驗證：不通過時兩端標紅框，範圍下方顯示原因
        document.getElementById('check').addEventListener('click', () => {
            if (range.isValid()) range.clearError();
            else range.setError(range.getValidationError());
        });
    </script>
</body>
</html>
```
