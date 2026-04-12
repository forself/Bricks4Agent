# Crawler Worker 與 Website Analysis Agent 設計

日期：2026-04-12  
狀態：draft  
範圍：公開網站重製用途的站點爬取、站點地圖與內容盤點

## 1. 目的

為 broker-governed agent 架構補上兩個能力：

- `crawler-worker`
  - 無 AI
  - 對公開網站做確定性爬取
  - 產出可審計的 crawl artifacts
- `website-analysis agent`
  - 有 AI
  - 消化 crawl artifacts
  - 產出站點地圖與內容盤點

此功能的用途是網站重製與站點理解，不是 SEO 優化器，也不是互動式瀏覽器自動化。

## 2. 核心原則

### 2.1 責任分離

- `crawler-worker` 只做確定性爬取、抽取、截圖、artifact 寫入
- `website-analysis agent` 只做盤點、整理、歸納、輸出
- `broker` 只做：
  - 任務建立
  - 舊資料查詢
  - 是否重抓的使用者意圖確認
  - worker / agent orchestration
  - artifact 保存與即時回傳

### 2.2 使用者意圖優先

是否重抓不是 `website-analysis agent` 的自由裁量，而是使用者意圖：

- 若使用者明講要重抓：直接新 crawl
- 若使用者明講只用舊資料：直接重用既有 crawl artifact
- 若使用者未明講：
  - 先查是否有舊 crawl artifact
  - 若有多份，只取最新一份
  - 先回覆最新舊資料的抓取時間與 scope 摘要
  - 由使用者決定：
    - 使用舊資料分析
    - 或重新抓取
- 若完全沒有舊資料：
  - 先回一句確認
  - 使用者確認後才開始 crawl

### 2.3 合法性責任

`crawler-worker` v1 不遵守 `robots.txt`。  
此能力的用途是重製與盤點，合法性與使用責任由使用者自行承擔。

系統仍保留最基本審計：

- 誰觸發
- 起始網址
- 時間
- 輸出 artifacts

## 3. v1 邊界

### 3.1 Crawl 邊界

- 只支援匿名公開網站
- 不登入
- 不填表
- 不處理 user-delegated session

### 3.2 Scope 邊界

- 起點：單一 `start_url`
- 範圍：同主網域與所有子網域
- 遍歷策略：廣度優先
- 不設定頁數上限

### 3.3 例外排除

為避免無限展開，對以下頁型做例外排除或只記錄不展開：

- 明顯的搜尋型頁面
- 明顯的日曆型頁面

這些頁面會進入 `excluded_urls[]` 或標記為 `non_expandable`，但不繼續擴張 queue。

### 3.4 非 HTML 資源

下列資源不做內容分析，只保留連結與來源頁：

- PDF
- 圖片
- 下載檔
- 影片頁或影片檔

之後若重製站點遇到 CORS 或檔案存取問題，交由重製後端代理與後端能力處理，不由 crawl 階段處理。

### 3.5 輸出形式

v1 同時提供：

- 結構化 JSON
- Markdown 報告

兩者都必須從同一批 crawl/result data 生成，不能形成兩套真相。

## 4. Crawler Worker 設計

### 4.1 能力定位

`crawler-worker` 是一個新的 deterministic worker，不是 browser-worker 的別名。

它可以重用：

- 現有 Playwright browser service
- 現有單頁讀取抽取邏輯

但對 broker 暴露的是新能力，不和 `browser.read` 混在一起。

### 4.2 Request Contract

```json
{
  "start_url": "https://example.com",
  "scope_policy": {
    "mode": "same_registrable_domain_with_subdomains"
  },
  "traversal_policy": {
    "strategy": "breadth_first",
    "page_limit": null
  },
  "page_policy": {
    "capture_title": true,
    "capture_description": true,
    "capture_text": true,
    "capture_internal_links": true,
    "capture_screenshot": true
  },
  "resource_policy": {
    "html": "analyze",
    "pdf": "link_only",
    "image": "link_only",
    "download": "link_only",
    "video": "link_only"
  },
  "exception_policy": {
    "exclude_calendar_like": true,
    "exclude_search_like": true,
    "respect_robots": false
  },
  "output_policy": {
    "return_inline_summary": true,
    "persist_artifacts": true
  }
}
```

### 4.3 Runtime 行為

`crawler-worker` 的固定流程：

1. 正規化 `start_url`
2. 建立 BFS queue
3. 逐頁抓取 HTML
4. 對 URL 做去重
5. 驗證是否仍在同主網域或子網域
6. 應用搜尋型/日曆型 heuristics
7. HTML 頁面抽取：
   - title
   - description
   - text excerpt
   - internal links
   - screenshot
8. 非 HTML 資源只記錄：
   - url
   - asset type
   - referrer
9. 寫入 crawl artifacts
10. 回傳 inline summary

### 4.4 Worker Output

```json
{
  "crawl_run_id": "crawl_...",
  "status": "completed",
  "root_scope": {
    "start_url": "https://example.com",
    "registrable_domain": "example.com"
  },
  "visited_pages": [],
  "discovered_assets": [],
  "excluded_urls": [],
  "crawl_artifacts": {
    "page_records_artifact_id": "artf_...",
    "link_graph_artifact_id": "artf_...",
    "screenshot_bundle_artifact_id": "artf_..."
  },
  "inline_summary": {
    "page_count": 0,
    "subdomain_count": 0,
    "excluded_count": 0,
    "asset_count": 0
  }
}
```

其中：

- `visited_pages[]` 每筆至少含：
  - `url`
  - `final_url`
  - `title`
  - `description`
  - `text_excerpt`
  - `internal_links`
  - `page_class`
  - `screenshot_artifact_id`
- `discovered_assets[]` 每筆至少含：
  - `url`
  - `asset_type`
  - `referrer_url`
- `excluded_urls[]` 每筆至少含：
  - `url`
  - `reason`

### 4.5 Heuristics 邊界

`crawler-worker` 不做 AI 判斷。

`page_class` 與 `excluded reason` 只能由規則式 heuristics 產生，例如：

- URL pattern
- query string pattern
- path segment pattern
- title / form 結構 pattern

它不能自己推理「這頁像不像產品頁」。  
真正的高階內容分類，交給 `website-analysis agent`。

## 5. Website Analysis Agent 設計

### 5.1 能力定位

`website-analysis agent` 負責：

- 站點地圖整理
- 內容盤點
- 子網域與頁面群組歸納
- JSON + Markdown 輸出

v1 不做：

- UX critique
- SEO 建議
- IA 重構建議
- 重製方案規劃

### 5.2 輸入

agent 可接受兩種分析目標：

1. `crawl_run_id`
2. `start_url`

若是 `start_url`，是否重抓由 intake/broker 依使用者意圖先處理，不由 agent 自行決策。

### 5.3 與 Crawl 的關係

`website-analysis agent` v1 必須支援兩條路：

- 分析既有 crawl artifact
- 要求 broker 發起新的 crawl，再分析新結果

但 agent 不自己執行 crawl；它只請 broker 做 orchestrate。

### 5.4 主要輸出

#### `site-map.json`

最少包含：

- root site
- 子網域節點
- page nodes
- page-to-page internal link edges
- page grouping / section grouping

#### `content-inventory.json`

最少包含：

- 每頁 title / description / text excerpt
- 推定內容型別
- 所屬 section
- 所屬子網域
- 關聯的非 HTML 資源連結

#### `analysis-report.md`

最少包含：

- 站點概覽
- 子網域概覽
- 主要 section / cluster
- 內容盤點摘要
- 排除頁面統計
- 非 HTML 資源摘要
- crawl coverage 與明顯缺口

### 5.5 即時回傳

agent 對使用者的即時回傳不回整包 JSON，而是回摘要：

- crawl run id
- 頁數
- 子網域數
- 主要 section
- artifact references

完整 JSON 與 Markdown 走 broker-managed artifacts 保存，且也可作為即時回傳中的下載/查看目標。

## 6. Intake / Broker Orchestration

### 6.1 使用者意圖解析

intake 層要先把使用者指令壓成：

- `analysis_target`
- `crawl_intent`
  - `force_recrawl`
  - `reuse_existing`
  - `unspecified`

### 6.2 舊資料查詢規則

當 `crawl_intent = unspecified`：

1. 先查同 root scope 的 crawl artifacts
2. 若有多份，只取最新一份
3. 先回覆使用者：
   - 有舊資料
   - 抓取時間
   - scope 摘要
4. 等使用者回：
   - 用這份舊資料
   - 或重新抓取

### 6.3 沒有舊資料時

若查不到舊資料：

- 不直接開始 crawl
- 先回一句確認
- 使用者確認後才派送 `crawler-worker`

### 6.4 Artifact 策略

兩種輸出都要有：

- 即時回傳結果
- broker-managed artifacts 落檔保存

保存的 artifacts 至少包括：

- crawl raw page records
- crawl link graph
- screenshots
- `site-map.json`
- `content-inventory.json`
- `analysis-report.md`

## 7. 風險與限制

### 7.1 不限頁數的風險

v1 雖然不設頁數上限，但仍要用以下方式防爆：

- BFS 去重
- 搜尋型頁面排除
- 日曆型頁面排除
- queue 去重
- URL normalization

若之後實測仍有失控站點，應加：

- host-level crawl budget
- wall-clock timeout
- manual cancel

但這些不作為本 spec 的 v1 基本邊界。

### 7.2 非 HTML 資源

v1 只保留連結，不做內容解析。  
這代表 PDF 與影片類內容盤點只到「被引用」與「可重製時需處理」的層級。

### 7.3 合法性

系統不判斷合法性，也不遵守 `robots.txt`。  
這不是安全模型缺失，而是明確產品定位。

## 8. 驗證要求

### 8.1 Crawler Worker

- 驗證 BFS traversal 正常
- 驗證同主網域與子網域 scope 正常
- 驗證跨網域 URL 不進 queue
- 驗證搜尋型與日曆型頁面被排除或不展開
- 驗證每頁預設截圖
- 驗證非 HTML 資源只記錄連結
- 驗證 artifacts 落檔成功

### 8.2 Website Analysis Agent

- 驗證可從既有 crawl artifact 產生 `site-map.json`
- 驗證可從既有 crawl artifact 產生 `content-inventory.json`
- 驗證可產生 `analysis-report.md`
- 驗證即時回傳摘要與 artifact 內容一致

### 8.3 Intake / Broker

- 驗證使用者明講重抓時直接新 crawl
- 驗證使用者明講只用舊資料時直接重用
- 驗證未明講時先查最新舊資料並提示使用者
- 驗證無舊資料時先詢問確認再抓

## 9. v1 不做的事

- authenticated crawl
- form interaction
- delegated browser session
- SEO / UX / IA 分析
- 自動重製建議
- robots 合規模式
- 多起點 crawl
- sitemap.xml 優先導入模式

