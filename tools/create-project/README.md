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
| `node scripts/publish.mjs` | 發佈 `dist\`:快照(穿透 junction)+ `SNAPSHOT.json` 憑證(commit/dirty/時間/檔數)+ 封閉驗證(fail 即擋) |
| `scripts\verify-sealed.mjs` | 靜態解析 JS/HTML/CSS 全部相對引用:禁逃出、禁外部腳手架路徑、禁根絕對、禁 bare import |
| `src\frontend\` | 最小起始頁(示範相對深度規則、元件掛載、escapeHtml、theme 鏈) |
| `docs\dev-and-publish.md` | 機制文件(核心不變式:引用字面永遠是 `lib/…` 相對路徑) |

產生器動作:複製範本 → 烙入專案名({{PROJECT_NAME}})→ 寫入 `.b4a-root`(每機腳手架指標,gitignored)→ git init → 建 junction → 驗證 lib 可達。

**腳本內零寫死路徑**:各 script 經 `scripts/resolve-b4a.mjs` 解析腳手架位置,順序=env `B4A_ROOT` → 專案根 `.b4a-root` → 同層搜尋(往上 3 層找 `Bricks4Agent\`)。換機器 clone:同層佈局零設定;否則設 env 或寫 `.b4a-root` 一行。

> 注意:清除含 junction 的專案前先跑 `node scripts/dev-link.mjs --unlink`(PS5.1 的 `Remove-Item -Recurse` 會追進 junction 誤刪腳手架)。

**機制 v2.0.0(2026-07-07)**:全腳本零依賴 Node(跨平台:Windows=junction、POSIX=symlink);**釘版**(`sync-lib.mjs --pin` → `b4a.lock.json` 入版控,git worktree 取 commit 內容,可重現);**publish 強制**(lock 不符=fail,`--allow-drift` 硬闖記入 SNAPSHOT 供稽核);`--no-junction` 建案自動釘版(團隊成員/CI);**升級工具** `upgrade-project.mjs --dest <專案> [--dry-run]`(只更新機制腳本、跳過 mechanism.json 的 customized 清單)。

驗證紀錄(2026-07-07):junction 開發開機+互動 ✅、publish 封閉 ✅、dist 密封開機 ✅、釘版建案+強制通過 ✅、lock 竄改 fail ✅、--allow-drift 稽核軌跡 ✅、升級工具(dry-run/customized 跳過)✅。POSIX 未實測(開發機為 Windows)。
