# FeedCard

Facebook 風格的動態貼文卡片，顯示作者、時間、類型、內容、圖片 Grid 和標籤。支援內容截斷展開。

## API

### Constructor

```js
import { FeedCard } from './FeedCard.js';

const feed = new FeedCard(options);
```

**options 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `avatar` | `string` | `''` | 作者頭像 URL |
| `author` | `string` | `''` | 作者名稱 |
| `authorSub` | `string` | `''` | 作者副標題（如所屬組織） |
| `timestamp` | `string` | `''` | 時間（ISO 字串），自動轉為相對時間 |
| `type` | `string` | `''` | 類型標籤 |
| `typeColor` | `string` | `''` | 類型色彩（可選，依 type 自動選色） |
| `title` | `string` | `''` | 標題 |
| `content` | `string` | `''` | 內容（超過 150 字自動截斷） |
| `images` | `string[]` | `[]` | 圖片 URL 陣列（最多顯示 4 張，超過顯示 +N） |
| `tags` | `string[]` | `[]` | 標籤陣列（最多 5 個） |
| `relatedCount` | `number` | `0` | 關聯人數 |
| `onClickDetail` | `Function\|null` | `null` | 點擊「查看詳情」回調 |
| `onClickAuthor` | `Function\|null` | `null` | 點擊作者名稱回調 |

### 靜態屬性

- `FeedCard.TYPE_COLORS` — 類型預設色彩映射（緊急事件、一般活動等）

### 靜態方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `FeedCard.listHTML(items)` | `string` | 批次產生多張貼文的 HTML |

### 實例方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `toHTML()` | `string` | 產生 HTML 字串 |
| `mount(container)` | `void` | 掛載到容器 |
| `update(options)` | `void` | 更新配置並重新渲染 |
| `destroy()` | `void` | 移除 DOM 元素 |

### 依賴

- `Avatar` — 內部使用 Avatar 元件渲染作者頭像

### 使用範例

```js
import { FeedCard } from '../packages/javascript/browser/ui_components/social/FeedCard/FeedCard.js';

const feed = new FeedCard({
    author: '張三',
    authorSub: '工程團隊',
    timestamp: '2026-02-15T14:30:00',
    type: '緊急事件',
    title: '販毒案件',
    content: '於台北市中山區查獲販毒案件...',
    tags: ['販毒', '台北市'],
    relatedCount: 3,
    onClickDetail: () => console.log('detail'),
    onClickAuthor: () => console.log('author')
});
feed.mount('#feed-container');
```

## Demo

`demo.html` — 在瀏覽器直接開啟（需本地伺服器支援 ES module）。
