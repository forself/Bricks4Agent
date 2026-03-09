# Timeline

垂直時間軸元件，按時間排列事件列表，支援按月份分組和類型色標。

## API

### Constructor

```js
import { Timeline } from './Timeline.js';

const timeline = new Timeline(options);
```

**options 參數：**

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `items` | `Object[]` | `[]` | 時間軸項目（見下方） |
| `grouped` | `boolean` | `true` | 是否按月份分組 |
| `emptyText` | `string` | `'暫無活動紀錄'` | 無資料時顯示文字 |

**items[] 項目結構：**

| 欄位 | 型別 | 說明 |
|------|------|------|
| `timestamp` | `string` | 時間（ISO 字串或可解析日期） |
| `type` | `string` | 類型標籤 |
| `icon` | `string` | 圖示 emoji（可選） |
| `color` | `string` | Marker 顏色（可選，依 type 自動選色） |
| `title` | `string` | 標題 |
| `description` | `string` | 描述文字（最多顯示 2 行） |
| `onClick` | `Function` | 點擊回調（可選） |

### 靜態屬性

- `Timeline.TYPE_COLORS` — 類型預設色彩映射

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `toHTML()` | `string` | 產生 HTML 字串（自動按時間降序排列） |
| `mount(container)` | `void` | 掛載到容器（自動綁定點擊事件委派） |
| `update(options)` | `void` | 更新配置並重新渲染 |
| `destroy()` | `void` | 移除 DOM 元素 |

### 使用範例

```js
import { Timeline } from '../packages/javascript/browser/ui_components/social/Timeline/Timeline.js';

const timeline = new Timeline({
    items: [
        {
            timestamp: '2026-02-15', type: '緊急事件',
            title: '販毒案件', description: '於台北市查獲...',
            onClick: (item) => console.log(item)
        },
        {
            timestamp: '2026-01-20', type: '一般活動',
            title: '聚會活動', description: '成員聚會...'
        }
    ],
    grouped: true
});
timeline.mount('#timeline-container');
```

## Demo

`demo.html` — 在瀏覽器直接開啟（需本地伺服器支援 ES module）。
