# RemoteSelect 遠端搜尋下拉選單

選項來自非同步來源（API）的可搜尋下拉選單，實作 WAI-ARIA combobox 模式。適合選項太多、無法一次載入的情境，例如從人員名單或會議室清單中挑選。

- 輸入後防抖查詢；新查詢會中止舊請求，晚到的過期回應一律丟棄
- 載入中、查無資料、載入失敗（可重試）三種狀態
- 分頁：捲到清單底部，或以鍵盤選取「載入更多」
- 同一查詢字串的結果在元件存活期間快取
- 單選 / 多選（標籤呈現、可設上限）
- 欄位錯誤標示契約（`setError` / `clearError`）

## 建構子

```javascript
import { RemoteSelect } from './RemoteSelect.js';

const select = new RemoteSelect({
    fetchOptions: async (query, { page, pageSize, signal }) => ({ items: [], hasMore: false }), // 必填：非同步取得選項
    debounce: 300,               // 停止輸入多少毫秒後才查詢
    minQueryLength: 1,           // 查詢字串（去除頭尾空白後）最少字元數；0 表示展開即以空字串查詢
    pageSize: 20,                // 每頁筆數，原樣傳給 fetchOptions
    multiple: false,             // 多選模式
    value: null,                 // 單選初始值
    values: [],                  // 多選初始值
    initialItems: [],            // 初始值的標籤來源 [{ value, label, description? }]
    resolveLabels: null,         // async (values) => items；已選值查不到標籤時呼叫
    placeholder: '輸入關鍵字搜尋', // 預設取自語系 remoteSelect.placeholder
    clearable: true,             // 有選取值時顯示清除按鈕
    disabled: false,             // 停用
    width: '100%',               // 元件寬度（數字視為 px）
    maxSelected: null,           // 多選上限；null 表示不限
    cacheResults: true,          // 依查詢字串快取結果，存活到 destroy()
    ariaLabel: '',               // 沒有可見標籤時給輸入框與清單的無障礙名稱
    onChange: null,              // 單選 (value, item)；多選 (values, items)
    onError: null                // (error) 查詢或標籤解析失敗
});
```

### fetchOptions 合約

- 參數：`query`（已去除頭尾空白）與 `{ page, pageSize, signal }`，`page` 從 1 開始。
- 回傳 `{ items, hasMore }`；也接受直接回傳陣列（視為沒有下一頁）。`items` 每筆至少要有 `value`；缺少 `label` 時以 `String(value)` 顯示；可另帶 `description`（第二行說明）與 `disabled`。
- `signal` 是 `AbortSignal`：查詢被新字串取代、查詢字串被重設、元件停用或銷毀時會中止。交給 `fetch(url, { signal })` 即可省下多餘的網路流量；就算來源忽略 signal，過期回應也不會蓋掉新結果（請求序號守衛）。
- 拋錯或 reject 時顯示「載入失敗」與「重試」按鈕，並呼叫 `onError(error)`。
- 所有文字都以 `textContent` 呈現，不解析 HTML。

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載到容器（元素或選擇器），回傳 `this` |
| `getValue()` | 單選回傳值或 `null`；多選回傳值陣列（副本） |
| `setValue(value, item?)` | 設定選取值，不觸發 `onChange`。單選傳值與選用的項目物件（提供標籤）；多選傳值陣列與選用的項目陣列；多選超過 `maxSelected` 的部分捨棄。查不到標籤且有 `resolveLabels` 時會非同步解析 |
| `getSelectedItems()` | 已選項目陣列（單選也回傳陣列）；查不到的項目以 `{ value, label: String(value) }` 代替 |
| `setDisabled(bool)` | 設定停用狀態；停用時收合清單並中止進行中的請求 |
| `clear()` | 清除選取值與查詢字串，不觸發 `onChange` |
| `setError(msg, { display })` / `clearError()` | 標示 / 清除欄位錯誤：紅框、`aria-invalid` 與錯誤文字；`display: false` 只標示狀態、不顯示文字 |
| `refresh()` | 清空快取並重新執行目前的查詢，回傳查詢完成的 Promise |
| `focus()` | 聚焦輸入框 |
| `open()` / `close()` | 展開 / 收合清單；`close()` 保留查詢字串（同第一次 Escape） |
| `snapshot()` | 目前內部狀態的快照（除錯用） |
| `destroy()` | 中止進行中的請求、清除計時器與監聽並移除 DOM；可重複呼叫，之後呼叫其他方法不會拋錯 |

## 事件回呼

| 回呼 | 時機 |
|---|---|
| `onChange(value, item)` | 單選：使用者選取項目（值有改變時），或按清除按鈕（`(null, null)`） |
| `onChange(values, items)` | 多選：使用者新增、取消或移除標籤 |
| `onError(error)` | `fetchOptions` 失敗，或 `resolveLabels` 失敗 |

`setValue()` 與 `clear()` 不觸發 `onChange`。

## 行為細節

- **單選**：選取後清單收合，輸入框顯示選取項目的標籤；聚焦時全選文字，直接輸入即開始新查詢。焦點離開元件時放棄未確認的查詢字串，輸入框回到已選標籤。
- **多選**：選取後清單保持展開並保留查詢字串，方便連續挑選；再選一次已選項目即取消。達到 `maxSelected` 時其餘項目標示為停用，並顯示「最多可選 N 項」。
- **載入中**：防抖期間與請求進行中顯示「載入中…」，清單標示 `aria-busy="true"`，控制框右側顯示轉動圖示。
- **分頁**：`hasMore` 為 true 時清單末端出現「載入更多」項目；捲到底部自動載入下一頁（載入失敗後不因捲動自動重試）。以鍵盤載入更多時，高亮移到第一筆新項目。
- **快取**：每個查詢字串（含已載入的分頁）快取到 `destroy()`；`cacheResults: false` 關閉快取；`refresh()` 清空快取並重查。
- **標籤解析**：預設值的標籤依序取自 `initialItems`、已查到的結果；都沒有時呼叫 `resolveLabels(values)`，解析期間顯示「載入中…」，失敗時顯示原始值並呼叫 `onError`。
- **浮層**：清單展開時留在元件內，改以 `position: fixed` 依控制框座標定位（上層有 transform / filter 時自動補償），下方空間不足時翻到上方。`document` / `window` 監聽只在展開期間掛載。

## 鍵盤與無障礙

| 按鍵 | 行為 |
|---|---|
| 輸入文字 | 展開清單並（防抖後）查詢 |
| `ArrowDown` / `ArrowUp` | 收合時展開，並在有結果時高亮第一 / 最後一項；展開時移動高亮（略過停用項目） |
| `Home` / `End` | 清單展開時高亮第一 / 最後一項 |
| `Enter` | 選取高亮項目；高亮「載入更多」時載入下一頁；載入失敗且沒有高亮項目時重試。清單收合時不攔截，表單照常送出 |
| `Escape` | 第一次收合清單並保留字串，第二次清除查詢字串。有處理時停止事件傳遞，不會連帶關閉外層對話框 |
| `Backspace` | 多選且輸入框為空時移除最後一個標籤 |
| `Tab` | 可到達每個標籤的移除按鈕（`<button>`，Enter / Space 移除，移除後焦點回到輸入框） |

- 輸入框：`role="combobox"`、`aria-autocomplete="list"`、`aria-haspopup="listbox"`、`aria-expanded`、`aria-controls`（指向清單）、`aria-activedescendant`（高亮項目）。
- 清單：`role="listbox"`；多選時 `aria-multiselectable="true"`；載入中 `aria-busy="true"`。選項為 `role="option"`，帶 `aria-selected`，停用項目帶 `aria-disabled="true"`；已選項目除了底色，另有 ✓ 符號與粗體。
- 狀態列為 `role="status"`：載入中、查無資料、錯誤、字數提示直接顯示；有結果時只給螢幕閱讀器「共 N 筆結果」。
- 已選標籤清單為 `role="list"`，移除按鈕的 `aria-label` 為「移除 {標籤}」。
- 聚焦時控制框顯示主色邊框與外光暈；錯誤狀態以 outline 紅框標示，不與聚焦樣式互相覆蓋。
- 所有文字走 `Locale.t('remoteSelect.*')`，提供 zh-TW 與 en。

## 範例

```javascript
import { RemoteSelect } from './ui_components/form/RemoteSelect/index.js';

// 模擬的人員資料來源：依關鍵字過濾、分頁，並尊重 AbortSignal。
// 實際使用時改成 fetch(`/api/staff?q=...&page=...`, { signal })。
const staff = Array.from({ length: 120 }, (_, i) => ({
    value: i + 1,
    label: `人員 ${i + 1}`,
    description: `分機 ${1000 + i}`
}));

function searchStaff(query, { page, pageSize, signal }) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            const matches = staff.filter((item) => item.label.includes(query));
            const start = (page - 1) * pageSize;
            resolve({ items: matches.slice(start, start + pageSize), hasMore: start + pageSize < matches.length });
        }, 200);
        signal?.addEventListener('abort', () => {
            clearTimeout(timer);
            reject(new DOMException('Aborted', 'AbortError'));
        });
    });
}

const select = new RemoteSelect({
    fetchOptions: searchStaff,
    multiple: true,
    maxSelected: 3,
    values: [5],
    initialItems: [{ value: 5, label: '人員 5' }],
    ariaLabel: '與會人員',
    onChange: (values, items) => console.log('已選', values, items)
}).mount('#host');

// 送出前驗證
document.querySelector('#submit').addEventListener('click', () => {
    if (select.getValue().length === 0) select.setError('請至少選擇一位與會人員');
    else select.clearError();
});
```
