# 網站重製封裝

爬取已確認的公開 URL，優先使用瀏覽器渲染後的視覺與功能線索，轉換成元件庫限定的 `site.json`，通過品質檢查後產生本機靜態網站包。

## 能力

- Tool ID: `site.reconstruct.package`
- Capability ID: `site.reconstruct_package`
- Route: `site_reconstruct_package`
- Status: `beta`
- Risk: medium

## 執行前必要確認

執行前必須由使用者確認爬取深度，深度定義為「連結跳數」：

- 入口頁連結：`max_depth = 1`
- 兩次連結跳轉以內：`max_depth = 2`
- N 次連結跳轉以內：`max_depth = N`

只爬根頁或目前頁面時，`max_depth = 0` 只作為明確的診斷或手動模式。

`link_depth` 會以廣度優先逐層爬取；若遇到安全預算限制，必須先保留較低層的完整性，再進入更深層。URL 實體路徑深度不作為網站層級定義。

對學校、機構或多子網域網站，可用 `scope.allowed_host_suffixes` 納入公開的同站子網域，例如 `["ntub.edu.tw"]`。這仍會套用公開 HTTP/HTTPS 與私網阻擋規則，但允許 `www.ntub.edu.tw`、`sec.ntub.edu.tw` 等子網域被視為同一個重製範圍。不得用來擴大到無關網域。

基於效能，渲染後的 visual snapshots 可以有上限；但這個上限不得作為頁面或 route 爬取上限。

## 流程

1. 只在已確認的公開 HTTP/HTTPS 範圍內爬取。
2. 優先使用渲染後的視覺與功能線索，原始 HTML 只作為輔助證據。
3. 將網站意圖轉換為模板 slot 與元件庫節點。
4. 預設執行嚴格品質檢查。
5. 產生以 `index.html` 為入口的網站包。
6. 預設產生可攜式 zip 檔供交付。
7. 驗證 package 檔案、`site.json` 可渲染性與 archive 內容。

package runtime 會載入 `site.json` 與 `components/manifest.json`，再渲染元件樹。它不得輸出任意頁面 HTML，也不得產生 DOM 等價複製。

## 嚴格品質檢查

strict mode 預設啟用。下列狀況會阻止 package 建立：

- 文件仍含未解決的 `component_requests`。
- manifest 宣告 generated components。
- route 使用未知 component type。
- route path 重複。
- route root 不是 `PageShell`。

失敗時工具會回傳結構化 `quality_report`，且不寫出 package。

strict mode 也會在 package verification 失敗時阻止交付。工具會回傳 `verification_report`，並在可行時移除失敗的 package artifacts。

## Package 驗證

成功輸出會包含 `package.verification_report`。驗證內容包含：

- `index.html`
- `runtime.js`
- `styles.css`
- `site.json`
- `components/manifest.json`
- `README.md`
- `index.html` 宣告 `#app` 並載入 `./runtime.js`
- `runtime.js` 載入 `./site.json` 與 `./components/manifest.json`
- `components/manifest.json` 宣告 `site.json` 使用的所有 component type
- `site.json` 使用的每個非 generated component type 都有 `runtime.js` renderer
- generated component type 具備本機 `components/generated/<Type>.js` 與 `.json` 資產
- zip archive entries

一般使用者交付時，`package.quality_report.is_passed` 與 `package.verification_report.is_passed` 都必須為 true。

`package.verification_report.runtime_renderer_types` 會列出從 `runtime.js` 解析出的 renderer key。

## 輸出

成功結果包含：

- `crawl_run_id`
- `page_count`
- `excluded_count`
- `package.output_directory`
- `package.entry_point`
- `package.site_json_path`
- `package.manifest_path`
- `package.archive_path`
- `package.files`
- `package.quality_report`
- `package.verification_report`

結果是一個可重用的網站重製 package，不是來源網站的等價 clone。
