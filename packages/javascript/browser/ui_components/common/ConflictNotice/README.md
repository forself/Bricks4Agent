# ConflictNotice

樂觀並行控制（optimistic concurrency）衝突提示。儲存時伺服器回報「這筆資料在你編輯期間已被別人更新」（例如 HTTP 409、版本號或 ETag 不符），用 ConflictNotice 告訴使用者哪些欄位不同，並讓使用者選擇下一步：

| 動作 | 意義 |
|---|---|
| `'reload'` | 放棄自己的變更，載入伺服器上的最新資料 |
| `'overwrite'` | 以自己的版本覆寫伺服器資料（預設需要再確認一次） |
| `'cancel'` | 先不處理，回到編輯畫面 |

提供兩種呈現方式：

- **對話框**：`ConflictNotice.show(options)` 以 `ModalPanel`（`destroyOnClose: true`）顯示，回傳 `Promise<'reload' | 'overwrite' | 'cancel'>`。
- **行內**：`new ConflictNotice(options).mount(container)`，Alert 風格的 `role="alert"` 區塊，透過 `onResolve(action)` 或 `notice.result` 取得選擇。

> ConflictNotice 只負責「告知與取得選擇」。實際的重新載入、覆寫（通常是帶上 `force` 或新版本號重送）都由呼叫端執行；伺服器仍須自行檢查版本與權限。

## 建構子

```js
new ConflictNotice({
    title: null,               // 標題；null 時用 Locale（conflictNotice.title）
    message: null,             // 說明文字；null 時用 Locale（conflictNotice.message），'' 表示不顯示
    diffs: [],                 // 差異列：[{ field, label, local, server }]，一律以純文字顯示
    serverUpdatedBy: null,     // 伺服器端最後修改者（純文字）
    serverUpdatedAt: null,     // 伺服器端最後修改時間：Date、ISO 字串或毫秒數；無法解析時原樣顯示
    dateTimeFormat: { dateStyle: 'medium', timeStyle: 'short' }, // Intl.DateTimeFormat 選項（時間與 Date 值共用）
    actions: ['reload', 'overwrite', 'cancel'], // 顯示哪些選項與順序；未知值忽略，空陣列回到預設
    confirmOverwrite: true,    // 選擇覆寫時先在同一區塊內要求再確認一次
    danger: true,              // 覆寫按鈕（與確認覆寫按鈕）使用 danger 樣式
    labels: {},                // 覆寫 conflictNotice 命名空間的任一字串，例如 { reload: '重新整理' }
    formatValue: null,         // (value, { diff, side }) => string；回傳 undefined 改用預設格式
    onResolve: null            // (action) => void：使用者做出選擇時呼叫
});
```

### diffs

| 屬性 | 說明 |
|---|---|
| `field` | 欄位代碼；未給 `label` 時顯示它，也會寫到該列的 `data-field` |
| `label` | 顯示用欄位名稱 |
| `local` | 使用者這邊的值（「您的值」欄） |
| `server` | 伺服器目前的值（「目前的值」欄） |

預設值格式：`null`、`undefined`、`''` 顯示 Locale 的 `conflictNotice.empty`（—）；布林值顯示「是／否」；`Date` 依目前語系以 `Intl.DateTimeFormat` 格式化；陣列以「, 」串接；其他物件以 `JSON.stringify` 顯示。代碼類的值（狀態碼、選項值）請用 `formatValue` 轉成顯示文字。

所有文字（欄位名稱、值、修改者名稱）都以 `textContent` 寫入，資料中的 HTML 不會被解析。

## 靜態方法與屬性

| 名稱 | 說明 |
|---|---|
| `ConflictNotice.show(options)` | 開啟對話框，回傳 `Promise<'reload' \| 'overwrite' \| 'cancel'>`。Esc 與右上角關閉鈕一律視為 `'cancel'`（即使 `actions` 不含 `'cancel'`），點遮罩不會關閉。`options.onResolve` 也會收到同一個結果 |
| `ConflictNotice.ACTIONS` | `{ RELOAD: 'reload', OVERWRITE: 'overwrite', CANCEL: 'cancel' }` |

## 實例方法與屬性

| 名稱 | 回傳 | 說明 |
|---|---|---|
| `mount(containerOrSelector)` | `this` | 掛到容器（Element 或選擇器）；找不到容器時 `console.warn` |
| `focus()` | `this` | 聚焦最安全的選項（第一個不是覆寫的按鈕）；確認步驟中聚焦「返回」 |
| `snapshot()` | `object` | `{ lifecycle, step: 'choose' \| 'confirm' \| 'done', resolved }` |
| `destroy()` | `void` | 移除 DOM 與監聽；可重複呼叫。尚未選擇就銷毀時，`result` 以 `'cancel'` 結束（不呼叫 `onResolve`） |
| `result` | `Promise<string>` | 使用者的選擇 |
| `element` | `HTMLElement` | 根元素（銷毀後為 `null`） |

## 事件／回呼

- `onResolve(action)`：使用者選擇 `reload`、`cancel`，或完成覆寫確認時呼叫一次。之後所有按鈕停用，避免重複送出（例如連點兩次覆寫）。要再次詢問請建立新的 ConflictNotice。
- 回呼丟出的例外會被攔下並 `console.error`，不影響元件狀態。

## 鍵盤與無障礙

- 所有選項都是原生 `<button type="button">`：Tab 移動、Enter／Space 觸發，保留瀏覽器的焦點外框。
- 選擇覆寫且 `confirmOverwrite` 為 true 時，選項列隱藏、改顯示確認區塊（`role="group"`，以確認訊息作為 `aria-labelledby`），焦點移到「返回」；Esc 或「返回」回到選項列並把焦點放回覆寫按鈕。
- 對話框：面板設定 `role="alertdialog"`、`aria-modal="true"`、`aria-labelledby`（標題）與 `aria-describedby`（說明文字）；開啟時焦點放在最安全的選項，Tab／Shift+Tab 在對話框內循環；在選項列按 Esc 由 ModalPanel 關閉並得到 `'cancel'`，確認步驟中的 Esc 只會回到上一步；關閉後焦點回到開啟前的元素。
- 行內：根元素 `role="alert"`（插入時由螢幕報讀器朗讀），`aria-labelledby` 指向標題、`aria-describedby` 指向說明文字。行內版本不會自動搶走焦點，需要時呼叫 `focus()`。
- 差異表格有 `<caption>`，欄標題為 `<th scope="col">`、欄位名稱為 `<th scope="row">`。

## 樣式

只使用主題 token：行內區塊為 `--cl-warning` 邊框與 `--cl-warning-light` 底色，確認區塊為 `--cl-danger` 邊框與 `--cl-bg-danger-light` 底色；深色主題由 `[data-theme="dark"]` 的 token 值處理。

## 範例

```js
import { ConflictNotice } from './ui_components/common/ConflictNotice/index.js';

async function saveMeetingRoom(form, draft) {
    const response = await fetch(`/api/rooms/${draft.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'If-Match': draft.version },
        body: JSON.stringify(draft)
    });
    if (response.status !== 409) return response;

    const current = await response.json();
    const action = await ConflictNotice.show({
        diffs: [
            { field: 'name', label: '會議室名稱', local: draft.name, server: current.name },
            { field: 'capacity', label: '容納人數', local: draft.capacity, server: current.capacity },
            { field: 'projector', label: '投影機', local: draft.projector, server: current.projector }
        ],
        serverUpdatedBy: current.updatedBy,
        serverUpdatedAt: current.updatedAt
    });

    if (action === 'reload') form.load(current);
    if (action === 'overwrite') return saveMeetingRoom(form, { ...draft, version: current.version });
    return null;
}

// 行內版本：顯示在表單上方
const notice = new ConflictNotice({
    diffs: [{ field: 'status', label: '狀態', local: 1, server: 2 }],
    formatValue: (value, { diff }) => (diff.field === 'status' ? ['草稿', '已送出', '已完成'][value] : undefined),
    onResolve: (action) => {
        notice.destroy();
        if (action === 'reload') location.reload();
    }
}).mount('#form-messages');
notice.focus();
```
