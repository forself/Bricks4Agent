# 網站重製打包

爬取已確認的公開 URL，將渲染後的視覺與功能線索轉換為元件庫 `site.json`，執行產生品質門檻，並寫出本機靜態網站包。

## 能力

- Tool ID: `site.reconstruct.package`
- Capability ID: `site.reconstruct_package`
- Route: `site_reconstruct_package`
- Status: `beta`
- Risk: medium

## 執行前必要確認

執行前，使用者必須以連結深度確認爬取範圍：

- 入口頁連結：`max_depth = 1`
- 兩次連結跳轉以內：`max_depth = 2`
- N 次連結跳轉以內：`max_depth = N`

只爬根頁或目前頁面時，`max_depth = 0` 可作為明確的診斷或手動模式。

## 流程

1. 只爬取已確認的公開 HTTP/HTTPS 範圍。
2. 優先使用渲染後的視覺與功能線索；來源 HTML 只作輔助證據。
3. 將網站意圖轉換為模板 slot 與元件庫節點。
4. 預設執行品質門檻。
5. 寫出以 `index.html` 為進入點的 package。
6. 預設寫出可攜式 zip archive，供檔案交付使用。
7. 驗收 package 檔案、`site.json` 可渲染性與 archive 內容。

Package runtime 會載入 `site.json` 與 `components/manifest.json`，再渲染元件樹。它不得寫出任意頁面 HTML 或 DOM 等價複製。

## Strict 品質門檻

Strict mode 預設啟用。以下情況會阻擋 package 建立：

- 文件包含未解決的 `component_requests`；
- manifest 宣告生成元件；
- route 使用未知元件類型；
- route path 重複；
- route root 不是 `PageShell`。

失敗時，工具會回傳結構化的 `quality_report`，且不會寫出 package。

## Package 驗收

成功輸出會包含 `package.verification_report`。驗收器會檢查：

- `index.html`
- `runtime.js`
- `styles.css`
- `site.json`
- `components/manifest.json`
- `README.md`
- `index.html` 宣告 `#app` 並載入 `./runtime.js`
- `runtime.js` 載入 `./site.json` 與 `./components/manifest.json`
- `components/manifest.json` 宣告 `site.json` 使用到的每一種元件類型
- zip archive 條目

正常交付給使用者時，`package.quality_report.is_passed` 與 `package.verification_report.is_passed` 都必須為 true。

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

結果是一個可重用的重製網站 package，不是來源網站的等價 clone。
