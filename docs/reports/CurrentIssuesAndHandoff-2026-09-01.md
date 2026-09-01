# 修改與遺留問題 — 2026-09-01

> 2026-09-02 更新：本文件保留為 9/1 的歷史交接快照。其後已新增通用 lazy module loader，並將原本未納入主閘門的 Vitest 元件套件修至 210/210 通過且併入 `npm test`；請以目前 Git 與測試輸出判定最新狀態。

> 本文件自足，不依賴先前對話。涵蓋 `11e8e95..df2c31a` 這四個提交的內容，以及當下經覆核的遺留問題。
> 每一項遺留問題都附可自行驗證的證據（檔案:行號或指令），請勿只憑本文件的敘述行動——先覆核再動手。
> 基準：分支 `main`，工作樹狀態另查 `git status`。

---

## 1. 本次修改

四個提交，全部直接在 `main`，未開分支。閘門全過（見 §1.5）。

### 1.1 `4b6a654` — 讓三個「靜默」行為可被診斷

前次修復把三處行為改成「安全但沉默」：結果是對的，但看結果的人查不出原因。

| 行為 | 原本 | 現在 |
|---|---|---|
| `raw()` 標記 | 未加品牌的 `{ __html }` 被當一般文字跳脫（刻意，JSON 可偽造），但完全無聲 | 提示一次並指名 `raw()`，回傳值不變、不在渲染迴圈洗版 |
| 延後分頁 | 未啟用分頁的面板是空的，外觀與「定義有誤」相同 | 未填充的面板帶 `data-tab-pending="true"`，首次啟用後移除 |
| 定向更新 | 無工具可證明與完整重建等價 | 新增 `tools/scripts/lib/dom-equivalence.mjs` |

`dom-equivalence.mjs` 涵蓋 `:checked` / `:value` / `:disabled` / `:hidden` 等 `innerHTML` 看不到的 live property。
驗證：真實瀏覽器 DataTable 選取，正向等價通過，三項負向對照（注入 class、只改 property、還原）全部如預期。

> 該工具第一版是**假通過**——序列化出空樹，`空 === 空` 當然相等。是負向對照抓到的。
> 根因是 `fake-dom.mjs` 不解析 `innerHTML`；此陷阱已寫入該檔檔頭註解。

### 1.2 `6e13e09` — 兩處「驗了什麼都沒驗到」的閘門

**展示廊的 TreeList 從來沒渲染過任何節點。** `sample-data.js` 傳 `items`，元件讀 `options.data`；且節點沒有 `id`，而 TreeList 以 `node.id` 作 dataset / `expandedIds` / `activeId` 的鍵，補了 `data` 卻沒 id 會讓 `String(node.id)` 成為字面 `undefined`、所有節點共用展開狀態。兩者一起修。

瀏覽器負向對照：

| | 元素數 | 文字 |
|---|---|---|
| 修正前（`items`） | **0** | 空 |
| 修正後（`data` + id） | 2 | 節點1 |
| 展開後 | 4 | 節點1子節點 |

**`serializeDom` 看不見逐屬性 style 賦值。** 真實 DOM 會把逐屬性賦值回寫進 `cssText`，但兩套 fake DOM 的 `style` 是只有 `cssText` 的普通物件，因此 `el.style.background = ...` 對序列化完全隱形。EditableTable 正是逐屬性寫法，所以這不是理論問題。改為兩個來源都收、正規化成排序後的 `prop: value` 清單。

**驗證進得了 CI。** `ui_components` 底下 7 個 `*.test.mjs` 原本沒有任何 npm script 會執行，新增的 `dom-equivalence.mjs` 也零 import。新增 `npm run test:ui-components`（後於 `738be65` 擴及 `page-generator`）。

> 陷阱：`node --test` 給**目錄**參數時，會把有 `index.js` 的目錄當模組載入，載入成功就報 `ok`。
> `ui_components` 正是如此——看到 `ok 1 - ui_components` 時，7 個測試檔一個都沒跑。**必須用 glob。**

### 1.3 `738be65` — 綁定值沒變就不重推

`DynamicToolRenderer._applyBindings` 過去無條件套用：每次 `setState` 都深拷貝並呼叫 setter，而沒有對應 `set<Option>` setter 的綁定會走 `_replaceComponent`，也就是整個元件銷毀重建。消費端（如 Custom Component Studio 的 `syncUi`）習慣每次同步把整份 state 攤平後對每個葉路徑呼叫 `setState`。

實測（真實頁面，57 條綁定 / 98 個元件，同頁內還原 `applied` 旗標模擬改動前）：

| 一次無變動的同步 | DOM 變動 | 耗時 |
|---|---|---|
| 改動前 | **1900 次** | 182.2ms |
| 改動後 | **0 次** | 3.8ms |

負向對照確認沒有把 UI 凍住：真的改值 → 122 次 DOM 變動；還原 → 121 次；再送同值 → 0 次。

設計要點：

- `sameData()` 的值域與 `cloneData` 一致（JSON 相容、無函式、無迴圈、數值有限），故不必處理 Date / Map，也不會遇到 NaN。物件連鍵順序一起比——順序變了就當作變了，寧可多推一次，也不要漏掉「元件依 `Object.entries` 順序渲染」的情形。
- 比較對象刻意是**上次推給元件的值**，不是元件現值。拿現值比會在每次同步把使用者尚未提交的輸入蓋回去。

**刻意的行為變更（唯一對外可觀察的語意差異）：** 不相關的 `setState` 不再把元件自身狀態（未提交輸入、捲動位置）重設回綁定值。已記入 `CLAUDE.md`。

### 1.4 `df2c31a` — 預建實例不再帶著 mount 前的舊值進畫面

factory 無法回答「有沒有這個元件」時（`_factoryHas` 回 `null`），`init()` 會先用當時的 state 把實例建出來。`init()` 與 `mount()` 之間若呼叫 `setState`，`_applyBindings` 因尚未 mounted 而跳過；`mount()` 雖重算了一份 options，重用預建實例時卻把新值丟棄，舊值就這樣進入畫面，直到之後剛好有人動到同一條路徑為止。

修正三處，各自都有必要：`_prepareComponent` 記下建構當時實際用到的綁定值；`_renderComponent` 重用時以**那份舊值**當種子（種成新算的 options 會把舊值標記為已套用，該綁定將永遠收不到更新，等於把偶發的過時變成永久的過時）；`mount()` 收尾補一次 `_applyBindings()`，值沒變的會被相等性檢查跳過。

這不是新的失敗模式：綁定路徑若在 mount 時消失，`_renderComponent` 內的 `_createComponentOptions` 本來就會拋錯。

### 1.5 驗證狀態

| 閘門 | 結果 |
|---|---|
| `npm test` | PASS |
| `npm run test:ui-components` | 25 / 25 |
| `npm run test:custom-components` | 14 / 14 |
| `node tools/scripts/audit-csp.mjs` | 304 檔，CSP 0 / SVG 0 |
| `npm run validate:ui-library` | 全過（browser smoke 未要求） |
| `build-metadata.mjs --check` | exit 0 |
| `npm run test:theme-studio:browser` | **未跑成**——本機 Edge 起不來（見 §2.7） |

新增的測試都做過「對修正前版本執行」的負向對照，確認不是空過：

- 綁定相等性檢查：7 個案例中 5 個在改動前失敗，2 個兩邊都過（那兩個是「行為不得改變」的守門：`options` 同步、綁定路徑消失仍須拋錯）。
- 預建實例修正：新增的 3 個案例中,正好是預期的 2 個在改動前失敗，對照組「mount 前沒有變更時不得平白套用或重建」兩邊都過。

---

## 2. 遺留問題

依「值得先做的程度」排序，理由見 §3。

### 結構性 — 「修了但沒生效」

**2.1 `templates/` 不在資安閘門的掃描範圍內。**
`tools/scripts/audit-csp.mjs` 的 `roots`（第 14 行起）只有 5 個：`ui_components`、`page-generator`、`custom_components`、`tools/custom-component-studio`、`tools/theme-studio`。**不含 `templates/`。**
而 `templates/spa/frontend/components/Panel/`（`BasePanel.js` / `ModalPanel.js` / `PanelManager.js` / `ToastPanel.js`）是 `core/BasePage.js` 實際 import 的第二份副本。也就是說，真正出貨到生成專案的那份程式碼是沒有閘門的。

**2.2 tim-web 內嵌了約 6 份元件庫快照。**
`qa-publish-20260817`、`qa-live-fixed-20260817`、`qa-debug-20260817`、`outputs/validation/iis-fixed`、`outputs/validation/iis-fixed-final`、`artifacts/icon-guard-iis`，各自的 `wwwroot/lib/ui_components/`。上游修補（含本次的 `raw()` 品牌檢查、ModalPanel 洩漏修復）不會自動到達，要重新發佈才會。

### 驗證覆蓋

**2.3 沒有 CI。** 無 `.github/workflows`；所有閘門靠人記得跑。

**2.4 必跑的 `tools/theme-studio/run.mjs` 對 outline / palette / TreeList 零覆蓋。** 只斷言 gallery 的 116 與 `failed === 0`。§1.2 修好之後，gallery 對 TreeList 才第一次有渲染驗證價值；outline 面板仍無覆蓋。

**2.5 fake DOM 的能力缺口。** `scrollTop`、`customElements`、`MutationObserver`、`requestAnimationFrame` 在兩套 fake DOM 都不存在。因此「捲動位置保存」「Icon 連上 DOM 後重畫」「theme-bus 通知與訂閱洩漏」這幾類斷言在 Node 裡只能空過——寫了也是恆綠。要驗這些只能上真實瀏覽器。

**2.6 `4a5efe8` 宣稱的 100.3ms → 1.9ms 沒有對應腳本進版控**，無法覆核。本文件 §1.3 的量測同理——是在瀏覽器 console 手動跑的，沒有進版控的 benchmark。

**2.7 本機 Edge 起不來。** `node tools/theme-studio/run.mjs` 回報 `Could not connect to Edge DevTools pipe: Edge exited unexpectedly (code=0)`。本次改用內建瀏覽器手動驗證替代。原因未查（可能是既有實例佔著設定檔）。

### metadata 品質（這份 catalog 是對代理公開的產品本體）

**2.8 `EditableTable` 的 manifest 在說謊。**
`component.manifest.json` 宣告 `binding: { value_io: true, target_actions: ["clear", "setValue"] }`，但 `EditableTable.js` 這些方法**一個都沒有**（公開成員只有 `constructor` / `snapshot` / `send` / `mount` / `getRows` / `destroy`）。旗標來自 `this._cellInputs.clear()` 與對子元件 TextInput 的 `setValue` / `getValue` 呼叫。

**2.9 推導方式是整檔正則。** `metadata/renderer.js` 的 `inferBinding` 對整份 source text 做 `/clear\s*\(/`、`/(?:reload|refresh)\s*\(/`、`/setItems\s*\(/` 等比對，因此**把方法命名成 `clear(` / `refresh(` / `setItems(` 就會污染 catalog**，且 `build-metadata --check` 是逐位元組比對，會直接失敗。
19 個元件檔含 `setData(`；若要讓 `inferBinding` 認得 `setData`，那是一次 19 個元件的 catalog diff，不是一個。

### 元件層（已評估，建議維持不做）

**2.10 TreeList `setData()` 無條件整棵重建**（`container.innerHTML = ''`）；不修剪 `expandedIds`、不驗證 `activeId` 仍存在於新資料、不保存 `scrollTop`。`_visibleSignature()` 已存在，可用來做 early-return。
唯一真實消費端是 Custom Component Studio 的結構 Outline 面板（`tools/theme-studio/studio.page.json` 的 `outline-tree`），版控中的四個 custom component 定義節點數為 1 / 3 / 3 / 5、深度 ≤2，因此上述三個缺陷有兩個實際觸發不到、一個無感。

**2.11 EditableTable SORT 一律全表重繪** → 抹掉未提交輸入、驗證錯誤訊息、焦點與游標；且沒有公開 API 可更新列。目前沒有真實使用者會點到那個排序表頭。

**2.12 DataTable `setSelectedRows()` 仍是整體重繪**（`DataTable.js:312-315` 直接 `this.render()`），排序 / 分頁 / `setData` 亦然。先前的定向更新只覆蓋 checkbox 的 change 事件路徑。

**2.13 完整清單協調（新增 / 刪除 / 重排）：不建議做。**
評估為 450–750 行實作 + 400–600 行測試。成本驅動因子不是 diff 演算法，是不變式數量：TreeList 把 `level` 烘進樣式字串，使「搬移子樹」無法用純 DOM 搬移完成（這正是現行程式碼寧可摧毀重建的原因）；每列固定 2 個 Icon 各自訂閱 theme-bus，漏 `destroy` 就是新的洩漏。
且兩個元件都在 `generator-support-matrix.json` 的 `manual_only_components`（`generator.usable = false`），欄位驅動的產生器在架構上永遠吐不出它們——使用量不會自然長出來。

### 其他

**2.14 studio 主控台三個既有錯誤**：`AddressInput requires a real loadCities data loader`、`OrganizationInput requires a real loadUnits data loader`。展示用資料載入器缺失，與綁定層無關，但代表這幾個元件在 studio 裡是壞的。

**2.15 `_replaceComponent` 的節省目前是潛在的。** studio 那 57 條綁定的 setter 恰好都存在，所以 §1.3 量到的收益全部來自 setter 側。一旦有 tool page 綁到沒有 setter 的選項，省下的就是整個元件的銷毀重建。

---

## 3. 建議順序

1. **2.1（`templates/` 沒有資安閘門）** — 這是資安閘門有洞，而且洞的另一邊正是出貨給下游的程式碼。
2. **2.3（沒有 CI）** — 所有閘門都靠人記得跑；上面每一項驗證的長期價值都繫於此。
3. **2.8 / 2.9（catalog 說謊）** — 這份 metadata 是 AI 代理選用元件的輸入，錯誤宣告會直接誤導產生的頁面。

2.10 – 2.13 維持不做，理由如各項所述：改的是沒人走的路，而風險類別（元件回收洩漏）恰好是近期才付出代價修好的。

---

## 4. 本次新增 / 變更的檔案

| 檔案 | 動作 |
|---|---|
| `packages/javascript/browser/ui_components/utils/security.js` | `raw()` 未加品牌時提示一次 |
| `packages/javascript/browser/page-generator/DynamicDetailRenderer.js` | `data-tab-pending` 標記 |
| `packages/javascript/browser/page-generator/DynamicToolRenderer.js` | `sameData` + 綁定相等性檢查 + 預建實例種子 + mount 收尾套用 |
| `packages/javascript/browser/page-generator/DynamicToolRenderer.bindings.test.mjs` | 新增，10 個案例 |
| `packages/javascript/browser/ui_components/common/TreeList/test-dom.mjs` | `FakeClassList` 補 `add` / `remove` / `toggle` |
| `tools/scripts/lib/dom-equivalence.mjs` | 新增，DOM 等價比對 |
| `tools/scripts/lib/dom-equivalence.test.mjs` | 新增，5 個案例（含負向對照） |
| `tools/theme-studio/sample-data.js` | TreeList sample 由 `items` 改 `data` 並補 `id` |
| `package.json` | 新增 `test:ui-components` |
| `CLAUDE.md` / `AGENTS.md` / `AGENT-UI-GUIDE.md` | 對應說明 |
| `.gitignore` | 忽略 160MB 的 `.codegraph-cache/` |
