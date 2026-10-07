系統原型（Bricks4Agent 定義網站）
================================

這是一份由頁面定義產生的前端原型：列表、明細與表單頁都可以操作，
資料只存在目前瀏覽器分頁的記憶體中（重新整理即清空），不連任何後端。

開啟方式
--------
瀏覽器基於安全規則，無法直接以雙擊 index.html（file://）的方式載入本原型的
JavaScript 模組，請改用本機 HTTP 伺服器開啟：

1. 在本檔所在的資料夾（與 index.html 同一層）開啟終端機。
2. 執行下列任一指令（只在本機 127.0.0.1 上提供，同網段的其他電腦連不到）：
     python -m http.server 8000 --bind 127.0.0.1
     npx http-server -a 127.0.0.1 -p 8000
   （本機沒有 http-server 時，npx 會先詢問是否下載，確認後才執行。）
3. 以瀏覽器開啟 http://127.0.0.1:8000/

若頁面空白，請確認伺服器以 text/javascript 回應 .js 檔案。

內容說明
--------
index.html、boot.js、app.css     外殼與路由
site.json                          網站標題與頁面順序
definitions/                       各頁的原始定義
definition-template.json           經驗證的完整定義
runtime/                           Bricks4Agent 元件庫與頁面渲染器
../report/                         驗證結果與檔案清單（sha256）


Prototype generated from page definitions (Bricks4Agent definition site)
------------------------------------------------------------------------
Browsers do not load JavaScript modules from file:// URLs. Serve this folder
from a local HTTP server bound to this computer only, for example
"python -m http.server 8000 --bind 127.0.0.1" or
"npx http-server -a 127.0.0.1 -p 8000" (npx asks before downloading the package),
and open http://127.0.0.1:8000/ . Records are kept in memory in the current tab only.
