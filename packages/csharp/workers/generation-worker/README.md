# generation-worker

受治理生成（governed generation）的執行節點。broker 把三個生成能力派給它；它以 Node 執行 B4A 的確定性生成器（`tools/generation/cli.mjs`），用元件庫產出 DefinitionTemplate 驅動的多頁前端原型，並打包成 zip。

它只做確定性工作：不呼叫 LLM、不連外、不讀寫輸出根目錄以外的位置。撰寫定義的是 broker 依任務啟動的受控代理；代理碰不到這個 worker，只有 broker 會派工給它。

## 能力

| 能力 | route（代理工具名） | 行為 |
|---|---|---|
| `generation.catalog.query` | `query_component_catalog` | 轉呼叫 CLI 的 `catalog`，原樣回傳 JSON（型錄摘要的 overview、field_types、example、component 四節） |
| `generation.definition.validate` | `validate_definition` | 轉呼叫 CLI 的 `validate`，原樣回傳 JSON；定義不通過時仍是成功的呼叫（`ok:false` 加結構化 errors），代理依錯誤修正。結果超過 `MaxResultBytes` 時改回前幾筆錯誤與警告、`total_errors` 與 `truncated: true`（仍是成功的呼叫）。grant scope 帶 `max_pages` 時，選取的頁數超過它也回 `ok:false` 與 `MAX_PAGES_EXCEEDED` |
| `generation.scaffold.generate` | `generate_scaffold` | CLI 的 `build`（先跑與 validate 相同的驗證）寫到工作目錄，再由 C# 以決定性方式打包成 zip |

三個能力的定義以 `packages/csharp/broker/tool-specs/generation.*/tool.json` 為唯一來源，broker 啟動時同步成 capability。三個 route 都不在 broker 的程序內降級清單中：沒有這個 worker 時請求直接失敗，不會在 broker 程序內生成。

## 輸出位置、冪等與回傳內容

- 輸出位置**只取自 grant scope**（broker 寫入）：`{Generation:OutputRoot}/{output_slot}/{requestId}/`。scope 必須含 `output_slot`、`package_name`（兩者只能是英數、底線、連字號，1～80 字）、`max_pages`（1～100 的整數）與 `package: "definition-site-v1"`，缺少或格式不符就拒絕。請求參數中任何路徑類欄位一律不採用。
- 冪等：同一 requestId 的 `result.json` 已存在，且 zip 的 sha256 與大小相符時，直接回傳同一結果（broker 逾時重派不會重複生成）；否則清空該 requestId 目錄後重做。worker 一次只處理一個請求（`MaxConcurrent=1`），同一請求的並行重派會等前一次完成後取用其結果。
- 定義不通過：回傳失敗，錯誤訊息是 `{"ok":false,"errors":[...]}`，不留下任何檔案。頁數超過 scope 的 `max_pages` 也以同樣格式回報（`MAX_PAGES_EXCEEDED`）。
- zip：`{package_name}-scaffold.zip`，頂層只有 `site/` 與 `report/`；條目依路徑排序、固定時間戳與權限位元，同樣的內容永遠得到同樣的 sha256。生成器輸出含符號連結、未回報的檔案、頂層多出其他項目，或 report 中出現本機路徑時都拒絕。
- 回傳 payload 不含檔案內容與主機路徑：

```json
{
  "output_slot": "task_…",
  "request_id": "req_…",
  "zip": { "path": "<output_slot>/<requestId>/<package_name>-scaffold.zip", "sha256": "…", "size": 0 },
  "pages": [{ "id": "…", "type": "list", "field_count": 0 }],
  "file_count": 0,
  "validation_digest": "…",
  "generator_version": "…",
  "catalog_sha256": "…"
}
```

`zip.path` 相對於輸出根目錄。broker 端依自己的 `Generation:OutputRoot` 解析，驗證路徑與 sha256 後才交付給使用者。

## 子程序

CLI 以 `node <ToolsRoot>/tools/generation/cli.mjs <command>` 執行：不經 shell、參數陣列、輸入只經 stdin、有逾時（catalog／validate 30 秒、build 120 秒）、stdout 超過上限即終止子程序並視為失敗、stderr 只寫進日誌。子程序的環境變數會移除 worker 憑證與其他密鑰類變數，以及 `NODE_OPTIONS`、`NODE_PATH`。

## 設定

| 鍵 | 說明 |
|---|---|
| `Generation:ToolsRoot` | 含 `tools/generation/cli.mjs` 的 repo 子集根目錄（必填；啟動時檢查） |
| `Generation:OutputRoot` | 產物根目錄（必填，絕對路徑）；broker 端要指向同一份內容 |
| `Generation:NodePath` | node 執行檔；未設時用環境變數 `B4A_NODE_PATH`（檔案存在時），再退到 PATH 上的 `node` |
| `Generation:QueryTimeoutSeconds` | catalog／validate 逾時，預設 30 |
| `Generation:BuildTimeoutSeconds` | build 逾時，預設 120 |
| `Generation:MaxStdoutBytes` | CLI 輸出上限，預設 4 MiB |
| `Worker:Auth:*` | worker 憑證（WorkerType `generation-worker`） |

設定也可用 `WORKER_` 前綴的環境變數提供，例如 `WORKER_Generation__OutputRoot`。

## 部署

- compose 測試堆疊：`generation-worker` 服務只在 `--profile generation` 時啟動，只接 internal 的 `generation-net`（成員只有 broker 與這個 worker），寫入 `generation-out` volume，broker 以唯讀方式掛同一個 volume 在 `/generation-out`。憑證由 `gen-stack-secrets.mjs` 產生（`GENERATION_WORKER_AUTH_KEY_ID`、`GENERATION_WORKER_AUTH_SHARED_SECRET`）。見 [tools/agent/container/README.md](../../../../tools/agent/container/README.md)。
- 映像：`Containerfile` 以 sdk:10.0 建置、從 node:22-bookworm-slim 只取 node 執行檔、最終為 aspnet:10.0，三者都以 digest 釘選；只複製生成器需要的 repo 子集（元件庫排除 data、refresource、示範頁與測試），建置時產生型錄摘要；以 uid 10008 執行，不宣告 VOLUME。
- Windows sidecar：`start-sidecar-stack.ps1 -GenerationMode Governed` 會發布並以主機程序啟動這個 worker，並把 broker 切到 `HighLevelCoordinator:Generation:SystemScaffoldMode=Governed`；兩者共用 sidecar 資料目錄下的 `generation-out`（在各使用者工作區之外）。預設（`Legacy`）行為不變。
- 手動啟動：`run-worker.ps1 -Worker generation`（可加 `-GenerationOutputRoot`）。

## 測試

```bash
# handler、scope、冪等、zip 決定性、逾時與輸出過大、frame 讀寫（以測試用的假 CLI 執行真正的 node 子程序）
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "FullyQualifiedName~Generation|FullyQualifiedName~Workers.Sdk"
# compose 接線、映像與 sidecar 的靜態檢查
npm run validate:agent-container-config
# 代理工具與 system prompt
npm run validate:agent-governed
```
