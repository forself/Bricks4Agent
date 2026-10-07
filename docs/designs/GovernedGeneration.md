# 受治理生成（Governed Generation）

日期：2026-10-07
狀態：第一個切片已實作（system_scaffold）；預設仍是 Legacy，以設定開啟。

## 1. 摘要

使用者在 LINE 或 portal 要求「產生系統雛形」（`/建立 完整系統雛形 … #名稱`，或 `/proj` 訪談後 `/ok`），確認 draft 回 `y` 之後：

- **Legacy（預設）**：沿用 broker 程序內的佔位頁生成（`HighLevelSystemScaffoldService`），行為不變。
- **Governed**：broker 不在程序內寫任何專案檔案，而是依任務啟動一個受控代理。代理經 broker 的 LLM 代理撰寫 DefinitionTemplate，再透過三個生成能力走 PEP（查型錄、驗證定義、生成並打包）。generation-worker 以 B4A 元件庫確定性產出多頁前端原型並打成 zip；broker 驗證產物的路徑與 sha256 後交付給使用者。

模式開關是 `HighLevelCoordinator:Generation:SystemScaffoldMode`，值為 `Legacy` 或 `Governed`。其他值視為設定錯誤：走 Governed 路徑並由就緒檢查拒絕，不會靜默退回 Legacy。

產物是 DefinitionTemplate 驅動的多頁前端原型（list、detail、form 三種頁型），用瀏覽器內記憶體 store 讓頁面可以操作，不含後端。zip 的內容與外殼見 [tools/generation](../../tools/generation/cli.mjs) 與 [templates/definition-site](../../templates/definition-site/README.txt)。Governed 模式的 draft 預覽照實說明這個產物（前端可操作原型，不含後端、資料庫與登入），不列 Legacy scaffold 的前端框架、後端、資料庫、登入與封裝格式。

## 2. 端到端流程

| # | 執行者 | 動作 |
|---|---|---|
| 1 | 使用者入口 | line-worker 呼叫 `/api/v1/high-level/line/process`，或 portal 呼叫 `/api/v1/portal/commands` |
| 2 | coordinator | 分類 → AllowProduction 權限閘 → 建立 system_scaffold draft → 使用者回 `y` → `ConfirmDraft`。同一位使用者的確認依序執行並重新讀取 draft，一份 draft 只確認一次（重複的 `y` 回 `draft_not_pending`）；確認時重新檢查 AllowProduction，draft 建立後被降級的使用者回 `production_disabled` |
| 3 | coordinator（Governed） | 就緒檢查（見 §5）。不就緒就回覆「系統雛形生成暫不可用」，不建立任務、不建立專案資料夾 |
| 4 | coordinator | 名稱重查（在就緒檢查之前）、升格閘、ExecutionIntent；名額檢查（§5）通過後建立 Task、Plan、Handoff（與 Legacy 相同的前半段）。名額檢查到代理啟動與其他受治理的啟動依序進行 |
| 5 | `GovernedGenerationLauncher` | 建立 AI 主體 `prn_{任務後綴}`，任務指派給它與 `role_executor`；runtimeDescriptor 換成只含三個 grant（scope 由 broker 寫入、配額以任務累計）、生成上限與可用時的 `llm` 的精簡版本；handoff 加上淨化過的 `generation_request`，讀取權只限系統 |
| 6 | `GovernedGenerationLauncher` | 寫入執行紀錄，簽發註冊憑證並啟動代理容器（`AGENT_RUN` 帶工作項，`AGENT_MAX_ITERATIONS` 預設 12）。啟動不跟著請求取消。成功才刪除 draft，立即回覆「已受理（任務 id）」 |
| 7 | 受控代理 | `query_component_catalog` → 經 `/api/v1/llm/chat` 撰寫定義 → `validate_definition`（依錯誤修正）→ `generate_scaffold` |
| 8 | broker PEP | session、grant、配額、PolicyEngine（route 相符；catalog 與 validate 為 auto；generate 為 auto_if_task_scope_match）→ 分派給 generation-worker |
| 9 | generation-worker | 重新完整驗證定義，輸出位置只取自 grant scope：`{Generation:OutputRoot}/{output_slot}/{requestId}/`；決定性打包 zip，回傳相對路徑、sha256 與摘要 |
| 10 | `GenerationIngestingDispatcher` | 驗證 worker 的結果（§6），把 zip 複製到使用者的文件區，寫入證據文件 `generation.execution.{requestId}`，執行請求記為 Succeeded 並帶 evidenceRef |
| 11 | `GenerationDeliveryService` | 交付：產物紀錄（RelatedTaskId 為該任務，portal 產物清單看得到）→ Drive 上傳（失敗、逾時或網路錯誤時改用簽章下載連結）→ LINE 通知 → 任務 Completed → 停用代理（主體停用、撤銷憑證與 session）並停止容器 |

LINE 的處理逾時有上限，所以確認之後改為非同步：先回「已受理」，完成後以 LINE 通知與 portal 產物清單送達。回覆不帶主機路徑。

## 3. 能力與 grant scope

三個能力以 tool-spec（`packages/csharp/broker/tool-specs/generation.*`）為唯一來源，由 broker 啟動時同步成 capability，status 為 beta，`runtime_required` 為 `generation-worker`。它們不在 in-process 降級清單中：沒有 generation-worker 時直接失敗，不會在 broker 程序內執行。

| capability | route（同代理工具名） | 風險／審批 | 每個任務的配額 | broker 寫入的 scope |
|---|---|---|---|---|
| `generation.catalog.query` | `query_component_catalog` | Low／auto | 20 | `{"routes":["query_component_catalog"]}` |
| `generation.definition.validate` | `validate_definition` | Low／auto | 6 | `{"routes":["validate_definition"],"max_pages":12}` |
| `generation.scaffold.generate` | `generate_scaffold` | Medium／auto_if_task_scope_match | 2 | `{"routes":["generate_scaffold"],"output_slot":"<任務 id>","package_name":"<淨化後的資料夾名>","max_pages":12,"package":"definition-site-v1"}` |

- validate 的 scope 也帶 `max_pages`：選取的頁數超過時，worker 在驗證時就回 `ok:false` 與 `MAX_PAGES_EXCEEDED`，代理不必等到 generate 才知道。
- generate 的審批：使用者在 ConfirmDraft 回的 `y` 即同意；請求超出 scope 時送管理員審批，審批畫面顯示標題、各頁的 id／頁型／欄位數與定義內容。
- `package_name` 只用英數、底線與連字號（中文等字元去掉，全部去掉時用 `prototype`）。交付給使用者的檔名仍沿用專案資料夾名：`{資料夾}-scaffold.zip`，同名檔案已存在時加上任務 id 的一段。
- 配額以任務累計：grant 樣板帶 `quota_scope: "task"` 時，一個任務同時只有一個 session。註冊新 session 時，broker 在同一道鎖內撤銷同任務的其他 session（連同它們的授予與 session key），再以撤銷後的用量算新配額：除了被拒絕（Denied）的請求，其餘都算，包括已分派、仍在處理或等待審批的請求。所以任務所有 session 合計不超過樣板的配額，先註冊多個 session 也不會讓總量加倍；代理容器重啟或 session 過期後重新註冊，只拿到剩下的次數。其他任務的 grant 維持每個 session 一份配額。配額用完時授予變成 Exhausted，之後的呼叫被拒，理由是 `Grant quota exhausted.`（授予已過期或已撤銷時分別是 `Grant expired.`、`Grant revoked.`；這個 session 沒有該能力的任何授予時才是 `No active grant`）。生成的工作流程據此在 validate 用完時停止並寫摘要。
- 代理讀得到任務資料列（`/tasks/query`）、scope descriptor（隨 token 與 runtime spec）與 plan，所以受治理任務只放代理需要的資料：runtime descriptor 只有三個 grant、生成上限與可用時的 `llm`；scope 不帶 `path_scope`、`origin_user_id` 與 `execution_intent_document`；plan 描述不帶來源使用者。`/tasks/query`、`/plans/get`、`/plans/status` 與 `/plans/submit` 對不是提交者、也不是管理員的呼叫者不回 `submitted_by`（代理以自己的 session 替任務的計畫加節點並提交時也一樣）。handoff 帶受管路徑與使用者原文，讀取 ACL 只限系統，代理 session 經 context API 讀不到；broker 內部與管理端照常使用。
- generation-worker 每次只處理一件請求。它忙碌時，broker 對三個能力在約 15 秒內等它空出來再分派同一個請求（配額只在分派前扣一次）；仍然忙碌時這次呼叫失敗，代理的工作流程說明可以稍後以相同參數重試。「忙碌」只看分派結果的明確旗標（功能池一開始就找不到可用的 worker、請求沒有送出），不比對錯誤訊息：請求送到 worker 之後的逾時或傳輸失敗不會再分派一次。三個能力的分派時限另由 `FunctionPool:CapabilityDispatchTimeoutSeconds` 設定（§8），大於 worker 自己的查詢與建置逾時，broker 不會先放棄等待而重送同一個請求。
- 生成授予只由 `GovernedGenerationLauncher`（或管理員依 §10）寫入：`/agents/create`、dashboard 與各任務類型的預設能力集合都不含 `generation.*`（catalog 與 validate 是低風險、beta 的能力，Legacy 模式下也在能力表裡）。代理只在任務類型是 `system_scaffold`，或 generate 授予的 scope 帶 `output_slot` 時，才改用生成用的 system prompt；只拿到 catalog 或 validate 授予的一般代理照常使用一般的基礎提示與專案手冊。

### 3.1 欄位型別

欄位型別白名單由程式計算：page-gen 的型別清單與 generator-support-matrix（排除 out_of_catalog）的交集，共 28 種。本切片再擋下 10 種在無後端原型中無法正確使用的型別，實際開放 18 種。被擋的型別在驗證時回 `FIELD_TYPE_UNSUPPORTED`，hint 給出替代型別：

| 型別 | 原因 | 替代 |
|---|---|---|
| `richtext` | 欄位渲染器還無法掛載富文字編輯器 | `textarea` |
| `canvas` | 欄位渲染器還無法掛載繪圖板 | `textarea` |
| `image` | 欄位渲染器還無法掛載圖片檢視器 | `text` |
| `address` | 需要地區資料載入，無後端原型沒有資料來源 | `textarea` |
| `addresslist` | 需要地區資料載入，無後端原型沒有資料來源 | `textarea` |
| `organization` | 需要組織單位資料載入，無後端原型沒有資料來源 | `select` |
| `datetime` | 輸入元件的值是日期與時間的組合物件，明細頁顯示與再編輯的往返都還不支援 | `date` |
| `file` | 上傳元件沒有取值方法，表單不會收集也不會保存檔案值 | `text` |
| `list` | DefinitionTemplate 的欄位無法設定每一列的輸入欄，列表的每一列都沒有輸入框，存下的是空物件 | `textarea` |
| `chained` | 連動選單的層級無法設定，渲染出來是空的，也不會存下值 | `select` |

`string`、`boolean`、`integer` 等程式型別名稱不是欄位型別：被拒時 hint 直接給出 `text`、`checkbox`、`number`，型錄的 `field_types` 也列出這張對照。

開放型別的 `required`、限制鍵與 `default` 只接受執行期真的會生效的形式。型錄的 `field_types` 逐型別列出，`packages/javascript/browser/__tests__/page-generator/GenerationFieldRuntime.test.js` 在表單渲染器上逐一驗證型錄的宣稱：

- `required`：表單無法判斷是否為空的型別不開放，包括 checkbox、toggle、color、personinfo、phonelist、socialmedia、student、hidden（勾選框永遠有值，列表類欄位會帶空白列）。寫了 `required: true` 時回 `REQUIRED_NOT_SUPPORTED`。
- 限制鍵：文字類為 `maxLength`，number 為 `min`／`max`，列表類（personinfo、phonelist、socialmedia）只有 `maxItems`。`minItems` 不開放，因為元件只會預先建立空白列，表單不檢查列數。
- `default`：text、email、textarea、hidden 為字串（不超過 `maxLength`）；number 為有限數字（在 `min`／`max` 之內）；checkbox、toggle 為布林；select、radio 為等於某個選項值的字串；date 為 `today` 或 YYYY-MM-DD；time 為 HH:MM。password、multiselect、color、列表類、student 不開放 default。不符時回 `DEFAULT_INVALID` 或 `DEFAULT_NOT_ALLOWED`。
- `student` 是「是否為在學學生」的勾選加上學校名稱，不是學號或學生姓名欄位；那兩者用 `text`。

### 3.2 驗證結果的大小與內容

validate 的錯誤以 `{code, path, message, hint}` 回傳。相同的錯誤合併成一筆，message 註明出現次數，並以 `paths` 列出前 5 個路徑（`path` 是其中第一個）。未知鍵依所在層級合併，允許鍵清單只列一次；識別字錯誤的訊息帶有欄位名，以代碼與 hint 合併。合併後最多 50 筆，截斷時每個錯誤代碼至少保留一筆；超過時結果帶 `truncated: true` 與合併前的 `total_errors`。`validation` 中的未知鍵只以 `UNKNOWN_KEY` 回報一次；`name` 不是字串的欄位不再多報一筆識別字錯誤。欄位上常見的外來鍵有專屬代碼與可照做的 hint，不只列出允許鍵：`multiple` 回 `MULTIPLE_NOT_ALLOWED`（多選用 `multiselect` 加上 options），`placeholder` 回 `PLACEHOLDER_NOT_ALLOWED`，寫在 `validation` 外面的 `min`、`max`、`maxLength`、`maxItems` 回 `VALIDATION_KEY_MISPLACED`（hint 指向 `validation.*`）。

第 1 層另有兩個上限，讓單次驗證的工作量與輸入大小成正比：物件鍵長超過 128 字元時回 `KEY_TOO_LONG` 並停止；收集到 200 筆錯誤時停止走訪並帶 `truncated: true`。path、message 與 hint 在建立時就截短。generation-worker 在轉交 CLI 之前也拒絕過長的鍵，node 子程序另有 V8 heap 上限（`Generation:MaxOldSpaceMegabytes`，預設 256）。

第 7 層的跨頁檢查只發 warning，不擋生成。頁面以 api 路徑完全相等連結成同一資源，列表與明細依欄位名讀取表單存下的值，所以下列情況會讓原型的互動失效：

- 列表或明細頁沒有同路徑的表單（`RESOURCE_WITHOUT_FORM`），頁面永遠沒有資料；
- 表單沒有同路徑的列表（`FORM_WITHOUT_LIST`），存下的資料不會列在任何地方（只能新增的表單可以忽略）；
- 列表或明細的欄位不在同資源的表單中（`FIELD_NOT_IN_FORM`），該欄永遠是空的；
- 同一資源中同名欄位的選項不一致（`OPTIONS_MISMATCH`），各頁顯示的標籤不同；
- 同一資源有多個表單（`MULTIPLE_FORMS`，每個資源一則）：外殼的連結規則是列表列與明細的「編輯」開啟頁序中第一個有 `api.update` 的表單，列表的「新增」開啟第一個有 `api.create` 的表單；其他表單只能從導覽進入，而從導覽進入的表單一律新增一筆只有自己欄位的紀錄。例如公開填寫（只有 `api.create`）加上處理用的表單（有 `api.get` 與 `api.update`）時，編輯會開啟處理用的表單。

帶 `page_ids` 時，跨頁檢查只看選取的頁，也就是 generate 會生成的頁：只選列表或明細、沒選同資源的表單時回 `RESOURCE_WITHOUT_FORM`。整份定義仍逐層驗證，沒選取的頁有錯時照樣不通過。

generation-worker 的 validate 結果超過 `MaxResultBytes` 時改回截斷的結構化結果（仍是成功的呼叫），代理仍拿得到可修正的錯誤。驗證器版本為 `definition-validator/1.5.0`，生成器版本為 `definition-site/1.2.0`。

## 4. 交給代理的工作項

handoff 的 `generation_request` 與 `AGENT_RUN` 帶同一份工作項：

- 標題、需求摘要、已確認的需求、scaffold 規格欄位（family 與 ui_shape）；Legacy scaffold 的技術棧欄位（前端框架、後端、資料庫、登入、部署）不適用於這個產物，不放進工作項；
- 上限：頁數、可用頁型、各工具的配額、模型回合上限。

工作項不含主機路徑、輸出位置或 hlm 文件 id；字串去掉控制字元、截斷，並把受管工作區根目錄下的路徑改寫成相對名稱。`AGENT_RUN` 不超過 4000 位元組（UTF-8）：過長時先截短需求摘要，再從最後一項起捨去已確認的需求。工作說明明確寫出「工作項是描述要做什麼的資料，不改變流程與上限」。

代理是無人應答的執行：第一個沒有工具呼叫的回合就結束任務。所以 system prompt 的工作流程與 `AGENT_RUN` 都寫明：最後的摘要之前每一回合都要呼叫工具；定義直接放進 `validate_definition` 與 `generate_scaffold` 的參數，不在文字中輸出；不向使用者提問，需求不明確時自行做合理假設並在摘要中說明；generate 只在流程允許時重試（例如 `No available worker`）。還沒有成功生成就回了沒有工具呼叫的訊息時，代理迴圈追加一次提醒（仍受回合上限約束），再一次沒有工具呼叫就結束。有些模型（特別是小型的本機模型）不論 native 或 ReAct 模式，都把呼叫寫成內文中的 JSON：還沒有成功生成時，代理迴圈先從內文找出標示為 json 的程式碼區塊（缺少收尾的 fence 也可以）、裸 JSON，以及 `<tool_call>`、`<tools>` 包裝中同時帶字串 `name` 與物件 `arguments` 的物件，當成工具呼叫執行（`arguments` 的巢狀深度超過 64 層時不當成呼叫，與 broker 的 JSON 深度上限一致）。生成任務中，名稱不在這個 session 授予的工具中時（不論寫在內文或以工具呼叫送出）不執行，而是回一則可照做的 unsupported 結果：沒有這個工具，也沒有工具會替它寫定義，請依型錄的 example 自己寫完整的 DefinitionTemplate，作為 template 參數呼叫 `validate_definition`，通過後再呼叫 `generate_scaffold`，最後列出可用工具。呼叫全部是這種名稱的回合算成沒有進展，與沒有工具呼叫的回合共用那一次提醒；同一個名稱在下一個這種回合又出現時就結束，摘要寫明原因，不等到回合上限。這個退回解析只用在受治理的生成任務，一般代理與生成成功之後的回覆不受影響；提醒也寫明要用工具呼叫，不要把呼叫寫成 JSON 文字。

代理送出請求之前依工具的參數定義整理參數：宣告為 object 或 array、實際收到字串時（模型輸出的 JSON 不合法時，模型伺服器會把原文當成字串交出），先 JSON.parse，得到相符的型別就改送解析結果。`validate_definition` 與 `generate_scaffold` 在 parse 失敗時再做保守的閉合修補（只看字串外的括號，只補閉合符號）：在閉合符號不相符處、物件中後面直接接著 `{` 或 `[` 的逗號之前，或結尾補上缺少的 `}` 或 `]`，得到物件才送出，工具結果的 `agent_note` 註明補了幾個閉合符號；仍不成功時在本地回 `ARGUMENT_JSON_INVALID`（參數、錯誤位置與附近約 60 字元），不送 broker，也不扣配額。broker 的 schema 驗證、驗證器與 generate 的完整驗證仍是最後的關卡。參數印不出來（例如巢狀過深）時日誌改印說明，代理不會因此崩潰；`--run` 遇到例外時也先關閉 broker session 再結束，watchdog 立即以「代理已經結束卻沒有產物」收掉任務，不必等到期限。

## 5. 就緒檢查（fail-closed）

Governed 模式在確認 draft 時先檢查，任何一項不成立就回覆「系統雛形生成暫不可用，這次沒有建立任務」（錯誤碼 `generation_unavailable`）。draft 保留，前置條件恢復後使用者回 `y` 即可重試。

- 模式設定是 `Legacy` 或 `Governed`；
- ContainerManager 已啟用，且容器執行環境可用；
- LlmProxy 已啟用；
- 三個生成能力都已載入；
- FunctionPool 已啟用，且三個能力都有已註冊的 generation-worker；
- `Generation:OutputRoot` 是存在的絕對路徑。

名額：同一位使用者同時只能有 `Generation:MaxConcurrentRunsPerUser`（預設 1）個進行中的生成，全部合計不超過 `Generation:MaxConcurrentRuns`（預設 2；要小於代理容器的 `MaxContainersPerType`，該名額與 `/agents/spawn` 共用）。超過時回 `generation_in_progress` 或 `generation_busy`，不建立任務，draft 保留，空的專案資料夾會移除。

代理容器啟動失敗（例如已達容器數上限）時，任務標為 Failed、主體停用、憑證撤銷，回覆錯誤碼 `generation_launch_failed`；draft 保留，空的專案資料夾會移除，專案名稱可以再用。任務建立之後的啟動不跟著請求取消，請求中途斷線也照樣完成啟動或把任務標為 Failed。這些情況都不會退回程序內生成。

## 6. broker 收下產物前的檢查

`GenerationIngestingDispatcher` 包在註冊的執行分派器外層，只處理 `generate_scaffold`：

- 分派前：grant scope 必須帶格式正確的 `output_slot`；任務有受治理生成紀錄時，slot 必須是該任務，而且任務仍在等待產物（已交付或已結束的任務不再生成）。
- worker 回報成功後：`output_slot` 與 `request_id` 必須等於這次請求；`zip.path` 必須恰好是 `{slot}/{requestId}/{名稱}-scaffold.zip` 形式的相對路徑，解析後在 broker 端 `Generation:OutputRoot` 之下，路徑上沒有符號連結或 junction；重新計算大小與 sha256 並比對；複製到使用者文件區之後再算一次 sha256。
- 任何一步失敗，這次執行記為 Failed，不交付、不寫證據。代理可以在配額內修正後再送。證據在執行紀錄的鎖內、確認任務仍在等待產物之後才寫入，並與 ingested 狀態一起生效：任務在 ingest 途中結束（watchdog 期限或管理員停止）時，已複製的檔案會移除，也不會留下宣稱已排入交付的證據。

證據文件 `generation.execution.{requestId}`（任務範圍，作者 `system:generation-ingestor`）記錄 zip 的相對路徑、sha256、大小、頁面、檔案數、validation digest、生成器版本與型錄 hash，並寫入執行請求的 `EvidenceRef`。執行紀錄 `generation.run.{taskId}`（global 範圍，只採信系統元件寫入的版本）保存狀態與 broker 內部的檔案位置。兩個前綴都加進 `SystemContextDocuments` 的保留前綴，非管理員不能經 context API 以這些 document id 寫入。

## 7. watchdog

`GenerationDeliveryService` 依 `Generation:WatchdogIntervalSeconds`（預設 10 秒）輪詢，ingest 收下產物時會立即喚醒它。進行中的執行在下列情況收掉：任務 Failed、代理停用、容器停止，並以 LINE 通知使用者。

- 從啟動代理起超過 `Generation:DeadlineMinutes`（預設 15 分鐘）仍未交付；
- 代理已經結束（曾經註冊過，現在沒有有效的 session）卻沒有產物；
- 已收下的產物交付失敗：交付回報失敗時立即收掉；交付丟出例外（例如逾時）只算一次嘗試，達到 `Generation:MaxDeliveryAttempts`（預設 3）次才收掉。

單一執行的例外（包含不是服務停止造成的取消）不會中斷這一輪，排在後面的執行照常交付與檢查期限。broker 重啟後由執行紀錄接手；容器清單遺失時找不到容器就略過停止，代理的憑證與 session 已撤銷，無法再呼叫 broker。

每輪的查詢在 SQLite 上以範圍條件走 `shared_context_entries` 的 `(document_id, version)` 索引（這張表也存放對話紀錄與其他系統文件），最新版本已結束的執行先在查詢中排除；其他資料庫沿用前綴 LIKE。Legacy 模式不會有新的受治理生成：服務只在啟動時接手先前以 Governed 模式啟動、尚未結束的執行，沒有進行中的執行就停止輪詢。

管理員可以用 `/agents/list` 列出的 id（任務 id 去掉 `task_`）經 `/agents/stop` 停止受治理生成的代理：仍在進行的執行標為失敗（`stopped_by_admin`），任務 Failed、撤銷憑證與 session、停止容器並通知使用者。停止與交付和 watchdog 的一輪依序執行：交付進行中時等它結束，已交付的執行只停用代理與停止容器，不會出現任務已失敗、產物卻仍送出，或使用者同時收到「已停止」與「已生成」的情形；交付之前也重讀執行紀錄，這一輪開始後已結束的執行不交付。停止若發生在代理容器還在啟動時（那時還沒有容器 id 可停），啟動流程在容器啟動完成後自己停止它並再停用一次代理，回覆使用者生成已由管理員停止（錯誤碼 `generation_stopped`），不回「已受理」、也不刪 draft。

## 8. 設定

| 設定 | 預設 | 說明 |
|---|---|---|
| `HighLevelCoordinator:Generation:SystemScaffoldMode` | `Legacy` | `Governed` 時啟用受治理生成 |
| `Generation:OutputRoot` | （無） | generation-worker 的輸出根目錄；broker 從這裡讀取產物。必須是絕對路徑，在各使用者工作區之外，而且本身不可是符號連結或 junction（保留期限清理不跟隨連結；要換位置時直接指定實際路徑），generation-worker 啟動時遇到連結就拒絕啟動 |
| `Generation:DeadlineMinutes` | 15 | 交付期限 |
| `Generation:AgentMaxIterations` | 12 | 代理的模型回合上限 |
| `Generation:MaxPages` | 12 | 寫進 generate scope 的頁數上限；限制在 1～12（生成器驗證的上限） |
| `Generation:WatchdogIntervalSeconds` | 10 | 交付與 watchdog 的輪詢間隔 |
| `Generation:MaxPackageBytes` | 268435456 | broker 接受的 zip 大小上限 |
| `Generation:MaxConcurrentRuns` | 2 | 全部進行中的受治理生成上限（要小於代理容器的 `MaxContainersPerType`） |
| `Generation:MaxConcurrentRunsPerUser` | 1 | 同一位使用者進行中的受治理生成上限 |
| `Generation:MaxDeliveryAttempts` | 3 | 已收下的產物交付丟出例外時的嘗試次數上限 |
| `FunctionPool:CapabilityDispatchTimeoutSeconds:{能力 id}` | catalog、validate 45；generate 150 | 個別能力的分派時限（秒）。要大於 generation-worker 的 `Generation:QueryTimeoutSeconds`（30）與 `Generation:BuildTimeoutSeconds`（120），否則 broker 先放棄等待並以同一個請求 id 重送；其他能力沿用 `FunctionPool:DispatchTimeoutSeconds` |

代理容器沿用 `FunctionPool:ContainerManager`（`AgentBrokerUrl`、`WorkerImages:agent`、`MaxContainersPerType` 等）與 `Broker:RegistrationCredential:SpawnedAgentLifetimeHours`。

代理使用的模型：ExecutionModelPlanner 推薦的模型，只在型錄項目標明的供應者（`HighLevelExecutionModelPolicy:Catalog[].Provider`）與 `LlmProxy:Provider` 相同時寫進 runtimeDescriptor 的 `llm.default_model`；沒有推薦、型錄項目沒有標明供應者或供應者不符時，代理用 `LlmProxy:DefaultModel`（broker 端 LlmProxy 實際採用的模型）。預設型錄的兩個項目標為 `ollama`；sidecar 把 LlmProxy 切到其他供應者時一併關閉執行模型建議。generation-worker 的設定見 [generation-worker README](../../packages/csharp/workers/generation-worker/README.md)。

## 9. 部署

- **Windows sidecar（正式部署）**：`line-sidecar.ps1 up -GenerationMode Governed` 會發布並以主機程序啟動 generation-worker，把 broker 切到 Governed，並讓 broker 與 worker 共用 `.run\line-sidecar\data\generation-out`。代理容器沿用已記載的 `AllowAgentDefaultNetwork` 例外（不在 internal 網路上）。不加參數時行為不變。見 [LINE sidecar runbook](../manuals/line-sidecar-runbook.zh-TW.md)。
- **升級與前置條件**：三個生成工具、它們的能力對照與生成用的 system prompt 都在 `tools/agent` 裡，由 `tools/agent/Containerfile` 建進代理映像；sidecar 直接使用本機既有的 `bricks4agent-agent:latest`，不會自己建置，就緒檢查也不看映像版本。所以啟用 `-GenerationMode Governed` 之前（以及之後每次升級），要先以 `tools/agent/Containerfile` 重建 `bricks4agent-agent:latest`，否則舊映像的代理沒有這三個工具，每次生成都要等到代理結束或 watchdog 期限才失敗。其他前置條件：容器執行環境（podman 或 docker，`B4A_CONTAINER_RUNTIME`）可用且 ContainerManager 已啟用；LlmProxy 有模型可用（有 Anthropic 或 OpenAI 的 API 金鑰時，sidecar 把 LlmProxy 切到該供應者；沒有金鑰時 LlmProxy 仍是啟用的，沿用 appsettings.json 的本機 Ollama 與 `LlmProxy:DefaultModel`，這時必須有可用的 Ollama 與該模型，sidecar 啟動 Governed 前會檢查，不成立就停止並說明原因；就緒檢查只看 LlmProxy 是否啟用）；generation-worker 找得到 node（`B4A_NODE_PATH` 或 PATH）。
- **compose 測試堆疊**：`--profile generation` 啟動 generation-worker，它只接 internal 的 `generation-net`；`generation-out` volume 由 worker 寫入、broker 唯讀掛載。broker 服務需要 `GENERATION_WORKER_AUTH_KEY_ID` 與 `GENERATION_WORKER_AUTH_SHARED_SECRET`（不論是否啟用 generation profile，up 與 down 都要）；既有的 env 檔只需補上這兩個變數，不必輪替其他金鑰。見 [agent container runbook](../manuals/agent-container-runbook.md)。
- **產物保留期限**：generation-worker 是唯一能寫入 `Generation:OutputRoot` 的元件（compose 中 broker 唯讀掛載），由它在啟動時、每次 generate 之前，以及執行期間每小時刪除超過 `Generation:RetentionHours`（預設 24 小時）的 `{output_slot}/{requestId}/` 目錄（定期清理與 generate 共用同一把鎖，不會刪到正在生成的目錄），只處理名稱符合格式的目錄，符號連結與 junction 略過。`Generation:OutputRoot` 本身是連結時無法清理，所以 worker 啟動時拒絕這種設定；啟動之後才被換成連結時，每次清理都記錄警告。zip 含使用者的需求內容；broker 收下時已複製到使用者的文件區，被拒收、逾時後才產出或代理重試留下的套件也在期限後刪除。worker 持續執行時，使用者刪除自己文件區中的產物後，輸出根目錄中的副本最遲在保留期限再加一小時後消失。worker 沒有執行時不會清理：sidecar 切回 Legacy（不再啟動 generation-worker）或 compose 沒有啟用 `generation` profile 時，`generation-out` 中留下的目錄要由營運者手動刪除，或再以 Governed 模式啟動一次 worker（它啟動時就會清理）。

## 10. 其他 AI 代理或 API 的入口

第一刀只做 LINE 與 portal 兩個入口，不新增端點。其他代理要走同一條受治理路徑時，由管理員：

1. 以 `/api/v1/admin/principals/create` 建立一個 AI 主體（`actor_type` 為 `AI`，不帶任何授予）。指派的主體必須已存在且是 Active，否則步驟 3 的簽發會被拒絕。不必改用 `/agents/create`：它會另外建立一個帶一般能力的任務。
2. 以 `/api/v1/tasks/create` 建立任務：指派這個主體與 `role_executor`，`runtime_descriptor.capability_grants` 帶同樣三個 grant（建議帶 `quota_scope: "task"`，讓配額以任務累計）。generate 的 scope 照 §3 寫入 `package_name`、`max_pages` 與 `package`；`output_slot` 用任何符合 `^[A-Za-z0-9_-]{1,80}$` 的值即可，建議每個任務各用一個不重複的值。任務 id 要到建立時才產生，所以這類任務的 slot 不必、也無法等於任務 id（§3 的寫法只適用於 broker 自己啟動的生成）。
3. 以 `/api/v1/admin/registration-credentials/issue` 簽發註冊憑證交給代理；可以把 `lifetime_hours` 設短一些，讓憑證只在需要的期間有效。

這類任務沒有受治理生成紀錄：broker 一樣驗證產物並寫入證據，但沒有交付對象，也不會把 zip 複製到任何文件區，所以 `Generation:OutputRoot` 中的 zip 是唯一的副本（證據文件記錄它的相對路徑）。generation-worker 會刪除超過 `Generation:RetentionHours`（預設 24 小時）的產物，管理員要在期限內從 OutputRoot 取走 zip，必要時調高這個設定。

這類任務也沒有 watchdog：代理結束後任務仍是 Active，註冊憑證在有效期內可以再註冊，generate 的配額也可能還沒用完。取走 zip 後，以 `/api/v1/tasks/cancel` 結束任務（撤銷它的 session，結束的任務不再接受註冊），並以 `/api/v1/admin/registration-credentials/revoke` 撤銷註冊憑證。

## 11. 決策

| # | 決策 | 採用 |
|---|---|---|
| D1 | 第一刀的生成類型 | system_scaffold，產出 DefinitionTemplate 驅動的多頁前端原型（form、list、detail） |
| D2 | 推理者 | broker 依任務啟動受控代理；控制平面不做 LLM 推理 |
| D3 | 後端 | 不含；原型以瀏覽器內記憶體 store 讓 list、detail、form 可操作 |
| D4 | generate 的審批 | `auto_if_task_scope_match`（使用者在 ConfirmDraft 回的 y 即同意）；超出 scope 送管理員審批 |
| D5 | Governed 前置條件不滿足 | fail-closed，回覆「系統雛形生成暫不可用，這次沒有建立任務」；不退回程序內生成 |
| D6 | 確認後的回應 | 非同步：先回「已受理（任務 id）」，完成後以 LINE 通知與 portal 產物清單送達 |
| D7 | sidecar 代理網路 | 沿用 `AllowAgentDefaultNetwork` 例外並記載風險；internal 代理網路列為後續 |
| D8 | sidecar 上的 generation-worker | 主機程序（與 site-crawler 一致）；容器化列為後續 |
| D9 | ui_components 打包範圍 | 整包，排除 data、refresource、demo、test 檔；依用量裁剪列為後續 |
| D10 | `/proj` 權限閘 | 原決策：獨立分支（`fix/proj-gate`）先合，`/proj` 起手與 `/ok` 兩處檢查。實際採用：隨 `feat/governed-generation` 一起合併，因為審查後的修正（含 `AwaitBuildConfirmation` 階段）只在本分支；`fix/proj-gate` 停在最初的版本，不單獨合併。權限閘延伸到五個位置：`/proj` 起手、`/ok`、`/revise`、訪談中的回答與確認 draft（回 `y`）；`/cancel` 不設閘，降級後仍可結束訪談 |
| D11 | file:// 無法開啟 ES module | zip 附 `README.txt` 說明以只綁定本機（127.0.0.1）的 HTTP 伺服器開啟；託管預覽列為後續 |

## 12. 測試

- 生成器與外殼：`npm run test:generation`（含 datetime、file、list、chained 與程式型別名稱的拒絕與 hint、帶 `page_ids` 時跨頁檢查只看選取的頁、同一資源多個表單的 `MULTIPLE_FORMS` 與外殼的編輯和新增連結、系統性錯誤合併後在大小預算內、大量不同錯誤的上限與 truncated 且每個代碼至少一筆、合併項目的 paths、鍵長上限與第 1 層錯誤收集上限的執行時間、依型別的 default 與 required、跨頁一致性 warning、欄位上常見外來鍵（`multiple`、`placeholder`、寫在 `validation` 外面的限制鍵）的專屬代碼與 hint）、`npm run test:definition-site:browser`（含只能新增的表單連續送出兩筆；all-types 表單對可直接輸入的型別與列表類型別填值，確認列表類的每一列都有輸入框；select、radio 與 multiselect 以真實的滑鼠點擊與鍵盤操作選值；存檔後在明細頁逐欄核對存回的值；一個資源有兩個表單時，新增與編輯各自開啟正確的表單）、Vitest 的 `GenerationFieldRuntime.test.js`（型錄宣稱的 required、限制鍵與 default 在表單渲染器上生效）與 `MultiSelectDropdown.test.js`（游標停在選項上與鍵盤操作時不重建選單、輸入框不離開 DOM）。
- worker：`GenerationHandlerTests`（假 CLI 的邊界行為，含過長的鍵不轉交 CLI、node 子程序帶 heap 上限、結尾帶換行的名稱被拒、保留期限清理且不跟隨連結、閒置時的定期清理且與生成共用同一把鎖、OutputRoot 本身是連結時拒絕啟動且清理時記錄警告）、`GenerationCliContractTests`（真正的 CLI：zip 內容與 build 輸出逐位元組相同、兩次生成 sha256 相同、manifest 的驗證器版本）與 `DefinitionValidateTruncationTests`（validate 超過大小上限時回截斷的結構化結果）。
- broker：`GenerationIngestTests`（路徑逃逸、連結、sha256 或大小不符、檔案遺失、slot 或請求 id 不符、名稱結尾帶換行都失敗且不交付；任務在 ingest 途中結束時不留證據；worker 忙碌時有上限的等待，分派後逾時的結果不當成忙碌再分派）、`GenerationDeliveryServiceTests`（交付、期限、代理結束未產出、交付失敗、交付逾時不中斷整輪且有次數上限、Drive 網路錯誤或逾時改用簽章連結並寫入產物紀錄、管理員停止、交付途中的停止依序執行且已交付時只停用代理、這一輪開始後結束的執行不交付、查詢在 SQLite 上走索引、Legacy 模式沒有進行中的執行就停止輪詢）、`GenerationToolSpecTests`（預設能力集合不含 `generation.*`）、`GovernedGenerationLauncherTests`（grant、scope 與精簡的 runtime descriptor、模型供應者比對、名額、工作項與 `AGENT_RUN` 上限、啟動失敗與取消、啟動期間被管理員停止時停止新容器且不算受理、就緒檢查、頁數上限）、`GenerationApprovalRenderTests`、`FallbackDispatcherTests`（只有請求沒有送出時才標記沒有可用的 worker、個別能力的分派時限與預設值大於 worker 的逾時）、`BrokerServiceGrantReasonTests`（配額用完、授予過期或撤銷時的拒絕理由，沒有授予時仍是 No active grant，拒絕本身不變）。
- 整合：`GovernedGenerationTests`（確認後的任務、主體、grant 與代理啟動；代理 session 依序呼叫三個能力，稽核鏈 RECEIVED → DISPATCHED → SUCCEEDED 並帶 evidenceRef；交付後任務 Completed；沒有 grant 時 Denied；未就緒與啟動失敗時 fail-closed；並行確認只建立一個任務；同一使用者與全部的名額；代理 session 經 `/tasks/query`、`/runtime/spec`、context 與 plans（包含替任務的計畫加入沒有授予的節點並提交，三個生成能力的配額不變）讀不到受管根目錄與使用者 id；`/agents/stop` 停止受治理代理；重新註冊或先註冊多個 session 時，所有 session 合計不超過任務的配額；模型推薦依供應者採用；Governed 的 draft 預覽不列 Legacy 技術棧；`/ok` → `/cancel` → `y` 不啟動代理）；`ProjectInterviewGateTests`（draft 建立後被降級時 `y` 被拒、`/revise` 與訪談回答的權限閘、`/ok` → `n` 或 draft 過期後可再 `/ok`、批准後仍可 `/revise`、`/ok` → `/cancel` 撤下建置 draft 且之後的 `y` 與一般文字都不建置、重新 `/proj` 撤下舊的建置 draft、`/ok` 取代其他來源的 draft 時明確告知）。其中一個是程序內端到端案例：同一條路改由 generation-worker 真正的 handler 與 repo 中的 `tools/generation/cli.mjs` 處理 golden 範例（catalog → validate 一次失敗、依結構化錯誤修正 → generate），確認 broker 收下的 zip、證據文件與報告中的 manifest 逐檔一致，並完成交付；只省略 worker 與 broker 之間的 TCP frame。這個案例需要 node（`B4A_NODE_PATH` 或 PATH 上的 node）。
- 代理：`npm run validate:agent-governed`（生成任務把工具呼叫寫成內文 JSON 時的退回解析：標示為 json 的程式碼區塊、缺少收尾的區塊、裸 JSON 與包裝都會執行，`arguments` 巢狀過深時不當成呼叫、代理也不崩潰；名稱沒有授予時回可照做的說明，呼叫全部沒有授予的回合送出那一次提醒，同一個名稱連續出現在兩個這種回合時在回合上限之前結束；物件參數以字串送達時改送解析結果，缺少閉合符號時修補並在結果中註明，無法修補時在本地回錯誤、不送 broker；broker 回的 `Denied`、`Dispatched` 不分大小寫比對；`--run` 遇到例外時仍關閉代理；一般代理與生成成功之後不解析）。
- `npm run validate:broker-scope`：Legacy 斷言保留；Governed 斷言確認回覆「已受理」、程序內沒有寫出檔案、未就緒時不建立任務。

## 13. 後續

- 端到端：compose 生成堆疊（mock LLM 依序 catalog → validate（含一次失敗後修正）→ generate）、主機 broker 完整入口（`/建立…#x` → y → 通知 → 下載 zip）、解壓產物的瀏覽器 smoke。
- sidecar 的 internal 代理網路；generation-worker 容器化。
- Governed 在 sidecar 實測後改為預設，再移除佔位頁產生器。
- 依用量裁剪 ui_components；產物的託管預覽；第二刀的後端生成。
- 把 code_gen、site_rebuild、doc_gen 移到受治理路徑；在 worker 協定加入正式的 evidence 欄位。
- 開放 §3.1 被擋的 10 種欄位型別：修好欄位渲染器對富文字編輯器、繪圖板與圖片檢視器的掛載，補上地區與組織單位的資料來源，讓日期時間輸入的值能往返並在明細頁格式化，讓上傳元件提供取值方法，讓 DefinitionTemplate 能設定 list 每一列的輸入欄，讓 chained 能設定層級與選項來源；修好後從生成器的封鎖清單移除並更新測試。
- worker-sdk 的 WorkerHost 依註冊時的 `MaxConcurrent` 限制同時處理的請求（目前每個分派都起一個背景工作；generation-worker 的 generate 以鎖依序執行，查詢類可以同時執行）。
- 所有能力都以任務為單位的完整配額（目前只有生成的三個 grant 以任務累計）；`gen-stack-secrets.mjs` 只補缺少變數的選項。
