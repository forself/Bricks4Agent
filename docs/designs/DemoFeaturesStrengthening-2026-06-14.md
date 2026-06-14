# 展示功能強化計畫(三個 Demo 功能)

Date: 2026-06-14
Status: **規劃,依序實作(#3 → #1 → #2)**
範圍: 三個展示用功能的「特別強化」。先三個一起規劃,再依序測試先行實作。

## 0. 三個展示功能

1. **交通查詢** —— 日期/時間範圍、出發地、目的地、方式(rail/HSR/bus/flight)。
2. **web 取指定資料 → 產生報表/報告**。
3. **依某網站 → 產生「基於組件庫的複製品」或「雛形」**;**不同次、不同台電腦的執行結果必須穩定(決定性)**。

三者程式都在 main(`.worktrees/*` 為平行副本;關鍵檔均在 main)。

---

## 1. #3 網站複製/雛形 —— 決定性(最先做)

**現況**:管線在 `packages/csharp/workers/site-crawler-worker/`(`SiteCrawlerService`/`DeterministicSiteExtractor`/`SiteGeneratorConverter`/`StaticSitePackageGenerator`),已有單元測試。決定性大致良好 —— 節點 ID 用 SHA256 內容雜湊、無 `Date`/`random`/`Guid` 進輸出、字串比較用 `Ordinal`。

**不穩定來源(會讓輸出隨爬取順序/機器而變,須修)**:
| 嚴重度 | 來源 | 位置 | 落在輸出 |
|--------|------|------|---------|
| 🔴 高 | 主題 token `Dictionary<string,string>`(colors/typography)序列化依插入(爬取)順序 | `Models/SiteCrawlContracts.cs`、`Models/GeneratorSiteContracts.cs`;合併於 `SiteCrawlerService.MergeThemeTokens` | `site.json` |
| 🟠 中 | 動態元件清單(manifest.Components / ComponentRequests)順序依區塊處理順序 | `Services/SiteGeneratorConverter.cs` | `components/manifest.json` |
| 🟡 低 | zip entry 順序依檔案系統列舉 | `Services/StaticSitePackageGenerator.cs`(`ZipFile.CreateFromDirectory`) | `.zip` |

**強化(測試先行)**:
1. 先寫**決定性測試**(RED):同一 crawl 結果 → 連續 `Convert` + `Generate` 兩次 → 兩次輸出(檔案集合 + 內容)位元組完全相同;對輸出做雜湊比對。
2. 修 token 字典:改 `SortedDictionary<string,string>`(Ordinal)或序列化前排序;合併也用穩定排序。
3. 修元件清單:輸出前 `OrderBy(c => c.Type, Ordinal)`(且 ComponentRequests 同樣穩定排序)。
4. 修 zip:列舉檔案後 `OrderBy(Ordinal)` 再逐一加入,而非 `CreateFromDirectory`。
5. (選)cross-process:測試以子行程跑兩次或清快取重跑,確認跨執行穩定。

**驗收**:同輸入連跑兩次(同/不同行程)→ 輸出位元組相同。

---

## 2. #1 交通查詢 —— 涵蓋、精度、健壯性

**現況**:4 模式 + 追問 + 範圍回答;`TransportQueryContextResolver` 解析站點/日期/時段。

**弱點 → 強化**:
- **站點涵蓋 + 撞名**:只硬編 ~18 TRA / 16 THSR / 16 機場;`Contains` 子字串比對會撞名(新竹/新左營)。→ 擴充清單;比對改「最長優先 + 邊界」避免子字串誤判。
- **時段精度**:目前只有「上午/下午/晚上/凌晨」4 段。→ `ExtractTimeRange` 加解析絕對時刻「18:00」「18:00-20:00」「下午3點」。
- **日期**:加「下週一/週末」等相對日;補時區說明。
- **無結果**:目前回靜態訊息。→ 退一天重試或明確提示「該日無班次,試試其他日期」。
- **測試**:時段解析、站點解析(含撞名)、無結果路徑。

**驗收**:`?rail 板橋 高雄 明天 18:00-20:00` 能正確過濾時段;撞名站點解析正確;無結果有有用提示。

---

## 3. #2 web 取資料 → 報表 —— 端到端串接

**現況**:`web.search`/`web.fetch`(main)、`HighLevelDocumentArtifactService`(LLM 產文件)、`LineArtifactDeliveryService`(交付)都在,但**高階層沒串成一條**「抓指定資料 → LLM 綜合 → 報表 → 交付」;資料→報表那段未用 LLM 綜合。

**強化**:
- 一條端到端路徑/命令:給定主題或 URL → `web.search`/`web.fetch` 收集 → LLM 綜合成結構化**報表(markdown)** → 存 artifact → 交付(LINE/Drive)。
- 報表附**來源證據**(URL 清單)以可追溯。
- **測試**:報表組裝(輸入數段抓取內容 → 產出含各來源的 markdown)單元測試;端到端 wiring。

**驗收**:給一主題,系統抓數個來源、產出一份附來源的 markdown 報表並交付。

---

## 4. 實作順序與理由

1. **#3 決定性** —— 最具體、有硬性可驗收標準(位元組穩定)、程式+測試都在 main。先做最扎實。
2. **#1 交通** —— 多為界線清楚的補強,單元可測。
3. **#2 web→報表** —— 串接 + LLM 綜合,較整合性。

每步測試先行、各自 commit、跑完整驗證(per CLAUDE.md)再進下一個。
