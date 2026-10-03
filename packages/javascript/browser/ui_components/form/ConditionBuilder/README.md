# ConditionBuilder 條件建構器

以「欄位＋運算子＋值」的規則與可巢狀的 且／或 群組，編輯布林篩選條件。產出的是**純資料**：元件不執行任何程式碼、不組 SQL 或查詢字串；把條件轉成查詢時，後端必須以白名單比對欄位與運算子，並以參數化方式帶入每個值。

值的形狀：

```javascript
{
    combinator: 'and',            // 'and' | 'or'
    not: false,                   // 只在 allowNot: true（或原值就是 true）時出現
    rules: [
        { field: 'status', operator: 'eq', value: 'open' },
        { combinator: 'or', rules: [ /* 巢狀群組 */ ] }
    ]
}
```

值編輯器沿用本庫既有元件：TextInput、NumberInput、DatePicker、Dropdown（searchable）、MultiSelectDropdown；`between` 使用兩個輸入框。

## 建構

```javascript
import { ConditionBuilder } from './ConditionBuilder.js';

const builder = new ConditionBuilder({
    fields: [],         // 欄位定義，見下表
    value: null,        // 初始條件；null 視為 { combinator: 'and', rules: [] }
    maxDepth: 3,        // 群組巢狀層數上限（根群組為第 1 層；到上限時不再顯示「新增群組」）
    maxRules: 50,       // 整棵樹的條件（葉節點）數上限；到上限時停用新增按鈕
    allowNot: false,    // 每個群組可勾選 NOT（反轉）
    disabled: false,    // 停用
    onChange: null      // (value) => void，使用者每次修改後觸發
});
builder.mount('#host');
```

欄位定義 `fields[]`：

| 屬性 | 說明 |
|---|---|
| `key` | 欄位代碼（必填、不可重複），寫入規則的 `field` |
| `label` | 顯示名稱，預設同 `key` |
| `type` | `'text'`（預設）\| `'number'` \| `'date'` \| `'select'` \| `'multiselect'` \| `'boolean'` |
| `options` | `select` / `multiselect` 的選項 `[{ value, label }]` |
| `operators` | 覆寫可用運算子：只能是該型別預設清單的子集，可調整順序；無效項目會被略過 |
| `editorOptions` | 轉交給值編輯器的選項，例如 NumberInput 的 `precision`、`min`、`max`，DatePicker 的 `format`，TextInput 的 `enableSecurity`；`value`、`items`、`disabled`、`onChange` 由本元件控制 |

## 運算子

| 型別 | 預設運算子（第一個為新規則的預設值） |
|---|---|
| text | `eq` `ne` `contains` `notContains` `startsWith` `endsWith` `isEmpty` `isNotEmpty` |
| number | `eq` `ne` `gt` `gte` `lt` `lte` `between` `isEmpty` `isNotEmpty` |
| date | `eq` `before` `after` `between` `isEmpty` `isNotEmpty` |
| select | `eq` `ne` `in` `notIn` |
| multiselect | `containsAny` `containsAll` |
| boolean | `isTrue` `isFalse` |

值的形狀依運算子而定：`isEmpty` / `isNotEmpty` / `isTrue` / `isFalse` 為 `null`；`between` 為 `[起, 迄]`；`in` / `notIn` / `containsAny` / `containsAll` 為陣列；其餘為單一值。數字存成 number，日期存成 `'YYYY-MM-DD'` 字串，選項存選項的 `value`。尚未填寫的值為 `null`。

`ConditionBuilder.DEFAULT_OPERATORS` 與 `ConditionBuilder.OPERATOR_ARITY` 提供上述對照表。

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載到容器（元素或選擇器），回傳 `this` |
| `destroy()` | 銷毀所有值編輯器（含浮出的月曆與下拉清單）、移除 DOM 與 `window` 上的語系監聽；可重複呼叫 |
| `getValue()` | 回傳條件資料（深拷貝） |
| `setValue(value, { emit = false })` | 設定條件並檢查形狀：無法辨識的節點略過並 `console.warn`；未知欄位或運算子的規則會保留、在畫面上標示，由 `validate()` 回報；規則與群組上的額外屬性（例如 `id`）會原樣保留。預設不觸發 `onChange` |
| `clear()` | 清成空的根群組；不觸發 `onChange` |
| `setDisabled(bool)` | 停用／啟用所有控制項 |
| `validate(value?)` | 回傳 `[{ path, code, message }]`，沒有問題時為空陣列；未傳入時檢查目前的值 |
| `describe(value?)` | 以目前語系把條件轉成可讀文字；未傳入時描述目前的值 |
| `setError(message, { display = true })` | 標示欄位錯誤（`aria-invalid`、外框、`role="alert"` 文字）；`display: false` 只標示狀態 |
| `clearError()` | 清除 `setError` 的標示與文字 |
| `show()` / `hide()` | 顯示／隱藏 |
| `snapshot()` | 回傳 lifecycle／visibility／availability 狀態 |

### validate()

`path` 描述節點位置，根群組為 `''`，其餘如 `'rules[1].rules[0]'`。`code` 為：

| code | 情況 |
|---|---|
| `missingValue` | 需要值卻沒有填（空字串、只含空白、`between` 缺一端、清單為空） |
| `invalidValue` | 值不符欄位型別（例如不存在的日期、不在選項內的值） |
| `badRange` | `between` 的起始值大於結束值 |
| `unknownField` / `unknownOperator` | 欄位或運算子不在定義中 |
| `emptyGroup` | 巢狀群組內沒有任何條件 |
| `maxDepth` / `maxRules` | 超過層數或條件數上限（多半來自 `setValue`） |

`message` 是目前語系的說明文字，例如「優先度：起始值不可大於結束值」。

### describe()

```text
狀態 等於 進行中 且 (優先度 大於 3 或 主旨 包含 「會議」)
Status equals Open AND (Priority is greater than 3 OR Title contains "Meeting")
```

巢狀群組加上括號，`not: true` 的群組以「非 (…)」/「NOT (…)」表示；沒有任何條件時回傳「（無條件）」/「(no conditions)」。

## 事件

`onChange(value)`：新增、移除、切換且／或、切換 NOT、變更欄位、運算子或值時觸發，參數為完整的條件資料。變更欄位時，若新欄位不支援原運算子會改用第一個運算子並清空值；型別不同或值不符新欄位時也會清空值。變更運算子時，值會轉成新形狀（例如 `eq` 的值變成 `in` 的一元陣列）。

## 鍵盤與無障礙

- 所有控制項都能以 Tab 到達：欄位與運算子為原生 `<select>`，且／或為兩個 `aria-pressed` 按鈕，NOT 為核取方塊，新增／移除為按鈕。每個控制項都有描述位置的 `aria-label`（例如「條件 2 的運算子」「移除第 2 層群組」）。
- 根元素、每個群組與每條規則都是 `role="group"`；巢狀群組以 `--cl-space-*` token 縮排，並以 token 色的導引線與底色區分層級。
- 新增規則或群組後，焦點移到新規則的欄位選單；移除後移到相鄰的規則，沒有相鄰規則時移到該群組的「新增條件」。新增／移除會以 `role="status"` 播報。
- 未知欄位或運算子：選單加上 `aria-invalid` 並以 `aria-describedby` 連到提示文字，原值以唯讀文字顯示。
- 日期：本版 DatePicker 只支援滑鼠操作，ConditionBuilder 另外補上鍵盤操作：觸發區可 Tab 聚焦（`role="button"`），Enter／空白鍵／↓ 開啟月曆並聚焦日期，方向鍵在日期間移動，Escape 或 Tab 移出月曆時關閉並回到觸發區。
- 單選值使用可搜尋的 Dropdown（可輸入篩選、↑／↓、Enter、Escape）；多選值使用 MultiSelectDropdown（↑／↓、Enter／空白鍵切換、Escape）。
- 語系切換時會即時重繪文字，值不變。

## 注意事項

- NumberInput 依 `precision` 顯示並在失焦時四捨五入，預設 `precision: 0`；需要小數的欄位請設定 `editorOptions: { precision: 2 }`。預設不限範圍（±`Number.MAX_SAFE_INTEGER`），空白時按 ↑／↓ 從 0 開始。
- TextInput 預設開啟 `enableSecurity`，輸入看起來像 SQL 或路徑的字時會顯示安全提醒；本元件只產生資料，若這個提醒在篩選情境造成困擾，可用 `editorOptions: { enableSecurity: false }` 關閉。

## 範例

```javascript
import { ConditionBuilder } from './ConditionBuilder.js';

const builder = new ConditionBuilder({
    fields: [
        { key: 'title', label: '主旨', type: 'text' },
        { key: 'priority', label: '優先度', type: 'number' },
        { key: 'due', label: '到期日', type: 'date' },
        { key: 'status', label: '狀態', type: 'select', options: [
            { value: 'open', label: '進行中' },
            { value: 'done', label: '已完成' }
        ] },
        { key: 'tags', label: '標籤', type: 'multiselect', options: [
            { value: 'urgent', label: '急件' },
            { value: 'review', label: '待審閱' }
        ] },
        { key: 'archived', label: '已封存', type: 'boolean' }
    ],
    value: {
        combinator: 'and',
        rules: [
            { field: 'status', operator: 'eq', value: 'open' },
            { combinator: 'or', rules: [
                { field: 'priority', operator: 'gt', value: 3 },
                { field: 'title', operator: 'contains', value: '會議' }
            ] }
        ]
    },
    allowNot: true,
    onChange: (value) => {
        document.querySelector('#summary').textContent = builder.describe(value);
    }
});
builder.mount('#host');

document.querySelector('#apply').addEventListener('click', () => {
    const errors = builder.validate();
    if (errors.length) {
        builder.setError(errors.map((error) => error.message).join('；'));
        return;
    }
    builder.clearError();
    fetch('/api/tasks/search', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(builder.getValue())
    });
});
```
