# Transfer 雙清單選擇器

左側列出可選項目、右側列出已選項目，勾選後用中間的按鈕、Enter 或雙擊在兩側之間移動。適合從大量主檔資料（人員、會議室、設備、專案）中挑選一組值，並可選擇性地排列已選順序。

- 兩側清單都是 `role="listbox"` + `aria-multiselectable="true"`，以 roving tabindex 管理焦點。
- 已選清單維持 `value` 的順序；新移入的項目依可選清單的順序附加在最後。
- 每側都有獨立的搜尋框：不分大小寫、不分重音（`normalize('NFKD')` 後去掉變音符號，全形字母也能比對）。
- 值元件：提供 `getValue` / `setValue` / `setDisabled` / `clear` 與欄位錯誤契約 `setError` / `clearError`。

## 建構

```javascript
import { Transfer } from './Transfer.js';

const transfer = new Transfer({
    items: [],              // [{ value, label, description?, disabled? }]；value 以 SameValueZero 比對
    value: [],              // 已選值（依序）；不存在於 items 的值與重複值會被略過
    titles: null,           // [可選清單標題, 已選清單標題]；null 用語系預設（可選項目／已選項目）
    searchable: true,       // 每側清單上方的搜尋框
    showSelectAll: true,    // 標題列的全選核取方塊（只作用於目前顯示的項目）
    maxSelected: null,      // 已選上限；null 不限（只限制使用者操作，不截斷 setValue）
    sortable: false,        // 已選清單可用上移／下移按鈕與 Alt+↑／↓ 排序
    height: '280px',        // 清單高度（CSS 長度字串，或數字視為 px）
    renderItem: null,       // (item) => Node；自訂每列內容，預設顯示 label 與 description
    disabled: false,        // 停用
    onChange: null          // (values, { moved, direction }) => void
});
transfer.mount('#host');
```

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載到容器（元素或選擇器），回傳 `this` |
| `destroy()` | 移除 DOM 與 `window` 上的語系監聽；可重複呼叫，之後呼叫其他方法不會拋錯 |
| `getValue()` | 回傳已選值陣列（依序，為複本） |
| `setValue(values, { emit = false })` | 設定已選值；預設不觸發 `onChange`，`emit: true` 時以 `{ moved: [], direction: null }` 觸發 |
| `setItems(items)` | 替換 `items`；已選值中不存在的項目會被移除 |
| `clear()` | 清空已選值、勾選與搜尋字；不觸發 `onChange` |
| `setDisabled(bool)` | 停用／啟用整個元件 |
| `setError(message, { display = true })` | 標示欄位錯誤：兩個清單加上 `aria-invalid` 與 `aria-describedby`、清單外框標紅、元件下方顯示 `role="alert"` 文字；`display: false` 只標示狀態 |
| `clearError()` | 清除 `setError` 的標示與文字 |
| `show()` / `hide()` | 顯示／隱藏 |
| `snapshot()` | 回傳內部狀態的複本（lifecycle、availability、value、checked、query） |

## 事件

`onChange(values, { moved, direction })`：使用者移動或排序後觸發。

| direction | 時機 | moved |
|---|---|---|
| `'right'` | 移到已選清單 | 被移入的值（依可選清單順序） |
| `'left'` | 移回可選清單 | 被移出的值（依原已選順序） |
| `'up'` / `'down'` | 排序（`sortable`） | 實際改變位置的值 |

`values` 永遠是移動後完整的已選值陣列（複本）。

## 鍵盤與無障礙

焦點在清單項目上時：

| 按鍵 | 動作 |
|---|---|
| ↑ / ↓、Home / End、PageUp / PageDown | 移動焦點（每側只有一個 Tab 停駐點） |
| 空白鍵 | 切換目前項目的勾選 |
| Shift＋↑ / ↓（Home / End / PageUp / PageDown） | 從錨點延伸勾選到新位置 |
| Shift＋空白鍵 | 勾選錨點到目前項目的範圍 |
| Ctrl / Cmd＋A | 勾選目前顯示的所有可用項目；已全部勾選時改為全部取消 |
| Enter | 移動勾選的項目；沒有勾選時移動目前項目 |
| Alt＋↑ / ↓ | （`sortable`，已選清單）上移／下移勾選的項目，沒有勾選時移動目前項目 |

- 搜尋框：↓ 進入清單、Escape 清除搜尋字、Enter 不會送出外層表單。
- 滑鼠：點擊切換勾選、Shift＋點擊勾選範圍、雙擊只移動被雙擊的那一項。
- 中間的 → / ← 按鈕與排序的 ↑ / ↓ 按鈕都有 `aria-label`，沒有可操作的項目時為 `disabled`；按鈕因移動而變成停用時，焦點會移回清單而不會遺失。
- 清單以 `aria-labelledby` 連到標題、以 `aria-describedby` 連到操作說明；每列帶 `aria-selected`（勾選）、`aria-disabled`、`aria-setsize`、`aria-posinset`。
- 移動、排序與達到上限時，以 `role="status"` 的隱藏區域播報結果。
- 語系切換（`Locale.setLang`）時會即時更新所有文字。

## 大量資料

採**分段渲染**：每側先建立前 100 列；捲動接近底部，或以鍵盤移到尚未建立的位置（例如 End）時才附加下一段。勾選只重繪有變化的列，搜尋與全選為 O(n)，2,000 筆資料可流暢操作。由於不是虛擬捲動，捲到底後所有列都會留在 DOM 中；資料量遠大於數千筆時，建議先以後端查詢縮小範圍。

## 範例

```javascript
import { Transfer } from './Transfer.js';

const rooms = Array.from({ length: 30 }, (_, index) => ({
    value: `room-${index + 1}`,
    label: `會議室 ${index + 1}`,
    description: index % 2 ? '可視訊' : '一般',
    disabled: index === 3
}));

const transfer = new Transfer({
    items: rooms,
    value: ['room-2'],
    maxSelected: 5,
    sortable: true,
    titles: ['可預約的會議室', '本次使用'],
    onChange: (values, { moved, direction }) => {
        console.log('已選', values, '本次移動', moved, direction);
        if (values.length) transfer.clearError();
    }
});
transfer.mount('#host');

document.querySelector('#save').addEventListener('click', () => {
    if (!transfer.getValue().length) transfer.setError('請至少選擇一間會議室');
});
```
