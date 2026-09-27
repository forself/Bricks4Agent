# 介面守衛工具：PermissionGate 與 DirtyGuard

兩個跨頁面共用的工具，都是零依賴的 ES module：

| 工具 | 檔案 | 用途 |
|---|---|---|
| PermissionGate | `utils/permission-gate.js` | 依「能力字串」隱藏或停用畫面元素（前端 UX） |
| DirtyGuard | `utils/dirty-guard.js` | 追蹤尚未儲存的變更，離開前請使用者確認 |

字串分別註冊在 `permissionGate`、`dirtyGuard` 命名空間（`permission-gate.locale.js`、`dirty-guard.locale.js`，匯入工具時自動註冊），提供 zh-TW 與 en。

---

## PermissionGate

> **安全警告：PermissionGate 不是存取控制。**
> 它只是讓介面「不顯示或不能點」使用者沒有權限的功能，改善操作體驗。使用者可以用瀏覽器開發者工具移除 `hidden`／`disabled`，也可以直接呼叫 API。
> **每一個能力都必須由伺服器端在每個請求再次驗證**；前端閘門擋不住任何請求，也不能用來保護資料。

### 建立

```js
import { createPermissionGate } from './ui_components/utils/permission-gate.js';

const gate = createPermissionGate({
    can,                 // (capability, context) => boolean | Promise<boolean>（必填）；只有 true 才算允許
    mode: 'hide',        // 預設處理方式：'hide' 隱藏、'disable' 停用
    context: null,       // 傳給 can() 的第二個參數，例如目前使用者
    deniedReason: null   // disable 模式的提示原因：字串或 (requirement, context) => string；null 用 Locale 預設
});
```

能力字串對 PermissionGate 是**不透明**的，不做任何解析（`'a || b'` 就是一個名為 `a || b` 的能力）。組合條件只用物件：

| 寫法 | 意義 |
|---|---|
| `'orders.delete'` | 需要這個能力 |
| `{ anyOf: ['rooms.book', 'rooms.admin'] }` | 任一項成立即可 |
| `{ allOf: ['tasks.edit', 'tasks.close'] }` | 全部成立才可 |
| `{ allOf: [...], anyOf: [...] }` | allOf 全部成立，且 anyOf 至少一項成立 |

空字串、空陣列或型別錯誤會丟出 `TypeError`。

`can()` 回傳 `true`（或 resolve 成 `true`）才算允許；`false`、其他值、丟出例外或 reject 一律視為拒絕（fail closed），並以 `console.warn` 提示。同一輪 `apply`／`scan`／`refresh` 中，同一個能力只會問一次 `can()`。

### gate 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `apply(target, requirement, { mode, reason } = {})` | handle | 對一個目標套用權限。`target` 可以是 Element 或 B4A 元件；`reason` 為字串或函式，只用於 disable 模式 |
| `scan(root = document)` | handle[] | 處理 `root`（含 root 本身）內所有宣告 `data-permission*` 的元素。已處理且宣告未變的元素沿用原 handle；宣告改變時釋放舊的再重建 |
| `refresh()` | `Promise<void>` | 重新評估所有已套用的目標（登入、登出、角色變更後呼叫） |
| `setContext(context, { refresh = true } = {})` | `Promise<void>` | 更新傳給 `can()` 的 context，預設立即 `refresh()` |
| `getContext()` | any | 目前的 context |
| `destroy()` | void | 釋放所有 handle 並還原所有目標；之後的呼叫都不會丟例外（`apply` 回傳已釋放的空 handle、`scan` 回傳 `[]`） |

### handle

| 成員 | 說明 |
|---|---|
| `allowed` | `true`／`false`；非同步結果尚未回來時為 `null` |
| `pending` | 是否有評估進行中 |
| `ready` | 最近一次評估完成的 Promise（resolve 成 `allowed`） |
| `released` | 是否已釋放 |
| `refresh()` | 只重新評估這個目標 |
| `release()` | 停止套用，並把目標還原成**第一次套用前的原樣**（屬性值、`disabled`、`hidden`、`title`、行內 `display`）；可重複呼叫 |
| `target`、`mode`、`requirement` | 建立時的參數（requirement 為正規化後的 `{ allOf, anyOf }`） |

### 模式行為

**hide**

- Element：設定 `hidden` 屬性。只有在 `hidden` 被行內 `display` 或作者樣式蓋掉、元素仍然顯示時，才另外加上 `display: none !important` 與 `aria-hidden="true"`。
- 元件：有 `hide()`／`show()` 時呼叫它們（有 `setVisibility()` 的面板會還原原本的可見狀態值）；否則改對 `component.element` 做上述 DOM 處理。原本就隱藏的元件，還原後維持隱藏。

**disable**

- 原生可停用的元素（button、input、select、textarea、fieldset…）：設定 `disabled`。
- 所有 Element：設定 `aria-disabled="true"`，並以原因文字設定 `title`（滑鼠提示）與 `aria-description`（螢幕報讀器描述）。
- 沒有原生 `disabled` 的元素（連結、`role="button"` 的元素）：在元素上以捕獲階段攔下 `click`、`auxclick`，以及元素本身的 Enter／Space `keydown`；元素仍可聚焦，讓使用者讀到原因。
- 元件：有 `setDisabled()` 時呼叫 `setDisabled(true)`，還原時回到原本的停用狀態；原因文字寫在 `component.element`。沒有 `setDisabled()` 時改對 `component.element` 做 DOM 處理。
- 原因順序：`apply` 的 `reason` → gate 的 `deniedReason` → Locale `permissionGate.deniedReason`。給空字串 `''` 表示不顯示原因。

> 要停用整個區塊，請把目標設為 `<fieldset>`（原生停用所有子控制項），或改用 hide 模式。

### 宣告式屬性（scan）

| 屬性 | 說明 |
|---|---|
| `data-permission="a"` | 需要 `a`；以逗號分隔多個時**全部**需要（`"a, b"` 等同 allOf） |
| `data-permission-all="a, b"` | 全部需要 |
| `data-permission-any="a, b"` | 任一項即可 |
| `data-permission-mode="disable"` | 覆寫 gate 預設模式；未知值用預設並警告 |
| `data-permission-reason="…"` | disable 模式的原因文字 |

同一元素可混用；`data-permission` 與 `data-permission-all` 合併成 allOf。宣告了屬性卻沒有任何能力（例如 `data-permission=""`）時視為拒絕並警告。

### 非同步、重新評估與組合

- **不閃爍**：非同步 `can()` 的結果回來前，目標維持目前狀態。第一次套用時會先保持原樣（可見、可用），結果回來才隱藏或停用；若不希望首次載入時短暫出現，請在權限載入完成前不要顯示該區塊，或 `await handle.ready` 後再顯示。
- **過期結果丟棄**：較早開始、較晚回來的評估結果會被忽略；`release()`／`destroy()` 之後回來的結果也會被忽略。
- **多個 handle 疊加**：同一目標可被多個 handle（甚至多個 gate）套用。任一 handle 拒絕就維持隱藏／停用；全部放行或全部釋放後，才還原成第一次套用前的狀態。
- 套用期間請勿由其他程式碼改動同一個屬性；還原時會寫回套用前的值。若其他程式碼重新啟用了元素，呼叫 `refresh()` 會再次套用。
- 不會在 `document`／`window` 上掛任何監聽；DOM 寫入會批次處理，所有寫入完成後才一次讀取樣式。
- 以 `scan()` 處理後被移出 DOM 的元素仍由 gate 持有；頁面卸載時請呼叫 `gate.destroy()`（或逐一 `release()`）。

### 範例

```html
<div id="order-toolbar">
    <button type="button" data-permission="orders.create">新增訂單</button>
    <button type="button" data-permission="orders.export" data-permission-mode="disable">匯出</button>
    <a href="#/orders/archive" data-permission-any="orders.archive, orders.admin">封存區</a>
</div>
```

```js
import { createPermissionGate } from './ui_components/utils/permission-gate.js';

const gate = createPermissionGate({
    can: (capability, user) => Boolean(user && user.permissions.includes(capability)),
    context: currentUser,
    deniedReason: '請聯絡管理員開通此功能'
});

gate.scan(document.getElementById('order-toolbar'));
gate.apply(deleteButton, { allOf: ['orders.delete', 'orders.edit'] }, { mode: 'disable' });

// 角色變更後
await gate.setContext(nextUser);

// 頁面卸載
gate.destroy();
```

---

## DirtyGuard

追蹤表單或元件是否有尚未儲存的變更；離開頁面（關閉分頁、重新整理）時交給瀏覽器的 `beforeunload` 提示，SPA 內的路由切換或關閉按鈕則用 `confirmLeave()`／`wrap()` 顯示確認對話框。

### 建立

```js
import { createDirtyGuard } from './ui_components/utils/dirty-guard.js';

const guard = createDirtyGuard({
    confirm: null,        // (message, { dirtyKeys }) => boolean | Promise<boolean>；回傳 true 表示離開。null 用 ModalPanel.confirm
    message: null,        // 確認訊息：字串或 (dirtyKeys) => string；null 用 Locale（dirtyGuard.message）
    beforeUnload: true    // 有未儲存變更時是否掛 beforeunload 提示
});
```

### 追蹤來源

`guard.track(source, { key })` 回傳 `untrack()` 函式。`key` 預設自動產生（`source-1`…）；重複的 key 會先取消舊的追蹤。

| 來源 | 判斷方式 |
|---|---|
| 表單／容器 Element | 監聽內部 `input`、`change` 事件，比對所有 `input`／`select`／`textarea` 值的快照（改回原值即恢復乾淨）。`button`／`submit`／`reset`／`image` 不計入；`[data-dirty-ignore]` 內的控制項不計入（例如表單內的搜尋框）。點擊（新增／刪除列）與 `reset` 事件後也會重新比對 |
| 具 `isDirty()` 的物件 | 狀態由物件自己決定；`guard.markClean()` 會呼叫它的 `markClean()`（若有） |
| 具 `getValue()` 的元件 | `track` 當下記錄基準值快照；以深度比較判斷，物件鍵的順序不影響結果，值為 `undefined` 的鍵忽略；Date、Map、Set、循環參照都可處理 |

B4A 元件改值時多半只呼叫 `options.onChange`，不發 DOM 事件，下拉選單或日曆也可能渲染在元件外。因此只要有元件或 `isDirty()` 物件被追蹤，DirtyGuard 會在 `document` 以捕獲階段監聽 `input`、`change`、`click`、`keyup`、`pointerup`，並在事件處理結束後（`setTimeout 0`，同一時間只排一次）重新比對；沒有這類來源時不掛這些監聽。**程式化改值**（例如 `setValue()`）請接著呼叫 `guard.check()`。

請在載入資料之後才 `track()`，或載入後呼叫 `markClean()`，否則載入本身會被當成變更。

### 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `track(source, { key })` | `Function` | 開始追蹤，回傳 `untrack()` |
| `check()` | `boolean` | 立即重新讀取所有來源（程式化改值後呼叫），回傳是否有未儲存變更 |
| `isDirty()` | `boolean` | 同 `check()` |
| `getDirtyKeys()` | `string[]` | 有未儲存變更的 key（依 track 順序） |
| `markClean(key?)` | void | 以目前值作為新基準（儲存成功後呼叫）；不給 key 表示全部 |
| `onChange(listener)` | `Function` | dirty key 集合改變時呼叫 `listener(dirty, dirtyKeys)`；回傳取消訂閱函式。listener 丟出的例外會被攔下 |
| `confirmLeave()` | `Promise<boolean>` | 沒有未儲存變更時直接 `true`；否則詢問，選擇離開為 `true`。對話框開啟期間重複呼叫共用同一個結果 |
| `wrap(fn)` | `Function` | 回傳 async 函式：`confirmLeave()` 為 true 才執行 `fn`（保留 `this` 與參數，resolve 成 `fn` 的回傳值；取消時為 `undefined`） |
| `destroy()` | void | 移除所有監聽與計時器；開啟中的預設對話框會關閉，等待中的 `confirmLeave()` 得到 `false`。之後的呼叫都不會丟例外 |

### beforeunload

- 只在**有未儲存變更**且 `beforeUnload: true` 時才在 `window` 掛 `beforeunload` 監聽；變乾淨或 `destroy()` 時移除（不影響瀏覽器的往返快取）。
- 觸發時會先重新比對，確定仍有變更才 `preventDefault()` 並設定 `returnValue`。現代瀏覽器只顯示內建文字，自訂 `message` 只用於確認對話框。
- 使用者在確認對話框選擇「離開」後，`beforeunload` 會暫停，直到內容再次變動；避免接著做整頁導向時又跳出第二次瀏覽器提示。

### 確認對話框、鍵盤與無障礙

預設 `confirm` 使用 `ModalPanel.confirm`（`destroyOnClose: true`，關閉後自行銷毀），標題、訊息、按鈕文字來自 Locale（「尚未儲存的變更」「離開」「留在此頁」）：

- 對話框面板設定 `role="alertdialog"`、`aria-modal="true"`，並以 `aria-labelledby`／`aria-describedby` 指向標題與訊息。
- 開啟時焦點放在「留在此頁」（不會遺失資料的選項）；Tab／Shift+Tab 在對話框內循環。
- Esc、右上角關閉鈕、「留在此頁」都得到 `false`；「離開」得到 `true`。關閉後焦點回到開啟前的元素。
- 自訂 `confirm` 只有回傳（或 resolve 成）`true` 才算同意離開；丟出例外或 reject 視為留下。

### 範例

```js
import { createDirtyGuard } from './ui_components/utils/dirty-guard.js';
import { TextArea } from './ui_components/form/TextArea/TextArea.js';

const form = document.getElementById('task-form');
const notes = new TextArea({ value: task.notes }).mount('#task-notes');

const guard = createDirtyGuard();
guard.track(form, { key: 'task' });
guard.track(notes, { key: 'notes' });
guard.onChange((dirty) => saveButton.setDisabled(!dirty));

saveButton.addEventListener('click', async () => {
    await saveTask();
    guard.markClean();
});

// SPA 路由離開前
router.beforeLeave(() => guard.confirmLeave());

// 關閉抽屜前
closeButton.addEventListener('click', guard.wrap(() => drawer.close()));

// 頁面卸載
guard.destroy();
```
