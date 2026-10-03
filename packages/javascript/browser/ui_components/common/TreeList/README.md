# TreeList

樹狀導航列表。支援無限層級、父節點與葉節點皆可選取（單選高亮）、箭頭獨立展開/收合、四種主題，並提供兩項**預設關閉**的進階能力：

- **勾選多選**（`checkable`）：每個節點前顯示勾選框，預設上下連動並呈現半選（mixed）；`checkStrictly: true` 可關閉連動。
- **延遲載入**（`loadChildren`）：節點第一次展開時才非同步載入子節點，含「載入中」提示與失敗重試。

兩項能力都關閉時，DOM 結構、class、`data-*` 屬性、樣式、選取與展開行為都與先前版本相同；唯一差異是新增的 WAI-ARIA 屬性與 roving `tabindex`（見〈鍵盤與無障礙〉）。

## 建構子

```js
import { TreeList } from './TreeList.js';

const tree = new TreeList({
    data: [],               // 樹狀資料，節點格式見下方〈節點資料〉
    activeId: null,         // 初始選取（高亮）的節點 id；會自動展開其祖先
    onSelect: null,         // (node) => void，點擊整列、Enter（或未勾選模式下的 Space）選取時呼叫
    width: '260px',         // 容器寬度
    theme: 'modern',        // 'minimal' | 'classic' | 'modern' | 'dark'
    ariaLabel: '',          // 樹的無障礙名稱，寫入 role="tree" 的 aria-label；空字串則不設定
    checkable: false,       // true = 每個節點顯示勾選框
    checkStrictly: false,   // true = 勾選不上下連動、沒有半選
    checkedKeys: [],        // 初始勾選的節點 id（陣列或 Set）
    onCheck: null,          // (checkedKeys, { node, checked }) => void，只在使用者操作時呼叫
    loadChildren: null,     // async (node) => children[]；設定後才啟用延遲載入
});
tree.mount('#tree');
```

### 節點資料

| 欄位 | 型別 | 說明 |
|---|---|---|
| `id` | `string \| number` | 節點唯一鍵。選取、展開、勾選、延遲載入都以 `===` 比對（數字 `1` 與字串 `'1'` 不同），必須在整棵樹中唯一 |
| `label` | `string` | 顯示文字（以 `textContent` 寫入，不解析 HTML） |
| `icon` | `string` | Icon 註冊名稱（如 `'calendar'`）或最多兩個字元的 emoji；其他值（含 HTML/SVG 字串）會被拒絕並改顯示 `help` 圖示 |
| `children` | `Array` | 子節點。未提供時為葉節點；啟用延遲載入時則代表「尚未載入」 |
| `disabled` | `boolean` | 勾選框停用（見〈勾選規則〉）。為維持既有行為，**不會**阻擋單選 |
| `disableCheckbox` | `boolean` | 只停用勾選框，效果與 `disabled` 在勾選上相同 |
| `isLeaf` | `boolean` | 延遲載入模式下標示「確定沒有子節點」，不顯示展開箭頭 |
| `hasChildren` | `boolean` | 延遲載入模式下標示「有子節點待載入」，即使 `children` 是空陣列也會顯示展開箭頭 |

## 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `mount(container)` | `this` | 掛載到元素或 CSS 選擇器 |
| `destroy()` | `void` | 移除 DOM、容器事件、所有 Icon；進行中的延遲載入結果會被丟棄。可重複呼叫，之後呼叫其他方法不會拋錯 |
| `setData(data)` | `this` | 取代整棵資料並重繪；保留展開與勾選狀態，清除延遲載入快取（仍展開的延遲節點會自動重新載入） |
| `setActive(id)` | `this` | 設定選取節點並展開其祖先；不觸發 `onSelect` |
| `setActiveId(id)` | `this` | `setActive` 的別名 |
| `setTheme(name)` | `void` | 切換主題並重繪 |
| `getCheckedKeys({ leafOnly = false, includeIndeterminate = false } = {})` | `Array` | 目前勾選的 id。已載入的依樹狀順序排列，之後接著「已設定為勾選但尚未載入」的 id。`leafOnly` 只回傳沒有已載入子節點的節點；`includeIndeterminate` 一併回傳半選節點 |
| `setCheckedKeys(keys)` | `this` | 以程式設定勾選，**不觸發** `onCheck`。連動模式下會帶動子孫、推導父節點；停用節點照給定值設定 |
| `checkAll()` | `this` | 勾選所有已載入且未停用的節點，不觸發 `onCheck` |
| `uncheckAll()` | `this` | 取消所有未停用節點的勾選（含尚未載入的 id），不觸發 `onCheck` |
| `reloadNode(key)` | `Promise<boolean>` | 清除延遲節點（及其子孫）的載入快取；節點展開中則立即重新載入。重新載入成功 resolve `true`；節點收合（只清快取）、找不到、非延遲節點、載入失敗或被後續操作取代時 resolve `false`。不會 reject |

公開屬性（既有）：`element`（根元素）、`data`、`activeId`、`expandedIds`（`Set`）、`options`。

## 事件 / 回呼

| 回呼 | 參數 | 觸發時機 |
|---|---|---|
| `onSelect` | `(node)` | 點擊整列、在節點上按 Enter；未開啟 `checkable` 時 Space 也會選取。點擊展開箭頭或勾選框不會觸發 |
| `onCheck` | `(checkedKeys, { node, checked })` | 使用者點擊勾選框或按 Space 變更勾選後。`checkedKeys` 等同 `getCheckedKeys()`，`checked` 是被操作節點的新狀態。程式方法（`setCheckedKeys` / `checkAll` / `uncheckAll`）與延遲載入的承接都不會觸發 |
| `loadChildren` | `(node)` → `children[]` 或其 Promise | 延遲節點第一次展開時（同步呼叫）。回傳非陣列或拋錯都視為載入失敗 |

## 勾選規則（`checkable: true`）

- **連動（預設）**：勾選節點會勾選它所有已載入的子孫（包含收合中的）；父節點在「所有未停用的子節點都勾選」時自動勾選，部分勾選或有半選子節點時顯示半選（`aria-checked="mixed"`、勾選框 `indeterminate`）。點擊半選節點 = 全部勾選。
- **`checkStrictly: true`**：每個節點獨立勾選，沒有連動也沒有半選。
- **停用節點**（`disabled` 或 `disableCheckbox`）：使用者無法變更，連動時整個停用子樹都會被略過；停用節點也不參與父節點的推導。其狀態只能由 `checkedKeys` / `setCheckedKeys()` 設定。停用節點底下的可用節點仍可單獨勾選，但不會往上推導越過停用節點。
- **尚未載入的 id**：`checkedKeys` / `setCheckedKeys()` 中找不到的 id 會被保留（出現在 `getCheckedKeys()` 結果尾端），等該節點載入後自動套用。
- 單選（`activeId` / `onSelect`）與勾選彼此獨立。

## 延遲載入（`loadChildren`）

- 設定 `loadChildren` 後，符合下列任一條件的節點會顯示展開箭頭並視為「尚未載入」：
  - `children` 為 `undefined` 且 `isLeaf !== true`；
  - `hasChildren: true` 且 `children` 不是非空陣列。
  （`children: null` 或 `children: []` 且沒有 `hasChildren` 視為葉節點。）
- 第一次展開時呼叫 `loadChildren(node)` 一次，期間節點列帶 `aria-busy="true"`，子層顯示「載入中...」（`.tree-node-loading`）。結果快取在元件內，**不會改寫**傳入的節點物件；再次收合/展開不會重新載入。
- 載入到空陣列時，節點變成葉節點（箭頭消失）。
- 失敗時子層顯示錯誤訊息與「重試」按鈕（`.tree-node-error`，`role="alert"`；按鈕 `.tree-node-retry`），節點仍可收合；收合後再展開也會重新嘗試。
- 競態處理：載入中收合 → 結果照樣快取，但不會渲染到收合的節點；載入中再展開 → 不會重複呼叫；`reloadNode()`、`setData()` 或 `destroy()` 之後才回來的舊結果一律丟棄。
- 連動模式下，新載入的子節點承接父節點狀態：父節點已勾選 → 子節點（停用者除外）一起勾選；使用者先前明確取消勾選該父節點 → 子節點一律未勾選（覆蓋先前以 `checkedKeys` 預設的 id）；其餘情況套用已保留的 id。`checkStrictly` 時不承接。

## 鍵盤與無障礙

採 WAI-ARIA tree 模式：

- 根元素 `role="tree"`（可用 `ariaLabel` 命名）；每一列 `role="treeitem"`，帶 `aria-level`、`aria-setsize`、`aria-posinset`、`aria-selected`，可展開的節點帶 `aria-expanded`，勾選模式帶 `aria-checked`（`true` / `false` / `mixed`），延遲載入中帶 `aria-busy`；子層容器 `role="group"`。
- Roving tabindex：整棵樹只有一列 `tabindex="0"`（預設為選取的節點，否則第一列），其餘為 `-1`；Tab 進出樹只停一次。焦點移到哪一列，停駐點就跟到哪一列。
- 展開箭頭與圖示為裝飾（`aria-hidden="true"`），列的名稱只取文字標籤。勾選框是 `tabindex="-1"`、`aria-hidden="true"` 的原生 checkbox，只負責視覺與滑鼠操作，勾選語意由 treeitem 的 `aria-checked` 提供。

| 按鍵 | 行為 |
|---|---|
| ↓ / ↑ | 移到下一個 / 上一個可見節點 |
| → | 收合的父節點：展開（延遲節點開始載入）；已展開：移到第一個子節點 |
| ← | 已展開：收合；否則移到父節點 |
| Home / End | 移到第一個 / 最後一個可見節點 |
| Enter | 選取（觸發 `onSelect`），與點擊整列相同 |
| Space | `checkable` 時切換勾選（停用節點不變）；否則選取 |

加上 Ctrl / Alt / Meta 的組合鍵不處理。焦點使用瀏覽器預設的 focus 樣式。

## DOM 結構與 class

```
div.tree-list.theme-{theme}                         role="tree"
  div.tree-node-wrapper
    div.tree-node-row[data-node-id]                 role="treeitem"
      div.tree-node-toggle[data-node-id]            展開箭頭
      input.tree-node-checkbox[data-node-id]        僅 checkable
      div                                           節點圖示
      span                                          文字標籤
    div                                             role="group"，僅展開時存在
      div.tree-node-wrapper ...                     子節點
      div.tree-node-loading                         延遲載入中
      div.tree-node-error > button.tree-node-retry  延遲載入失敗
```

## 範例

```html
<link rel="stylesheet" href="../../theme.css">
<div id="tree"></div>
<script type="module">
    import { TreeList } from './TreeList.js';

    const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

    const tree = new TreeList({
        ariaLabel: '組織與會議室',
        checkable: true,
        checkedKeys: ['room-a'],
        data: [
            {
                id: 'dept',
                label: '部門',
                children: [
                    { id: 'sales', label: '業務部' },   // children 未定義 → 展開時延遲載入
                    { id: 'ops', label: '營運部' },
                    { id: 'legal', label: '法務部', isLeaf: true },
                ],
            },
            {
                id: 'rooms',
                label: '會議室',
                children: [
                    { id: 'room-a', label: 'A 會議室', children: [] },
                    { id: 'room-b', label: 'B 會議室（維修中）', children: [], disableCheckbox: true },
                ],
            },
        ],
        loadChildren: async (node) => {
            await wait(400); // 模擬後端延遲
            return [
                { id: `${node.id}-lead`, label: `${node.label}主管`, isLeaf: true },
                { id: `${node.id}-staff`, label: `${node.label}同仁`, isLeaf: true },
            ];
        },
        onSelect: (node) => console.log('選取', node.id),
        onCheck: (keys, { node, checked }) => console.log('勾選', keys, node.id, checked),
    }).mount('#tree');

    // 例如在工具列上提供「全選 / 全不選 / 重新整理業務部」
    // tree.checkAll(); tree.uncheckAll(); await tree.reloadNode('sales');
</script>
```

## 已知限制

- 尚未載入的節點位置未知：以 `checkedKeys` 預設、但其祖先後來被使用者取消勾選的 id，在該子樹載入前仍會出現在 `getCheckedKeys()` 中（載入時會依使用者的取消勾選清除）；尚未載入子節點的父節點也無法顯示半選。
- `setData()` 或 `reloadNode()` 後已不存在的 id 仍保留在勾選集合中，需要時請以 `setCheckedKeys()` 或 `uncheckAll()` 重設。
- 所有可見節點都會實際渲染（未做虛擬捲動），大量節點建議搭配延遲載入。
- 未提供 type-ahead（輸入字母跳到節點）與 `*`（展開同層全部節點）快捷鍵。

## Demo

開啟同目錄的 `demo.html` 在瀏覽器中檢視主題與選取行為。
