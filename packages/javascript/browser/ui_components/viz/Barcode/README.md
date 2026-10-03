# Barcode 條碼產生器

把文字畫成一維條碼。純 Canvas、零依賴，編碼器自行實作。

| 格式 | 可編碼的內容 | 常見用途 |
|---|---|---|
| `code128`（預設） | ASCII 0～127；連續數字自動用 C 字集縮短 | 學號、單據編號、資產標籤 |
| `code39` | 0-9、大寫 A-Z、`-` `.` 空白 `$` `/` `+` `%`；可附 mod 43 檢查字元 | 台灣超商代收與郵局劃撥的繳費單 |
| `ean13` | 12 碼自動補檢查碼，或 13 碼驗證檢查碼 | 商品條碼、書籍 |
| `ean8` | 7 碼自動補檢查碼，或 8 碼驗證檢查碼 | 小型商品 |

- 預設以固定模組寬（`moduleWidth`）畫出自然寬度；給 `width` 時縮放到該寬度
- 條邊對齊整數像素；`exportPNG(scale)` 輸出高倍率圖檔供列印
- EAN 依標準把護線延伸到數字列，數字分組排在條下
- 顏色預設取主題 token；深色主題下自動對調，條永遠比底色暗
- 內容不合格式時不擲錯，改在畫面上顯示訊息，`getError()` 取得原因

繳費單的條碼內容（例如超商代收的三段條碼各段要放什麼、檢查碼怎麼算）由代收單位的規格決定，請在呼叫端依規格組好字串再交給本元件，本元件只負責畫出符合 Code 39 的條碼。

## 建構子

```javascript
import { Barcode } from './index.js';

const barcode = new Barcode({
    value: '',                 // 要編碼的內容；空字串時不畫
    format: 'code128',         // 'code128' | 'code39' | 'ean13' | 'ean8'
    checkDigit: false,         // Code 39 是否附 mod 43 檢查字元
    wideRatio: 3,              // Code 39 寬窄比（2～3）
    moduleWidth: 2,            // 最窄條的寬度（px）
    width: undefined,          // 指定寬度時縮放到此寬（例如 '100%'）；未指定時依內容的自然寬度
    height: 80,                // 總高度（px，含數字列）
    showText: true,            // 是否在條下顯示可讀文字
    fontSize: 14,              // 可讀文字的字級（px）
    quietZone: null,           // 左右留白的模組數 { left, right }；預設依格式
    darkColor: '--cl-text',    // 條色：token 名稱或 CSS 顏色
    lightColor: '--cl-bg',     // 底色：token 名稱或 CSS 顏色
    ensureContrast: true,      // 依亮度自動對調，確保條比底色暗
    ariaLabel: '',             // 無障礙名稱；預設為「條碼（格式）：內容」
    onError: null              // 編碼失敗時呼叫 (error) => void
});
barcode.mount(container);
```

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛到容器，回傳自己 |
| `getValue()` | 目前的內容（呼叫端給的原文） |
| `setValue(value)` | 更換內容並重繪；未指定 `width` 時寬度跟著內容調整 |
| `clear()` | 清空內容 |
| `update(patch)` | 同時更新多個選項，例如 `{ value, format }` |
| `getError()` | 最近一次編碼失敗的原因 `{ code, message, detail }`，成功時為 `null`；`code` 為 `empty`、`invalid_char`、`invalid_length`、`bad_check_digit` 或 `invalid_option` |
| `getInfo()` | 編碼結果 `{ format, data, modules }`；`data` 含自動補上的檢查碼 |
| `exportPNG(scale = 2)` | 以 `scale` 倍率輸出 PNG 的 data URL |
| `destroy()` | 移除元件並釋放監聽 |
| `Barcode.formats` | 支援的格式清單 |

## 只要編碼結果

```javascript
import { encodeBarcode } from './index.js';

const { widths, modules } = encodeBarcode('ABC-123', { format: 'code39' });
// widths 是「條、空、條、空…」交錯的寬度（單位為模組，第一個一定是條）
```

## 列印建議

- 最窄條至少 0.25 公釐；用 `exportPNG(4)` 以上的倍率輸出再放進文件
- 左右留白不可省；Code 128 與 Code 39 至少 10 個模組，EAN-13 左 11、右 7 個模組
- Code 39 的寬窄比在最窄條小於 0.5 公釐時不得低於 2.2，建議維持預設的 3

## 測試

`barcode-encoder.test.mjs` 檢查 Code 128 符號表（107 個、模組數、條寬奇偶性）、Code 39 字元表（與標準寫法逐字比對）、EAN 檢查碼的已知例子，並以另外寫的解碼器把 Code 128 與 EAN-13 的結果讀回比對。
