# DatePicker 日期選擇器

高度可自訂的日期選擇器組件，支援**民國年（ROC）格式**和**日期範圍限制**功能。

## 功能特性

- ✅ **雙曆法支援**: 西元年 / 民國年（ROC）格式切換
- ✅ **日期範圍限制**: 設定最小/最大可選日期
- ✅ **三種尺寸**: Small、Medium、Large
- ✅ **視覺回饋**: 禁用日期自動標示為灰色不可點擊
- ✅ **月份導航**: 下拉選單快速切換年月
- ✅ **今日標記**: 自動高亮顯示今天日期
- ✅ **響應式設計**: 自適應不同螢幕尺寸
- ✅ **無外部依賴**: 純 JavaScript 實作

---

## 安裝

### ES6 模組導入

```javascript
import { DatePicker } from './packages/javascript/browser/ui_components/form/DatePicker/DatePicker.js';
```

### CSS 樣式（可選）

DatePicker 使用 inline styles，但如果需要全域樣式調整，可以導入 CSS 檔：

```html
<link rel="stylesheet" href="./packages/javascript/browser/ui_components/form/DatePicker/DatePicker.css">
```

---

## 基本用法

### 1. 西元年日期選擇器

```javascript
const picker = new DatePicker({
    label: '選擇日期',
    placeholder: '請選擇日期',
    onChange: (date, formatted) => {
        console.log('選擇的日期:', date);
        console.log('格式化輸出:', formatted); // 2026/01/24
    }
});

picker.mount('#date-container');
```

### 2. 民國年日期選擇器

```javascript
const pickerROC = new DatePicker({
    format: 'taiwan',  // 使用民國年格式
    label: '出生日期',
    placeholder: '請選擇日期',
    onChange: (date, formatted) => {
        console.log('格式化輸出:', formatted); // 115/01/24
    }
});

pickerROC.mount('#date-roc-container');
```

### 3. 限制未來日期（只能選擇今天或過去）

```javascript
const pickerPast = new DatePicker({
    max: new Date(),  // 最大日期為今天
    placeholder: '選擇過去日期'
});

pickerPast.mount('#date-past-container');
```

### 4. 限制過去日期（只能選擇今天或未來）

```javascript
const pickerFuture = new DatePicker({
    min: new Date(),  // 最小日期為今天
    placeholder: '選擇未來日期'
});

pickerFuture.mount('#date-future-container');
```

### 5. 限制特定日期範圍

```javascript
const past30 = new Date();
past30.setDate(past30.getDate() - 30);

const pickerRange = new DatePicker({
    min: past30,        // 30 天前
    max: new Date(),    // 今天
    placeholder: '選擇過去 30 天'
});

pickerRange.mount('#date-range-container');
```

---

## API 參考

### 構造函數選項

| 參數 | 類型 | 預設值 | 說明 |
|------|------|--------|------|
| `label` | String | `''` | 標籤文字 |
| `placeholder` | String | `'請選擇日期'` | 未選擇時的提示文字 |
| `value` | Date | `null` | 初始日期值 |
| `disabled` | Boolean | `false` | 是否禁用 |
| `required` | Boolean | `false` | 是否必填（顯示紅色星號） |
| `size` | String | `'medium'` | 尺寸：`'small'` / `'medium'` / `'large'` |
| `useROC` | Boolean | `false` | 是否使用民國年格式（舊參數，建議改用 `format`） |
| `format` | String | `'western'` | 日期格式：`'western'`（西元）/ `'taiwan'`（民國） |
| `min` | Date | `null` | 最小可選日期 |
| `max` | Date | `null` | 最大可選日期 |
| `className` | String | `''` | 自訂 CSS 類別名稱 |
| `onChange` | Function | `null` | 日期變更回調函數：`(date, formatted) => {}` |

### 公開方法

#### `mount(container)`
將組件掛載到指定容器。

```javascript
picker.mount('#container');
// 或
picker.mount(document.getElementById('container'));
```

**參數**:
- `container` (String | HTMLElement): CSS 選擇器字串或 DOM 元素

**返回**: HTMLElement (組件的根元素)

---

#### `render(container)`
渲染組件並掛載到容器（與 `mount()` 功能相同）。

```javascript
const element = picker.render('#container');
```

---

#### `getValue()`
獲取當前選擇的日期（Date 物件）。

```javascript
const selectedDate = picker.getValue();
console.log(selectedDate); // Date object or null
```

**返回**: Date | null

---

#### `getFormattedValue()`
獲取格式化後的日期字串。

```javascript
const formatted = picker.getFormattedValue();
console.log(formatted); // "2026/01/24" 或 "115/01/24"
```

**返回**: String

---

#### `setValue(value)`
設定日期值。

```javascript
picker.setValue(new Date('2026-01-24'));
```

**參數**:
- `value` (Date | String): 日期物件或可轉換為日期的字串

---

#### `clear()`
清除當前選擇的日期。

```javascript
picker.clear();
```

---

#### `open()`
打開日曆面板。

```javascript
picker.open();
```

---

#### `close()`
關閉日曆面板。

```javascript
picker.close();
```

---

#### `toggle()`
切換日曆面板的開啟/關閉狀態。

```javascript
picker.toggle();
```

---

#### `destroy()`
銷毀組件並從 DOM 中移除。

```javascript
picker.destroy();
```

---

## 進階範例

### 範例 1: 民國年 + 日期範圍限制

```javascript
const picker = new DatePicker({
    format: 'taiwan',       // 民國年格式
    label: '入職日期',
    required: true,
    min: new Date(2020, 0, 1),    // 2020/01/01
    max: new Date(),               // 今天
    onChange: (date, formatted) => {
        console.log('民國年:', formatted);     // 115/01/24
        console.log('西元年:', date.getFullYear()); // 2026
    }
});

picker.mount('#join-date');
```

### 範例 2: 限制特定月份

```javascript
// 只能選擇 2026 年 1 月
const picker = new DatePicker({
    label: '選擇 2026 年 1 月日期',
    min: new Date(2026, 0, 1),   // 2026/01/01
    max: new Date(2026, 0, 31),  // 2026/01/31
    size: 'large'
});

picker.mount('#specific-month');
```

### 範例 3: 動態更新日期範圍

```javascript
const picker = new DatePicker({
    label: '選擇日期',
    placeholder: '請選擇日期'
});

picker.mount('#dynamic-date');

// 根據業務邏輯動態更新範圍
document.getElementById('limit-future').addEventListener('click', () => {
    picker.options.max = new Date();
    picker._renderCalendar();
    console.log('已限制為今天或之前的日期');
});
```

### 範例 4: 表單整合

```javascript
const form = document.getElementById('myForm');

const startDatePicker = new DatePicker({
    label: '開始日期',
    required: true,
    onChange: (date) => {
        // 動態設定結束日期的最小值
        endDatePicker.options.min = date;
        endDatePicker._renderCalendar();
    }
});

const endDatePicker = new DatePicker({
    label: '結束日期',
    required: true
});

startDatePicker.mount('#start-date');
endDatePicker.mount('#end-date');

form.addEventListener('submit', (e) => {
    e.preventDefault();

    const startDate = startDatePicker.getValue();
    const endDate = endDatePicker.getValue();

    if (!startDate || !endDate) {
        alert('請選擇日期');
        return;
    }

    console.log('開始日期:', startDate);
    console.log('結束日期:', endDate);
});
```

### 範例 5: 三種尺寸對比

```javascript
const sizes = ['small', 'medium', 'large'];

sizes.forEach(size => {
    new DatePicker({
        label: `${size.toUpperCase()} 尺寸`,
        size: size,
        placeholder: `${size} size picker`
    }).mount(`#picker-${size}`);
});
```

---

## 日期格式說明

### 西元年格式 (format: 'western')

```javascript
// 輸出格式: YYYY/MM/DD
new DatePicker({
    format: 'western'
});

// 選擇 2026 年 1 月 24 日
// 顯示: 2026/01/24
```

### 民國年格式 (format: 'taiwan')

```javascript
// 輸出格式: YYY/MM/DD (民國年)
new DatePicker({
    format: 'taiwan'
});

// 選擇 2026 年 1 月 24 日（民國 115 年）
// 顯示: 115/01/24
// 年份下拉選單顯示: 民國 115 年
```

**民國年轉換公式**: 民國年 = 西元年 - 1911

---

## 日期範圍限制

### 視覺效果

超出範圍的日期會自動套用以下樣式：

| 屬性 | 效果 |
|------|------|
| `color` | 灰色 (`#ccc`) |
| `opacity` | 半透明 (0.5) |
| `cursor` | 禁止圖標 (`not-allowed`) |

### 日期標準化

DatePicker 會自動移除時間部分，只比較年月日：

```javascript
const picker = new DatePicker({
    min: new Date(),  // 2026-01-24 15:30:45
    // 內部轉換為: 2026-01-24 00:00:00
});
```

這確保了日期比較的準確性。

### 雙重驗證機制

1. **渲染層面**: 禁用日期添加 `data-disabled="true"` 屬性
2. **事件層面**: 點擊事件檢查 `data-disabled` 屬性
3. **邏輯層面**: `_selectDate()` 方法內再次檢查日期範圍

這種三層防護確保絕對不會選擇超出範圍的日期。

---

## 事件回調

### onChange 回調函數

當用戶選擇日期時觸發，接收兩個參數：

```javascript
const picker = new DatePicker({
    onChange: (date, formattedString) => {
        // date: Date 物件
        console.log('Date 物件:', date);
        console.log('年份:', date.getFullYear());
        console.log('月份:', date.getMonth() + 1);
        console.log('日期:', date.getDate());

        // formattedString: 格式化字串
        console.log('格式化輸出:', formattedString);
    }
});
```

**參數說明**:
- `date` (Date): JavaScript Date 物件
- `formattedString` (String): 根據 `format` 參數格式化的日期字串

---

## 樣式自訂

### 使用 className 自訂樣式

```javascript
const picker = new DatePicker({
    className: 'my-custom-datepicker'
});
```

```css
/* 自訂樣式 */
.my-custom-datepicker .datepicker__input-wrapper {
    border: 2px solid #667eea !important;
    border-radius: 12px !important;
}

.my-custom-datepicker .datepicker__calendar {
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.2) !important;
}
```

### 尺寸對照表

| 尺寸 | 高度 | 內距 | 字體大小 |
|------|------|------|----------|
| `small` | 32px | 0 8px | 13px |
| `medium` | 40px | 0 12px | 14px |
| `large` | 48px | 0 16px | 16px |

---

## 向後相容性

### useROC 參數（已棄用）

舊版本使用 `useROC` 參數控制民國年格式，新版本建議改用 `format` 參數：

```javascript
// 舊方式（仍然支援）
new DatePicker({
    useROC: true
});

// 新方式（推薦）
new DatePicker({
    format: 'taiwan'
});
```

兩種方式功能完全相同，但 `format` 參數更直觀易懂。

---

## 瀏覽器相容性

| 瀏覽器 | 最低版本 |
|--------|----------|
| Chrome | 90+ |
| Firefox | 88+ |
| Safari | 14+ |
| Edge | 90+ |

**必要條件**:
- ES6 模組支援
- CSS Grid 支援
- Flexbox 支援

---

## 常見問題

### Q1: 如何清空已選擇的日期？

```javascript
picker.clear();
```

### Q2: 如何獲取當前選擇的日期？

```javascript
const date = picker.getValue();        // Date 物件
const formatted = picker.getFormattedValue();  // 字串
```

### Q3: 如何禁用組件？

```javascript
const picker = new DatePicker({
    disabled: true
});
```

### Q4: 如何設定初始值？

```javascript
// 方式 1: 構造函數設定
const picker = new DatePicker({
    value: new Date('2026-01-24')
});

// 方式 2: 使用 setValue 方法
picker.setValue(new Date('2026-01-24'));
```

### Q5: 日期範圍可以動態更新嗎？

可以，但需要手動觸發重新渲染：

```javascript
picker.options.min = new Date('2026-01-01');
picker.options.max = new Date('2026-12-31');

// 更新 minDate 和 maxDate
const minDate = new Date(picker.options.min);
picker.minDate = new Date(minDate.getFullYear(), minDate.getMonth(), minDate.getDate());

const maxDate = new Date(picker.options.max);
picker.maxDate = new Date(maxDate.getFullYear(), maxDate.getMonth(), maxDate.getDate());

// 重新渲染日曆
picker._renderCalendar();
```

### Q6: 如何整合到表單驗證？

```javascript
const form = document.getElementById('myForm');
const picker = new DatePicker({
    required: true
});

picker.mount('#date-field');

form.addEventListener('submit', (e) => {
    const date = picker.getValue();

    if (!date) {
        e.preventDefault();
        alert('請選擇日期');
        return;
    }

    // 表單提交...
});
```

---

## 完整範例

```html
<!DOCTYPE html>
<html lang="zh-TW">
<head>
    <meta charset="UTF-8">
    <title>DatePicker 完整範例</title>
</head>
<body>
    <div style="max-width: 600px; margin: 50px auto;">
        <h1>DatePicker 範例</h1>

        <!-- 西元年選擇器 -->
        <div id="date-western" style="margin-bottom: 20px;"></div>

        <!-- 民國年選擇器 -->
        <div id="date-taiwan" style="margin-bottom: 20px;"></div>

        <!-- 未來日期限制 -->
        <div id="date-future" style="margin-bottom: 20px;"></div>

        <!-- 過去日期限制 -->
        <div id="date-past" style="margin-bottom: 20px;"></div>

        <!-- 特定範圍 -->
        <div id="date-range" style="margin-bottom: 20px;"></div>

        <button onclick="showResults()">顯示所有選擇的日期</button>
        <div id="results" style="margin-top: 20px; padding: 15px; background: #f5f5f5; border-radius: 8px;"></div>
    </div>

    <script type="module">
        import { DatePicker } from './packages/javascript/browser/ui_components/form/DatePicker/DatePicker.js';

        // 西元年選擇器
        const pickerWestern = new DatePicker({
            label: '西元年日期',
            placeholder: '選擇日期',
            size: 'large'
        });
        pickerWestern.mount('#date-western');

        // 民國年選擇器
        const pickerTaiwan = new DatePicker({
            label: '民國年日期',
            format: 'taiwan',
            placeholder: '選擇日期',
            size: 'large'
        });
        pickerTaiwan.mount('#date-taiwan');

        // 未來日期限制
        const pickerFuture = new DatePicker({
            label: '未來日期（今天或之後）',
            min: new Date(),
            placeholder: '選擇未來日期'
        });
        pickerFuture.mount('#date-future');

        // 過去日期限制
        const pickerPast = new DatePicker({
            label: '過去日期（今天或之前）',
            max: new Date(),
            placeholder: '選擇過去日期'
        });
        pickerPast.mount('#date-past');

        // 特定範圍（過去 30 天）
        const past30 = new Date();
        past30.setDate(past30.getDate() - 30);

        const pickerRange = new DatePicker({
            label: '過去 30 天',
            min: past30,
            max: new Date(),
            placeholder: '選擇過去 30 天'
        });
        pickerRange.mount('#date-range');

        // 顯示結果
        window.showResults = () => {
            const results = document.getElementById('results');
            results.innerHTML = `
                <h3>選擇的日期：</h3>
                <p><strong>西元年：</strong>${pickerWestern.getFormattedValue() || '未選擇'}</p>
                <p><strong>民國年：</strong>${pickerTaiwan.getFormattedValue() || '未選擇'}</p>
                <p><strong>未來日期：</strong>${pickerFuture.getFormattedValue() || '未選擇'}</p>
                <p><strong>過去日期：</strong>${pickerPast.getFormattedValue() || '未選擇'}</p>
                <p><strong>過去 30 天：</strong>${pickerRange.getFormattedValue() || '未選擇'}</p>
            `;
        };
    </script>
</body>
</html>
```

---

## 相關檔案

- **組件原始碼**: [DatePicker.js](DatePicker.js:1)
- **樣式檔案**: [DatePicker.css](DatePicker.css:1)
- **Demo 範例**: `demos/form/DatePicker.html`
- **測試頁面**: `demos/test_datepicker_range.html`
- **實作報告**: `demos/DATEPICKER_RANGE_COMPLETE.md`

---

## 更新日誌

### v2.0.0 (2026-01-24)
- ✨ 新增 `min` 和 `max` 參數支援日期範圍限制
- ✨ 新增 `format` 參數（`'western'` / `'taiwan'`）
- ✨ 禁用日期視覺效果（灰色、半透明、不可點擊）
- 🐛 修復民國年顯示錯誤問題
- 📚 新增完整文檔和測試頁面

### v1.0.0
- 🎉 初始版本
- ✅ 基本日期選擇功能
- ✅ 民國年支援（`useROC` 參數）

---

## 授權

MIT License

---

**Bricks4Agent** - Pure JavaScript UI Component Library
