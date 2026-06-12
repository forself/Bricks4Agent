# 網站來源爬取

在已確認的連結深度範圍內安全爬取公開 HTTP/HTTPS 網站，回傳渲染後的視覺與功能線索，以及供下游生成器重製用的輔助原始碼提示。

## 能力

- Tool ID: `site.crawl.source`
- Capability ID: `site.crawl_source`
- Route: `site_crawl_source`
- Status: `beta`
- Risk: medium

## 執行前必要確認

執行前必須由使用者確認爬取深度：

- 入口頁連結：`max_depth = 1`
- 兩次連結跳轉以內：`max_depth = 2`
- N 次連結跳轉以內：`max_depth = N`

只爬根頁或目前頁面時，`max_depth = 0` 只在使用者明確要求的手動或安全診斷模式下使用。

範圍以連結跳數為準，URL 實體路徑深度不作為網站層級定義。`link_depth` 會以廣度優先逐層爬取；若遇到安全預算限制，必須先保留較低層的完整性，再進入更深層。

對學校、機構或多子網域網站，可用 `scope.allowed_host_suffixes` 納入公開的同站子網域，例如 `["ntub.edu.tw"]`。這仍會套用公開 HTTP/HTTPS 與私網阻擋規則，但允許 `www.ntub.edu.tw`、`sec.ntub.edu.tw` 等子網域被視為同一個爬取範圍。不得用來擴大到無關網域。

## 安全規則

- 只允許公開 HTTP/HTTPS URL。
- 不使用登入、委派憑證、cookies、私人 session、localhost、loopback、link-local 或私有網路目標。
- 執行中不得超出已確認的 same-origin 或 allowed same-site host-suffix 範圍。
- budgets 是安全限制，不可替代使用者確認的爬取深度。

## 輸出契約

結果是 `SiteCrawlResult`，包含 `crawl_run_id`、`status`、`root`、`pages`、`excluded`、`extracted_model` 與 `limits`。

頁面可包含渲染後的 visual snapshots：可見區塊、版面座標、文字階層、媒體、連結、表單與來源 selector。爬取可以包含比 visual snapshots 更多的頁面與 routes；`html`、`text_excerpt`、`links`、`forms` 等原始碼導向欄位仍只是輔助證據。

輸出是渲染版面線索擷取，不是完整 CSS/assets 擷取、完整 JavaScript 行為擷取、登入流程擷取或等價 clone。

## 下游生成器規則

下游 converter 必須以渲染後的視覺線索作為主要重製參考，原始 HTML 只作輔助證據。converter 必須將線索映射到自訂元件庫；不得產生 DOM、像素、標籤等價或渲染行為等價的 clone。若沒有現有元件能對應某個功能或版面線索，converter 階段必須先嘗試由原子元件組裝，再記錄元件庫缺口。
