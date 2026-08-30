# 腳手架引用機制:開發直連、發佈快照

**設計目標**:開發階段**直接使用** Bricks4Agent 腳手架(改元件即時生效、零複製漂移);發佈時引用落地為**專案內複本**並打包(產物完全自含,附來源憑證)。

## 核心不變式:引用字面永遠是 `lib/…` 相對路徑

程式碼在開發與發佈**一字不改**——變的只有 `lib\` 的實體:

| 階段 | `lib\` 是什麼 | 建立方式 |
|---|---|---|
| 開發 | **NTFS junction** → `Bricks4Agent\packages\javascript\browser\{ui_components,page-generator}`(活腳手架) | `node scripts/dev-link.mjs` |
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
  lib\SNAPSHOT.json     # 來源憑證:Bricks4Agent tree/dirty、repository commit、專案 commit、UTC 時間、檔數/位元組
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

- **建案分流**:`create-project.mjs` 預設連結(腳手架維護者);`--no-junction` 走複本並**自動釘版**(團隊成員/CI)。

- **機制升級**:`node <B4A>/tools/create-project/upgrade-project.mjs --dest <專案> [--dry-run]`——只更新機制腳本、跳過 customized、印變更清單,git diff 審閱後 commit。

## 封閉性驗證(scripts\verify-sealed.mjs)

publish 內建、可單獨跑:`node scripts\verify-sealed.mjs dist`。逐檔靜態解析 JS(import/from/動態 import/new URL,先剝註解)、HTML(src/href)、CSS(@import/url()),斷言每個相對引用 (a) 不逃出 dist 根 (b) 目標存在;另擋:引用含 `packages/javascript/browser`|`Bricks4Agent`、根絕對路徑、JS bare 匯入。`${…}` 模板字串與 `node:`/http(s)/data: 協定略過。

## 陷阱備忘

- `lib/`、`dist/` 不入版控；可重現性靠 v2 tree lock 與 SNAPSHOT.json。

- PS 5.1 腳本一律英文註解(無 BOM UTF-8 中文會 parse 失敗);PS 內呼叫 git 走 `cmd /c … 2>nul`(原生 stderr 轉錄陷阱)。

- 別手改 `lib\` 內容(junction 模式下那就是腳手架本體!改元件回 Bricks4Agent repo 走正規流程)。
