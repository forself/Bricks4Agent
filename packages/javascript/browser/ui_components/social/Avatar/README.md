# Avatar

圓形頭像元件，支援多尺寸、圖片載入失敗回退顯示姓名首字、角標數字。

## API

### Constructor

```js
import { Avatar } from './Avatar.js';

const avatar = new Avatar(options);
```

**options 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `src` | `string` | `''` | 圖片 URL |
| `alt` | `string` | `''` | 替代文字（也用於產生回退首字） |
| `size` | `string` | `'md'` | 尺寸：`'xs'`(24px) / `'sm'`(32px) / `'md'`(48px) / `'lg'`(72px) / `'xl'`(96px) |
| `badge` | `number\|null` | `null` | 角標數字，`null` 不顯示，超過 99 顯示 `99+` |
| `onClick` | `Function\|null` | `null` | 點擊回調 |

### 靜態屬性

- `Avatar.SIZES` — 尺寸對應 px 映射表
- `Avatar.COLORS` — 回退色彩池（依名稱 hash 選色）

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `toHTML()` | `string` | 產生 HTML 字串，可直接嵌入 template |
| `mount(container)` | `void` | 掛載到容器（接受 `HTMLElement` 或 CSS 選擇器字串） |
| `update(options)` | `void` | 更新配置並重新渲染 |
| `destroy()` | `void` | 移除 DOM 元素 |

### 使用範例

```js
import { Avatar } from '../packages/javascript/browser/ui_components/social/Avatar/Avatar.js';

const avatar = new Avatar({
    src: '/photos/user1.jpg',
    alt: '張三',
    size: 'lg',
    badge: 5,
    onClick: () => console.log('clicked')
});
avatar.mount(document.getElementById('container'));
```

## Demo

`demo.html` — 在瀏覽器直接開啟（需本地伺服器支援 ES module）。
