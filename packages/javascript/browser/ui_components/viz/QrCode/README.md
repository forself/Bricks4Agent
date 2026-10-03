# QrCode QR Code 產生器

把文字或網址畫成 QR Code，例如繳費資訊、證明文件的查驗連結、課程大綱或教室課表的公開網址。純 Canvas、零依賴，編碼器依 ISO/IEC 18004 自行實作。

- 版本 1～40，錯誤修正等級 L、M、Q、H；依內容自動選數字、英數或位元組（UTF-8）模式，中文可直接編碼
- 模組對齊整數像素，邊緣銳利；`exportPNG(scale)` 輸出高倍率圖檔供列印
- 顏色預設取主題 token；深色主題下自動對調，模組永遠比底色暗，換膚後仍可掃描
- 內容過長或設定錯誤時不擲錯，改在畫面上顯示訊息，`getError()` 取得原因
- 無障礙名稱預設為「QR Code：內容」，內容改變時同步更新

## 建構子

```javascript
import { QrCode } from './index.js';

const qr = new QrCode({
    value: '',                // 要編碼的文字或網址；空字串時不畫
    ecLevel: 'M',             // 'L' 7% | 'M' 15% | 'Q' 25% | 'H' 30%（可容許的損毀比例）
    size: 160,                // 邊長（數字為 px）
    margin: 4,                // 四周留白的模組數（標準為 4，貼在深色背景上不可省）
    minVersion: 1,            // 最小版本（1～40）
    maxVersion: 40,           // 最大版本；內容放不下時顯示錯誤
    mask: 'auto',             // 0～7，預設依罰分自動選
    boostEcl: true,           // 同一版本放得下時自動提高錯誤修正等級
    eci: false,               // 加上 UTF-8 的 ECI 宣告（少數舊掃描器才需要）
    darkColor: '--cl-text',   // 模組色：token 名稱或 CSS 顏色
    lightColor: '--cl-bg',    // 底色：token 名稱或 CSS 顏色
    ensureContrast: true,     // 依亮度自動對調，確保模組比底色暗
    ariaLabel: '',            // 無障礙名稱；預設為「QR Code：內容」
    onError: null             // 編碼失敗時呼叫 (error) => void
});
qr.mount(container);
```

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛到容器，回傳自己 |
| `getValue()` | 目前的內容 |
| `setValue(value)` | 更換內容並重繪 |
| `clear()` | 清空內容 |
| `update(patch)` | 同時更新多個選項，例如 `{ value, ecLevel }` |
| `getError()` | 最近一次編碼失敗的原因 `{ code, message }`，成功時為 `null`；`code` 為 `too_long` 或 `invalid_option` |
| `getInfo()` | 編碼結果 `{ version, size, ecLevel, mask, mode }`，沒有內容時為 `null` |
| `exportPNG(scale = 2)` | 以 `scale` 倍率輸出 PNG 的 data URL |
| `destroy()` | 移除元件並釋放監聽 |

## 只要編碼結果

不需要畫面時（例如在伺服器端產生文件），直接用編碼器：

```javascript
import { encodeQr } from './index.js';

const { size, isDark } = encodeQr('https://example.edu.tw', { ecLevel: 'Q' });
for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
        if (isDark(x, y)) { /* 畫一個模組 */ }
    }
}
```

## 選擇錯誤修正等級

| 等級 | 可容許的損毀 | 適合 |
|---|---|---|
| L | 約 7% | 螢幕顯示、內容很長 |
| M | 約 15% | 一般用途（預設） |
| Q | 約 25% | 列印在紙本、可能折損 |
| H | 約 30% | 中央要放標誌，或環境惡劣 |

內容越長、等級越高，版本（邊長模組數）越大；邊長固定時每個模組就越小。列印時每個模組建議至少 0.3 公釐。

## 測試

`qr-encoder.test.mjs` 以標準的已知數值（格式資訊、版本資訊、產生多項式、HELLO WORLD 的碼字與錯誤修正碼、對齊圖形位置、各版本容量）驗證編碼器，並以另外寫的解碼器把數字、英數、中文、長文字、八種遮罩與版本 7～40 的結果讀回比對。
