# ConnectionCard

關聯人員/組織小卡片，類似 LinkedIn「你可能認識的人」，展示頭像、名稱、副標題和標籤。

## API

### Constructor

```js
import { ConnectionCard } from './ConnectionCard.js';

const card = new ConnectionCard(options);
```

**options 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|---|---|---|---|
| `avatar` | `string` | `''` | 頭像 URL |
| `name` | `string` | `''` | 名稱 |
| `subtitle` | `string` | `''` | 副標題（如所屬組織、職務） |
| `tags` | `string[]` | `[]` | 標籤陣列（最多顯示 3 個） |
| `onClick` | `Function\|null` | `null` | 點擊回調 |

### 靜態方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `ConnectionCard.gridHTML(items)` | `string` | 批次產生多張卡片的 HTML（Flex Grid 佈局）；嵌入 DOM 後需呼叫 `ConnectionCard.applyStyles(container)`，且卡片不會綁定點擊事件 |
| `ConnectionCard.applyStyles(container)` | `void` | 為嵌入的 HTML 卡片（含頭像）套用樣式 |

### 實例方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `toHTML()` | `string` | 產生 HTML 字串（嵌入後同樣需 `ConnectionCard.applyStyles(container)`，不綁定 `onClick`） |
| `mount(container)` | `void` | 掛載到容器 |
| `update(options)` | `void` | 更新配置並重新渲染 |
| `destroy()` | `void` | 移除 DOM 元素 |

### 依賴

- `Avatar` — 內部使用 Avatar 元件渲染頭像

### 使用範例

```js
import { ConnectionCard } from './ConnectionCard.js';

// 單張卡片
const card = new ConnectionCard({
    avatar: '/photos/member1.jpg',
    name: '張三',
    subtitle: '技術部 · 主管',
    tags: ['販毒', '勒索'],
    onClick: () => console.log('navigate')
});
card.mount('#card-container');

// 批次 Grid（HTML 字串：需手動套樣式，且不會有點擊事件）
const grid = document.getElementById('grid');
grid.innerHTML = ConnectionCard.gridHTML([
    { name: '張三', subtitle: '幹部', tags: ['販毒'] },
    { name: '李四', subtitle: '成員', tags: ['勒索'] }
]);
ConnectionCard.applyStyles(grid);
```

## Demo

`demo.html` — 在瀏覽器直接開啟（需本地伺服器支援 ES module）。
