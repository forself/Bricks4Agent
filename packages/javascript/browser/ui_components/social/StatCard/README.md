# StatCard

統計數字卡片元件，顯示圖示、數值、標籤和趨勢指示，適用於 Profile 頁面的數據摘要區塊。

## API

### Constructor

```js
import { StatCard } from './StatCard.js';

const card = new StatCard(options);
```

**options 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `icon` | `string` | `''` | 圖示（emoji 或文字） |
| `label` | `string` | `''` | 標籤文字 |
| `value` | `number\|string` | `0` | 數值 |
| `trend` | `string\|null` | `null` | 趨勢方向：`'up'` / `'down'` / `null` |
| `trendValue` | `string` | `''` | 趨勢文字（如 `'+5'`、`'-3%'`） |
| `color` | `string` | `'#4A90D9'` | 主題色（用於圖示背景和文字色） |
| `onClick` | `Function\|null` | `null` | 點擊回調 |

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `toHTML()` | `string` | 產生 HTML 字串 |
| `mount(container)` | `void` | 掛載到容器 |
| `update(options)` | `void` | 更新配置並重新渲染 |
| `destroy()` | `void` | 移除 DOM 元素 |

### 使用範例

```js
import { StatCard } from '../packages/javascript/browser/ui_components/social/StatCard/StatCard.js';

const card = new StatCard({
    icon: '👥',
    label: '成員數',
    value: 42,
    trend: 'up',
    trendValue: '+5',
    color: '#4A90D9'
});
card.mount('#stat-container');
```

## Demo

`demo.html` — 在瀏覽器直接開啟（需本地伺服器支援 ES module）。
