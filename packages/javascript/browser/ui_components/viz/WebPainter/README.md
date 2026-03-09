# WebPainter

一個高度可配置的網頁繪圖板元件，基於 HTML5 Canvas，支援圖層、物件選取、編輯歷史等功能。

> **注意**：如果只需要簡單的手繪功能（畫筆、橡皮擦、直線），建議使用輕量的 [DrawingBoard](../DrawingBoard/README.md) 元件。

## Features

- **多圖層管理**：新增、刪除、隱藏、切換圖層。
- **豐富的繪圖工具**：文字、畫筆（自由繪製）、矩形、圓形、線條、箭頭、打點標記。
- **物件編輯**：選取移動、刪除、修改屬性（顏色、字型、線寬）。
- **歷史記錄**：支援 Undo/Redo (Ctrl+Z / Ctrl+Y)。
- **剪貼簿**：支援複製與貼上 (Ctrl+C / Ctrl+V)。
- **縮放功能**：支援畫布縮放，並保持標註點位準確。
- **高度客製化**：可透過參數決定要顯示哪些功能按鈕與面板。

## Usage

```javascript
import { WebPainter } from "./components/viz/WebPainter/WebPainter.js";

// 初始化
const painter = new WebPainter({
  container: "#editor-container",
  width: 1000,
  height: 800,
  features: {
    // UI 區塊開關
    header: true, // 上方工具列
    settings: true, // 設定面板

    // 功能按鈕細項
    upload: true, // 上傳底圖按鈕
    tools: true, // 繪圖工具箱
    delete: true, // 刪除按鈕
    clear: true, // 清空按鈕
    export: true, // 匯出/存檔按鈕
    zoom: true, // 縮放控制按鈕
    layers: true, // 圖層管理面板及其開關
  },
});
```

### 簡易模式 (例如：僅供瀏覽與簡單標記)

```javascript
const viewer = new WebPainter({
  container: "#viewer",
  features: {
    header: true,
    settings: false, // 隱藏設定面板
    layers: false, // 隱藏圖層功能
    upload: false,
    delete: false,
    clear: false,
    export: false,
  },
});
```

## Shortcuts

- **Del**: 刪除選中物件
- **Ctrl + Z**: 復原 (Undo)
- **Ctrl + Y** (or Ctrl + Shift + Z): 重做 (Redo)
- **Ctrl + C**: 複製 (Copy)
- **Ctrl + V**: 貼上 (Paste)
