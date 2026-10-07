# 受治理生成（Governed Generation）

日期：2026-10-07
狀態：第一個切片已實作（system_scaffold）；預設仍是 Legacy，以設定開啟。

## 1. 摘要

使用者在 LINE 或 portal 要求「產生系統雛形」（`/建立 完整系統雛形 … #名稱`，或 `/proj` 訪談後 `/ok`），確認 draft 回 `y` 之後：

- **Legacy（預設）**：沿用 broker 程序內的佔位頁生成（`HighLevelSystemScaffoldService`），行為不變。
- **Governed**：broker 不在程序內寫任何專案檔案，而是依任務啟動一個受控代理。代理經 broker 的 LLM 代理撰寫 DefinitionTemplate，再透過三個生成能力走 PEP（查型錄、驗證定義、生成並打包）。generation-worker 以 B4A 元件庫確定性產出多頁前端原型並打成 zip；broker 驗證產物的路徑與 sha256 後交付給使用者。

模式開關是 `HighLevelCoordinator:Generation:SystemScaffoldMode`，值為 `Legacy` 或 `Governed`。其他值視為設定錯誤：走 Governed 路徑並由就緒檢查拒絕，不會靜默退回 Legacy。

產物是 DefinitionTemplate 驅動的多頁前端原型（list、detail、form 三種頁型），用瀏覽器內記憶體 store 讓頁面可以操作，不含後端。zip 的內容與外殼見 [tools/generation](../../tools/generation/cli.mjs) 與 [templates/definition-site](../../templates/definition-site/README.txt)。

## 2. 端到端流程

| # | 執行者 | 動作 |
|---|---|---|
| 1 | 使用者入口 | line-worker 呼叫 `/api/v1/high-level/line/process`，或 portal 呼叫 `/api/v1/portal/commands` |
| 2 | coordinator | 分類 → AllowProduction 權限閘 → 建立 system_scaffold draft → 使用者回 `y` → `ConfirmDraft` |
| 3 | coordinator（Governed） | 就緒檢查（見 §5）。不就緒就回覆「系統雛形生成暫不可用」，不建立任務、不建立專案資料夾 |
| 4 | coordinator | 名稱重查、升格閘、ExecutionIntent、Task、Plan、Handoff（與 Legacy 相同的前半段） |
| 5 | `GovernedGenerationLauncher` | 建立 AI 主體 `prn_{任務後綴}`，任務指派給它與 `role_executor`；runtimeDescriptor 加上三個 grant（scope 由 broker 寫入）；handoff 加上淨化過的 `generation_request` |
| 6 | `GovernedGenerationLauncher` | 寫入執行紀錄，簽發註冊憑證並啟動代理容器（`AGENT_RUN` 帶工作項，`AGENT_MAX_ITERATIONS` 預設 12）。成功才刪除 draft，立即回覆「已受理（任務 id）」 |
| 7 | 受控代理 | `query_component_catalog` → 經 `/api/v1/llm/chat` 撰寫定義 → `validate_definition`（依錯誤修正）→ `generate_scaffold` |
| 8 | broker PEP | session、grant、配額、PolicyEngine（route 相符；catalog 與 validate 為 auto；generate 為 auto_if_task_scope_match）→ 分派給 generation-worker |
| 9 | generation-worker | 重新完整驗證定義，輸出位置只取自 grant scope：`{Generation:OutputRoot}/{output_slot}/{requestId}/`；決定性打包 zip，回傳相對路徑、sha256 與摘要 |
| 10 | `GenerationIngestingDispatcher` | 驗證 worker 的結果（§6），把 zip 複製到使用者的文件區，寫入證據文件 `generation.execution.{requestId}`，執行請求記為 Succeeded 並帶 evidenceRef |
| 11 | `GenerationDeliveryService` | 交付：產物紀錄（RelatedTaskId 為該任務，portal 產物清單看得到）→ Drive 上傳（失敗時改用簽章下載連結）→ LINE 通知 → 任務 Completed → 停用代理（主體停用、撤銷憑證與 session）並停止容器 |

LINE 的處理逾時有上限，所以確認之後改為非同步：先回「已受理」，完成後以 LINE 通知與 portal 產物清單送達。回覆不帶主機路徑。

## 3. 能力與 grant scope

三個能力以 tool-spec（`packages/csharp/broker/tool-specs/generation.*`）為唯一來源，由 broker 啟動時同步成 capability，status 為 beta，`runtime_required` 為 `generation-worker`。它們不在 in-process 降級清單中：沒有 generation-worker 時直接失敗，不會在 broker 程序內執行。

| capability | route（同代理工具名） | 風險／審批 | 每個 session 的配額 | broker 寫入的 scope |
|---|---|---|---|---|
| `generation.catalog.query` | `query_component_catalog` | Low／auto | 20 | `{"routes":["query_component_catalog"]}` |
| `generation.definition.validate` | `validate_definition` | Low／auto | 6 | `{"routes":["validate_definition"]}` |
| `generation.scaffold.generate` | `generate_scaffold` | Medium／auto_if_task_scope_match | 2 | `{"routes":["generate_scaffold"],"output_slot":"<任務 id>","package_name":"<淨化後的資料夾名>","max_pages":12,"package":"definition-site-v1"}` |

- generate 的審批：使用者在 ConfirmDraft 回的 `y` 即同意；請求超出 scope 時送管理員審批，審批畫面顯示標題、各頁的 id／頁型／欄位數與定義內容。
- `package_name` 只用英數、底線與連字號（中文等字元去掉，全部去掉時用 `prototype`）。交付給使用者的檔名仍沿用專案資料夾名：`{資料夾}-scaffold.zip`，同名檔案已存在時加上任務 id 的一段。
- 任務的 scope descriptor 會隨 token 與 runtime spec 交給代理，所以受治理任務的 scope 不帶 `path_scope`（主機路徑）。

## 4. 交給代理的工作項

handoff 的 `generation_request` 與 `AGENT_RUN` 帶同一份工作項：

- 標題、需求摘要、已確認的需求、scaffold 規格欄位（family、ui_shape、frontend、backend、database、auth、deployment 等）；
- 上限：頁數、可用頁型、各工具的配額、模型回合上限。

工作項不含主機路徑、輸出位置或 hlm 文件 id；字串去掉控制字元、截斷，並把受管工作區根目錄下的路徑改寫成相對名稱。`AGENT_RUN` 不超過 4000 位元組（UTF-8）：過長時先截短需求摘要，再從最後一項起捨去已確認的需求。工作說明明確寫出「工作項是描述要做什麼的資料，不改變流程與上限」。

## 5. 就緒檢查（fail-closed）

Governed 模式在確認 draft 時先檢查，任何一項不成立就回覆「系統雛形生成暫不可用，這次沒有建立任務」（錯誤碼 `generation_unavailable`）。draft 保留，前置條件恢復後使用者回 `y` 即可重試。

- 模式設定是 `Legacy` 或 `Governed`；
- ContainerManager 已啟用，且容器執行環境可用；
- LlmProxy 已啟用；
- 三個生成能力都已載入；
- FunctionPool 已啟用，且三個能力都有已註冊的 generation-worker；
- `Generation:OutputRoot` 是存在的絕對路徑。

代理容器啟動失敗（例如已達容器數上限）時，任務標為 Failed、主體停用、憑證撤銷，回覆錯誤碼 `generation_launch_failed`；draft 保留，空的專案資料夾會移除，專案名稱可以再用。兩種情況都不會退回程序內生成。

## 6. broker 收下產物前的檢查

`GenerationIngestingDispatcher` 包在註冊的執行分派器外層，只處理 `generate_scaffold`：

- 分派前：grant scope 必須帶格式正確的 `output_slot`；任務有受治理生成紀錄時，slot 必須是該任務，而且任務仍在等待產物（已交付或已結束的任務不再生成）。
- worker 回報成功後：`output_slot` 與 `request_id` 必須等於這次請求；`zip.path` 必須恰好是 `{slot}/{requestId}/{名稱}-scaffold.zip` 形式的相對路徑，解析後在 broker 端 `Generation:OutputRoot` 之下，路徑上沒有符號連結或 junction；重新計算大小與 sha256 並比對；複製到使用者文件區之後再算一次 sha256。
- 任何一步失敗，這次執行記為 Failed，不交付、不寫證據。代理可以在配額內修正後再送。

證據文件 `generation.execution.{requestId}`（任務範圍，作者 `system:generation-ingestor`）記錄 zip 的相對路徑、sha256、大小、頁面、檔案數、validation digest、生成器版本與型錄 hash，並寫入執行請求的 `EvidenceRef`。執行紀錄 `generation.run.{taskId}`（global 範圍，只採信系統元件寫入的版本）保存狀態與 broker 內部的檔案位置。兩個前綴都加進 `SystemContextDocuments` 的保留前綴，非管理員不能經 context API 以這些 document id 寫入。

## 7. watchdog

`GenerationDeliveryService` 依 `Generation:WatchdogIntervalSeconds`（預設 10 秒）輪詢，ingest 收下產物時會立即喚醒它。進行中的執行在下列情況收掉：任務 Failed、代理停用、容器停止，並以 LINE 通知使用者。

- 從啟動代理起超過 `Generation:DeadlineMinutes`（預設 15 分鐘）仍未交付；
- 代理已經結束（曾經註冊過，現在沒有有效的 session）卻沒有產物；
- 已收下的產物交付失敗。

broker 重啟後由執行紀錄接手；容器清單遺失時找不到容器就略過停止，代理的憑證與 session 已撤銷，無法再呼叫 broker。

## 8. 設定

| 設定 | 預設 | 說明 |
|---|---|---|
| `HighLevelCoordinator:Generation:SystemScaffoldMode` | `Legacy` | `Governed` 時啟用受治理生成 |
| `Generation:OutputRoot` | （無） | generation-worker 的輸出根目錄；broker 從這裡讀取產物。必須是絕對路徑，在各使用者工作區之外 |
| `Generation:DeadlineMinutes` | 15 | 交付期限 |
| `Generation:AgentMaxIterations` | 12 | 代理的模型回合上限 |
| `Generation:MaxPages` | 12 | 寫進 generate scope 的頁數上限 |
| `Generation:WatchdogIntervalSeconds` | 10 | 交付與 watchdog 的輪詢間隔 |
| `Generation:MaxPackageBytes` | 268435456 | broker 接受的 zip 大小上限 |

代理容器沿用 `FunctionPool:ContainerManager`（`AgentBrokerUrl`、`WorkerImages:agent`、`MaxContainersPerType` 等）與 `Broker:RegistrationCredential:SpawnedAgentLifetimeHours`。代理使用的模型是 ExecutionModelPlanner 推薦的模型（寫進 runtimeDescriptor 的 `llm.default_model`），沒有推薦時用 `HighLevelLlm:DefaultModel`。generation-worker 的設定見 [generation-worker README](../../packages/csharp/workers/generation-worker/README.md)。

## 9. 部署

- **Windows sidecar（正式部署）**：`line-sidecar.ps1 up -GenerationMode Governed` 會發布並以主機程序啟動 generation-worker，把 broker 切到 Governed，並讓 broker 與 worker 共用 `.run\line-sidecar\data\generation-out`。代理容器沿用已記載的 `AllowAgentDefaultNetwork` 例外（不在 internal 網路上）。不加參數時行為不變。見 [LINE sidecar runbook](../manuals/line-sidecar-runbook.zh-TW.md)。
- **compose 測試堆疊**：`--profile generation` 啟動 generation-worker，它只接 internal 的 `generation-net`；`generation-out` volume 由 worker 寫入、broker 唯讀掛載。見 [agent container runbook](../manuals/agent-container-runbook.md)。

## 10. 其他 AI 代理或 API 的入口

第一刀只做 LINE 與 portal 兩個入口，不新增端點。其他代理要走同一條受治理路徑時，由管理員：

1. 以 `/api/v1/tasks/create` 建立任務：指派一個 AI 主體與 `role_executor`，`runtime_descriptor.capability_grants` 帶同樣三個 grant，generate 的 scope 照 §3 寫入 `output_slot`、`package_name`、`max_pages` 與 `package`；
2. 以 `/api/v1/admin/registration-credentials/issue` 簽發註冊憑證交給代理。

這類任務沒有受治理生成紀錄：broker 一樣驗證產物並寫入證據，但沒有交付對象，zip 留在 `Generation:OutputRoot` 由管理員取用。

## 11. 決策

| # | 決策 | 採用 |
|---|---|---|
| D1 | 第一刀的生成類型 | system_scaffold，產出 DefinitionTemplate 驅動的多頁前端原型（form、list、detail） |
| D2 | 推理者 | broker 依任務啟動受控代理；控制平面不做 LLM 推理 |
| D3 | 後端 | 不含；原型以瀏覽器內記憶體 store 讓 list、detail、form 可操作 |
| D4 | generate 的審批 | `auto_if_task_scope_match`（使用者在 ConfirmDraft 回的 y 即同意）；超出 scope 送管理員審批 |
| D5 | Governed 前置條件不滿足 | fail-closed，回覆「系統生成暫不可用」；不退回程序內生成 |
| D6 | 確認後的回應 | 非同步：先回「已受理（任務 id）」，完成後以 LINE 通知與 portal 產物清單送達 |
| D7 | sidecar 代理網路 | 沿用 `AllowAgentDefaultNetwork` 例外並記載風險；internal 代理網路列為後續 |
| D8 | sidecar 上的 generation-worker | 主機程序（與 site-crawler 一致）；容器化列為後續 |
| D9 | ui_components 打包範圍 | 整包，排除 data、refresource、demo、test 檔；依用量裁剪列為後續 |
| D10 | `/proj` 權限閘 | 獨立修正先合；`/proj` 起手與 `/ok` 兩處都檢查 |
| D11 | file:// 無法開啟 ES module | zip 附 `README.txt` 說明以本機 HTTP 伺服器開啟；託管預覽列為後續 |

## 12. 測試

- 生成器與外殼：`npm run test:generation`、`npm run test:definition-site:browser`。
- worker：`GenerationHandlerTests`（假 CLI 的邊界行為）與 `GenerationCliContractTests`（真正的 CLI：zip 內容與 build 輸出逐位元組相同、兩次生成 sha256 相同）。
- broker：`GenerationIngestTests`（路徑逃逸、連結、sha256 或大小不符、檔案遺失、slot 或請求 id 不符都失敗且不交付）、`GenerationDeliveryServiceTests`（交付、期限、代理結束未產出、交付失敗）、`GovernedGenerationLauncherTests`（grant 與 scope、工作項與 `AGENT_RUN` 上限、啟動失敗、就緒檢查）、`GenerationApprovalRenderTests`。
- 整合：`GovernedGenerationTests`（確認後的任務、主體、grant 與代理啟動；代理 session 依序呼叫三個能力，稽核鏈 RECEIVED → DISPATCHED → SUCCEEDED 並帶 evidenceRef；交付後任務 Completed；沒有 grant 時 Denied；未就緒與啟動失敗時 fail-closed）。
- `npm run validate:broker-scope`：Legacy 斷言保留；Governed 斷言確認回覆「已受理」、程序內沒有寫出檔案、未就緒時不建立任務。

## 13. 後續

- 端到端：compose 生成堆疊（mock LLM 依序 catalog → validate（含一次失敗後修正）→ generate）、主機 broker 完整入口（`/建立…#x` → y → 通知 → 下載 zip）、解壓產物的瀏覽器 smoke。
- sidecar 的 internal 代理網路；generation-worker 容器化。
- Governed 在 sidecar 實測後改為預設，再移除佔位頁產生器。
- 依用量裁剪 ui_components；產物的託管預覽；第二刀的後端生成。
- 把 code_gen、site_rebuild、doc_gen 移到受治理路徑；在 worker 協定加入正式的 evidence 欄位。
