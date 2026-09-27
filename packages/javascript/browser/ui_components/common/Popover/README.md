# Popover 互動式浮動面板

依附在錨點元素旁的浮動面板。與 `Tooltip` 不同，面板內可以放按鈕、連結、表單等可互動內容，並負責焦點管理（自動聚焦、焦點陷阱、關閉後送回焦點）。

- 觸發方式：`click` / `hover` / `focus` / `manual`
- 開啟時移到 `document.body`，以 `position: fixed` 定位；空間不足時自動翻面，並平移留在視窗內
- 捲動、視窗縮放、錨點或面板尺寸改變時自動重新定位（以 `requestAnimationFrame` 合併）
- 錨點自動加上 `aria-haspopup` / `aria-expanded` / `aria-controls`，`destroy()` 時還原成原本的值
- `document` / `window` 監聽與 `ResizeObserver` 只在開啟期間掛載

## 建構子

```javascript
import { Popover } from './Popover.js';

const popover = new Popover({
    anchor: button,                          // 必填：錨點元素
    content: null,                           // Node、純文字字串（不解析 HTML），或回傳 Node / 字串的函式（每次開啟時呼叫）
    title: '',                               // 標題；有標題時以 aria-labelledby 作為面板名稱
    trigger: 'click',                        // 'click' | 'hover' | 'focus' | 'manual'
    placement: 'bottom-start',               // top / bottom / left / right，可加 -start 或 -end
    offset: 8,                               // 與錨點的距離（px）
    closeOnOutsideClick: true,               // 點擊面板與錨點以外的區域時關閉
    closeOnEscape: true,                     // 按 Escape 關閉
    autoFocus: true,                         // 以點擊或鍵盤開啟時，焦點移到面板內第一個可聚焦元素
    trapFocus: false,                        // Tab 焦點限制在面板內（role 為 dialog 時加上 aria-modal="true"）
    returnFocus: true,                       // 關閉時若焦點在面板內，送回錨點
    hoverDelay: { open: 150, close: 200 },   // hover 觸發的開啟 / 關閉延遲（ms）
    role: 'dialog',                          // 面板的 ARIA role
    ariaLabel: '',                           // 沒有 title 時的無障礙名稱
    width: null,                             // 面板寬度（數字視為 px）；null 依內容，最寬 360px
    closeButton: false,                      // 在標題列顯示關閉按鈕
    onOpen: null,                            // () 開啟後
    onClose: null                            // (reason) 關閉後
});
```

Popover 建構後即可使用，不一定要呼叫 `mount()`；`mount(container)` 只用來指定面板關閉期間的存放位置（未指定時，關閉期間面板不在 DOM 中）。

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 選用：面板關閉期間隱藏放在此容器，開啟時仍移到 `document.body`；回傳 `this` |
| `open({ focus })` | 開啟。程式呼叫預設不移動焦點；`{ focus: true }` 時比照鍵盤開啟套用 `autoFocus` |
| `close(reason = 'api')` | 關閉；`reason` 傳給 `onClose` |
| `toggle()` | 切換開關 |
| `isOpen()` | 是否開啟中 |
| `setContent(content)` | 更換內容；關閉期間傳入函式時，延到下次開啟才呼叫 |
| `updatePosition()` | 重新計算位置（開啟期間捲動、縮放與尺寸變化時會自動呼叫） |
| `destroy()` | 移除面板、全域監聽與計時器，還原錨點的 ARIA 屬性；焦點在面板內時送回錨點；不觸發 `onClose`；可重複呼叫 |

## 事件回呼

| 回呼 | 時機 |
|---|---|
| `onOpen()` | 開啟後 |
| `onClose(reason)` | 關閉後。`reason`：`trigger`（再次點擊錨點）、`outside`（點擊外部）、`escape`、`hover`（指標離開）、`blur`（焦點離開）、`close-button`、`api`（程式呼叫 `close()` / `toggle()`） |

## 觸發方式

| trigger | 行為 |
|---|---|
| `click` | 點擊錨點（或在按鈕錨點上按 Enter / Space）切換開關 |
| `hover` | 指標停在錨點 `hoverDelay.open` 後開啟；指標離開錨點與面板 `hoverDelay.close` 後關閉（移到面板上、或焦點在面板內時保持開啟）。錨點取得鍵盤焦點時立即開啟，焦點離開後延遲關閉 |
| `focus` | 錨點取得焦點時開啟；焦點離開錨點與面板時關閉 |
| `manual` | 不在錨點上掛任何觸發監聽，由 `open()` / `close()` / `toggle()` 控制 |

錨點應為可聚焦的元素（通常是 `<button>`），鍵盤使用者才能觸發。

## 定位

`placement` 指定偏好方向與對齊，例如 `bottom-start` 表示放在錨點下方、左緣對齊。偏好方向放不下而對側空間較大時翻到對側；再依視窗邊界（保留 8px）平移。實際採用的位置寫在面板的 `data-placement`。

## 外部點擊與巢狀浮層

`closeOnOutsideClick` 以 `mousedown`（捕獲階段）判斷。下列位置都不算「外部」：面板本身、錨點，以及其他浮在 body 上的浮層（帶 `data-portal="body"`，例如面板內 DatePicker 的月曆、TimePicker 的面板、巢狀開啟的 Popover）。因此面板內可以放日期 / 時間選擇器，也可以從面板內再開一層 Popover；點擊真正的頁面區域時，所有開著的 Popover 一起關閉。`focus` / `hover` 觸發的「焦點離開」判斷套用同一規則。

## 鍵盤與無障礙

| 按鍵 | 行為 |
|---|---|
| `Escape` | 關閉（`closeOnEscape`）。在面板或錨點上按下時停止事件傳遞，不會連帶關閉外層對話框；焦點在面板內時送回錨點 |
| `Tab`（錨點上） | 面板開啟時直接進入面板第一個可聚焦元素——面板在 DOM 中位於 body 末端，這樣維持「面板緊接在錨點之後」的順序 |
| `Shift+Tab`（面板第一個元素） | 回到錨點，面板保持開啟 |
| `Tab`（面板最後一個元素） | 移到錨點之後的下一個可聚焦元素並關閉面板（`manual` 不關閉） |
| `Tab` / `Shift+Tab`（`trapFocus: true`） | 在面板內循環 |

- 面板：`role`（預設 `dialog`）、`tabindex="-1"`；有標題時 `aria-labelledby` 指向標題，否則使用 `ariaLabel`；`trapFocus` 且 role 為 `dialog` 時加上 `aria-modal="true"`。
- 錨點：`aria-haspopup`（role 為 dialog / menu / listbox / tree / grid 時）、`aria-expanded`、`aria-controls`；`destroy()` 還原成建構前的值（原本沒有就移除）。
- 字串內容一律以純文字呈現；需要結構化內容時傳入自行建立的 Node。
- 關閉按鈕的標籤走 `Locale.t('popover.close')`（zh-TW「關閉」、en「Close」），每次開啟時更新。

## 範例

```javascript
import { Popover } from './ui_components/common/Popover/index.js';

const button = document.createElement('button');
button.type = 'button';
button.textContent = '欄位設定';
document.querySelector('#host').appendChild(button);

function buildSettings() {
    const form = document.createElement('div');
    for (const name of ['負責人', '截止日', '狀態']) {
        const label = document.createElement('label');
        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.checked = true;
        label.append(checkbox, ` ${name}`);
        form.appendChild(label);
    }
    const apply = document.createElement('button');
    apply.type = 'button';
    apply.textContent = '套用';
    apply.addEventListener('click', () => popover.close());
    form.appendChild(apply);
    return form;
}

const popover = new Popover({
    anchor: button,
    title: '任務清單欄位',
    content: buildSettings,
    closeButton: true,
    placement: 'bottom-end',
    onClose: (reason) => console.log('關閉原因', reason)
});

// 不再需要時
// popover.destroy();
```
