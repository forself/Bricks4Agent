# DrawingBoard

純繪圖板元件，專注於基本的手繪功能。基於 HTML5 Canvas，無任何第三方依賴。

## 與 WebPainter 的區別

| 功能 | DrawingBoard | WebPainter |
|------|-------------|------------|
| 畫筆/橡皮擦 | O | O |
| 直線工具 | O | O |
| 螢光筆 | O | X |
| 透明度控制 | O | X |
| 文字工具 | X | O |
| 形狀（矩形/圓形） | X | O |
| 物件選取/移動 | X | O |
| 圖層管理 | X | O |
| 複製/貼上 | X | O |

**建議使用場景：**
- **DrawingBoard**：簽名、簡單草圖、手寫筆記、快速標記
- **WebPainter**：完整圖片編輯、多元素標註、專業編輯需求

## Features

- **畫筆工具**：自由繪製
- **橡皮擦**：擦除繪製內容
- **直線工具**：拖曳繪製直線，即時預覽
- **螢光筆**：半透明標記，適合重點標示
- **透明度控制**：10%-100% 可調整
- **顏色選擇**：6 種預設顏色 + 自訂顏色
- **筆觸粗細**：1-50px 可調整
- **復原/重做**：支援 Ctrl+Z / Ctrl+Y
- **背景圖片**：可載入圖片作為底圖描繪
- **匯出 PNG**：一鍵匯出繪圖結果
- **觸控支援**：支援觸控螢幕繪製

## Usage

### 基本使用

```javascript
import { DrawingBoard } from './DrawingBoard/index.js';

const board = new DrawingBoard({
    container: '#drawing-container',
    width: 800,
    height: 600
});
```

### 完整配置

```javascript
const board = new DrawingBoard({
    container: '#drawing-container',  // 容器選擇器或 DOM 元素
    width: 800,                       // 畫布寬度
    height: 600,                      // 畫布高度
    strokeColor: '#333333',           // 預設筆觸顏色
    lineWidth: 3,                     // 預設筆觸粗細
    opacity: 1.0,                     // 預設透明度
    onDraw: (point, settings) => {    // 繪製回調
        console.log('Drawing at', point);
    },
    onClear: () => {                  // 清除回調
        console.log('Canvas cleared');
    }
});
```

### API 方法

```javascript
// 設定背景圖片
await board.setBackgroundImage('path/to/image.png');
// 或使用 File 物件
await board.setBackgroundImage(file);

// 復原
board.undo();

// 重做
board.redo();

// 清除畫布
board.clear();

// 匯出 PNG（觸發下載）
board.exportPNG('my-drawing.png');

// 取得 Data URL
const dataUrl = board.getDataURL();

// 調整畫布尺寸
board.resize(1024, 768);

// 銷毀元件
board.destroy();
```

## Shortcuts

- **Ctrl + Z**：復原
- **Ctrl + Y**：重做

## 工具說明

### 畫筆
預設工具，自由繪製平滑曲線。使用二次貝茲曲線讓筆觸更自然。

### 橡皮擦
擦除已繪製的內容。游標會顯示橡皮擦大小預覽。

### 直線
按住拖曳繪製直線，放開確定終點。繪製過程中有即時預覽。

### 螢光筆
半透明標記工具，使用 `multiply` 混合模式。切換到螢光筆時會自動將透明度設為 40%。

## 技術細節

### 零依賴
- 純 JavaScript 實現
- 使用原生 Canvas API
- 無需任何第三方函式庫

### 瀏覽器支援
- Chrome/Edge 90+
- Firefox 88+
- Safari 14+
- 需要支援 ES6 模組

## Demo

訪問 `demos/viz/DrawingBoard.html` 查看完整示範。

## 檔案結構

```
DrawingBoard/
├── DrawingBoard.js   # 核心元件類別
├── index.js          # 模組匯出
└── README.md         # 說明文件（本檔案）
```

---

**Created with care for Bricks4Agent Project**
