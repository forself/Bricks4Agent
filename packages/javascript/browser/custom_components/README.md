# custom_components

開發者將 Custom Component Studio 匯出的 `*.json` 放進 [`definitions/`](definitions/)，再於 repository root 執行：

```bash
npm run custom-components:build
npm run custom-components:check
npm run test:custom-components
```

瀏覽器使用 `registry.json` 發現定義，不能直接列舉資料夾。完整 JSON 格式、三層分類、runtime 與 DynamicPageRenderer 範例見 [`CUSTOM-COMPONENTS.md`](../../../../CUSTOM-COMPONENTS.md)。

請勿在此資料夾新增 `*.manifest.json`；內建 115 元件的 metadata manifest 與客製 JSON registry 是兩套獨立契約。
