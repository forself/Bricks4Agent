# Bricks4Agent 目前架構與進度說明

更新日期：2026-03-26  
文件性質：架構現況、進度盤點、理念分析、風險評論

## 1. 這份文件在說什麼

這份文件的目的不是展示功能清單，而是把目前 `Bricks4Agent` 已經做成什麼、還沒做成什麼、設計理念是什麼、以及哪些地方其實只是暫時可用的 POC 狀態，講清楚。

如果只看表面，這個專案現在已經有：

- LINE 對話入口
- broker 控制核心
- 受控工具與高階查詢
- Browser 能力治理模型
- Azure VM IIS 部署能力
- Google Drive 交付能力
- 本機管理後台

但如果看深一層，現況更準確的描述是：

- 高階入口與控制核心已經有骨架
- 若干垂直能力已經打通
- 系統開始具備受治理代理平台的輪廓
- 但還沒有完全成為穩定、可長期運營的產品級控制平面

## 2. 核心理念

### 2.1 分工原則

目前整體設計的核心不是「做一個很聰明的單一代理」，而是把角色拆開：

1. 高階入口模型  
   負責與人互動、理解需求、澄清問題、決定是否要查詢、是否要建立任務、是否要升級為可執行工作。

2. 中介核心 / broker  
   不負責聰明判斷，不負責替模型做主。它負責把行為收斂到可驗證、可記錄、可重現：
   - 驗證語法
   - 驗證狀態機
   - 驗證 capability / scope
   - 產生 execution intent
   - 轉發到真正的工具與執行面

3. 執行代理 / worker / tool  
   負責執行已被核准的事情，不負責自行重新定義任務意圖。

這個方向是對的。真正錯的做法是把所有理解、判斷、授權、執行都塞進同一個代理裡，那只會讓系統難以治理。

### 2.2 為什麼高階模型存在

高階模型存在的意義，不是把使用者原句原封不動丟給工具，再把結果原封不動貼回來。那樣它只是昂貴的轉接器。

高階模型的合理職責是：

- 判斷問題類型
- 決定應該走哪一類工具或來源
- 改寫查詢
- 從工具結果中抽取重點
- 把資料重新組織成可理解的回覆
- 在不夠明確時要求澄清

也就是說：

高階模型負責認知與規劃  
中介核心負責約束與執行  
工具負責取資料與做事

這個邊界如果守不住，整個系統最後一定會再次退化成「一個會亂動世界的聊天機器人」。

### 2.3 對隨機性的態度

這個專案目前最重要的理念之一，是壓低隨機性。

正確做法不是相信模型會「大致上聰明」，而是要求：

- 行為要可預測
- 升格條件要可審查
- 請求格式要明確
- 執行路徑要可重現

這也是為什麼後續加入：

- command parser
- workflow state machine
- trust / taint gate
- interaction log / interpretation / memory / execution intent 分層

這些東西不是工程潔癖，而是防止系統被自然語言和外部資料拖進不可控狀態的必要代價。

## 3. 目前架構總覽

### 3.1 LINE 入口主路徑

目前 canonical path 是：

`LINE webhook -> line-worker -> broker /api/v1/high-level/line/process -> HighLevelCoordinator`

重點：

- `line-worker` 只是 ingress bridge，不做高階決策
- 真正的對話、澄清、分流、指令解析都在 broker 高階層
- 對外主入口已經不再是 agent 直接吃 LINE

相關檔案：

- [InboundDispatcher.cs](/d:/Bricks4Agent/packages/csharp/workers/line-worker/InboundDispatcher.cs)
- [HighLevelEndpoints.cs](/d:/Bricks4Agent/packages/csharp/broker/Endpoints/HighLevelEndpoints.cs)
- [HighLevelCoordinator.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelCoordinator.cs)

### 3.2 Sidecar 拓樸

目前本機 sidecar 是：

- broker：`127.0.0.1:5361`
- line-worker webhook：`127.0.0.1:5357`
- public ingress：ngrok

操作入口：

- [line-sidecar.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/line-sidecar.ps1)
- [start-sidecar-stack.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/start-sidecar-stack.ps1)
- [status-sidecar-stack.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/status-sidecar-stack.ps1)
- [stop-sidecar-stack.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/stop-sidecar-stack.ps1)

這個設計的優點是：

- 單指令操作
- 多程序分工
- 未來仍可拆到不同機器

這個設計的缺點是：

- 還依賴本機 ngrok 與本機 sidecar 存活
- 不屬於正式生產部署拓樸
- 現在仍偏向運維方便，不是正式雲端治理形態

## 4. 高階入口層

### 4.1 目前使用的高階模型

目前 LINE 高階回應模型預設是：

- provider：`openai-compatible`
- model：`gpt-5.4-mini`

這層負責：

- 一般對話
- 查詢結果綜整
- 需求澄清
- execution model request 規劃

不是 execution LLM，也不是 deployment/browser worker 本身的模型。

相關檔案：

- [LineChatGateway.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/LineChatGateway.cs)
- [appsettings.json](/d:/Bricks4Agent/packages/csharp/broker/appsettings.json)

### 4.2 指令語法與 workflow gate

高階入口不是直接看自然語言就執行，而是先進入明確語法：

- `?help` / `?h`
- `?search` / `?s`
- `?rail` / `?r`
- `?hsr` / `?thsr`
- `?bus` / `?b`
- `?flight` / `?f`
- `?profile` / `?p`
- `/name` / `/n`
- `/id` / `/i`
- `#專案名稱`
- `confirm`
- `cancel`

然後再經過 workflow state machine。

這層的價值在於：

- 明確命令才有明確行為
- 一般對話不會直接升格成執行
- command 與 data 的邊界開始出現

相關檔案：

- [HighLevelCommandParser.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelCommandParser.cs)
- [HighLevelWorkflowStateMachine.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelWorkflowStateMachine.cs)

### 4.3 信任與污染邊界

目前高階入口已經開始防 prompt injection 類問題，至少做到：

- 區分 raw user input、external content、decoded/transformed content
- 不讓 transformed content 直接升格成 command
- command path 受 source / taint gate 限制

這只是起點，不是終點。現況仍然沒有做到完整的：

- nested instruction grammar
- transform-triggered escalation 全面封堵
- 外部搜尋與 RAG 回填的全鏈路污染治理

所以這一塊現在是「方向正確」，不是「已經安全」。

相關檔案：

- [HighLevelInputTrustPolicy.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelInputTrustPolicy.cs)

### 4.4 記錄、記憶、執行意圖分層

目前高階層已開始把不同資料責任拆開：

- raw interaction log
- interpretation record
- memory projection
- execution intent

這個方向正確，因為：

- log 應保存真相
- memory 應保存未來可調用狀態
- execution 應保存可執行意圖

但要說得不討好一點：  
現在這套分層還不是成熟的 memory architecture，比較像是從混在一起的流程中硬拉出邊界。它已經比以前好很多，但離穩定可長期維護的狀態仍有距離。

相關檔案：

- [HighLevelInteractionRecorder.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelInteractionRecorder.cs)
- [HighLevelInterpretationStore.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelInterpretationStore.cs)
- [HighLevelMemoryStore.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelMemoryStore.cs)
- [HighLevelExecutionIntentStore.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelExecutionIntentStore.cs)
- [HighLevelExecutionPromotionGate.cs](/d:/Bricks4Agent/packages/csharp/broker/Services/HighLevelExecutionPromotionGate.cs)

## 5. 使用者與工作區模型

### 5.1 LINE 使用者

目前 LINE 使用者是以：

- `channel + userId`

自動建立高階 profile。  
並支援：

- 顯示稱呼
- 英數字 ID
- per-user 高階權限
- 註冊狀態
- 測試帳戶 / 真實 LINE 使用者標記

### 5.2 Managed Workspace

目前使用者專屬工作區根目錄是絕對路徑：

`%LOCALAPPDATA%\Bricks4Agent\managed-workspaces`

目錄模型：

- `{AccessRoot}/{channel}/{userId}/conversations`
- `{AccessRoot}/{channel}/{userId}/documents`
- `{AccessRoot}/{channel}/{userId}/projects/{projectName}`

這是目前做得對的一塊。至少「產物放哪裡」這個常被忽略的問題，已經不再是模糊地帶。

但這塊還沒完全成熟，因為：

- retention policy 還沒定
- 清理策略還沒定
- documents / projects / future artifacts 的生命周期還沒統一

## 6. 查詢能力現況

### 6.1 查詢不再只是貼搜尋結果

目前顯式查詢會走 broker mediated retrieval，再由高階模型綜整回覆。這比以前只貼網址好。

已支援：

- `?search`
- `?rail`
- `?hsr`
- `?bus`
- `?flight`

### 6.2 交通工具查詢

目前交通查詢已拆開：

- `?rail`：台鐵
- `?hsr` / `?thsr`：高鐵
- `?bus`：公車 / 客運類
- `?flight`：航班

這一步是必要修正。先前把高鐵和台鐵混成一個 `rail`，本質上就是工具設計偷懶，不是可接受的抽象化。

### 6.3 不討好的評論

查詢這條路目前的問題很明顯：

- 還太依賴搜尋引擎
- relation-aware query routing 只是起步
- 高階模型雖然已開始綜整結果，但很多時候仍只是「整理過的結果列表」
- 對需要關係推理的問題，例如行政區、鄰接關係、主詞歧義，品質仍不穩

所以現況不能說成「查詢能力已成熟」。  
更準確的是：

- 已從純搜尋結果貼文，進步到有來源路由與綜整
- 但離真正可靠的知識型助理還差很多

## 7. Browser 能力治理

### 7.1 身份來源分層

目前 browser 能力已經不是單一模糊概念，而是先以身份來源切分：

- `anonymous`
- `system_account`
- `user_delegated`

這個切法是對的，而且應該維持。因為 browser 自動化最先要分清楚的不是功能，而是「用誰的身份在做事」。

### 7.2 目前已落地的內容

已完成：

- browser tool spec registry
- request builder
- site binding / lease / user grant / system binding 資料模型
- validation
- preview path

已經不是空設計稿。

### 7.3 還沒做完的內容

還沒完成：

- 真正可長駐的 browser worker runtime
- delegated authenticated browser actions
- DOM/action policy engine
- user delegated consent lifecycle
- credential vault / secret hardening

這代表 browser 目前是：

- 治理模型比執行模型完整

這並不糟，但要誠實承認：  
現在比較強的是 policy 與 contract，不是 end-to-end browser automation。

## 8. Azure VM IIS 部署

### 8.1 已完成

目前已具備 broker-governed Azure VM IIS deployment：

- deployment target registry
- request builder
- preview
- execute
- WinRM / PowerShell remoting
- `site_root`
- `iis_application`

且已補 child application 模式：

- `deployment_mode = iis_application`
- `application_path`
- `health_check_path`

### 8.2 還缺什麼

還缺：

- 真實 Azure VM live deployment 實測
- path-base 自動化
- release / rollback
- deploy 後 health check 一致化

所以現在這條路線屬於：

- 技術上已可執行
- 產品上仍未完整閉環

## 9. Google Drive 交付

### 9.1 service account 路線的結論

這條路已經被實測證明：

- 對個人 Google 帳號的 My Drive，不適合用 service account 當主要交付方式
- 問題不是程式寫錯，而是 Google 的 quota / ownership 模型就是如此

這個結論很重要，因為它避免了後續在錯路上浪費更多時間。

### 9.2 OAuth delegated 路線

目前已打通：

- Google OAuth delegated authorization
- refresh token 入庫
- access token refresh
- delegated upload 到使用者個人 Google Drive
- 產生分享連結

### 9.3 Artifact delivery

目前已能：

1. 在使用者 `documents_root` 內產生 UTF-8 檔案
2. 上傳到 delegated Google Drive
3. 產生分享連結
4. 建立 LINE 通知 payload

這條線已經不是設計，而是實際可跑。

### 9.4 不討好的評論

這條線現在最大的問題不是 Drive，而是「最後一公里」：

- 若使用者不是有效 LINE `U...` id，通知就送不出去
- 目前 live 成功驗證的是 synthetic user 的交付，不是真實 LINE 使用者的完整到達

所以這條能力現在應該描述成：

- 交付鏈已技術上打通
- 真實使用者端的完整交付還需要用真實 LINE 帳戶做最後驗證

## 10. 統一後台

### 10.1 入口

目前本機統一後台入口：

`http://127.0.0.1:5361/line-admin.html`

### 10.2 已整合內容

目前這個後台已整合：

- LINE 使用者列表
- 註冊政策
- 使用者審核
- per-user 高階權限
- Browser bindings / grants / leases
- Deployment targets
- Tool specs
- Google Drive OAuth / delegated credential / artifact delivery

這已經不只是示意頁，而是真正可操作。

### 10.3 安全現況

目前：

- 僅允許 localhost
- 有本機 admin login
- 若資料庫沒有密碼資料，初始密碼為 `admin`
- 首次登入要求改密碼

但這仍是本機管理後台，不是正式遠端多管理員控制台。

## 11. 測試與驗證現況

目前重要 build / verify 路徑有持續驗證：

- `dotnet build packages/csharp/broker/Broker.csproj -c Release --disable-build-servers -nodeReuse:false`
- `dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj`

live 驗證已覆蓋：

- LINE webhook ingress
- sidecar broker / line-worker
- 高階對話
- production draft / confirm
- Google Drive delegated callback
- delegated upload
- 後台 UI smoke

### 不討好的評價

測試比過去完整很多，但還是不夠漂亮。

現在的驗證特色是：

- 核心路線有 build 與 verify
- 很多關鍵功能有 live smoke
- 但仍偏向「把重要主線跑通」

還不是那種：

- 具有大規模 regression grid
- 有清楚 fixture 管理
- 各子系統獨立可重放

所以現在的測試水平應該評為：

- 已脫離純 smoke
- 但還沒有進入成熟產品的完整測試工程

## 12. 總體評價

### 12.1 做對的地方

目前這個專案真正做對的地方有幾個：

- 沒把整個系統做成單一萬能代理
- 開始把高階理解、控制核心、執行面分層
- 已重視 capability / scope / workflow gate
- 已把使用者工作區、檔案交付、後台管理這些實務問題拉進來
- 對 browser、deployment、delivery 這些高風險能力，開始採用治理優先，而不是「先能動再說」

### 12.2 還在自欺的地方

也有幾個地方不能粉飾：

1. 很多東西已「可用」，但還沒有「穩定」
   可用和穩定不是同義詞。現在不少能力處在可用但脆弱的狀態。

2. browser 治理模型比 runtime 完整
   這是現實，不是羞恥。但若長期停在這裡，會變成設計文件先進、產品能力滯後。

3. query 綜整已進步，但還不是強知識系統
   目前還不足以宣稱已具備高品質知識助理能力。

4. deployment 能力還沒經過真實 Azure VM 長鏈驗證
   現在只能說具備部署引擎，不該說已是穩定部署產品。

5. Google Drive 交付已技術打通，但還沒完成真實 LINE 使用者閉環

### 12.3 總結

這個系統現在最準確的定位不是：

- 已完成的智能代理平台

而是：

- 已具備明確方向、核心控制面開始成形、數條關鍵能力已打通的受治理代理平台原型

這個評價不討喜，但比較接近真相。

## 13. 下一步建議

1. 用真實 LINE 使用者完成 Google Drive delegated artifact delivery 閉環  
2. 補 deployment live Azure VM 驗證與 health check  
3. 補 browser runtime skeleton，先做匿名 / read-only / public-open  
4. 繼續把 relation-aware query routing 往主詞-關係抽取推進  
5. 開始整理由多個 POC 路徑累積出的後台與資料模型債務

## 14. 參考文件

- [HighLevelModelRoutingAndMemory.md](/d:/Bricks4Agent/docs/designs/HighLevelModelRoutingAndMemory.md)
- [HighLevelMemoryAndLoggingModel.md](/d:/Bricks4Agent/docs/designs/HighLevelMemoryAndLoggingModel.md)
- [ToolSpecRegistry.md](/d:/Bricks4Agent/docs/designs/ToolSpecRegistry.md)
- [BrowserCapabilityIdentityModel.md](/d:/Bricks4Agent/docs/designs/BrowserCapabilityIdentityModel.md)
- [BrowserBindingAndLeaseModel.md](/d:/Bricks4Agent/docs/designs/BrowserBindingAndLeaseModel.md)
- [BrowserRuntimeContract.md](/d:/Bricks4Agent/docs/designs/BrowserRuntimeContract.md)
- [AzureVmIisDeployment.md](/d:/Bricks4Agent/docs/designs/AzureVmIisDeployment.md)
- [GoogleDriveDelivery.md](/d:/Bricks4Agent/docs/designs/GoogleDriveDelivery.md)
- [SystemTestReport-2026-03-22.md](/d:/Bricks4Agent/docs/reports/SystemTestReport-2026-03-22.md)
