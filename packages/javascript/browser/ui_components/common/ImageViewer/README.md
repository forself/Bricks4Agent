# ImageViewer

圖片展示器元件，提供燈箱效果（Modal），支援滾輪縮放、拖曳平移、左右切換導航。

## API

ImageViewer 使用靜態方法開啟，全域同時只有一個實例。

### 靜態方法

| 方法 | 說明 |
|------|------|
| `ImageViewer.open(src, options?)` | 開啟圖片展示器 |
| `ImageViewer.close()` | 關閉圖片展示器 |

### Constructor（通常透過 `open()` 呼叫）

```js
new ImageViewer(src, options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `src` | `string` | — | 圖片來源 URL |
| `options.minZoom` | `number` | `0.1` | 最小縮放比例 |
| `options.maxZoom` | `number` | `3` | 最大縮放比例 |
| `options.zoomStep` | `number` | `0.2` | 縮放步長 |
| `options.onPrev` | `Function` | `null` | 上一張回調（有值時顯示左箭頭） |
| `options.onNext` | `Function` | `null` | 下一張回調（有值時顯示右箭頭） |

### 實例方法

| 方法 | 說明 |
|------|------|
| `setSrc(src)` | 切換圖片來源 |
| `setOptions(options)` | 更新選項 |
| `destroy()` | 關閉並銷毀 |

### 操作方式

- 滾輪：縮放
- 拖曳：平移（放大後）
- 點擊遮罩：關閉
- Escape 鍵：關閉
- 左右箭頭鍵：切換圖片（需提供 onPrev/onNext）
- 工具列：縮小、放大、重設、關閉

## 使用範例

```js
import { ImageViewer } from './ImageViewer.js';

// 基本用法
ImageViewer.open('/path/to/image.jpg');

// 相簿瀏覽
const images = ['a.jpg', 'b.jpg', 'c.jpg'];
let idx = 0;
ImageViewer.open(images[idx], {
    onPrev: () => { idx = (idx - 1 + images.length) % images.length; ImageViewer.open(images[idx]); },
    onNext: () => { idx = (idx + 1) % images.length; ImageViewer.open(images[idx]); }
});
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
