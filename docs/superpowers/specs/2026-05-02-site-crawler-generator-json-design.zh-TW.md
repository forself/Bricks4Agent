# Site Crawler to Generator JSON 設計

日期：2026-05-02
狀態：approved design draft
範圍：從使用者在 LINE 指定網站，到產出可由現有 generator 消費的網站定義 JSON；若現有元件庫無法覆蓋，產生新元件與元件 manifest。

## 1. 問題定義

使用者希望提供一個網址，系統爬取該網址範圍內的網站程式碼與資產，並轉換成可以直接交給既有 SPA/page generator 使用的 JSON。此流程不能讓 LLM 自由猜測網站結構，因為輸出必須能落到現有 generator、元件庫與可驗證的 artifacts。

本設計取代舊的「網站分析報告」思路。新目標不是只產生 `site-map.json` 或 markdown 分析，而是產生 generator-native bundle。

## 2. 核心原則

1. 使用者意圖先決
   - 系統在開始爬取前必須取得使用者對爬取範圍的意圖。
   - 範圍以起始 URL 的路徑層級定義，不以頁數作為主要語意。

2. Deterministic extraction 先決
   - crawler 與 extractor 必須先以確定性規則產生 DOM tree、CSS tokens、asset manifest、route graph、forms/interactions、layout blocks。
   - LLM 只允許在固定 schema 內做命名、歸納、語意分類與缺口說明。

3. 元件庫對應先決
   - 每個頁面區塊必須先嘗試對應到 `packages/javascript/browser/ui_components/metadata/component-catalog.json` 中的可用元件。
   - 若無法對應，系統必須產生新的元件規格、元件程式、樣式、manifest 與 generator import mapping。
   - 最終輸出不得包含無法被 generator 解析的裸區塊。

## 3. 使用者問答流程

當使用者只給 URL，例如：

```text
/clone https://example.com/docs/
```

broker/high-level intake 不直接執行 crawl，而是回問爬取範圍：

```text
請選擇要擷取的範圍：
1. 只擷取這個頁面
2. 擷取這個路徑下第一層
3. 擷取這個路徑下兩層以內
4. 指定 N 層
```

若起始 URL 是 `https://example.com/docs/`：

- 只擷取這個頁面：只抓 `/docs/`
- 第一層：抓 `/docs/a`、`/docs/b`，不抓 `/docs/a/detail`
- 兩層以內：抓 `/docs/a`、`/docs/a/detail`
- N 層：抓深度小於或等於 N 的同路徑子頁

使用者回答後，系統建立 crawl intent：

```json
{
  "start_url": "https://example.com/docs/",
  "scope": {
    "kind": "path_depth",
    "max_depth": 2,
    "same_origin_only": true,
    "path_prefix_lock": true
  },
  "budgets": {
    "max_pages": 50,
    "max_total_bytes": 10485760,
    "max_asset_bytes": 2097152,
    "wall_clock_timeout_seconds": 180
  }
}
```

`max_pages` 與 byte budgets 是安全上限，不代表使用者語意。

## 4. 系統架構

### 4.1 元件

1. `site-crawler-worker`
   - capability：`site.crawl_source`
   - route：`site_crawl_source`
   - 責任：安全抓取 URL、同路徑 BFS、收集 HTML/CSS/JS/asset metadata、輸出 raw crawl bundle。

2. `site-generator-agent-worker`
   - capability：`site.convert_to_generator_json`
   - route：`site_convert_to_generator_json`
   - 責任：將 crawl bundle 轉成 generator-native bundle。
   - 這個 agent 在執行層是特殊 worker，不是自由聊天 agent。

3. `SiteCloneCoordinator`
   - broker service。
   - 責任：LINE intake 狀態管理、crawl intent 建立、呼叫兩個 worker、保存 artifacts、回傳結果。

4. `ComponentResolver`
   - 讀取元件 catalog。
   - 將 extracted blocks 對應到既有元件。
   - 無法對應時產生 new component requirement。

5. `GeneratedComponentSynthesizer`
   - 依 new component requirement 產生元件 JS、CSS、manifest 與 generator mapping。
   - 新元件必須可被 generator import。

6. `GeneratorBundleValidator`
   - 驗證輸出的 page definitions、component mappings、generated components、asset references。
   - 必須在交付前實際跑 generator validation。

### 4.2 資料流

```text
LINE user
  -> line-worker
  -> broker high-level intake
  -> scope clarification
  -> SiteCloneCoordinator
  -> site-crawler-worker
  -> deterministic extraction
  -> ComponentResolver
  -> GeneratedComponentSynthesizer
  -> site-generator-agent-worker constrained summarization
  -> GeneratorBundleValidator
  -> artifact delivery
  -> LINE reply
```

## 5. Crawler Worker 設計

### 5.1 Request

```json
{
  "request_id": "req_123",
  "start_url": "https://example.com/docs/",
  "scope": {
    "kind": "path_depth",
    "max_depth": 2,
    "same_origin_only": true,
    "path_prefix_lock": true
  },
  "capture": {
    "html": true,
    "rendered_dom": true,
    "css": true,
    "scripts": true,
    "assets": true,
    "screenshots": false
  },
  "budgets": {
    "max_pages": 50,
    "max_total_bytes": 10485760,
    "max_asset_bytes": 2097152,
    "wall_clock_timeout_seconds": 180
  }
}
```

### 5.2 Path depth 規則

Crawler 先 canonicalize start URL：

- 移除 fragment。
- 正規化 trailing slash。
- 只允許 `http` 與 `https`。
- `path_prefix_lock = true` 時，只展開以起始 path prefix 開頭的 URL。
- `same_origin_only = true` 時，只展開同 scheme、host、port 的 URL。

Depth 計算以 start path 為 root：

- start URL depth = 0
- start path 下第一個子 segment = 1
- 再下一層 = 2

Query string 不增加 depth，但不同 query 預設不展開，除非後續版本明確支援 faceted pages。

### 5.3 Output

```json
{
  "crawl_run_id": "crawl_123",
  "status": "completed",
  "root": {
    "start_url": "https://example.com/docs/",
    "normalized_start_url": "https://example.com/docs/",
    "origin": "https://example.com",
    "path_prefix": "/docs/"
  },
  "pages": [
    {
      "url": "https://example.com/docs/a",
      "final_url": "https://example.com/docs/a",
      "depth": 1,
      "status_code": 200,
      "title": "A",
      "html_ref": "artifacts/crawl_123/pages/a.html",
      "rendered_dom_ref": "artifacts/crawl_123/pages/a.dom.json",
      "text_excerpt": "Visible text excerpt",
      "links": [],
      "forms": [],
      "resources": []
    }
  ],
  "assets": [
    {
      "url": "https://example.com/assets/app.css",
      "type": "stylesheet",
      "content_ref": "artifacts/crawl_123/assets/app.css",
      "sha256": "..."
    }
  ],
  "excluded": [
    {
      "url": "https://example.com/private",
      "reason": "outside_path_depth"
    }
  ],
  "limits": {
    "truncated": false,
    "page_limit_hit": false,
    "byte_limit_hit": false
  }
}
```

## 6. Deterministic Extraction

Extractor 對每個 page 產出以下中介資料，供 converter 使用。

### 6.1 DOM section tree

```json
{
  "page_url": "https://example.com/docs/a",
  "sections": [
    {
      "id": "sec_hero",
      "tag": "section",
      "role": "hero",
      "text": {
        "headline": "Product Name",
        "body": "Short intro"
      },
      "media": [],
      "children": [],
      "source_selector": "main > section:nth-of-type(1)"
    }
  ]
}
```

Roles 由 deterministic rule 先給候選值，例如 `hero`、`navigation`、`card_grid`、`form`、`table`、`gallery`、`article`、`footer`。LLM 可以重新命名 label，但不能創造不存在的 source section。

### 6.2 CSS/theme tokens

```json
{
  "colors": {
    "primary": "#1f6feb",
    "surface": "#ffffff",
    "text": "#111827"
  },
  "typography": {
    "font_family": "Inter, sans-serif",
    "heading_scale": ["32px", "24px", "20px"]
  },
  "spacing": {
    "unit": 8,
    "observed": ["8px", "16px", "24px", "32px"]
  }
}
```

### 6.3 Route graph

```json
{
  "routes": [
    {
      "path": "/docs/a",
      "page_id": "docs-a",
      "depth": 1,
      "title": "A"
    }
  ],
  "edges": [
    {
      "from": "/docs/",
      "to": "/docs/a",
      "kind": "internal_link"
    }
  ]
}
```

### 6.4 Interaction inventory

Forms、buttons、menus、tabs、filters、search boxes 先以 DOM/event attributes 推斷。無法確認的行為標記為 `static_replica`，不得推測後端 API。

## 7. Component Resolver

Resolver 的輸入是 extracted sections 與元件 catalog。輸出必須分成三類：

```json
{
  "mapped": [],
  "generated": [],
  "unresolved": []
}
```

交付前 `unresolved` 必須為空。若無法清空，整個流程回傳失敗並說明缺口。

### 7.1 可直接映射

以下情況可直接映射：

- section 是 form field，且 field type 對應 catalog 中 `generator.usage_mode = field_direct` 的元件。
- section 是明確 action group，且可對應 `definition_explicit` 元件。
- section 是 generator 既有 page type 可表達的 `form`、`list`、`detail`、`dashboard`。

Resolver 需要記錄 evidence：

```json
{
  "source_section_id": "sec_contact_form",
  "component_id": "form.text_input",
  "registry_name": "TextInput",
  "reason": "input[type=email] maps to field type email",
  "confidence": 0.95
}
```

### 7.2 必須生成新元件

以下情況必須生成新元件：

- 視覺或互動結構無法用現有 `field_direct`、`definition_explicit` 或 page type 表達。
- catalog 中只有 `manual_only` 元件，且 generator 不能直接 consume。
- 原站有複合區塊，例如價格方案卡、品牌 hero、特殊 carousel、產品比較表、互動 map、非標準 article layout。

生成要求：

```json
{
  "component_id": "generated.pricing_plan_grid",
  "registry_name": "GeneratedPricingPlanGrid",
  "category": "generated",
  "source_section_ids": ["sec_pricing"],
  "props_schema": {
    "plans": "array",
    "cta_label": "string"
  },
  "style_contract": {
    "uses_theme_tokens": true,
    "css_file": "GeneratedPricingPlanGrid.css"
  },
  "behavior_contract": {
    "events": ["selectPlan"],
    "side_effects": []
  }
}
```

## 8. Generator Bundle Contract

最終輸出是一個 bundle，不只是單一 page definition。

```json
{
  "kind": "site-generator-bundle",
  "version": "0.1.0",
  "source": {
    "crawl_run_id": "crawl_123",
    "start_url": "https://example.com/docs/",
    "scope": {
      "kind": "path_depth",
      "max_depth": 2
    }
  },
  "definitions": {
    "pages": [],
    "apps": []
  },
  "theme_tokens": {},
  "asset_manifest": [],
  "component_resolution": {
    "mapped": [],
    "generated": [],
    "unresolved": []
  },
  "generated_components": [],
  "generator_overrides": {
    "component_paths": {}
  },
  "validation": {
    "page_definitions_valid": true,
    "component_imports_valid": true,
    "unresolved_count": 0
  }
}
```

`definitions.pages[].definition` 必須符合目前 `PageDefinition` 可接受的 `name`、`type`、`description`、`components`、`fields`、`api`、`behaviors`、`styles` 結構。若生成新元件，bundle 必須同時提供 `generator_overrides.component_paths`，讓 generator 能解析新元件 import path。

## 9. Generator 相容性調整

目前 `PageGenerator` 的 `ComponentPaths` 與 `AvailableComponents` 是靜態清單。為支援新元件，實作時需要新增一層 dynamic component registry：

1. 讀取既有 `component-catalog.json`。
2. 讀取 bundle 中的 `generated_components[]`。
3. 合併成 runtime component path map。
4. `PageGenerator.generate()` 接受 `componentPathOverrides` 或 `componentRegistry`。
5. validation 不再只看靜態 `AvailableComponents.custom`，也要看 registry 中 `generator.usable = true` 的元件。

這是本功能的必要前置改造；否則新元件即使產生，generator 仍會報 unavailable。

## 10. LLM 使用邊界

LLM 只允許處理：

- 將 deterministic sections 命名為人可讀頁面名稱。
- 將相近 sections 歸納成 component requirement。
- 在 schema 內填寫 description、props 名稱、content labels。
- 產生新元件程式碼時，必須以 component requirement、theme tokens、asset refs 為輸入。

LLM 不允許：

- 自行新增 crawler 未抓到的頁面。
- 自行新增沒有 source evidence 的 section。
- 自行改變 path depth scope。
- 自行引用不存在的元件。
- 輸出未通過 schema validation 的 JSON。

## 11. 安全約束

Crawler 必須防 SSRF：

- 拒絕 localhost、loopback、private IP、link-local、metadata IP。
- 拒絕 `file:`、`data:`、`ftp:`、`chrome:` 等非 http/https scheme。
- redirect 後仍需重新檢查 URL。
- DNS resolution 結果若落在禁止網段，拒絕。
- 同 origin/path prefix/depth 檢查在每次 enqueue 與 fetch 前都要做。

資源限制：

- 預設 `max_pages = 50`。
- 預設 `max_total_bytes = 10MB`。
- 預設 `max_asset_bytes = 2MB`。
- 預設 `wall_clock_timeout_seconds = 180`。
- 超限時保留 partial bundle，但標記 `limits.truncated = true`，不得宣稱完整轉換。

## 12. Artifacts

流程產出以下 broker-managed artifacts：

- `crawl-source-bundle.json`
- `extracted-site-model.json`
- `component-resolution.json`
- `generated-components/`
- `site-generator-bundle.json`
- `validation-report.json`
- 可選：`source-screenshots.zip`

LINE 回覆只提供摘要與 artifact link，不直接貼大量 JSON。

## 13. 驗收條件

1. 使用者只提供 URL 時，系統必須先問 path depth scope。
2. Crawler 不會抓取 path prefix 之外的頁面。
3. Crawler 不會抓取 private/localhost/metadata 網段。
4. Converter 必須先產生 deterministic extracted model。
5. 每個 output page block 必須有 source evidence。
6. 每個 component reference 必須能解析到既有元件或新生成元件。
7. `component_resolution.unresolved` 在成功交付時必須為空。
8. 新生成元件必須有 JS、CSS、manifest、generator import path。
9. `PageGenerator.generate()` 必須能使用 bundle registry 成功產生 page code。
10. 若任何 validation 失敗，LINE 回覆要說明失敗階段與可修正方向。

## 14. 分階段實作建議

### Phase 1：安全 crawler 與 path depth scope

建立 `site-crawler-worker`，只產出 crawl source bundle 與 extracted site model。先不做 generator conversion。

### Phase 2：component resolver 與 generator registry 改造

讓 generator 能吃 catalog 與 component path overrides。建立 resolver 的 deterministic mapping。

### Phase 3：converter worker

建立 `site-generator-agent-worker`，輸出 `site-generator-bundle.json`，並跑 validator。

### Phase 4：generated component synthesizer

補上無法對應元件時的新元件生成、manifest 生成、smoke validation。

### Phase 5：LINE high-level orchestration

將 `/clone` 或同義 production intent 接到問答、crawl、convert、artifact delivery。

## 15. 需要保留的未來能力

以下能力不列入 v1，但設計不封死：

- robots.txt policy mode。
- sitemap.xml seed。
- user-delegated authenticated crawl。
- form interaction replay。
- multi-origin asset mirroring。
- visual diff validation against screenshots。
