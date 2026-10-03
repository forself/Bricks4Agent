# TimeRangePicker 時間範圍選擇器

以兩個 [TimePicker](../TimePicker/README.md) 組成的時間範圍輸入元件：選擇開始與結束時間，兩端互相約束，支援跨午夜（隔日）、最短／最長時間長度、驗證、欄位錯誤標示與完整鍵盤操作。

- 值的格式：`{ start: 'HH:mm' | null, end: 'HH:mm' | null }`（24 小時制、兩位數小時）
- 沿用 TimePicker 的選項名稱 `minuteStep`
- 不修改 TimePicker 原始碼；鍵盤、ARIA 與「不可選時間」由本元件從外部補上

---

## 建構選項

```javascript
const picker = new TimeRangePicker({
    value: null,               // 初始值 { start, end }；格式或順序不合時 console.warn 並以空值開始
    minuteStep: 1,             // 分鐘欄間隔（同 TimePicker）；須為 1–60 的整數，否則警告並用 1
    allowOvernight: false,     // true：結束早於開始代表隔日（例 22:00 至 06:00）
    minDurationMinutes: 0,     // 最短時間長度（分鐘）
    maxDurationMinutes: null,  // 最長時間長度（分鐘）；null 不限
    required: false,           // 必填；只影響 isValid() / getValidationError()
    startPlaceholder: '開始時間', // 預設 Locale timeRangePicker.startPlaceholder；也是開始端的無障礙名稱
    endPlaceholder: '結束時間',   // 預設 Locale timeRangePicker.endPlaceholder；也是結束端的無障礙名稱
    separator: '至',            // 兩端之間的文字；預設 Locale timeRangePicker.separator
    label: '',                 // 可見標籤；有值時群組以 aria-labelledby 指向它
    ariaLabel: '',             // 沒有可見標籤時的群組名稱；空值用 Locale timeRangePicker.groupLabel
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
| `setValue(value)` | `this` | 設定值，**不觸發** `onChange`；`null` / `undefined` / `''` 代表清空。格式不合、`allowOvernight: false` 時結束不晚於開始、或結束等於開始時，`console.warn` 並保持原值 |
| `clear()` | `this` | 清空兩端；值真的改變時才觸發 `onChange` |
| `setDisabled(disabled)` | `this` | 停用／啟用兩端與清除按鈕 |
| `isValid()` | `boolean` | 目前的值是否通過驗證 |
| `getValidationError()` | `string` | 驗證失敗的原因（依目前語系）；通過時為空字串 |
| `setError(message, { display = true })` | `this` | 兩端都標示紅框與 `aria-invalid`，訊息只顯示一則、位於範圍列下方；空訊息等同 `clearError()`；`display: false` 只標示狀態 |
| `clearError()` | `this` | 清除 `setError` 的標示與文字 |
| `snapshot()` | `object` | 內部狀態快照（`lifecycle`、`availability`、`start`、`end`） |
| `destroy()` | — | 銷毀兩個 TimePicker（含浮在 `document.body` 的面板）、移除所有監聽與 DOM；可重複呼叫 |

`startPicker` / `endPicker` 屬性是內部的 TimePicker 實例，僅供檢查，請勿直接呼叫它們的 `setValue`。

---

## 事件

`onChange(value)`：使用者在面板按「確認」、按「清除」或呼叫 `clear()` 而**值真的改變**時觸發一次；只挑小時或分鐘（尚未確認）不會觸發；`setValue()` 與建構時的 `value` 也不會觸發。

---

## 規則與設計取捨

**時間長度的算法。** `allowOvernight: false` 時長度 = 結束 − 開始，且結束必須晚於開始；`allowOvernight: true` 時結束早於開始代表隔日，長度 = (結束 − 開始 + 24 小時) mod 24 小時。兩種模式下結束都不可等於開始（零長度沒有意義，24 小時又有歧義）。跨午夜時結束端旁顯示「隔日」，結束端的無障礙名稱也變成「結束時間（隔日）」。

**兩端互相約束。** 選了開始時間後，結束端面板中不符合規則（順序、`minDurationMinutes`、`maxDurationMinutes`）的時間會標成不可選：某小時的所有分鐘都不可選時，該小時變淡且點不了；選定小時後，該小時內不可選的分鐘同樣變淡。反之亦然。

**順序錯誤一律拒絕，不自動對調。** 面板中的草稿時間不合規則時（例如先選了分鐘、再換到一個會讓時間不合規則的小時），「確認」會被擋下，面板內以 `role="alert"` 顯示原因，值不變。`setValue()` 收到順序錯誤的值時 `console.warn` 並保持原值。

**`setValue()` 對時間長度不設防。** 格式與順序正確、但違反最短／最長時間長度的值會照樣顯示（保留既有資料），由 `isValid()` / `getValidationError()` 回報。

**必填與半開區間。** `required: false` 時只填一端視為有效；`required: true` 時兩端都要填。

**驗證訊息**（`timeRangePicker.errors.*`）：`required` 請選擇時間範圍、`startRequired` / `endRequired`、`order` 結束時間必須晚於開始時間、`sameTime` 結束時間不可與開始時間相同、`minDuration` 時間長度不可少於 {duration}、`maxDuration` 時間長度不可超過 {duration}。`{duration}` 以「1 小時 30 分鐘」（英文 `1 h 30 min`）顯示，單位字串在 `timeRangePicker.units`。

---

## 鍵盤與無障礙

| 位置 | 按鍵 | 行為 |
|---|---|---|
| 觸發框 | `Tab` | 依序到達開始端、結束端、清除按鈕 |
| 觸發框 | `Enter` / `Space` / `↓` | 開啟面板，焦點移到「小時」清單 |
| 觸發框 | `Esc` | 關閉面板 |
| 小時／分鐘清單 | `↑` / `↓` | 上／下一個**可選**的值（跳過不可選的時間） |
| 小時／分鐘清單 | `Home` / `End` | 第一個／最後一個可選的值 |
| 小時／分鐘清單 | `PageUp` / `PageDown` | 往前／往後 5 個可選的值 |
| 小時／分鐘清單 | `←` / `→` | 切換到小時欄／分鐘欄 |
| 小時／分鐘清單 | `Enter` | 確認；關閉後焦點回到觸發框 |
| 面板 | `Esc` | 關閉面板（不變更值），焦點回到觸發框 |
| 面板 | `Tab` / `Shift+Tab` | 在「小時、分鐘、確認」之間循環 |

- 外層 `role="group"`，以 `aria-labelledby`（有 `label` 時）或 `aria-label` 命名。
- 觸發框：`role="combobox"`、`aria-haspopup="dialog"`、`aria-expanded`、`aria-controls`、`aria-label`、必填時 `aria-required`，停用時 `tabindex="-1"` 與 `aria-disabled`；取得焦點時以 box-shadow 顯示外框。
- 面板：`role="dialog"`；小時、分鐘欄為 `role="listbox"`（`aria-label` 取自 TimePicker 的「小時」「分鐘」字串），項目為 `role="option"`，以 `aria-selected` 與 `aria-activedescendant` 表示目前選擇，不可選的項目 `aria-disabled="true"`。
- 以 `Tab` 從觸發框移到其他元素時，已開啟的面板會自動關閉。

---

## 範例

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>TimeRangePicker 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <div id="shift"></div>
    <button type="button" id="check">檢查</button>
    <p id="result"></p>

    <script type="module">
        import { TimeRangePicker } from './TimeRangePicker.js';

        const result = document.getElementById('result');
        const picker = new TimeRangePicker({
            label: '值班時段',
            required: true,
            minuteStep: 30,
            allowOvernight: true,
            minDurationMinutes: 60,
            maxDurationMinutes: 12 * 60,
            value: { start: '22:00', end: '06:00' },
            onChange: (value) => {
                picker.clearError();
                result.textContent = `${value.start ?? '—'} ~ ${value.end ?? '—'}`;
            }
        }).mount('#shift');

        document.getElementById('check').addEventListener('click', () => {
            if (picker.isValid()) picker.clearError();
            else picker.setError(picker.getValidationError());
        });
    </script>
</body>
</html>
```
