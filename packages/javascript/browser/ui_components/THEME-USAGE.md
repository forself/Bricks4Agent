# Bricks4Agent 設計系統 — 使用說明

架構原理見 [DESIGN-SYSTEM.md](DESIGN-SYSTEM.md)。本文講:如何在網站引入樣式、用 Theme Studio 調校、以及**客製化產出如何使用**。

## 1. 在網站引入整套樣式

只要引入 `theme.css`,它會自動 `@import` 色階 foundation `palette.css`。富文本頁面再加 `richtext.css`。

```html
<!-- 基本(所有頁面):theme.css 會自動載入 palette.css -->
<link rel="stylesheet" href="/lib/ui_components/theme.css">

<!-- 有富文本(rt-* class / .rich-content 渲染)時再加 -->
<link rel="stylesheet" href="/lib/ui_components/editor/richtext.css">

<!-- 客製化覆蓋(見 §3),務必放最後 -->
<link rel="stylesheet" href="/theme.custom.css">
```

> 路徑提醒:上例的 `/lib/...` 為**絕對路徑,假設網站部署於網域根**。若部署在子路徑(如 `/web3/`)或用 Live Server,請改為相對於頁面的路徑或加上部署前綴,否則會 404。

深色主題:在 `<html>` 或容器加 `data-theme="dark"` 即切換。

直接用 token(不經 Theme Studio):任何元件/CSS 都可用 `var(--cl-*)`,例如 `var(--cl-primary)`、`var(--cl-blue-500)`、`var(--cl-radius-md)`。

## 2. Theme Studio 操作

Theme Studio 路徑全為相對路徑,任何靜態伺服器/任何根都可(含 VS Code Live Server):

```powershell
# 例:任一根啟動靜態伺服器,或於 VS Code 對 index.html 按「Go Live」
python -m http.server 8124 --bind 127.0.0.1
# 開 .../tools/theme-studio/index.html(前綴依伺服器根)
```

- **左側**:分頁 token 編輯器(語意色 / 圓角 / 字級 / 效果·字體 / 進階 JSON)——調整即時套用到右側全部元件。
- **右側**:元件展示廊(隨 token 即時變化)。
- **頂部**:深色切換、**儲存**(localStorage,供下次繼續)、**匯出 tokens.json**、**匯出 custom.css**、**匯入 tokens**、重置。

## 3. 客製化產出如何使用 ★

Theme Studio 產出**兩個檔**,用途不同:

| 產物 | 是什麼 | 誰用 |
|---|---|---|
| `theme.tokens.json` | 只含有調整的 token(來源檔) | **開發者/AI 的單一事實來源**,進版控、可再編 |
| `theme.custom.css` | 由 tokens 產生的 `:root{…}` 覆蓋樣式表 | **網站實際載入**的檔 |

### 3.1 套用到網站(最常用)

1. 在 Theme Studio 調好 → 點「匯出 custom.css」下載 `theme.custom.css`。
2. 放進你的網站(例:`tim-web/src/frontend/theme.custom.css`)。
3. 在頁面 **theme.css 之後**引入(見 §1 的第三個 `<link>`)。順序錯了不會生效。
4. 完成——覆蓋全站生效;未調整的 token 沿用 theme.css 預設。

> 原理:`theme.custom.css` 是 `:root{ --cl-x: 值 }`,CSS 後載入者優先 → 覆蓋 theme.css 的語意色。

### 3.2 AI / CI 管道(零依賴,推薦長期維護方式)

把 `theme.tokens.json` 當**唯一事實來源**進版控;`theme.custom.css` 由它生成(勿手改)。

```bash
# AI 代理直接編 theme.tokens.json,再產出樣式表:
node tools/theme-studio/gen-custom-css.mjs theme.tokens.json theme.custom.css
```

`theme.tokens.json` 結構:
```jsonc
{
  "meta":       { "name": "TimWeb Theme" },
  "tokens":     { "--cl-primary": "#7c3aed", "--cl-radius-md": "14px", "--cl-font-size-md": "14px" },
  "tokensDark": { "--cl-primary": "#a78bfa" }        // 選填:深色主題另外覆蓋 → 產出 [data-theme="dark"]{…}
}
```
- AI 代理調主題:**改 tokens.json → 跑 gen-custom-css.mjs**,不需開 UI。
- 也可把匯出的 tokens.json 用 Theme Studio「匯入 tokens」載回繼續編。

### 3.4 個別元件覆蓋(★ 只調某個元件)

除了全域 token,Theme Studio 每張**已渲染元件卡**右上角有 **⚙**:點開右側抽屜可**只調該元件**(同一套語意色/圓角/字級/效果控制項),即時預覽。

- **作用域 = 具名 class**,預設 `b4a-c-<元件名>`,可在抽屜內改名 → 同一元件能建立**多種變體**(例:`cta-primary`、`cta-danger`)。
- 匯出時併入**同一個** `theme.custom.css`:`:root{全域}` 在前、各 `.具名class{元件覆蓋}` 在後 → CSS cascade 讓元件覆蓋贏過全域。tokens.json 也多一段 `components`。

```jsonc
{
  "tokens":     { "--cl-primary": "#7c3aed" },              // 全域
  "components": {                                            // 各元件覆蓋
    "BasicButton": { "className": "b4a-c-BasicButton", "tokens": { "--cl-radius-md": "2px" } },
    "AuthButton":  { "className": "cta-danger",        "tokens": { "--cl-primary": "#d32f2f" } }
  }
}
```

產出的 `theme.custom.css`:
```css
:root { --cl-primary: #7c3aed; }
/* :root 之後 → 元件覆蓋優先 */
.b4a-c-BasicButton { --cl-radius-md: 2px; }
.cta-danger        { --cl-primary: #d32f2f; }
```

**在正式站套用**:把該 class 加到元件的**根元素**即生效(元件內所有 `var(--cl-*)` 會繼承此作用域的 token 值):
```html
<div class="b4a-c-BasicButton"> <!-- 這顆按鈕用 2px 圓角,其餘全站不受影響 --></div>
```
> gen-custom-css.mjs 同樣讀 `components` 產出上述 class 區塊,UI 與 AI 管道一致。

### 3.3 深色主題客製

- UI:頂部「深色」切換僅預覽;要為深色另設值,填 `tokensDark`(或用 gen-custom-css 的 `tokensDark`),產出會多一段 `[data-theme="dark"]{…}`。

## 4. 載入順序總表(重要)

```
palette.css   (theme.css @import,色階 primitive)
theme.css     (語意層 + light/dark)
richtext.css  (富文本 rt-* / .rich-content,選用)
theme.custom.css  (客製覆蓋,務必最後)
```

## 5. 常見問題

- **客製沒生效?** 檢查 `theme.custom.css` 是否在 `theme.css` **之後**載入,且路徑正確。
- **色階 token(--cl-blue-500)不見?** 確認 `theme.css` 能載到同目錄的 `palette.css`(@import 相對路徑);或直接 `<link>` palette.css。
- **要新增色/改色階?** 改 `editor/richtext-palette.js` 的 `MATERIAL` → `node editor/gen-palette-css.mjs`,token/class/吸附/白名單全部同步。
- **富文本顏色怎麼存?** 存的是 `rt-color-*` class(吸附最近色),非 inline style;渲染頁需引入 `richtext.css` + `palette.css`。
