# Bricks4Agent 設計系統 — 設計說明

本文說明色彩／樣式 token 的架構、元件如何消費 token、富文本樣式白名單、以及 Theme Studio 的自舉設計。
使用方式(啟動 Theme Studio、套用客製產出)見 [THEME-USAGE.md](THEME-USAGE.md)。

## 1. 核心原則

1. **零 runtime 依賴**:元件為純 Vanilla JS class,不 import 任何外部套件。
2. **樣式一律走 token**:顏色/圓角/陰影/字級/字體/過渡全部用 `var(--cl-*)` CSS 變數;元件內用 `element.style.cssText`(CSSOM,不受 CSP `style-src` 限制)。
3. **嚴格 CSP 相容**:`script-src 'self'; style-src 'self'`,不需 `unsafe-inline`/`unsafe-eval`(無 inline 事件屬性、無 eval、無 inline `<style>`)。
4. **換膚集中**:淺/深色由文件根 `[data-theme="dark"]` 切換,元件不寫 media query。

## 2. 三層色彩 token 架構(primitive → semantic → override)

```
palette.css   (primitive)   --cl-<hue>-<step>   例 --cl-blue-500:#2196F3
      │  ← theme.css 開頭 @import './palette.css'
theme.css     (semantic)    --cl-primary: var(--cl-blue-500)   (light)
      │                      --cl-primary: var(--cl-blue-300)   ([data-theme=dark])
      ▼
theme.custom.css (override)  :root{ --cl-primary: … }   ← Theme Studio 產出,最後載入覆蓋
```

- **palette.css**(`ui_components/palette.css`,**自動生成,勿手改**):Material 級 16 色相 × 50→900(共 160)+ 黑/白/default + `transparent` + `.opacity-*` 工具階。提供 `--cl-<hue>-<step>` **token**(全站可用)與 `.rt-color-*` **class**(文字色/富文本)。來源=`editor/richtext-palette.js`,重生:`node editor/gen-palette-css.mjs`。
- **theme.css**(`ui_components/theme.css`):語意層。品牌/語意色、字級、圓角、陰影、字體、過渡;`@import './palette.css'` 後,語意色以 `var(--cl-<hue>-<step>)` 引用色階(單一色彩來源);含 `[data-theme="dark"]` 覆蓋。
- **theme.custom.css**(客製產出):由 Theme Studio 或 `tools/theme-studio/gen-custom-css.mjs` 產生,只含有調整的 token,載於 theme.css **之後**覆蓋。見 [THEME-USAGE.md](THEME-USAGE.md)。

> 為何調 token 能即時重繪:元件全走 `var(--cl-*)`,只要 `document.documentElement.style.setProperty('--cl-primary', v)`,所有已渲染元件立即套用,無需重建。這是 Theme Studio WYSIWYG 的基礎。

## 3. 富文本樣式(class 化,禁自由 inline CSS)

富文本(WebTextEditor)樣式一律以 class 表達,不存 inline style:
- **色彩**:有限調色盤;選色可用光譜,但**存檔吸附到最相近的顏色 class**(`richtext-palette.js` 的 `nearestColorClass`)。
- **class 集合**:`rt-color-<hue>-<step>` / `rt-color-{default,black,white,transparent}` / `rt-size-*` / `rt-align-*` / `rt-lh-*` / `.opacity-*`(定義於 palette.css + `editor/richtext.css`)。
- **normalizer**:編輯器 `_normalizeStyles()` 把 execCommand 產生的 inline style 即時轉為 rt-* class 並移除 style。
- **清洗**:`utils/security.js` 的 `sanitizeHTML()` 用**白名單**(標籤/屬性/class 三層)+ 移除所有 style + URL 協定白名單;`ALLOWED_CLASS_PATTERN` 為 class 白名單單一事實來源。政策=禁指令碼/跳轉/非同源引用,實作以白名單(非黑名單,防 mXSS)。

## 4. 自舉(dogfooding)

`tools/theme-studio` 的介面**完全用本庫元件搭成**。這是覆蓋度的驗證:搭不出來就是缺必要元件。開發 Theme Studio 時即以此補上了 `form/Slider` 與 `form/TextArea` 兩個基礎控制項。

## 5. 元件契約(摘要)

`new X(options)` → `.mount(container)` → `.destroy()`;具值元件有 `getValue/setValue/setDisabled/clear`。內部狀態走 `utils/component-state.js` 的不可變狀態機。完整調用約定見 [/AGENT-UI-GUIDE.md](../../../../AGENT-UI-GUIDE.md)。

## 6. 權威來源與工具

| 項目 | 位置 |
|---|---|
| 元件權威清單 | `metadata/component-catalog.json`(112) |
| 色彩來源資料 | `editor/richtext-palette.js`(MATERIAL 色階 + 吸附 + class 白名單) |
| 色階 CSS 生成 | `editor/gen-palette-css.mjs` → `palette.css` |
| 客製樣式生成 | `tools/theme-studio/gen-custom-css.mjs` → `theme.custom.css` |
| 樣式稽核 | `npm run audit:ui-styles`(theme.css/palette.css/richtext-palette.js 為色彩來源,已排除) |
