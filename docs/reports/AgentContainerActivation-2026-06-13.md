# 受控代理容器啟用計畫與驗證

Date: 2026-06-13
Status: in progress
依據規格: [ControlledAutonomousAISystemTechnicalDesign.md](../designs/ControlledAutonomousAISystemTechnicalDesign.md)

## 1. 目的與「完成」的定義

設計規格 §6.6 定義 Agent 容器是「受控主體執行殼層」:它**只能**向控制平面請領工作、讀授權上下文、呼叫模型、產生結構化執行請求、回報結果;**不可**直接碰工具、資料源、倉庫、部署、模型供應商,或自行發放權限。§21 列出最小成功判準。

本專案的 agent runtime 程式碼齊全(`tools/agent`,約 4639 行:planner/state-machine、governed-executor、broker-client、tool-registry、safety),broker 端也有 capability catalog、scoped token、worker auth、audit、shared context。**但這條受控代理容器的端到端從未通電驗證過**——這份計畫的目標就是讓它第一次通電並通過治理驗證。

**「完成」的可驗證定義**:既有的 `tools/agent/tests/test-podman-governed-stack.js` 端到端測試通過(綠燈),即:

> 用 podman build 出 broker + mock-ollama + file-worker + line-worker + **agent** 五個容器映像,`podman compose up` 整個受控 stack,斷言 agent 容器輸出包含 `STACK_OK` 與 `[governed] read_file`——也就是 **agent 容器起來後,透過 broker 領到 capability、產生結構化請求、由控制平面裁決後執行了一個 governed 的 `read_file` 工具**,而非容器自己直連工具。

這正是 §21 判準「人類與 AI 走同一授權路徑」「容器被入侵也無法直接存取外部工具」的最小可驗證形式。

## 2. 現況評估(2026-06-13)

| 項目 | 狀態 | 證據 |
|------|------|------|
| agent runtime 程式碼 | ✅ 齊全 | `tools/agent` 4639 行;governed-executor 661、broker-client 307、tool-registry 578、state-machine 558 |
| podman 可用性 | ⚠️ 需手動啟動 | podman 5.8.2 已裝,但 machine `LAST UP: Never`——本機從未跑過任何容器(已於本次啟動) |
| FunctionPool(容器 spawn 子系統) | ⚠️ 預設關 | broker `appsettings.json` `FunctionPool.Enabled = false` |
| broker 容器映像 build | ❌ 失敗 | `NETSDK1152`:`broker.csproj` 引用 `site-crawler-worker`,worker 的 `appsettings.json` 與 broker 的同名,publish 輸出衝突 |
| governed stack e2e | ❌ 從未通過 | 卡在 broker 映像 build,連 stack 都起不來 |

### 2.1 第一阻擋的根因

`packages/csharp/broker/Broker.csproj` 引用了 `..\workers\site-crawler-worker\SiteCrawlerWorker.csproj`。這不是誤加——broker 的 `HighLevelSiteRebuildService.cs` 直接 `using SiteCrawlerWorker.Models / .Services`(Benson 的 site rebuild 服務消費了 worker 的 deterministic extractor 與 contracts)。

後果:worker 的 `appsettings.json`(標 `CopyToOutputDirectory=PreserveNewest`)透過 ProjectReference 流入 broker 的 publish 輸出,與 broker 自己的 `appsettings.json` 相對路徑相同 → `NETSDK1152`。

這同時違反規格 §3.2/§6.6 的架構原則(控制平面不應依賴 worker 實作)。修法見 §4。

## 3. 能力檢核表(對照規格,標示需在 e2e 驗證的項)

### 3.1 §6.6 Agent 容器「可做」
| 能力 | runtime 實作 | e2e 驗證 |
|------|------|------|
| 請領工作項 / 連控制平面 | broker-client.js | 待 e2e |
| 讀授權上下文 | broker-client + shared context | 待 e2e |
| 呼叫模型推理 | ollama/openai client(stack 用 mock-ollama) | 待 e2e |
| 產生結構化執行請求 | governed-executor.js | **e2e 核心斷言** |
| 回報進度/結果 | state-machine | 待 e2e |

### 3.2 §6.6 Agent 容器「不可做」(治理邊界)
| 禁止項 | 機制 | e2e 驗證 |
|------|------|------|
| 直接碰工具 | 工具執行走 broker 裁決,非容器直連 | `[governed] read_file` 證明走治理路徑 |
| 自行發放權限 | capability 由 broker 核發 scoped token | 待 e2e |
| 直連模型供應商 | stack 內只連 mock-ollama;§13 網路隔離 | 後續強化(見 §6) |

### 3.3 §13 容器安全基線(現有 Containerfile 對照)
| 基線 | agent Containerfile 現況 |
|------|------|
| non-root user | ✅ `USER agent`(uid 10001) |
| read-only rootfs | ⚠️ 未設(後續強化) |
| cap-drop=ALL / no-new-privileges | ⚠️ 未設(後續強化) |
| 不持長期 key、只持短時效 session | ✅ 設計如此(token 由 broker 發) |

## 4. 工作項與修法

1. **修 broker 容器 build 衝突(NETSDK1152)** — 必做,阻擋一切。
   - 方案 A(最小):在 broker 對 site-crawler-worker 的 ProjectReference 阻止 content 流入 publish(`<ExcludeAssets>contentFiles</ExcludeAssets>` 或 worker 的 appsettings 改 `CopyToPublishDirectory=Never`)。
   - 方案 B(較正規,符合 §3.2):把 broker 用到的 SiteCrawler 型別(Models/contracts + extractor)下沉到共用層,broker 不再引用 worker 專案。
   - 本次採可解 build 且風險最低者,並記錄取捨。
2. **重跑 governed stack e2e**,逐一解後續 build/compose/stack 問題直到綠。
3. **依本文件 §5 驗收條件確認**,把結果填回 §7。

## 5. 驗收條件(依此驗證)

- [ ] V1:五個容器映像(broker/mock-ollama/file-worker/line-worker/agent)全部 build 成功
- [ ] V2:`podman compose up` 整個受控 stack 起來,broker 健康
- [ ] V3:agent 容器註冊 session、領 capability、產生結構化執行請求(STACK_OK)
- [ ] V4:agent 透過 broker 裁決執行 governed `read_file`(`[governed] read_file`)——對應 §21「AI 走同一授權路徑、不直連工具」
- [ ] V5:`test-podman-governed-stack.js` 整體綠燈
- [ ] V6:既有單元測試(337)不被修復破壞;solution build 0 錯誤
- [ ] V7:compose down 清理乾淨,無殘留容器/測試 db

## 6. 明確劃為「後續」的(本次不做,記錄以免誤判完成)

對照規格但超出「首次通電」範圍、留待後續:
- §13 完整容器安全 hardening(read-only rootfs、cap-drop=ALL、seccomp、tmpfs)
- §13.1 嚴格網路隔離(容器只能連控制平面、禁直接對外)
- 審批服務 / 風險分級(§18.2 第二階段)
- repo-adapter / build-test-adapter 兩個執行配接器(§18.1 列為 MVP,但本次聚焦「代理容器通電 + governed 工具執行」這一條最小鏈)
- FunctionPool 在 sidecar 生產路徑的預設啟用策略

## 7. 執行紀錄與驗證結果

「通電」過程逐一暴露了三個單元測試抓不到、只有真正跑容器 stack 才會現形的整合 bug:

### Bug A:broker 容器 build 失敗(NETSDK1152)
- 根因:`broker.csproj` 引用 `site-crawler-worker`(`HighLevelSiteRebuildService` 就地用其 extractor + Playwright),worker 的 `appsettings.json` 流入 broker publish 與 broker 自己的同名衝突。
- 修法:broker.csproj 加 `RemoveTransitiveWorkerSettingsFromPublish` target,publish 時移除來自 worker 的重複設定。下沉型別成本過高(會把 Playwright 汙染進 broker-core)故不採。
- 驗證:本機 `dotnet publish broker` 無 NETSDK1152,broker 拿到自己的 appsettings(含 HighLevelLlm)。✅

### Bug B:監控 HealthScoreService 在 FunctionPool 關閉時無法啟動(我整合監控時引入的回歸)
- 根因:`HealthScoreService`(從 AnthonyLee 擷取)依賴 `IWorkerRegistry`(FunctionPool),但無條件註冊;FunctionPool 關閉時 `IWorkerRegistry` 不存在 → broker 啟動 DI activation 失敗。**預設設定(`FunctionPool:Enabled=false`)的 broker 因此完全起不來**。agent 1 在 worktree 只 build 沒 run 故漏掉。
- 修法:HealthScore 子系統(LeaderGuard/HealthScoreService/SnapshotService)只在 FunctionPool 啟用時註冊(它本質是監控 worker pool);LLM metrics(不依賴 pool)保持無條件。
- 驗證:本機起 broker(FunctionPool=false)正常啟動。✅

### Bug C:register 時 `Get<BrokerTask>` 在 Linux 容器丟「No data exists for the row/column」(500)
- 定位過程:本機(Windows)無論帶大 RuntimeDescriptor、FunctionPool=true、雙啟動都**無法重現**;把容器建的 broker.db 複製出來、本機讀仍正常 → 排除 schema/data,確認是 **Microsoft.Data.Sqlite 在 Linux 的 `IsDBNull(index)` 行為**(BrokerTask 欄位比 Principal 多才觸發,故 register 在 `Get<BrokerTask>` 那行才炸)。
- 修法:`BaseOrm.MapToObject` 不再用 `IsDBNull(index)`,改用 `GetValue(index)`(NULL → `DBNull.Value`,跨平台一致的 ADO.NET 契約)後判斷。
- 驗證:337 單元測試全綠(改動不破壞既有查詢);Linux 容器最終效果由 governed stack e2e 確認(進行中)。

### 最終 e2e 驗證 ✅ 全部通過(2026-06-13)

`node tools/agent/tests/test-podman-governed-stack.js` → exit 0,`Podman governed stack integration test passed.`

| 驗收條件 | 結果 | 證據 |
|------|------|------|
| V1 五映像 build | ✅ | broker/mock-ollama/file-worker/line-worker/agent 全 `Successfully tagged` |
| V2 stack 起來 | ✅ | broker tool-spec sync(inserted=15)、agent 連上 broker |
| V3 領 capability + STACK_OK | ✅ | `[Governed] session=ses_019EBDD3F462...` → `STACK_OK` |
| V4 governed 工具執行 | ✅ | `[governed] read_file(path: "README.md")`——agent 不直連工具,經 broker 裁決 |
| V5 e2e 整體綠 | ✅ | exit 0,integration test passed,無 500、無 No data exists |
| V6 unit test + build | ✅ | 337 unit test 綠,solution build 0 錯誤 |
| V7 清理 | ✅ | e2e finally 段 `podman compose down` |

**結論**:受控代理容器這條 e2e 第一次通電並通過治理驗證。對照規格 §21 最小成功判準的可驗證項——「人類與 AI 走同一授權路徑」「容器不直接碰工具、經控制平面裁決」——成立。§6 中明列「後續」的容器安全 hardening、網路隔離、執行配接器、審批服務仍未做,本次範圍止於「通電 + governed 工具執行」。

