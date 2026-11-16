# create-project — 新專案產生器

一鍵產出以 Bricks4Agent 為腳手架的新專案,**內建「junction 開發 + 發佈快照」機制**(開發直用活腳手架、發佈打包自含複本+來源憑證+封閉性驗證)。

```powershell
# 於 Bricks4Agent 根:
node tools/create-project/create-project.mjs --name my-app
# 選項:--dest D:\path\my-app(預設=Bricks4Agent 同層)、--no-junction(複本模式)、--no-git
```

產生內容(見 `template\`):

| 產物 | 說明 |
|---|---|
| `node scripts/dev-link.mjs` | lib\ → 腳手架 連結(產生時已自動執行;`--unlink` 可拆;Windows=junction、POSIX=symlink) |
| `node scripts/dev.mjs` | 靜態伺服(root=專案根) |
| `node scripts/sync-lib.mjs` | 無腳手架機器的複本後備(junction 護欄拒跑) |
| `node scripts/publish.mjs` | 發佈 `dist\`：預設完整快照（穿透 junction）+ `SNAPSHOT.json` 憑證 + 封閉驗證（fail 即擋）；另有**實驗性**的 policy 裁剪（見下方「發布裁剪 policy」） |
| `scripts\verify-sealed.mjs` | 靜態解析 JS/HTML/CSS 全部相對引用:禁逃出、禁外部腳手架路徑、禁根絕對、禁 bare import |
| `scripts\b4a-lock.mjs` | `b4a.lock.json` 解析與建立（v2 釘 Git tree，v1 commit 僅相容讀取）；dev-link／sync-lib／publish 共用 |
| `scripts\snapshot-integrity.mjs` | 三庫內容盤點：逐檔 SHA-256 → 每庫摘要與合併摘要；publish 寫入 `SNAPSHOT.json`，`tools/scripts/verify-consumer-snapshots.mjs` 用來驗證 |
| `scripts\publish-policy.mjs`、`scripts\module-analysis.mjs` | **實驗性**發布裁剪：policy 讀取、可達檔案規劃與 JS 模組分析；`verify-sealed.mjs` 也用後者解析 import |
| `src\frontend\` | 最小起始頁(示範相對深度規則、元件掛載、escapeHtml、theme 鏈) |
| `docs\dev-and-publish.md` | 機制文件(核心不變式:引用字面永遠是 `lib/…` 相對路徑) |

產生器動作:複製範本 → 烙入專案名({{PROJECT_NAME}})→ 寫入 `.b4a-root`(每機腳手架指標,gitignored)→ git init → 建 junction → 驗證 lib 可達。

**腳本內零寫死路徑**:各 script 經 `scripts/resolve-b4a.mjs` 解析腳手架位置,順序=env `B4A_ROOT` → 專案根 `.b4a-root` → 同層搜尋(往上 3 層找 `Bricks4Agent\`)。換機器 clone:同層佈局零設定;否則設 env 或寫 `.b4a-root` 一行。

> 注意:清除含 junction 的專案前先跑 `node scripts/dev-link.mjs --unlink`(PS5.1 的 `Remove-Item -Recurse` 會追進 junction 誤刪腳手架)。

**機制 v2.2.0**：在 v2.1 tree 釘版與強制檢查基礎上，加入預設不啟用、**實驗性**的可達依賴發布裁剪，以及 staging 驗證後原子替換；**升級工具** `upgrade-project.mjs --dest <專案> [--dry-run]` 更新 `template\scripts` 的全部機制腳本（含 `b4a-lock.mjs`、`snapshot-integrity.mjs`）並跳過 customized 清單。既有專案升級後請在 `.gitignore` 補上 `.dist-staging-*/` 與 `.dist-backup-*/`。

驗證要求：一般 clone、淺層 `--no-tags` clone 都必須能以 branch 內建 tree 完成 sync/publish；lock 竄改、tree 漂移、dirty 工作區與 drift bypass 均須 fail closed。

### 發布裁剪 policy（實驗性）

> **實驗性功能**：預設不啟用，沒有 policy 時發布行為與 v2.1 相同。啟用時 publish 會印出 `[實驗性]` 標示與漏帶資產警告。目前還不適合直接套在整套內建元件庫上，限制見本節末。

預設不裁剪，保留既有完整發布行為。要輸出可達依賴子集，在專案根建立 `publish-policy.json`（自動讀取），或傳 `--policy <檔案>`。入口會從 `src/frontend` 的 HTML/CSS/JS 引用開始，沿著 library 靜態 import/export、字面動態 import、`new URL`、CSS/HTML 資產引用與常見靜態 `fetch`/Worker/Audio URL 遞迴保留。`entrypoints` 可加入沒有被應用直接引用的庫入口；`keep` 可保留明列檔案或目錄；不確定的動態 import 必須用 `dynamicImportKeep` 列出該來源可能載入的所有檔案/目錄，否則發布失敗。為避免 JS regex 與除法詞法歧義，來源 JS 中任何斜線 token 以及任何含 `${...}` 插值的模板字串都視為不確定，需明列候選。

```json
{
  "version": 1,
  "entrypoints": ["page-generator/index.js"],
  "keep": ["ui_components/vendor/leaflet"],
  "dynamicImportKeep": {
    "ui_components/data/RegionMap/RegionMap.js": ["ui_components/data/RegionMap/maps"]
  }
}
```

policy 路徑皆相對 `lib/`，不得逸出；動態規則的來源可用 `lib/` 相對路徑，也可用 `src/frontend/` 相對路徑。JS 靜態 import/export 由 Node `vm.SourceTextModule` 解析；無法確定的動態載入必須列出候選，未解析的相對引用直接失敗。裁剪前會在同層 staging 建完整候選產物並通過封閉驗證，成功後才替換 `dist/`；失敗會保留舊 `dist/`。`SNAPSHOT.json` 會記錄 policy 路徑、實際逐檔路徑/位元組數/SHA-256 與摘要；消費端驗證逐檔核對實際產物與 manifest，再對映來源，容許 policy 省略來源裡未列入 manifest 的檔案。

**已知限制（列為實驗性的原因）**

1. **只認靜態可達性**：靜態 import/export、字面 `import('…')`、`new URL('…', import.meta.url)`、HTML `src`/`href`、CSS `@import`/`url()`，以及字面的 `fetch`/`Worker`/`SharedWorker`/`Audio('…')`。執行期才組出來的路徑一律看不到。

2. **非 JS 的執行期資產不會自動帶入**，必須用 `keep` 明列。內建元件庫已知的例子：RegionMap 的地圖圖檔（`ui_components/data/RegionMap/maps`，約 60 檔）、Leaflet 由 JS 組路徑的 marker 圖（`ui_components/vendor/leaflet/images`）、以 `<link>` 動態載入的元件 CSS（如 `ui_components/common/ActionButton/ActionButton.css`）、`custom_components` 的 `registry.json` 與 `definitions/`。

3. **漏帶只會警告，不會擋下發布**：publish 會印出「位於已納入目錄、卻沒有靜態引用而未發布」的資產檔數量與所在目錄。警告範圍是已納入檔案所在的目錄，加上其下不含 JS 的子目錄；`.js`/`.mjs`、`.md`、`.html` 與 `*.manifest.json` 不列入。沒有警告不代表沒有缺漏。

4. **分析器對內建元件庫過度回報**：為了避開 regex 與除法的詞法歧義，JS 裡任何 `/` token、任何含 `${…}` 插值的模板字串都算「無法解析」，即使該檔沒有動態 import。2026-11 量測：`ui_components` 335 支 JS 有 154 支、`page-generator` 26 支有 20 支、`custom_components` 5 支有 4 支被點名。被點名的檔案只要在可達範圍內，就得各自在 `dynamicImportKeep` 補一條規則才能發布（確定沒有動態載入時，候選填該檔本身即可）。上面的範例通過 policy 格式與路徑檢查，但拿來發布整套內建元件庫，還要先補齊這些規則。

5. **封閉性驗證只檢查靜態引用**，不保證裁剪後執行期完整。上線前請實際操作各頁面；有疑慮就不要放 policy，改用預設的完整快照。

可用 `npm run test:publish-policy` 跑真實小專案 fixture、parser fail-closed、policy schema、文件範例路徑、漏帶資產警告、裁剪 manifest 缺檔、升級工具檔案清單與現有跨語系穩定排序回歸。
