# 網站產生打包

從 `SiteCrawlResult` 或有效的 `GeneratorSiteDocument` 產生本機靜態網站包。

預設會啟用網站產生品質門檻。只有當文件可以完全使用已載入的元件庫渲染，且沒有生成元件或未解決的元件需求時，才會寫出 package。`enforce_quality_gate = false` 只應用於診斷元件庫缺口。

設定 `create_archive = true` 時，會額外輸出可攜式 zip 檔。若未指定 `archive_path`，zip 會建立在 package 目錄旁，檔名為 `<package_name>.zip`。

每個產出的 package 都會包含 `verification_report`。驗收器會檢查必要檔案、重新讀取 `site.json`、透過品質分析器確認可由元件庫渲染，並在有 zip 時檢查 archive 條目。

## 能力

- Tool ID: `site.generate.package`
- Capability ID: `site.generate_package`
- Route: `site_generate_package`
- Status: `beta`
- Risk: medium

## Package 契約

Package 進入點是 `index.html`。HTML 只是一個包含 `#app` 並載入 `runtime.js` 的外殼。

Runtime 會載入：

- `site.json`
- `components/manifest.json`

接著 runtime 會根據 `site.json` 裡的元件樹渲染網站。

Worker 會在有設定時從 `Generator:ComponentLibraryPath` 載入元件庫 manifest。若未設定，會使用內建預設 manifest：`component-libraries/bricks4agent.default/manifest.json`。

## 元件規則

產出的 package 只能渲染 `components/manifest.json` 宣告的元件類型。

如果來源線索無法以內建元件表示，轉換步驟必須在 manifest 新增本地生成元件定義，並記錄 `component_requests`。Runtime 只有在該生成元件已宣告於 manifest 時才能渲染它。

Generator 不得產生任意頁面 HTML、DOM 等價複製、像素等價複製，或藏在元件樹以外的自由版面。

在 strict mode 中，生成元件定義與 component request 都會被視為品質失敗。呼叫端會收到結構化的 `quality_report`，且不會寫出 package。

Strict mode 也會把 package 驗收失敗視為交付阻擋。呼叫端會收到結構化的 `verification_report`，失敗的 package 產物會在可行時被移除。

## Package 驗收

正常交付時，驗收必須通過。驗收器會檢查：

- `index.html`
- `runtime.js`
- `styles.css`
- `site.json`
- `components/manifest.json`
- `README.md`
- `index.html` 宣告 `#app` 並載入 `./runtime.js`
- `runtime.js` 載入 `./site.json` 與 `./components/manifest.json`
- `components/manifest.json` 宣告 `site.json` 使用到的每一種元件類型
- `site.json` 使用到的每一種非生成元件都必須有 `runtime.js` renderer
- 生成元件必須有本地 `components/generated/<Type>.js` 與 `.json` 資產
- `create_archive = true` 時的 zip 條目

`verification_report.runtime_renderer_types` 會列出從 `runtime.js` 解析出的 renderer key。

若 `enforce_quality_gate = false`，診斷 package 仍可能被寫出，並帶有失敗的 `quality_report` 與 `verification_report`。這類輸出只用於元件庫缺口分析，不適合作為使用者交付物。

## 輸出

結果包含：

- `output_directory`
- `entry_point`
- `site_json_path`
- `manifest_path`
- `archive_path`
- `files`
- `quality_report`
- `verification_report`
