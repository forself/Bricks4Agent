# 變更紀錄

本檔記錄 Bricks4Agent（B4A）對使用者可見的變更，依相容性分類：

- **新增**：新的選項、方法或匯出，預設不改變既有行為。
- **預設行為變更**：不改呼叫方式，但預設畫面或互動與前一版不同。
- **修正**：缺陷修正；若修正會改變可觀察行為，會在條目中寫明。

B4A 只收通用元件與通用能力；任何業務系統的專屬元件都不寫入 B4A。

## 未發行

### 修正：受治理生成與 `/proj` 的審查後修正（2026-10-07）

**預設行為變更**

- 確認 draft（回 `y`）時重新檢查 effective `AllowProduction`：draft 建立後才被降為 Basic 的使用者回 `production_disabled`，draft 保留，權限恢復後可再確認。專案訪談的 `/revise` 與訪談中的回答（會產生並交付審查檔）也要求 `AllowProduction`；訪談中途被降級的使用者只能 `/cancel`。這兩項屬於 `/proj` 權限閘的範圍。
- `/ok` 不再立即把訪談標為 Confirmed：訪談進入新的 `AwaitBuildConfirmation` 階段，建置任務建立成功後才標為 Confirmed。回 `n` 或 draft 過期後，可以再 `/ok` 重建 draft，或 `/revise` 修訂（修訂時撤下等待確認的舊 draft）。屬於 `/proj` 的範圍。
- 同一位使用者的 draft 確認依序執行，並在鎖內重新讀取 draft：一份 draft 只會建立一個任務，重複的 `y` 回 `draft_not_pending`。
- 受治理生成的名額：同一使用者同時只能有 `Generation:MaxConcurrentRunsPerUser`（預設 1）個進行中的生成（回 `generation_in_progress`），全部合計不超過 `Generation:MaxConcurrentRuns`（預設 2，要小於代理容器的 `MaxContainersPerType`）（回 `generation_busy`）；兩者都不建立任務，draft 保留。任務建立後的代理啟動不再跟著請求取消，失敗時一律把任務標為 Failed。
- 受治理任務交給代理的資料不再帶主機路徑與發起使用者的識別資料：runtime descriptor 只保留三個 grant、生成上限與可用時的 `llm`；scope descriptor 拿掉 `origin_user_id` 與 `execution_intent_document`；handoff 改為只有系統可讀（代理 session 經 context API 讀不到，broker 內部與管理端照常使用）；plan 描述不含來源使用者。`/tasks/query`、`/plans/get` 與 `/plans/status` 對不是提交者、也不是管理員的呼叫者（例如被指派到任務的代理）不回 `submitted_by`。
- 生成三個 grant 的配額改以任務累計（grant 樣板的 `quota_scope: "task"`）：代理容器重啟或 session 過期後重新註冊，只拿到這個任務尚未用掉的次數。其他任務的 grant 仍是每個 session 一份配額。
- 執行模型的推薦只在型錄項目標明的供應者（`HighLevelExecutionModelPolicy:Catalog[].Provider`）與 `LlmProxy:Provider` 相同時寫進受治理任務，否則代理用 `LlmProxy:DefaultModel`；先前推薦的本機模型會被送給 LlmProxy 目前的雲端供應者，每次生成都失敗。預設型錄的兩個項目標為 `ollama`；sidecar 把 LlmProxy 切到其他供應者時一併關閉執行模型建議。
- 生成器的欄位型別再擋下 `datetime`（明細頁顯示與再編輯的往返未支援，改用 `date`）與 `file`（原型不保存檔案值，改用 `text`），實際開放 20 種；驗證器版本改為 `definition-validator/1.1.0`。`string`、`boolean`、`integer` 等程式型別名稱被拒時，hint 直接給出對應的欄位型別，型錄的 `field_types` 也列出這張對照。
- `Generation:MaxPages` 限制在 1～12（生成器驗證的上限），先前可設到 100；validate 的 grant scope 也帶 `max_pages`，選取的頁數超過時在驗證就回 `MAX_PAGES_EXCEEDED`，不必等到 generate。

**修正**

- 受治理生成的交付：Drive 的網路錯誤、逾時與無效回應改為失敗結果，照常記錄產物並改用簽章下載連結（先前例外讓產物紀錄沒有寫入、任務直接標為失敗）；單筆交付丟出例外（含逾時）不再中斷整輪，排在後面的執行照常交付與檢查期限；已收下產物的交付例外最多嘗試 `Generation:MaxDeliveryAttempts`（預設 3）次才標為失敗。`GoogleDriveShareService` 的這項修正也適用於其他經它上傳的交付。
- `validate` 的錯誤合併與上限：相同的錯誤合併成一筆並註明出現次數（未知鍵依所在層級合併，允許鍵清單只列一次），合併後最多 50 筆，超過時帶 `truncated: true` 與 `total_errors`。generation-worker 的 validate 結果超過 `MaxResultBytes` 時改回截斷的結構化結果（仍是成功的呼叫），不再只回大小超限。
- generation-worker 忙碌時（每個 worker 一次處理一件），broker 對三個生成能力在約 15 秒內等它空出來再分派同一個請求，不重複扣配額；代理的工作流程說明遇到 `No available worker` 時可以稍後以相同參數重試。
- 生成類任務的 system prompt 改用精簡的基礎提示，不附專案手冊（手冊的 CLI 範例與欄位型別表和型錄衝突），也不列元件清單。
- 管理員可以用 `/agents/list` 列出的 id 經 `/agents/stop` 停止受治理生成的代理：任務標為 Failed、撤銷憑證與 session、停止容器並通知使用者（先前 id 對不上，只能等 watchdog）。

驗證入口：`npm run test:generation`、`npm run validate:agent-governed`、`dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`（GovernedGenerationLauncherTests、GenerationDeliveryServiceTests、GenerationIngestTests、DefinitionValidateTruncationTests）、`dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`（GovernedGenerationTests、ProjectInterviewGateTests）、`npm run validate:broker-scope`。

### 新增：受治理生成（system_scaffold 第一個切片）（2026-10-07）

**新增**

- 新的模式開關 `HighLevelCoordinator:Generation:SystemScaffoldMode`：`Legacy`（預設，行為不變）或 `Governed`。Governed 時，使用者確認系統雛形 draft 後，broker 不在程序內寫任何專案檔案：任務指派給自己的 AI 主體與 `role_executor`、帶三個生成 grant，broker 啟動受控代理並立即回覆「已受理（任務 id）」；代理經 LLM 代理撰寫 DefinitionTemplate，經 `generation.catalog.query`、`generation.definition.validate`、`generation.scaffold.generate` 三個能力走 PEP，generation-worker 確定性產出多頁前端原型（list、detail、form，瀏覽器內記憶體 store，不含後端）並打成 zip；broker 驗證路徑與 sha256、寫入證據文件後，以既有的產物紀錄、Drive 或簽章下載連結與 LINE 通知交付，任務 Completed 並停用代理。前置條件不滿足時回「系統雛形生成暫不可用」且不建立任務，不退回程序內生成；逾時或代理未產出時由 watchdog 把任務標為 Failed 並通知使用者。流程、設定與部署見 [GovernedGeneration.md](docs/designs/GovernedGeneration.md)。
- 生成器 `tools/generation/`（`cli.mjs` 的 catalog、validate、build，零依賴、Node 22）與通用外殼 `templates/definition-site/`（嚴格 CSP、hash 路由），golden 範例是通用的聯絡人三頁。外殼納入 `audit:csp` 掃描。欄位型別白名單是 page-gen 型別清單與 support matrix 的交集（28 種），本切片再擋下無後端原型中無法正確使用的型別，實際開放 20 種（被擋的型別、原因與替代見設計文件 §3.1）。
- 新的 worker `packages/csharp/workers/generation-worker/`（Containerfile、compose 的 `generation` profile 與 internal 的 `generation-net`、sidecar 的 `-GenerationMode Governed`、`run-worker.ps1 -Worker generation`）。compose 的 broker 服務新增必填的 `GENERATION_WORKER_AUTH_KEY_ID` 與 `GENERATION_WORKER_AUTH_SHARED_SECRET`，不論是否啟用 generation profile，up 與 down 都需要：既有的 env 檔只需補上這兩個變數，不必輪替其他金鑰（可用 `gen-stack-secrets.mjs --out` 在 repo 以外產生一份暫存檔，只複製這兩行，再刪除暫存檔）；三個能力以 tool-spec 為唯一來源；代理新增 `query_component_catalog`、`validate_definition`、`generate_scaffold` 三個工具，生成類任務的 system prompt 說明工作流程與上限，`AGENT_MAX_ITERATIONS` 可由環境變數設定。
- broker：`GovernedGenerationLauncher`、`GenerationIngestingDispatcher`（只處理 `generate_scaffold`）、`GenerationDeliveryService`（交付與 watchdog）、`AgentContainerLauncher`（`/agents/spawn` 與受治理生成共用的容器啟動，行為不變）；設定區段 `Generation`（`OutputRoot`、`DeadlineMinutes`、`AgentMaxIterations`、`MaxPages`、`WatchdogIntervalSeconds`、`MaxPackageBytes`）；保留前綴 `generation.execution.` 與 `generation.run.`；generate 超出 scope 送審時，審批畫面顯示標題、各頁摘要與定義內容。
- 任務狀態新增 `Failed`（終止狀態，與 Completed、Cancelled 一樣不再接受註冊、續期、憑證簽發與取消）；`AgentSpawnService.DeactivateTaskAgent` 依任務指派的主體停用代理。
- npm script：`test:generation`（已接進 `npm test`）與 `test:definition-site:browser`。

**修正**

- worker-sdk 的 `WorkerHost`：送結果、心跳與狀態回覆經同一個寫入鎖，不再交錯寫入同一個 stream；接收端保留同一次讀到的後續 frame。影響所有 worker。
- `PolicyEngine.IsScopeValid` 遇到例外時改為不在 scope 內；scope 的 `routes`、`paths` 存在但不是字串陣列時也視為不在 scope 內（先前會被當成沒有限制）。依審批政策改為 Deny 或送審。

驗證入口：`npm run test:generation`、`npm run test:definition-site:browser`、`dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`（GenerationHandlerTests、GenerationCliContractTests、GenerationIngestTests、GenerationDeliveryServiceTests、GovernedGenerationLauncherTests、GenerationApprovalRenderTests、WorkerFrameIoTests、PolicyEngineScopeFailClosedTests、GenerationToolSpecTests）、`dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`（GovernedGenerationTests，含以真正的 worker handler 與生成器 CLI 跑 golden 範例的程序內端到端案例，需要 node）、`npm run validate:broker-scope`、`npm run validate:agent-governed`、`npm run validate:agent-container-config`。

### 預設行為變更：`/proj` 與 `/ok` 需要 production 權限、`/ok` 改為建立 draft、高階回覆不帶主機路徑（2026-10-07）

**預設行為變更**

- 專案訪談的 `/proj`（起手）與 `/ok` 和 `/建立` 走同一個權限判斷：effective `AllowProduction` 不成立（Basic 層一律如此）時回 `production_disabled`，回覆與 `/建立` 被拒時相同，不會開始訪談，也不會建立 draft。先前這兩個指令在權限閘之前處理，Basic 層也能走到建置。既有的訪談整合測試要先以會員身分開啟 production（測試 fixture 已改為先呼叫 `EnableLineProductionAsync`）。
- `/ok` 不再當下建置。它把訪談標為 Confirmed，並把編譯出的專案定義轉成 `system_scaffold` draft，回覆摘要並請使用者回 `y` 確認或回 `n` 取消。回 `y` 後走與 `/建立` 相同的 draft 確認：專案名稱重查、升格閘、execution intent、task、plan、handoff，之後才生成與交付。使用者工作區已有同名專案資料夾時，draft 不帶專案名稱，改請使用者以 `#名稱` 補件。
- 高階回覆不帶主機絕對路徑。`?profile`、`/name`、`/id` 的回覆改寫 `workspace: line/<user>` 這類相對位置；code_gen、system_scaffold、site_rebuild 的完成回覆只寫 `project_folder`、相對的 `entry_file` 與 `package_file` 檔名；要求專案名稱的回覆不再列出工作區根目錄；生成失敗訊息中的絕對路徑只保留最後一段。回覆、後續訊息與診斷欄位在回傳前會把受管工作區根目錄底下的路徑改寫成相對名稱，portal 的結果紀錄顯示時也套用同樣的改寫。draft 與 plan 的描述改寫專案資料夾名，不再寫專案的絕對路徑。
- 新增 `HighLevelReplyRedactor`（路徑改寫工具）與 `HighLevelCoordinator.RedactHostPaths`。
- 整合測試的 `BrokerFixture` 改以 `UseSetting` 設定資料庫路徑、受管工作區根目錄與執行模型建議開關（`HighLevelExecutionModelPolicy:Enabled=false`，確認 draft 時不連本機模型）。這些值在 host 建置前就被讀取，先前以 `ConfigureAppConfiguration` 覆寫沒有生效，測試共用 bin 下的 `broker.db` 並寫入本機的受管工作區；結束時改為先釋放 SQLite 連線池再刪除暫存檔。

驗證入口：`dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`（ProjectInterviewGateTests、ProjectInterviewLifecycleTests、ProjectInterviewReviewTests、PortalEndpointTests）、`dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`（HighLevelReplyRedactorTests）、`npm run validate:broker-scope`。

### 預設行為變更：session 註冊需要註冊憑證、token 綁定所屬 session 並可續發（2026-10-05）

**預設行為變更**

- `sessions/register` 先驗證註冊憑證：交握的加密 payload 要帶該任務的 `registration_secret`。broker 只存 SHA-256 雜湊並以常數時間比對；缺少、錯誤、到期、撤銷，以及主體或任務不存在、任務未指派給該主體，一律回同一個 401（`Registration rejected.`），原因只寫進伺服器端 log 與稽核。憑證可重複使用到到期或撤銷，每一把都有到期時間；驗證只比對仍有效的憑證，同一任務累積再多已撤銷或到期的紀錄（例如 spawn 失敗留下的），仍有效的舊憑證照樣可用；任務狀態、角色與本機來源的規則不變。
- 憑證來源：`DevelopmentSeed:RegistrationSecret` 與 `DashboardSeed:RegistrationSecret`（每次啟動重新設定到期時間，`RegistrationSecretLifetimeHours` 預設 24，compose 以 `BROKER_REGISTRATION_SECRET_LIFETIME_HOURS` 調整；broker 連續執行超過這段時效後，以種子憑證註冊會被拒，須重啟 broker 或調高時效；非 Development／Testing 環境啟用 `DevelopmentSeed` 卻沒有可用密鑰時拒絕啟動）、`/api/v1/agents/spawn`（每次簽發新的一把，經 `SecretEnvironment` 交給容器，`Broker:RegistrationCredential:SpawnedAgentLifetimeHours` 預設 24；容器啟動成功後才撤銷上一把，spawn 失敗只撤銷新的這把，不影響仍在執行的舊容器）、管理員的 `/api/v1/admin/registration-credentials/issue`（另有 `revoke`、`list`）。停用 agent 會撤銷它的憑證與 session。kill switch 讓執行中的代理停止，但不撤銷憑證，之後新啟動的程序仍可以有效憑證註冊。
- compose 三個檔案新增必填的 `BROKER_REGISTRATION_SECRET`，broker 以它種入、agent 以它註冊；`gen-stack-secrets.mjs` 會產生它，既有的 env 檔請以 `--force` 重新產生（之後用 `down -v` 重建 stack）。agent 只從環境變數讀取（沒有命令列參數），system prompt 只放佔位字串。dashboard 登入表單多一個註冊密鑰欄位；e2e-bridge 與手動測試 `test-broker-integration.js` 從環境變數讀取。
- token 只能在自己的 session 使用：加密信封的 session 必須是 token 的 session；不論加密信封或 Bearer，session 關閉、撤銷、到期或任務取消後，該 session 的 token 立即失效（401）。交握信封只接受於 `sessions/register`，送往其他端點回 400。
- heartbeat 換發同一 session 的新 token（舊 token 自然到期；新 token 沿用請求 token 的 epoch，heartbeat 處理中才發生的 kill switch 也讓代理停止，處理中發現 epoch 已前進時回與 kill switch 相同的 401），並延長 session 與其 grants，上限為註冊時間加 `Broker:Session:MaxLifetimeMinutes`（預設 1440）；`Broker:Session:TtlMinutes` 預設 60，必須小於 120。session 無效時 heartbeat 由 400 改為 401。agent、dashboard、e2e-bridge 會定時 heartbeat；agent 收到 401 時以同一把註冊密鑰重新註冊一次；kill switch 造成的 401 讓 agent 停止，之後不再重新註冊（token 到期後也一樣）。e2e-bridge 收到 kill switch 或重新註冊被拒（400、401、403）時結束並回非零碼，網路錯誤與 5xx 仍會重試。
- 管理員以 `/api/v1/admin/registration-credentials/revoke` 撤銷憑證時，一併撤銷以該憑證註冊的 session（回應多一個 `sessions_revoked`；以 `principal_id`＋`task_id` 撤銷時，該任務的所有 session 都會結束），代理無法再續期；撤銷時正在進行的註冊回同一個 401，不留下可用的 session；heartbeat 也確認註冊這個 session 的憑證未被撤銷（憑證到期只影響之後的註冊）。session 會記錄註冊時使用的憑證（`container_sessions.registration_credential_id`，既有資料庫啟動時自動加欄位）。只撤銷 session 時，代理仍會以有效的憑證重新註冊。
- 種子憑證被管理員撤銷後，只要設定的密鑰不變，broker 重啟不會再種入（啟動時記警告）；要恢復請輪替密鑰（`gen-stack-secrets.mjs --force`）。
- agent 重新註冊被 broker 拒絕（400、401、403）後就停止：不再 heartbeat、註冊或呼叫 broker，`--run` 以錯誤結束，LINE listener 以非零碼結束；網路錯誤仍會重試。
- Windows sidecar 直接使用本機既有的 `bricks4agent-agent:latest`：升級 broker 後要以 `tools/agent/Containerfile` 重建這個映像，否則舊映像不送註冊密鑰，動態 spawn 的代理都會註冊失敗。
- 介面變更：`ISessionService.Heartbeat` 改回傳 `DateTime?`、`RegisterSession` 多一個選用的憑證 id 參數，並新增 `RevokeSessionsByCredential` 與 `RevokeSessionsBySubject`；`ICapabilityCatalog` 新增 `ExtendSessionGrants`，`IScopedTokenService` 新增 `TokenLifetime`，新增 `IRegistrationCredentialService`（`UpsertSeed` 可回傳 null，`IsRevoked` 查詢單一憑證是否已撤銷）；`AgentSpawnService` 多一個接收憑證與 session 服務的建構子。自行實作這些介面的程式要同步調整。

驗證入口：`dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`（SessionBindingTests、RegistrationCredentialTests、SessionLifecycleRaceTests）、`dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`（SessionLifetimeTests、RegistrationCredentialServiceTests）、`npm run validate:agent-governed`、`npm run validate:broker-llm-proxy`、設 `CONTAINER_ENGINE=docker` 後執行 `npm run validate:container-spawn` 與 `npm run validate:podman-governed-stack`。

### 預設行為變更：代理容器不掛 repo、所有容器加固、映像改為 .NET 10 與 Node 22（2026-10-05）

**預設行為變更**

- 代理容器不再掛載任何主機目錄：三個 compose 與 Windows sidecar 都拿掉 agent 的掛載，sidecar 不再把 managed-workspaces 掛給代理。專案手冊改烤進映像（`/app/AGENT.md`，經 `AGENT_MANUAL_PATH` 讀取）；專案目錄附近找得到 `AGENT.md` 時仍以專案的為準，local 模式不受影響。
- file-worker 收緊讀取面：sandbox 邊界改以「根目錄 + 分隔字元」完整比對並解析 symlink，路徑段含冒號或屬於 8.3 短檔名形式（`~` 後接數字）一律拒絕，已存在的路徑段必須與磁碟上列舉出的實際名稱相同（非 Windows 區分大小寫：Linux 容器掛載不分大小寫的目錄時，大小寫不同的寫法會被拒絕）；`.git`、`.claude`、`.codegraph-cache`、`.run`（本機執行期狀態）、`.env`、`.env.*`、`appsettings.Development.json`、`appsettings.Production.json`、`Api.txt`、`ngrok_recovery_codes.txt`、SQLite 資料庫檔、金鑰與憑證檔、下載的服務帳戶金鑰檔名、`line-worker/appsettings.json` 等拒絕清單對讀、列、搜尋、寫、刪一致套用，列舉與搜尋不進入 symlink；搜尋的 `pattern`／`file_pattern` 只能比對檔名，帶目錄部分即拒絕。搜尋改用與代理工具相同的參數（`pattern`、`directory`、`file_pattern`；仍接受 `query`、`path`），`directory` 經同樣的 sandbox 檢查。handler 依 `args` → `tool_args` → payload 根層的順序取參數，與 broker 的 PolicyEngine 相同；PolicyEngine 檢查這三個位置的所有路徑鍵。
- 非 strict 模式的降級分派：worker 回覆的結果（含拒絕）是最終結果，不再改交 broker 內建的 InProcess 實作重試；只有沒有可用的 worker，或分派逾時、傳輸失敗時才降級。InProcess 的搜尋套用相同的檔名 pattern 規則，sandbox 根目錄改以「根目錄 + 分隔字元」比對，只回傳根目錄內的檔案。`ExecutionResult` 新增 `AnsweredByWorker`。
- compose 的所有服務都套 §13.2 加固（`read_only`、`/tmp` tmpfs、`cap_drop: ALL`、`no-new-privileges`、`pids_limit`；broker 1024，其餘 256）。broker 不再掛 docker socket，`CONTAINER_MANAGER_ENABLED` 維持 false；line-worker 的音訊暫存改到 `/tmp/audio_temp`；dev seed 的 `file.search` 改為實際存在的 `file.search_name` 與 `file.search_content`。
- broker 動態啟動的容器一律帶 `--read-only`、noexec 的 `/tmp` tmpfs、`--cap-drop ALL`、`no-new-privileges`、`--pids-limit` 與記憶體上限；拒絕 root（`User` 任一部分為 `root` 或數值為 0，例如 `00`、`+0`）、`host`／`container:`／`ns:` 網路、agent 映像的掛載與發布埠、runtime socket、系統路徑與白名單外的 hostPath。非 agent worker 的發布埠必須綁 `127.0.0.1`：既有的 `WorkerImages` 設定若寫成 `8080:80`，升級後 spawn 會失敗，請改為 `127.0.0.1:8080:80`。agent 映像沒有自己的網路時預設拒絕，只有 sidecar 以 `AllowAgentDefaultNetwork=true` 明確例外。停止容器改為 `rm -f -v`。
- `IContainerManager.SpawnWorkerAsync` 改為接收 `ContainerSpawnRequest`：`TrustedEnvironment` 放 broker 自己組出的環境變數，`SecretEnvironment` 只以 `-e NAME` 出現在 CLI 參數中。自行實作 `IContainerManager` 的程式需同步調整。
- `/api/v1/workers/spawn` 的 `worker_type` 改為必填，不再能啟動 agent，也不再接受 `environment`；`worker_id` 只接受 1 到 64 個英數字與 `.`、`_`、`-`（第一個字元須為英數字），自動產生的 id 改為「12 個隨機字元-worker_type」；找不到 runtime CLI 時回 503，spawn 逾時回 504。`/api/v1/agents/spawn` 的 `broker_url` 只能等於設定的 `AgentBrokerUrl`，`max_iterations` 上限為 50。
- 映像改為 `sdk:10.0`／`aspnet:10.0` 與 `node:22-bookworm-slim`，全部以多架構 index digest 釘選；`/app` 由 root 擁有；各映像 UID 不重複（line-worker 改為 10005、mock-ollama 10006、mock-openai 10007）。基底映像的修補要以 `node tools/agent/container/resolve-base-image-digests.mjs` 更新 digest 後重建才會進來。

**新增**

- stack 測試設 `CONTAINER_ENGINE=docker` 即改用 Docker（預設仍為 podman），`up` 之後以 `inspect` 檢查每個容器的加固；governed stack 改為證明 file-worker 真的讀到檔案，execution-adapter stack 增加實際的 `dotnet build`。
- `npm run validate:container-images`（Containerfile 靜態檢查）、`npm run validate:container-spawn`（host broker 經 docker／podman 實際 spawn 代理容器）、`npm run validate:podman-execution-adapter-stack`。

驗證入口：`npm run validate:agent-container-config`、`npm run validate:agent-governed`、`dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`（SandboxPolicyTests、PayloadArgsConsistencyTests、PolicyEngineTests、InProcessDispatcherSearchTests、FallbackDispatcherTests）、`dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj`（Agent Container Tests）、設 `CONTAINER_ENGINE=docker` 後執行 `npm run validate:podman-governed-stack`。

### 預設行為變更：broker 驗證與授權收緊、compose 改用產生的密鑰（2026-10-04）

**預設行為變更**

- `/api/v1` 端點一律依匹配到的端點判斷驗證政策，預設需要 scoped token，與 HTTP 方法大小寫、尾斜線無關；公開端點只剩健康探測，簽章連結、local-admin、portal、worker 簽章路由各用自己的機制。
- 管理性端點（建立任務、agent 與 worker 管理、稽核查詢、LINE 使用者管理等）只限管理員；任務、plan、context 的查詢與寫入只限任務擁有者或管理員。
- `sessions/register` 只接受任務已指派的主體與角色；管理員角色只能從本機註冊。`DashboardSeed` 只在 Development 環境種入。`sessions/close` 會撤銷該 session 的 token。
- broker 啟動時檢查密鑰：曾隨 repo 發布的開發金鑰在所有環境都拒絕；非開發環境遇到佔位值拒絕啟動，除非明確設定 `Broker:AllowEphemeralKeys=true`（LINE sidecar 已設定，維持原行為）。
- compose 不再附預設密鑰：先執行 `node tools/agent/container/gen-stack-secrets.mjs` 在 repo 以外產生 env 檔，再以 `--env-file` 啟動；`WORKER_AUTH_ENFORCE` 預設改為 true，對外埠只綁 127.0.0.1。曾以舊預設值部署的環境請輪替所有 broker 與 worker 金鑰。

驗證入口：`dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`（BrokerAuthorizationRegressionTests）、`dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`（BrokerSecretsValidatorTests）、`npm run validate:agent-container-config`。

### 新增：QR Code 與條碼產生器（2026-10-03）

元件數由 132 增為 134。兩個元件在 catalog 中標為 `beta`、`manual_only`；字串由元件資料夾內的 `locale.js` 註冊 zh-TW 與 en。

**新增**

- `QrCode`：QR Code（ISO/IEC 18004），版本 1～40、錯誤修正 L/M/Q/H，自動選數字、英數或位元組（UTF-8）模式，八種遮罩依罰分選最小；可選 UTF-8 的 ECI 宣告。編碼器 `encodeQr` 可單獨匯入。
- `Barcode`：一維條碼 Code 128（自動切換 A/B/C 字集）、Code 39（可附 mod 43 檢查字元，台灣超商代收與郵局劃撥的繳費單即此格式）、EAN-13、EAN-8（自動補或驗證檢查碼，護線延伸、數字分組）。編碼器 `encodeBarcode` 可單獨匯入。
- 兩者都是純 Canvas、零依賴；模組與條邊對齊整數像素，`exportPNG(scale)` 輸出高倍率圖檔供列印；顏色取主題 token，深色主題下自動對調，模組或條永遠比底色暗；內容不合格式時在畫面上顯示訊息，`getError()` 取得原因。

驗證入口：`node --test packages/javascript/browser/ui_components/viz/QrCode/qr-encoder.test.mjs packages/javascript/browser/ui_components/viz/Barcode/barcode-encoder.test.mjs`（含標準已知數值與獨立解碼器的往返比對）。

### 修正：SPA 範本清理器邊界（2026-09-29）

- 保留 f905b2f 的「先清洗子節點再拆殼」修正；追加瀏覽器無 CSP 回歸，直接驗證清理器及 WebTextEditor 儲存／載入。
- SPA 範本的 URL 檢查先移除控制字元，阻擋被 Tab／換行切開的危險協定、協定相對 URL 與非白名單協定。
- 範本 HTML 改用屬性白名單，不再保留 inline style、srcset、ping、contenteditable 等主動屬性；圖片 data URL 僅保留點陣格式。無 DOMParser 時跳脫文字，不回傳原始 HTML。
- 一般格式文字、表格與本機相對連結保留；這是安全性收斂，依賴上述移除屬性的消費端需調整。TIM 使用元件庫清理器與專案防護包裝，不使用 SPA 範本清理器。

驗證入口：`npm --prefix packages/javascript/browser run test:vitest -- __tests__/security/SanitizeHtml.test.js`、`npm run test:sanitizer:browser`（需測試用 Playwright 與 Edge）。

### 新增：16 個通用元件與兩個工具（2026-09-27）

新元件在 catalog 中標為 `beta`、`manual_only`，以手動 `new` 使用；字串由各元件資料夾內的 `locale.js` 自行註冊，提供 zh-TW 與 en。元件數由 116 增為 132。

**新增**

- 排程與資料：`TimeGrid`（時段格線，項目跨格、重疊並排、點選與拖放、鍵盤搬移）、`DataGrid`（試算表式資料格，逐格驗證、方向鍵導覽、貼上 TSV、未存標記與 `getChanges()`；`EditableTable` 不變）。
- 輸入：`RemoteSelect`（遠端查找）、`DateRangePicker`、`TimeRangePicker`、`DateTimeRangePicker`（區間輸入）、`Transfer`（穿梭框）、`ConditionBuilder`（條件編輯器，輸出純資料）。以上都實作欄位錯誤契約 `setError` / `clearError`。
- 流程與回饋：`ImportWizard`（CSV/TSV 匯入精靈）、`IssueList`（問題清單）、`ApprovalTimeline`（審核歷程）、`NotificationCenter`（通知中心）、`Countdown`（倒數計時）、`ConflictNotice`（版本衝突提示）。
- 版面：`Popover`（可放按鈕與連結的浮層）、`PrintLayout`（只列印指定區塊）。
- 工具：`createPermissionGate`／`PermissionGate` 依權限隱藏或停用元素與元件，只改善介面，權限仍須由伺服器把關；`createDirtyGuard`／`DirtyGuard` 追蹤未存變更並在離開前確認。由 `utils/index.js` 匯出。

**已知限制**

- `DateRangePicker`、`TimeRangePicker`、`DateTimeRangePicker` 從外部替內部的 DatePicker、TimePicker 補上鍵盤與 ARIA，依賴它們的 DOM 結構；日後應內建到 DatePicker、TimePicker 本身。
- `PrintLayout` 的紙張大小與邊界需要 Chromium 或 Firefox 110 以上；頁首頁尾只印一次。

### 補強：既有元件的選配能力與無障礙（2026-09-27）

**新增**

- DataTable：
  - 伺服器端模式 `serverSide` + `dataSource(query)`，排序、分頁、快速篩選交給伺服器，過期回應會被丟棄；另有 `searchDebounce`、`onQueryChange`、`reload()`、`getQuery()`、`setData(rows, total)`。
  - 固定表頭 `stickyHeader` + `maxHeight`；欄位可設 `sticky: 'left' | 'right'`。
  - 欄位切換 `columnToggle`，方法 `setColumnVisible()`、`getColumnVisibility()`，回呼 `onColumnVisibilityChange`；欄位可設 `hideable: false`。
  - 列展開 `expandable`，方法 `expandRow()`、`collapseRow()`、`toggleRow()`、`getExpandedKeys()`。
  - 跨頁勾選：設 `rowKey` 後依鍵值追蹤勾選，新增 `getSelectedKeys()`、`setSelectedKeys()`、`clearSelection()` 與 `selectAllScope`。
- TreeList：勾選多選 `checkable`（含半選、`checkStrictly`、`checkedKeys`、`onCheck`、`getCheckedKeys()`、`setCheckedKeys()`、`checkAll()`、`uncheckAll()`）與延遲載入 `loadChildren`（含 `reloadNode()`）。
- WorkflowPanel：`stages`、`replaceStages`、`fieldMap` 與 `WorkflowPanel.DEFAULT_FIELD_MAP`。內建 13 個階段只為相容保留，新專案請傳入自己的階段。
- Progress：分段堆疊 `segments`、`showLegend`、`setSegments()`，另有 `mount()` 別名。
- Canvas 圖表（14 種）：`accessibleTable` 產生輔助科技可讀的資料表（`true` 視覺隱藏、`'visible'` 顯示在圖下），`accessibleTableMaxRows` 限制列數，子類以 `getDataTable()` 提供內容。說明見 `viz/ACCESSIBILITY.md`。
- ModalPanel：`ModalPanel.defaults.manageFocus` 與 `manageFocus`、`initialFocus`、`ariaLabel` 選項。開啟焦點管理後，開啟時焦點移入、Tab 限制在對話框內、關閉時還原焦點。預設關閉，新專案建議在啟動時開啟。

**預設行為變更**

- ModalPanel 帶 `role="dialog"`、`aria-modal="true"`，以標題命名；關閉鈕的 `aria-label` 改走語系（zh-TW 仍為「關閉」）。疊加多層時一次 Escape 只關最上層；內部元件已處理（`preventDefault`）的 Escape 不再連帶關閉對話框。
- Dropdown 選單展開時按 Escape 只關閉選單，不再同時關閉外層對話框；MultiSelectDropdown 選單已收合時不再攔下 Escape，外層對話框可正常關閉。
- TreeList 預設帶樹狀結構的 ARIA 角色與鍵盤操作（方向鍵、Home、End、Enter、Space），每棵樹成為一個 Tab 停駐點。
- DataTable 的 `rowKey` 原本不起作用，現在會改為依鍵值追蹤勾選。只影響可勾選的表格；Tim2026 傳入 `rowKey` 的表格都不可勾選，行為不變。
- WorkflowPanel 下一階段的「(待處理)」提示改走語系，英文介面顯示「(To do)」。
- Progress 圓形模式掛載後立即以主題色重繪，不再先以灰色顯示。
- Dropdown、MultiSelectDropdown 的輸入框文字改用主題文字色 `var(--cl-text)`。
- BasicButton 停用時不再顯示滑過效果。

**修正**

- BasicButton 以 `disabled: true` 建立後，`setDisabled(false)` 仍無法點擊；現在事件一律綁定，執行時才檢查停用狀態。
- Dropdown、MultiSelectDropdown 的輸入框在深色主題下是黑字、難以閱讀。
- InfoPanel、FunctionMenu、WorkflowPanel 的 CommonJS 相容段在唯讀的 ESM 環境（例如 Vitest）會在載入時拋錯。
- Progress 在 `destroy()` 後呼叫 `setValue`、`setVariant` 會拋錯；TreeList 在 `destroy()` 後呼叫 `setData` 會重新繪製並殘留圖示訂閱。
- Metadata 工具：`build-metadata.mjs --check` 改為唯讀，原本會刪除沒有註冊的 manifest；方法擷取不再把 `if`、`for` 等控制敘述當成方法，也不再漏掉預設參數含括號的方法；文件路徑優先選與元件同名的 README。

**相容性驗證**

- B4A：`npm test`（Vitest 61 個檔案、1102 項）、`audit-csp`、`audit-ui-style-rules`、`validate-ui-library`、`build-metadata --check`、`test-audit-csp-hard-zero`、`studio-self-host-audit` 全部通過；`validate:ui-state` 只剩 main 既有的 Badge 一項失敗。
- Tim2026：以包含本節與上一節全部變更的版本取代其內嵌副本，執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準完全相同，沒有退步。

### 補強：輸入元件的欄位錯誤標示（2026-09-27）

**新增**

- 所有輸入元件都有 `setError(message, { display })` 與 `clearError()`，錯誤外觀一致：控制項紅框、`aria-invalid="true"`，錯誤文字以 `role="alert"` 顯示並用 `aria-describedby` 連回控制項。`display: false` 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層使用。
  - 新增此方法的元件：Checkbox、ToggleSwitch、ColorPicker、Rating、Slider、TextArea、NumberInput、Dropdown、MultiSelectDropdown、DatePicker、TimePicker、DateTimeInput、TagInput、BatchUploader、CommandComposer、ChainedInput、ListInput，以及 `Checkbox.createGroup`、`Radio.createGroup` 回傳的群組。
  - Checkbox、ToggleSwitch、ColorPicker、Rating 的錯誤文字插在元件正後方，元件需已掛載；ChainedInput 只在整組下方顯示文字，不替個別欄位畫紅框。
- TextInput 的 `setError` 新增第二個參數 `{ display }`，預設行為不變。
- FormField 新增 `markControl` 選項，預設 `false`。設為 `true` 時，`setError` 也會標示內部元件的錯誤狀態。
- SearchForm 新增 `markInvalidFields` 選項，預設 `false`。設為 `true` 時，必填驗證失敗會標示欄位元件的錯誤狀態，錯誤文字仍只顯示一次。
- `utils/field-error.js` 提供 `setFieldError`、`clearFieldError`、`getFieldError`、`hasFieldError` 與 `FIELD_ERROR_CONTRACT`，也由 `utils/index.js` 匯出，自訂元件可直接沿用同一套外觀與無障礙標示。
- `Checkbox.createGroup` 回傳的群組新增 `destroy()`。

**修正**

- NumberInput：使用者直接輸入後按 Enter 或離開欄位，數值確實改變時會觸發 `onChange` 一次。原本只有 +/- 按鈕、方向鍵與 `clear()` 會觸發，直接輸入的數值不會通知呼叫端。程式呼叫的 `setValue()` 仍預設不觸發。
- TextInput 處於錯誤狀態時，輸入框會加上 `aria-invalid="true"`，讓螢幕報讀器能辨識錯誤欄位；畫面不變。
- SearchForm 銷毀時會一併銷毀 Checkbox 群組，釋放勾選圖示。

**相容性**

- SearchForm 預設只呼叫原本就有 `setError` 的元件，畫面與前一版相同。Tim2026 的查詢表單使用 TimePicker、Checkbox 群組與 Radio 群組，這些元件新增了 `setError`，但在預設設定下不會被標示。
- FormField 預設不轉呼叫內部元件，既有以 FormField 顯示錯誤的畫面不變。

**相容性驗證**

- B4A：`npm test`、`audit-csp`、`audit-ui-style-rules`、`validate-ui-library`、`build-metadata --check` 全部通過。
- `validate:ui-state`：測試用假 DOM 補上元件實際使用的標準 DOM API 後，除了 Badge 一項以外全部通過。Badge 一項在 main 上就已失敗，原因是假 DOM 不會把 `style.cssText` 反映到個別樣式屬性。上一版寫回後，Dropdown、TimePicker、MultiSelectDropdown、ChainedInput、PhoneListInput 五項曾因假 DOM 缺少 API 而失敗，已一併恢復。
- Tim2026：以本版本取代其內嵌副本，執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準完全相同，沒有退步。

### 安全修正：sanitizeHTML（2026-09-27）

**修正**

- `sanitizeHTML`：拆掉不在允許清單的包裝元素時，會先清洗其子節點。原本子節點移出後不再經過清洗，`<section><img src=x onerror=...></section>` 這類內容會保留事件屬性，造成 XSS。
- SPA 範本前端的 `sanitizeHTML` 修正同一個拆殼繞過。它原本連 `<body>` 本身也當成不允許的標籤拆掉，任何非空輸入都會丟出例外；改為只清洗 `<body>` 的子節點。

**相容性驗證**

- 新增 26 項測試，涵蓋包裝元素繞過樣本、允許清單內容不變、重複清洗結果不變，以及 SPA 範本版本。
- Tim2026 沒有直接呼叫 `sanitizeHTML`。以包含本修正的 B4A 工作樹執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準相同。

### 安全修正：sanitizeHTML（2026-09-27）

**修正**

- `sanitizeHTML`：拆掉不在允許清單的包裝元素時，會先清洗其子節點。原本子節點移出後不再經過清洗，`<section><img src=x onerror=...></section>` 這類內容會保留事件屬性，造成 XSS。
- SPA 範本前端的 `sanitizeHTML` 修正同一個拆殼繞過。它原本連 `<body>` 本身也當成不允許的標籤拆掉，任何非空輸入都會丟出例外；改為只清洗 `<body>` 的子節點。

**相容性驗證**

- 新增 26 項測試，涵蓋包裝元素繞過樣本、允許清單內容不變、重複清洗結果不變，以及 SPA 範本版本。
- Tim2026 沒有直接呼叫 `sanitizeHTML`。以包含本修正的 B4A 工作樹執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準相同。

### 寫回 Tim2026 內嵌副本的通用改進（2026-09-27）

Tim2026 以 `packages/Bricks4Agent` 內嵌 B4A，並在正式環境中做了一批通用改進。本次把其中屬於通用能力的部分寫回 B4A，預設值對齊 Tim2026 正式環境的行為；Tim 專屬的業務內容不寫回。

**新增**

- DataTable 快速篩選：`search: true` 時在工具列顯示篩選框，只篩選已載入的資料列。新增 `getSearchText()`、`getSearchDraft()`、`setSearchText()`、`clearSearch()`，欄位可設 `searchable: false`，文字可用 `options.textLabels.search` 覆寫。預設關閉。
- DynamicDetailRenderer 子表格：欄位支援 `hidden`、`width`、`link.route` 連結樣板與 `link.target`；表格支援 `titleTemplate`、`pagination`、`pageSize`、`rowsPerPageOptions`、`search`；資料中的 `subtableErrors[id]` 會以錯誤訊息取代空表。
- 查詢定義的 `table.textLabels.search` 會傳給 DataTable 快速篩選。
- Link：頁面有 `<meta name="app-path-base">` 時，外開的 hash 路由會補上部署路徑前綴；沒有該 meta 時行為不變。
- TextArea：新增 `sizing`、`sizingToggle`、`onSizingChange` 選項，`getSizing()`、`setSizing()` 方法，以及 `preferredTextAreaSizing()`、`TEXTAREA_SIZING_MODES`、`TEXTAREA_SIZING_STORAGE_KEY` 匯出。舊的 `autoResize: true` 等同 `sizing: 'auto'`。

**預設行為變更**

- TextArea 預設改為固定 5 行並顯示捲軸，框內右下角有「固定／依內容加高」切換鈕，使用者的選擇記在瀏覽器儲存空間，成為之後 TextArea 的預設。預設 `resize` 由 `vertical` 改為 `none`。原本預設為 4 行、可拖拉。
- Dropdown、MultiSelectDropdown 的選項清單展開時改用固定定位浮在最上層，不再被上層容器裁切，寬度依內容加寬。清單仍留在元件的 DOM 內。
- DatePicker 的月曆、TimePicker 的時間面板展開時暫時移到 `document.body` 並以固定定位顯示，收合後移回元件內。
- Dropdown 的選項節點只在展開時建立，收合時釋放。
- Dropdown 的值比對改為字串相等，例如 `1` 與 `'1'` 視為同一個選項。
- 宣告式列表（query 定義）的結果表預設開啟快速篩選；定義中設 `table.search: false` 可關閉。
- FieldResolver 產生的多行欄位不再依 `maxLength` 自動加高，一律由 TextArea 的共通行為決定；只有定義中明確指定 `rows` 時才傳入。

**修正**

- ModalPanel：`destroy()` 之後 `backdrop` 重新回到 `null`，恢復既有公開契約；在 `onClose` 內銷毀面板也能完整拆除遮罩。
- TgosMap：瀏覽器取不到 2D Canvas 時改用 TGOS 預設標記，不再導致地圖初始化失敗。
- QueryDefinitionAdapter：酬載樣板中沒有預設值的明確 `null` 會保留；空的動態綁定仍不會覆蓋 `payloadDefaults`。這同時修正了 `npm test` 中原本失敗的查詢酬載測試。
- Dropdown、MultiSelectDropdown、DatePicker、TimePicker 的 document 點擊與視窗捲動、縮放監聽，只在展開期間掛載，元件數量多時不再常駐。

**未寫回**

- Tim2026 版 TgosMap 以 SVG 圖片作為地圖標記，違反 B4A 禁用 SVG 的規則；保留 B4A 以 Canvas 繪製的標記。
- 其餘差異經比對為格式或註解差異，程式行為與 B4A 相同，保留 B4A 版本。

**相容性驗證**

- B4A：`npm test`、`audit-csp`、`audit-ui-style-rules`、`validate-ui-library`、`build-metadata --check` 全部通過。
- Tim2026：以本版本取代其內嵌副本，執行 Tim2026 的 2846 個單元與安全測試，結果與內嵌副本基準完全相同，沒有退步。
