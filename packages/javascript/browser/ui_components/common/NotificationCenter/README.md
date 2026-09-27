# NotificationCenter

通知中心（複合元件）。鈴鐺按鈕加未讀徽章，點開是浮出面板，列出一串通知並追蹤已讀狀態。

與 `common/Notification` 的差別：`Notification` 是一次性的 Toast（自動消失、不保存）；`NotificationCenter` 保存通知清單、記錄已讀、由使用者主動開啟瀏覽，兩者可以並用（例如新通知到達時同時彈 Toast 並 `add()` 進通知中心）。

- 觸發鈕的無障礙名稱含未讀數；徽章超過 99 顯示「99+」。
- 面板浮出方式比照 `form/Dropdown`：面板留在元件內（維持 DOM 契約），開啟時改 `position: fixed` 依觸發鈕定位，不受上層 `overflow` 裁切；上層有 `transform`／`filter` 時補償座標；下方空間不足時翻到上方。
- `document`／`window` 監聽只在面板開啟期間掛載。
- 項目的 `href` 一律經 `sanitizeUrl()`；不安全的網址退回按鈕。全部以 `createElement` + `textContent` 建構，符合嚴格 CSP；鈴鐺為 `common/Icon`（Canvas）。

## 建構

```js
import { NotificationCenter } from './index.js';

const center = new NotificationCenter({
    items: [],               // 通知（新到舊）：[{ id, title, message?, time?, read?: false, variant?: 'info'|'success'|'warning'|'danger', href? }]
    maxItems: 100,           // 記憶體中最多保留幾則，超過時丟棄最舊的；0 或非有限數＝不限
    emptyText: null,         // 沒有通知時的文字；null 使用 Locale notificationCenter.empty
    markReadOnOpen: false,   // 開啟面板時把目前未讀全部標為已讀（觸發 onMarkRead）
    markReadOnClick: true,   // 點選項目時標為已讀（觸發 onMarkRead）
    onItemClick: null,       // (item, event) => void；item 為呼叫端原物件
    onMarkRead: null,        // (ids) => void；只在使用者操作造成已讀變化時呼叫
    onLoadMore: null,        // async () => items[] | { items, hasMore } | void；提供且 hasMore 時顯示「載入更多」
    hasMore: false,          // 是否還有更舊的通知
    ariaLabel: null,         // 觸發鈕的基本名稱，也是面板標題；null 使用 Locale notificationCenter.label
    formatTime: null,        // (date: Date, item) => string；null 使用 Intl.DateTimeFormat(Locale 語言, medium／short)
    announceDelay: 1000      // 未讀數播報的節流間隔（ms）
});
center.mount('#header-actions');
```

### item 物件

| 欄位 | 型別 | 說明 |
|---|---|---|
| `id` | `string \| number` | 識別碼；去重、`markRead`、`remove` 以它比對（`'1'` 與 `1` 視為相同） |
| `title` | `string` | 標題 |
| `message` | `string?` | 內容（保留換行） |
| `time` | `string \| Date \| number?` | 時間；無法解析的字串原樣顯示 |
| `read` | `boolean?` | 是否已讀，預設 `false`；同 id 再次加入且未指定時沿用原本的已讀狀態 |
| `variant` | `'info' \| 'success' \| 'warning' \| 'danger'` | 類型（圖示、顏色與視覺隱藏的類型文字）；預設 `info` |
| `href` | `string?` | 有值時項目是連結（經 `sanitizeUrl()`），否則是按鈕 |

### 清單規則

- 清單順序一律「新到舊」。`add()` 把新通知放在最上方；傳陣列時陣列本身視為新到舊。
- 同 id 去重：`add()` 取代舊的並移到最上方；同一批內重複的 id 只保留第一筆。
- 超過 `maxItems` 時丟棄最舊（尾端）的通知。清單已達上限時不顯示「載入更多」。
- `onLoadMore` 回傳陣列或 `{ items, hasMore }` 時，較舊的通知接在尾端（已存在的 id 不重複加入）；回傳 `{ hasMore }` 會更新狀態，也可以在 callback 內自行呼叫 `setHasMore()`。失敗（reject）時恢復按鈕並以 live region 播報「載入失敗」。

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `setItems(items)` | `this` | 以新陣列（新到舊）取代全部通知 |
| `add(itemOrItems)` | `this` | 新增一則或多則放在最上方；同 id 取代並移到最上方 |
| `markRead(ids, { emit = false })` | `this` | 標為已讀；預設不觸發 `onMarkRead`，傳 `{ emit: true }` 才觸發 |
| `markAllRead({ emit = false })` | `this` | 全部標為已讀；同上 |
| `remove(id)` | `this` | 移除指定 id |
| `getItems()` | `object[]` | 目前的通知（新到舊；淺拷貝並帶上目前的 `read`） |
| `getUnreadCount()` | `number` | 未讀數 |
| `setHasMore(hasMore)` | `this` | 設定是否還有更舊的通知 |
| `open()` | `this` | 開啟面板並把焦點移入面板 |
| `close()` | `this` | 關閉面板；焦點原本在面板內（或已遺失）時還給觸發鈕 |
| `isOpen()` | `boolean` | 面板是否開啟 |
| `snapshot()` | `object` | 狀態機快照：`{ lifecycle, open, hasMore, loading }` |
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `destroy()` | `void` | 移除 DOM、Icon、所有監聽與計時器（開啟中也可直接呼叫）；可重複呼叫，之後呼叫其他方法不會拋錯 |

## 事件（callback）

| Callback | 參數 | 觸發時機 |
|---|---|---|
| `onItemClick` | `(item, event)` | 點選項目（連結或按鈕）。`item` 是呼叫端原物件；SPA 可在這裡 `event.preventDefault()` 後自行導頁 |
| `onMarkRead` | `(ids)` | 使用者操作讓項目變成已讀：點選項目（`markReadOnClick`）、「全部標為已讀」、開啟面板（`markReadOnOpen`）。只含這次真正變動的 id |
| `onLoadMore` | `()` | 點「載入更多」；回傳 Promise，完成前按鈕顯示「載入中…」並忽略重複點擊 |

點選項目的順序：標為已讀（`onMarkRead`）→ 關閉面板並把焦點還給觸發鈕 → `onItemClick`。以 Ctrl／⌘／Shift 或滑鼠中鍵點連結（開新分頁）時面板保持開啟。已讀只就地更新項目外觀，不重繪清單，所以連結點擊後瀏覽器仍會正常導覽。

## 鍵盤與無障礙

| 按鍵 | 行為 |
|---|---|
| Enter ／ Space（觸發鈕） | 開關面板（原生按鈕） |
| Escape | 關閉面板並把焦點還給觸發鈕；事件不再往外傳，外層對話框不會一起關閉 |
| ↓ ／ ↑ | 在項目之間移動（焦點在面板本身時 ↓ 到第一則） |
| Home ／ End | 第一則／最後一則 |
| Tab | 在面板內依序經過「全部標為已讀」、各項目、「載入更多」；Tab 離開元件時面板自動關閉（不搶焦點） |

- 觸發鈕：`aria-haspopup="dialog"`、`aria-expanded`、`aria-controls` 指向面板；無障礙名稱例如「通知（3 則未讀）」（Locale `triggerLabel` 的 `{count}` 為實際數字，不受 99+ 限制）。徽章 `aria-hidden="true"`。
- 面板：`role="dialog"`、`aria-modal="false"`、`aria-labelledby` 指向 `<h2>` 標題；開啟時焦點移到面板。
- 點面板外會關閉；如果使用者點到的是其他可聚焦元素，焦點留在那裡，只有焦點遺失（落在 body）時才還給觸發鈕。外部點擊以 capture 監聽，其他元素 `stopPropagation()` 也關得掉。
- 未讀以粗體標題、圓點與視覺隱藏的「未讀」文字表示；非 `info` 類型另有視覺隱藏的類型文字（例如「警告」）。
- 「全部標為已讀」沒有未讀時為 `aria-disabled="true"`（仍可聚焦，點擊不動作）；載入中清單 `aria-busy="true"`。
- 未讀數變動以 live region（`role="status"`、`aria-live="polite"`）依 `announceDelay` 節流播報，例如「4 則未讀通知」；初始資料不播報，數字沒變也不播報。
- `Locale.setLang()` 會即時更新觸發鈕名稱、標題、按鈕與項目文字。

## Locale（namespace：`notificationCenter`）

`label`、`triggerLabel`（`{label}`、`{count}`）、`markAllRead`、`loadMore`、`loading`、`loadFailed`、`empty`、`unread`、`unreadAnnouncement`（`{count}`）、`noUnread`、`variant.info`、`variant.success`、`variant.warning`、`variant.danger`。字串由同目錄 `locale.js` 註冊 zh-TW 與 en。

## 可執行範例

把下列內容存成本目錄的 `example.html`，以 HTTP 伺服器開啟（例如在 repo 根目錄執行 `python -m http.server 8124`；ES module 不能用 `file://`）。

```html
<!DOCTYPE html>
<html lang="zh-TW" data-theme="light">
<head>
    <meta charset="UTF-8">
    <title>NotificationCenter 範例</title>
    <link rel="stylesheet" href="../../theme.css">
</head>
<body>
    <header>
        <div id="header-actions"></div>
    </header>
    <script type="module">
        import { NotificationCenter } from './index.js';

        let page = 0;
        const center = new NotificationCenter({
            items: [
                { id: 'n3', title: '新的任務指派', message: '請於週五前完成庫存盤點', time: new Date(), variant: 'warning' },
                { id: 'n2', title: '會議室預約已確認', message: 'A 室 週三 10:00', href: '#/rooms/a' },
                { id: 'n1', title: '訂單已出貨', read: true, variant: 'success' }
            ],
            hasMore: true,
            onItemClick: (item, event) => console.log('開啟', item.id),
            onMarkRead: (ids) => console.log('同步已讀', ids),
            onLoadMore: async () => {
                page += 1;
                const items = [{ id: `old-${page}`, title: `較舊的通知 ${page}`, read: true }];
                return { items, hasMore: page < 3 };
            }
        }).mount('#header-actions');

        // 模擬推播
        setTimeout(() => center.add({ id: 'n4', title: '專案文件已更新', variant: 'info' }), 3000);
    </script>
</body>
</html>
```
