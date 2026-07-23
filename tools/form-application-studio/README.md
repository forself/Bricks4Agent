# Form Application Studio

此工具把單一資料表 schema 轉成可調整版面的表單，以及對應的 .NET 10 Minimal API、BaseOrm 程式碼與資料庫 SQL。工具頁的唯一權威定義是 `studio.page.json`，由 `DynamicPageRenderer({ mode: 'tool' })` 產生；核心設計畫布是正式 `FormDesigner` 元件。

## 使用方式

1. 從 repository root 啟動靜態伺服器，開啟 `/tools/form-application-studio/index.html`。
2. 貼上或匯入 schema JSON，按「解析並建立設計」。
3. 在欄位清單修改名稱、icon／元件，拖到 12 欄畫布後移動或縮放。
4. 選擇資料庫 provider 並視需要輸入連線字串。未提供或只含空白時，實際目的地固定改為本地 SQLite `data/<application_id>.db`。
5. 產生並下載設計 JSON或 bundle。

## 連線與寫入安全

- 預設為 preview/generate-only；Studio 不會直接連線、建表或修改外部資料庫。
- 連線字串只存在目前頁面的 controller closure，不寫入 URL、localStorage、PageDefinition 或一般設計 JSON。
- 一般匯出只保存 `connection_string_name`。只有明確勾選「backend-only 設定包含連線字串」時，bundle 的 backend development 設定才可包含目前值。
- 空白連線字串不會嘗試猜測或使用所選的外部 provider，而是強制使用本地 SQLite。
- 生成器不會產生自動 DROP／ALTER；既有表只提供差異預覽，套用工作必須另外確認 table、columns、source、write rules、exceptions 與 rollback plan。

## Schema 範例

見 `sample-schema.json`。表名、欄位名採可攜式識別字：`^[A-Za-z][A-Za-z0-9_]{0,62}$`。不接受任意 SQL、函式、raw HTML、prototype-sensitive key 或未核准的 provider／型別／元件。

## CLI 生成

```bash
node tools/form-application-studio/generate.mjs --schema tools/form-application-studio/sample-schema.json --output ./.test-output/customer-form
node tools/form-application-studio/generate.mjs --help
```

外部資料庫的連線字串只從 `--connection-string-env <環境變數名>` 讀取，不接受直接放在命令列。若同時指定 `--include-connection-string`，secret 只會寫入 backend development 設定；未指定時不會進入 bundle。CLI 不覆寫內容不同的既有檔案，也不會連接資料庫。

## 驗證

```bash
npm run test:form-designer
npm run test:form-designer:self-host
npm run test:form-designer:browser
npm run validate:ui-library
npm run audit:ui-styles
npm run audit:csp
```
