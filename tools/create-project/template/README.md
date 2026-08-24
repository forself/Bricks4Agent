# {{PROJECT_NAME}}

以 **Bricks4Agent** 元件庫 + 頁面生成器建置(零第三方 runtime)。腳手架引用機制=「junction 開發 + 發佈快照」,詳見 [docs/dev-and-publish.md](docs/dev-and-publish.md)。

## 結構

```
{{PROJECT_NAME}}\
├── lib\                 腳手架掛載點(gitignored):開發=junction、發佈=快照
│   ├── ui_components\   元件庫(權威清單 metadata\component-catalog.json)
│   └── page-generator\  PageDefinition 靜態產碼 + 動態渲染
├── src\frontend\        應用(index.html / app.js / styles\)
├── scripts\             dev-link(junction)/ dev / sync-lib(複本後備)/ publish(快照+封閉驗證)
├── docs\                機制文件
└── dist\                發佈產物(gitignored;publish.mjs 產生)
```

## 快速開始

```powershell
node scripts/dev-link.mjs     # 一次性:lib 轉 junction(建立專案時已自動執行)
node scripts/dev.mjs          # http://127.0.0.1:8123/src/frontend/index.html
node scripts/publish.mjs      # 發佈:dist\(快照 + SNAPSHOT.json 憑證 + 封閉性驗證)
```

## 規則

1. 只用 `lib\ui_components`,import 一律**相對路徑**(層數=檔案到專案根的深度;禁根絕對 `/lib/`、禁 `../Bricks4Agent/`——publish 驗證器會擋)。

2. 缺元件 → 加到 Bricks4Agent repo(照其 AGENT-UI-GUIDE.md),junction 模式即時生效。

3. 樣式只用 `var(--cl-*)` token;動態字串一律 escapeHtml;無 inline script/style(CSP)。

4. 元件調用入口=Bricks4Agent 的 `AGENT-UI-GUIDE.md`;主題客製=Theme Studio(`tools/theme-studio/`),產出 `theme.custom.css` 放本專案並最後載入。
