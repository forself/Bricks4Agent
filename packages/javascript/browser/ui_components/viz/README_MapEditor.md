# MapEditor - 圖片編輯器

## 📖 簡介

**MapEditor** 是一個零依賴的純 JavaScript 圖片編輯器，基於 Canvas API 實現。使用者可以上傳任意圖片作為底圖，並在圖片上添加文字、矩形、圓形、線條、箭頭等標註，最後匯出為 PNG 圖片。

## ✨ 主要功能

### 🖼️ 圖片管理

- 上傳本地圖片作為底圖
- 自動適應畫布大小
- 支援拖放操作

### 📝 文字工具

- 點擊畫布添加文字
- 雙擊文字可編輯內容
- 拖動調整文字位置
- 自訂字體大小（10-72px）
- 自訂文字顏色

### 🎨 形狀工具

- **矩形**：拖曳繪製矩形框
- **圓形**：拖曳繪製橢圓/圓形
- **線條**：繪製直線
- **箭頭**：繪製帶箭頭的直線
- 自訂線條顏色與粗細
- 自訂填充顏色（支援透明度）

### 🔧 編輯功能

- **選擇工具**：選中元素進行移動
- **拖動**：直接拖動元素調整位置
- **刪除**：刪除選中的元素
- **清空**：一鍵清除所有標註
- **復原**：支援 Ctrl+Z 復原操作

### 💾 匯出功能

- **PNG 圖片**：將編輯後的結果匯出為 PNG 格式
- **JSON 配置**：保存所有標註資料為 JSON
- **載入配置**：從 JSON 還原標註狀態
- **🔥 PNG 元數據嵌入**：匯出的 PNG 自動包含座標、文字、顏色等所有標註資訊

### 🔐 PNG 元數據功能（重要特色）

**匯出的 PNG 圖片會自動嵌入完整元數據**，使用 PNG tEXt chunk 標準格式：

**包含資訊：**

- ✅ 所有標註元素（文字、形狀、箭頭等）
- ✅ 完整座標資訊 (x, y, x2, y2)
- ✅ 文字內容與樣式（字體、大小、顏色）
- ✅ 形狀屬性（線條顏色、填充、粗細）
- ✅ 時間戳與版本資訊
- ✅ 畫布尺寸

**優點：**

- 📸 圖片與數據一體化，不需要分開保存
- 🔄 隨時重新載入 PNG 即可恢復所有編輯
- 📤 分享 PNG 時同時分享標註資訊
- 💯 符合 PNG 標準，任何圖片查看器都可正常顯示圖片本身

**使用方式：**

```javascript
// 匯出 PNG（自動嵌入元數據）
editor._exportImage();

// 載入帶元數據的 PNG
const file = document.querySelector("input[type=file]").files[0];
const metadata = await editor.loadImageWithMetadata(file);
console.log(metadata); // 顯示所有標註資訊
```

## 🚀 使用方式

### 基本使用

```html
<!DOCTYPE html>
<html>
  <body>
    <div id="editor-container"></div>

    <script type="module">
      import { MapEditor } from "./MapEditor.js";

      const editor = new MapEditor({
        container: "#editor-container",
        width: 800,
        height: 600,
      });
    </script>
  </body>
</html>
```

### API 方法

```javascript
// 設定背景圖片
editor.setBackgroundImage('path/to/image.png');

// 載入 JSON 配置
editor.loadJSON({
    elements: [...],
    settings: {...}
});

// 清空畫布
editor.clear();

// 匯出圖片（自動觸發下載）
editor._exportImage();

// 匯出 JSON（自動觸發下載）
editor._exportJSON();
```

### 配置選項

```javascript
new MapEditor({
  container: "#editor-container", // 容器選擇器或 DOM 元素
  width: 800, // 畫布寬度（像素）
  height: 600, // 畫布高度（像素）
});
```

## ⌨️ 快捷鍵

- **Delete**: 刪除選中元素
- **Ctrl+Z**: 復原上一步操作
- **雙擊文字**: 編輯文字內容

## 🎯 應用場景

- 地圖標註與說明
- 圖片講解與教學
- 流程圖繪製
- 簡報圖片編輯
- 產品功能標示

## 🛠️ 技術細節

### 零依賴

- 純 JavaScript 實現
- 使用原生 Canvas API
- 無需任何第三方函式庫

### 瀏覽器支援

- Chrome/Edge 90+
- Firefox 88+
- Safari 14+
- 需要支援 ES6 模組

### 高 DPI 支持

- 自動適配高解析度螢幕
- 使用 devicePixelRatio 確保清晰度

## 📝 啟動 Demo

### 方法 1：使用 Python

```bash
cd /path/to/Bricks4Agent
python -m http.server 5500
```

訪問：`http://localhost:5500/src/components/viz/demo_map_editor.html`

### 方法 2：使用 VS Code Live Server

1. 安裝 Live Server 擴充功能
2. 在 `demo_map_editor.html` 上點右鍵
3. 選擇「Open with Live Server」

### 方法 3：使用 Node.js http-server

```bash
npm install -g http-server
cd /path/to/Bricks4Agent
http-server -p 5500
```

## 🎨 介面預覽

編輯器提供完整的工具列介面：

1. **工具列**：檔案上傳、工具選擇、刪除、清空、匯出
2. **設定面板**：字體大小、顏色、線條粗細等
3. **畫布區域**：顯示圖片與標註元素
4. **快捷鍵提示**：方便記憶常用操作

## 📦 檔案結構

```
src/components/viz/
├── MapEditor.js           # 核心編輯器類別
├── demo_map_editor.html   # 完整示範頁面
└── README_MapEditor.md    # 說明文件（本檔案）
```

## 🔒 安全性考量

- 所有圖片僅在客戶端處理
- 無資料上傳至伺服器
- 匯出的 PNG/JSON 完全本地生成

## 🐛 已知限制

1. **ES Module 限制**：必須透過 HTTP 伺服器執行，無法直接開啟 HTML 檔案（`file://` 協議）
2. **大圖片效能**：極大圖片可能影響渲染效能
3. **歷史記錄**：復原功能目前僅支援單向復原（無重做）

## 💡 未來擴展

- [ ] 增加重做（Redo）功能
- [ ] 支援圖層管理
- [ ] 文字樣式預設（粗體、斜體）
- [ ] 更多形狀（多邊形、自由繪圖）
- [ ] 濾鏡與特效
- [ ] 觸控裝置支援

## 📄 授權

本組件遵循專案整體授權條款。

---

**Created with ❤️ for dF Project**
