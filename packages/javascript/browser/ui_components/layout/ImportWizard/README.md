# ImportWizard 資料匯入精靈

內嵌在頁面中（不是對話框）的四步驟匯入流程：

1. **上傳檔案**：原生檔案選擇器（可拖放），或直接貼上文字。
2. **欄位對應**：每個目標欄位用一個 Dropdown 選擇來源欄位，依標題自動比對。
3. **預覽與驗證**：型別轉換、必填與自訂驗證，顯示錯誤清單與前 100 列預覽。
4. **確認匯入**：呼叫 `onImport`，顯示忙碌狀態與結果（成功／失敗筆數），可重新開始。

所有來源值都當作文字處理，畫面一律以 `textContent` 呈現，不解讀任何標記。上方以 Stepper 顯示進度。

## 建構

```javascript
import { ImportWizard } from './ImportWizard.js';

const wizard = new ImportWizard({
    fields: [],                     // 目標欄位定義，見下表
    accept: '.csv,.tsv,.txt',       // 檔案選擇器的 accept，也用來檢查拖放的檔案（副檔名或 MIME）
    maxFileSize: 5 * 1024 * 1024,   // 檔案大小上限（位元組）；貼上的文字以 UTF-8 位元組數計算
    maxRows: 10000,                 // 資料列上限（不含標題列），超過時不載入
    encoding: 'utf-8',              // TextDecoder 編碼標籤，例如 'big5'；UTF-8 的 BOM 會移除
    delimiter: 'auto',              // 'auto'（在 , Tab ; 之間判斷）或固定的單一字元
    hasHeader: true,                // 第一列是否為標題；false 時依欄位順序對應，欄名顯示為「第 N 欄」
    allowPaste: true,               // 顯示貼上文字的輸入區
    allowPartial: false,            // false：有任何無效列時不能進入確認步驟
    onImport: null,                 // async (validRows, { invalidRows, mapping, headers, rowNumbers }) => result
    onCancel: null                  // () => void；提供時各步驟顯示「取消」按鈕（按下後先 reset；匯入完成後不顯示）
});
wizard.mount('#host');
```

欄位定義 `fields[]`：

| 屬性 | 說明 |
|---|---|
| `key` | 輸出資料列的屬性名稱（必填、不可重複） |
| `label` | 顯示名稱，預設同 `key` |
| `required` | 必須對應到來源欄位，且每列的值不可為空 |
| `type` | `'text'`（預設）\| `'number'` \| `'date'` \| `'boolean'` |
| `aliases` | 自動對應時可接受的其他標題，例如 `['full name', '電子郵件']` |
| `validate(value, row)` | 回傳錯誤訊息字串或 `null`；`value` 為轉換後的值，`row` 為該列所有欄位轉換後的值；空白且非必填時不呼叫 |
| `transform(value)` | 驗證通過後把值轉成輸出值；空白（`null`）時不呼叫 |

## 處理規則

- **解碼**：以 `TextDecoder(encoding)` 解碼；不支援的編碼顯示錯誤，出現無法解碼的字元時顯示警告（通常代表編碼選錯）。
- **解析**：同目錄的 `csv-parser.js`，支援 RFC 4180 引號、`""` 跳脫、引號內的換行與分隔字元、CRLF／LF、檔尾換行與 BOM；空白行及只有分隔字元的行會略過。欄位數與標題列不同的列（ragged row）、引號未關閉或格式錯誤的列都列為無效列。
- **自動對應**：標題經全形轉半形、轉小寫、去除空白與常見標點後，與欄位的 `key`、`label`、`aliases` 比對，每個來源欄位只對應一次。
- **轉換**：值先去除前後空白。`number` 接受全形數字、千分位逗號與科學記號；`date` 接受 `YYYY-MM-DD`、`YYYY/MM/DD`、`YYYY.MM.DD`（月日可一位數），輸出 `YYYY-MM-DD`，不存在的日期視為錯誤；`boolean` 接受 true/t/yes/y/1/on/是/真 與 false/f/no/n/0/off/否/假。
- **列號**：錯誤清單與 `invalid[].row` 使用來源中的行號（標題列為第 1 行），跨行的引號欄位以起始行計算。

## 方法

| 方法 | 說明 |
|---|---|
| `mount(container)` | 掛載到容器（元素或選擇器），回傳 `this` |
| `destroy()` | 銷毀子元件、移除 DOM 與 `window` 上的語系監聽；讀檔或匯入中的結果會被忽略；可重複呼叫 |
| `goTo(step)` | 前往 `0`–`3` 或 `'upload'`／`'mapping'`／`'preview'`／`'confirm'`；只能前往可到達的步驟（要先載入資料、必填欄位都對應、可確認匯入），讀檔中、匯入中或完成後回傳 `false` |
| `reset()` | 清除資料、對應與結果並回到第一步 |
| `getMapping()` | 回傳 `{ [fieldKey]: 來源欄位索引（0 起算）\| null }` |
| `getRows()` | 回傳 `{ valid, invalid }`：`valid` 為轉換後的資料列；`invalid` 為 `{ row, values, errors: [{ field, message }] }`，`field` 為 `null` 表示整列的問題 |
| `show()` / `hide()` | 顯示／隱藏 |
| `snapshot()` | 回傳內部狀態（step、status、source、mapping…） |

`ImportWizard.STEPS` 為步驟代碼陣列 `['upload', 'mapping', 'preview', 'confirm']`。

## 回呼

- `onImport(validRows, { invalidRows, mapping, headers, rowNumbers })`：在確認步驟按下「開始匯入」時呼叫，可回傳 Promise。執行期間畫面為忙碌狀態（`aria-busy`、按鈕停用、狀態播報）。
  - 回傳 `{ imported, failed }`（數字）時直接顯示；否則以有效列數與無效列數作為成功／失敗筆數。
  - 拋出錯誤或 Promise 被拒時顯示失敗訊息（錯誤的 `message` 以純文字附在後面），可「重試」或回上一步。
  - `rowNumbers` 與 `validRows` 一一對應，是每列在來源中的行號，方便回報後端的逐列錯誤。
- `onCancel()`：按下「取消」時先 `reset()` 再呼叫。

## 鍵盤與無障礙

- 全部使用原生控制項：檔案選擇器（以 `<label for>` 標示）、`<textarea>`、按鈕；欄位對應使用可搜尋的 Dropdown（↑／↓、Enter、Escape），每個都有「{欄位} 的來源欄位」的 `aria-label`，必填者加上 `aria-required`。
- 以按鈕換步驟時，焦點移到該步驟的標題（`role="heading"`，`tabindex="-1"`），並以 `role="status"` 播報「第 N 步，共 4 步」；以 `goTo()` 換步驟時，只有焦點原本就在元件內才會移到標題。錯誤以 `role="alert"` 顯示。
- 欄位對應未完成時按「下一步」：列出缺少的必填欄位、在對應的 Dropdown 標示錯誤，並把焦點移到第一個缺少的欄位。
- 預覽表格含 `<caption>` 與 `scope` 標頭，外框可 Tab 聚焦（`role="region"`）以便鍵盤捲動；「下一步」被擋住時以 `aria-describedby` 指向原因。
- 匯入中的按鈕以 `aria-disabled` 表示（仍可聚焦），完成後焦點移到「重新開始」，失敗時移到「重試」。
- 語系切換時會即時重繪文字。

## CSV 解析器

`csv-parser.js` 可單獨使用：

```javascript
import { parseCsv, detectDelimiter, stripBom } from './csv-parser.js';

const text = stripBom(rawText);
const { rows, lines, errors, truncated } = parseCsv(text, {
    delimiter: detectDelimiter(text),   // ',' '\t' ';' 中欄位數最一致者
    skipEmptyLines: 'greedy',           // true 略過空白行；'greedy' 連只有空白或分隔字元的行也略過
    maxRecords: 1000                    // 達上限即停止，truncated 為 true
});
// rows：string[][]；lines：每筆記錄起始行號；errors：[{ code: 'unterminatedQuote'|'malformedQuote', line, record }]
```

## 範例

```javascript
import { ImportWizard } from './ImportWizard.js';

const wizard = new ImportWizard({
    fields: [
        { key: 'name', label: '姓名', required: true, aliases: ['full name', 'name'] },
        {
            key: 'email',
            label: 'Email',
            required: true,
            aliases: ['e-mail', '電子郵件'],
            validate: (value) => (/^[^@\s]+@[^@\s]+$/.test(value) ? null : 'Email 格式錯誤')
        },
        { key: 'room', label: '會議室' },
        { key: 'seats', label: '座位數', type: 'number', validate: (value) => (value > 0 ? null : '必須大於 0') },
        { key: 'startDate', label: '開始日期', type: 'date' },
        { key: 'remote', label: '可視訊', type: 'boolean' }
    ],
    encoding: 'utf-8',
    allowPartial: true,
    onImport: async (rows, { invalidRows }) => {
        const response = await fetch('/api/bookings/import', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(rows)
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        const body = await response.json();
        return { imported: body.imported, failed: body.failed + invalidRows.length };
    },
    onCancel: () => wizard.hide()
});
wizard.mount('#import-host');
```
