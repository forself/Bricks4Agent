# 腳手架引用機制:開發直連、發佈快照

**設計目標**:開發階段**直接使用** Bricks4Agent 腳手架(改元件即時生效、零複製漂移);發佈時引用落地為**專案內複本**並打包(產物完全自含,附來源憑證)。

## 核心不變式:引用字面永遠是 `lib/…` 相對路徑

程式碼在開發與發佈**一字不改**——變的只有 `lib\` 的實體:

| 階段 | `lib\` 是什麼 | 建立方式 |
|---|---|---|
| 開發 | Windows **NTFS junction**／POSIX symlink → `Bricks4Agent\packages\javascript\browser\{ui_components,page-generator,custom_components}`(活腳手架) | `node scripts/dev-link.mjs` |
| 無腳手架的機器 | 真實複本(手動同步) | `node scripts/sync-lib.mjs`(複本模式後備,junction 護欄拒跑) |
| 發佈 | `dist\lib\` 真實快照(cpSync dereference 穿透連結取實體檔) | `node scripts/publish.mjs` |

不用 import map(嚴格 CSP 下內聯 importmap 需 hash 豁免)、不做發佈期改寫(改寫器本身就是風險面)——junction 讓兩個需求同時成立。

### 相對深度規則(寫新頁面必守)

import 的 `../` 層數 = 檔案到專案根的深度:

```
src/frontend/X.js                 → '../../lib/ui_components/...'
src/frontend/core/X.js            → '../../../lib/ui_components/...'
src/frontend/pages/<域>/X.js      → '../../../../lib/ui_components/...'
```

**禁**:根絕對 `/lib/...`(綁死部署根)、`../Bricks4Agent/...`、`packages/javascript/browser/...`(逃出專案)。發佈驗證器會全部擋下。

## 日常指令

```powershell
node scripts/dev-link.mjs          # 一次性:lib 轉 junction(開發模式)
node scripts/dev.mjs               # 靜態伺服 8123(root=專案根)
node scripts/publish.mjs           # 發佈:dist\(快照+憑證+封閉驗證,違規即 fail)
```

### 發布裁剪 policy（實驗性）

> **實驗性功能**：預設不啟用，沒有 policy 時發布行為不變。啟用時 publish 會印出 `[實驗性]` 標示與漏帶資產警告。目前還不適合直接套在整套內建元件庫上，限制見本節末。

預設保留三個 library 的完整快照。若要產生可達依賴子集，可在專案根加入 `publish-policy.json`（自動讀取），或執行 `node scripts/publish.mjs --policy <檔案>`：

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

`entrypoints`、`keep` 及動態候選檔案/目錄使用相對 `lib/` 的路徑。裁剪器從 `src/frontend` 的 HTML/CSS/JS 引用及列出的入口開始，沿靜態 import/export、字面動態 import、CSS/HTML 資產、`new URL` 與常見靜態 fetch/Worker/Audio URL 建立可達集合。遇到無法解析的 `import(expr)`、`import.meta.glob()`、JS 斜線（regex 與除法的詞法歧義），或含 `${...}` 插值的模板字串時，必須在 `dynamicImportKeep` 以來源路徑列出所有候選檔案/目錄；否則 fail closed。動態來源可用 `lib/` 或 `src/frontend/` 相對路徑。未解析的其他執行期資產路徑須以 `keep` 明列。

發布先建同層 staging、完成封閉性驗證後才替換 `dist/`；建置或驗證失敗保留舊產物。SNAPSHOT 記錄 policy 路徑與實際發布內容摘要；消費端按相對路徑逐檔核對已發布檔案對應的腳手架 bytes，policy 省略的檔案不視為漂移。

**已知限制（列為實驗性的原因）**

1. **只認靜態可達性**：靜態 import/export、字面 `import('…')`、`new URL('…', import.meta.url)`、HTML `src`/`href`、CSS `@import`/`url()`，以及字面的 `fetch`/`Worker`/`SharedWorker`/`Audio('…')`。執行期才組出來的路徑一律看不到。

2. **非 JS 的執行期資產不會自動帶入**，必須用 `keep` 明列。內建元件庫已知的例子：RegionMap 的地圖圖檔（`ui_components/data/RegionMap/maps`，約 60 檔）、Leaflet 由 JS 組路徑的 marker 圖（`ui_components/vendor/leaflet/images`）、以 `<link>` 動態載入的元件 CSS（如 `ui_components/common/ActionButton/ActionButton.css`）、`custom_components` 的 `registry.json` 與 `definitions/`。

3. **漏帶只會警告，不會擋下發布**：publish 會印出「位於已納入目錄、卻沒有靜態引用而未發布」的資產檔數量與所在目錄。警告範圍是已納入檔案所在的目錄，加上其下不含 JS 的子目錄；`.js`/`.mjs`、`.md`、`.html` 與 `*.manifest.json` 不列入。沒有警告不代表沒有缺漏。

4. **分析器對內建元件庫過度回報**：為了避開 regex 與除法的詞法歧義，JS 裡任何 `/` token、任何含 `${…}` 插值的模板字串都算「無法解析」，即使該檔沒有動態 import。2026-11 量測：`ui_components` 335 支 JS 有 154 支、`page-generator` 26 支有 20 支、`custom_components` 5 支有 4 支被點名。被點名的檔案只要在可達範圍內，就得各自在 `dynamicImportKeep` 補一條規則才能發布（確定沒有動態載入時，候選填該檔本身即可）。上面的範例通過 policy 格式與路徑檢查，但拿來發布整套內建元件庫，還要先補齊這些規則。

5. **封閉性驗證只檢查靜態引用**，不保證裁剪後執行期完整。上線前請實際操作各頁面；有疑慮就不要放 policy，改用預設的完整快照。

### 腳手架位置解析(腳本內零寫死路徑)

各 script 經 `scripts/resolve-b4a.mjs` 解析,順序:
1. 環境變數 **`B4A_ROOT`**(機器/CI 層級)
2. 專案根 **`.b4a-root`** 檔(單行路徑;每機自有、gitignored;建案時自動寫入)
3. **同層搜尋**:專案往上 3 層找 `Bricks4Agent\`(標準佈局零設定)

換機器 clone 後:符合同層佈局即直接可用;否則設 `B4A_ROOT` 或寫 `.b4a-root` 一行。

## 發佈產物(dist)

```
dist\
  index.html            # 轉導 src/frontend/
  lib\                  # 腳手架真快照(排除 node_modules、*.test.mjs、page-generator/examples)
  lib\SNAPSHOT.json     # 來源憑證:tree/dirty、commits、UTC、檔數/位元組，以及三個 library 的逐檔 SHA-256/content digest
  src\frontend\         # 應用(與 repo 同幾何 → import 原樣可用)
```

部署=任何靜態伺服器指向 dist(或作為後端的 wwwroot);進入點 `/src/frontend/index.html`。

## 釘版與強制(團隊/CI 模式)

| 檔案 | 性質 | 作用 |
|---|---|---|
| `b4a.lock.json`(專案根) | **入版控** | 全隊統一、由目前 branch 可達的腳手架 Git tree（`node scripts/sync-lib.mjs --pin` 產生/更新） |
| `lib/.sync-state.json` | gitignored(隨 lib) | 記錄 lib 實際同步自哪個 tree/source、是否釘版 |
| `scripts/mechanism.json` | 入版控 | 機制版本 + `customized` 清單(升級工具跳過自訂檔) |

- **複本模式 + lock**：v2 lock 直接釘 Git tree object；只接受乾淨、已 commit 且可由目前 branch 到達的 B4A tree。淺層或 `--no-tags` clone 不需額外 tag/split commit。

- **publish 強制**：lock 存在時，連結模式要求目前 tree==lock 且乾淨；複本模式要求 sync-state tree/source==lock。違者 fail；`--allow-drift` 已停用。

- **快照內容強制**：新 publish 會把 `ui_components`、`page-generator`、`custom_components` 的逐檔相對路徑、位元組數、SHA-256 與總 digest 寫入 `SNAPSHOT.json`。舊的完整未裁剪快照（沒有逐檔 entries）仍以原有 combined digest 與完整來源 inventory 驗證；裁剪快照必須有逐檔 entries。B4A 維護端可執行 `npm run verify:consumer-snapshots -- --consumer-root <consumer>`，同時驗證來源 tree、dirty 狀態、檔案映射、content digest 與實際 bytes；裁剪發布只能省略 manifest 未列出的來源檔案，實際產物缺少 manifest 列出的檔案仍會失敗。

- **建案分流**:`create-project.mjs` 預設連結(腳手架維護者);`--no-junction` 走複本並**自動釘版**(團隊成員/CI)。

- **機制升級**:`node <B4A>/tools/create-project/upgrade-project.mjs --dest <專案> [--dry-run]`——只更新機制腳本、跳過 customized、印變更清單,git diff 審閱後 commit。

## 封閉性驗證(scripts\verify-sealed.mjs)

publish 內建、可單獨跑:`node scripts\verify-sealed.mjs dist`。JS static import/export 由 Node `vm.SourceTextModule` 解析（跨行語法與註解不靠 import regex），另檢查字面動態 import、`new URL`、HTML(src/href)、CSS(@import/url())及其相對引用；相對引用缺檔、逃出 dist 根即失敗，另擋引用含 `packages/javascript/browser`|`Bricks4Agent`、根絕對路徑與 JS bare 匯入。不是 ES 模組、但能當傳統 script 編譯的檔案（例如 sloppy 語法的舊程式）只檢查字面動態 import 與 `new URL`；兩種都編譯不過才算解析失敗。無法解析的動態 `import(expr)` 或 `import.meta.glob()` 由發布 policy 要求明列動態候選檔案/目錄。

## 陷阱備忘

- `lib/`、`dist/` 不入版控；可重現性靠 v2 tree lock 與 SNAPSHOT.json。publish 的暫存目錄 `.dist-staging-*/`、`.dist-backup-*/` 正常會自行清掉，行程被強制中止時可能殘留，同樣不入版控（從舊版升級的專案請自行補進 `.gitignore`）。

- PS 5.1 腳本一律英文註解(無 BOM UTF-8 中文會 parse 失敗);PS 內呼叫 git 走 `cmd /c … 2>nul`(原生 stderr 轉錄陷阱)。

- 別手改 `lib\` 內容(junction 模式下那就是腳手架本體!改元件回 Bricks4Agent repo 走正規流程)。
