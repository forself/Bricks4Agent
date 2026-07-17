# 元件組合 Studio 相容入口

元件組合器已整合進同一個 JSON 產生的 Bricks4Agent Studio：

```text
/tools/theme-studio/index.html?tab=components
```

`/tools/custom-component-studio/index.html` 只保留為相容入口，會導向上面的同頁第二頁籤；它不是另一份手寫 Studio，也沒有獨立的頁面定義或工具控制。

本目錄中的 `controller.js` 提供元件組合狀態、可信 command registry、JSON 匯入匯出與預覽邏輯；實際 UI 的唯一權威定義是 [`../theme-studio/studio.page.json`](../theme-studio/studio.page.json)，由 `DynamicPageRenderer` 的 `tool` mode 產生。

完整的 JSON schema、三層分類、definitions folder、registry 與 runtime 用法見 [`CUSTOM-COMPONENTS.md`](../../CUSTOM-COMPONENTS.md)。自舉架構與驗收指令見 [`../theme-studio/README.md`](../theme-studio/README.md)。
