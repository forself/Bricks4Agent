# 新增 worker 容器設計草案(擴大功能跨度 + 撐起監督/failover 價值)

2026-05-30。目標:加跨度大、各展示不同治理面向、failure mode 不同的 worker,讓「階梯式維護(自癒/監督/failover)」有多元容器可管 → 撐起「治理平台」報告論點(非交易 bot)。

## worker 框架(既有、照抄即可)

- 一個 worker = `WorkerHostOptions`(broker host/port、workerId、workerType、auth)+ 註冊 1+ 個 `ICapabilityHandler` + `host.RunAsync()`。
- `ICapabilityHandler`:`CapabilityId`(如 `file.read`)+ `ExecuteAsync(requestId, route, payload, scope, ct) → (success, resultPayload, error)`。
- 上架:加 `ContainerConfig.WorkerImages` 條目(image/mem/cpu limit)→ broker 可 spawn + 納管;能力註冊進 capability catalog(ACL/quota/approval 治理)。
- **純加新檔 + 用 Benson 框架,不動他核心。**

## 既有能力(別重複)
file.*(讀寫列刪搜)、transport.query(TDX)、browser.read(抓網頁)、quote/strategy/risk/trading/line.*。
→ web-fetch 與 browser.read 重疊、不做;改用下面三個。

---

## ① Code-exec 沙箱 worker 🥇(報告最亮)

**能力**:`code.exec`(route: `python` / `bash`)
**intent**:`{ "language":"python", "code":"...", "timeout_s":10, "stdin":"" }` → `{ "stdout","stderr","exit_code","duration_ms","truncated" }`
**執行**:worker 收到 → spawn **拋棄式沙箱**(每次一個,跑完即刪):non-root、`--network none`、read-only rootfs + tmpfs workdir、`--memory 256m --cpus 0.5 --pids-limit 64`、`--cap-drop ALL`、硬 timeout kill。語言先支援 Python。
**治理掛點**:
- **approval gate**:`code.exec` 列入需審核能力(IApprovalService chain)→ 跑任意碼前要人/政策核准 ← **危險能力在治理下,核心展示**
- capability_grant ACL + quota(誰能跑、一天幾次)
- 完整 audit(誰、跑什麼碼、結果)
**failure mode → 監督**:無窮迴圈→timeout kill;OOM→mem limit kill;fork bomb→pids limit;外連竊取→network none 擋;逃逸→cap-drop+non-root。worker 本體若 hang → host watchdog 重啟。
**工程量**:中(沙箱 spawn + 限制 + 清理是重點;Python 先行)。
**報告**:「平台能安全地把『任意程式碼執行』這種最危險的能力,放在 approval+capability+audit+sandbox 治理下」——最強的治理 demo。

---

## ② 文件 / RAG worker 🥈(平台 AI operations 核心)

**能力**:`rag.ingest`(文件→chunk→embed→存)、`rag.query`(語意檢索 + 可選 LLM 摘要)
**intent**:ingest `{ "artifact_id" or "text", "tags":[] }`;query `{ "q":"...", "top_k":5, "summarize":true }` → `{ "hits":[{score,excerpt,source}], "summary":"..." }`
**執行**:reuse broker 既有 `EmbeddingService` / `VectorEntry`(向量表已存在)/ `LlmProxyService` / `RagPipelineService`;worker 負責 chunk + 呼叫 embed + 存 VectorEntry + 檢索 + 經 LLM proxy 摘要。可串你建的 **artifact 下載 API**(ingest 已存的 artifact)。
**治理掛點**:LLM proxy 已在治理下(quota/audit);capability_grant 控誰能查;敏感文件可加 approval。
**failure mode → 監督**:**依賴 LLM/embedding upstream**——你踩過「LLM 502 污染 worker connection」([[feedback_llm_exception_corrupts_worker_connection]])→ 正好示範 worker 對上游故障的退避/熔斷 + 監督層偵測卡死重啟。
**工程量**:中(broker 已有 embedding/RAG 元件,worker 是驅動 + chunk)。
**報告**:平台真正的「AI operations」——治理化的知識檢索/摘要,不只交易。

---

## ③ 媒體 / 轉錄 worker 🥉(多模態 + 重資源治理)

**能力**:`media.transcribe`(audio→text,Whisper)、(可選 `media.describe` 影像→文字)
**intent**:`{ "artifact_id" or "url", "lang":"auto" }` → `{ "text","segments":[{start,end,text}],"duration_ms" }`
**執行**:容器內跑 faster-whisper(tiny/base 模型,~幾百 MB);CPU 推論。**資源 profile 跟其他 worker 完全不同**(重、慢、模型大)。
**治理掛點**:capability_grant + quota(重運算要限);高 mem/cpu limit + 長 timeout(跟輕 worker 不同的資源治理)。
**failure mode → 監督**:大檔→慢/OOM→mem limit + timeout;模型載入慢→start_period 要長。**不同資源 profile 讓監督/autoscale 層更有看頭**(展示「不同容器不同資源治理」)。
**工程量**:中-高(模型 + 推論;或先接外部 API 但較不「自託管治理」)。
**報告**:多模態 + 異質資源治理——展示平台不是單一工作型態。

---

## 建議

- **先做 ① Code-exec 當「worker 範本」**:治理 demo 最強(approval+capability+audit+sandbox 全踩到)、failure mode 最硬核(hang/OOM/逃逸)、最能撐 failover/監督的價值。做紮實後 ②③ 照範本複製就快。
- 三個跨度:**compute / 知識AI / 多模態**——加上既有(檔案/交通/瀏覽器/金融),平台變成「多元能力的受治理容器生態」,你的監督+failover 層才「有多元容器可管」、報告論點才立得住。
- 之後再回 **Phase 2(broker lease-gating failover)**——那時平台已夠多元,failover 的價值才完整展示。
