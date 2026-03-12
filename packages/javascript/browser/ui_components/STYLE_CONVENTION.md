# Bricks4Agent UI 元件庫樣式規範

## 核心原則

1. **所有元件必須使用 `theme.css` 定義的 CSS 變數**（`--cl-*` 前綴），禁止硬編碼色碼
2. **深色主題由 `[data-theme="dark"]` 統一處理**，元件禁止自行實作 `@media (prefers-color-scheme: dark)`
3. **非色碼 Token（圓角、陰影、字型大小、過渡）也必須引用 CSS 變數**

---

## 色碼對應表

### 語意色 / 品牌色

| 硬編碼 | CSS 變數 |
|--------|----------|
| `#2196F3` | `var(--cl-primary)` |
| `#1976D2` | `var(--cl-primary-dark)` |
| `#e3f2fd` | `var(--cl-primary-light)` |
| `#4CAF50` | `var(--cl-success)` |
| `#2E7D32` | `var(--cl-success-dark)` |
| `#e8f5e9` | `var(--cl-success-light)` |
| `#F44336` | `var(--cl-danger)` |
| `#C62828` | `var(--cl-danger-dark)` |
| `#fdecea` | `var(--cl-danger-light)` |
| `#FF9800` | `var(--cl-warning)` |
| `#E65100` | `var(--cl-warning-dark)` |
| `#fff3e0` | `var(--cl-warning-light)` |
| `#03A9F4` | `var(--cl-info)` |

### Material 擴充色

| 硬編碼 | CSS 變數 |
|--------|----------|
| `#9C27B0` | `var(--cl-purple)` |
| `#7B1FA2` | `var(--cl-purple-dark)` |
| `#CE93D8` | `var(--cl-purple-light)` |
| `#3F51B5` | `var(--cl-indigo)` |
| `#303F9F` | `var(--cl-indigo-dark)` |
| `#009688` | `var(--cl-teal)` |
| `#00796B` | `var(--cl-teal-dark)` |
| `#00BCD4` | `var(--cl-cyan)` |
| `#00838F` | `var(--cl-cyan-dark)` |
| `#E91E63` | `var(--cl-pink)` |
| `#C2185B` | `var(--cl-pink-dark)` |
| `#FF5722` | `var(--cl-deep-orange)` |
| `#E64A19` | `var(--cl-deep-orange-dark)` |
| `#795548` | `var(--cl-brown)` |
| `#5D4037` | `var(--cl-brown-dark)` |
| `#607D8B`, `#78909C` | `var(--cl-blue-grey)` |
| `#455A64` | `var(--cl-blue-grey-dark)` |
| `#9E9E9E` | `var(--cl-grey)` |
| `#757575` | `var(--cl-grey-dark)` |
| `#BDBDBD` | `var(--cl-grey-light)` |
| `#8BC34A` | `var(--cl-light-green)` |
| `#84cc16` | `var(--cl-lime)` |
| `#FFC107` | `var(--cl-amber)` |

### 文字色

| 硬編碼 | CSS 變數 |
|--------|----------|
| `#333`, `#333333` | `var(--cl-text)` |
| `#444` | `var(--cl-text)` |
| `#555`, `#555555` | `var(--cl-text-secondary)` |
| `#666`, `#666666` | `var(--cl-text-secondary)` |
| `#888`, `#888888` | `var(--cl-text-muted)` |
| `#999`, `#999999` | `var(--cl-text-placeholder)` |
| `#aaa`, `#aaaaaa` | `var(--cl-text-light)` |
| `#b0b0b0` | `var(--cl-text-light)` |
| `#bbb` | `var(--cl-text-light)` |
| `#000`, `#000000` | `var(--cl-text-dark)` |
| `white` / `#fff` (用作文字色) | `var(--cl-text-inverse)` |
| `#495057` | `var(--cl-text-heading)` |
| `#8c8c8c` | `var(--cl-text-dim)` |

### 背景色

| 硬編碼 | CSS 變數 |
|--------|----------|
| `white`, `#fff`, `#ffffff` (背景) | `var(--cl-bg)` |
| `#f5f5f5` | `var(--cl-bg-secondary)` |
| `#f9f9f9` | `var(--cl-bg-secondary)` |
| `#f3f3f3` | `var(--cl-bg-secondary)` |
| `#f8f9fa` | `var(--cl-bg-tertiary)` |
| `#fafafa` | `var(--cl-bg-input)` |
| `#f0f2f5` | `var(--cl-bg-hover)` |
| `#f0f0f0` | `var(--cl-bg-subtle)` |
| `#e3f2fd` | `var(--cl-bg-active)` |
| `#e8e8e8` | `var(--cl-bg-hover)` |
| `#2b2b2b` | `var(--cl-bg-dark)` |
| `#37352f` | `var(--cl-bg-code)` |
| `rgba(0,0,0,0.5)` | `var(--cl-bg-overlay)` |
| `#ffeb3b` | `var(--cl-canvas-highlight)` 或 `var(--cl-amber)` |
| `#ff9800` (highlight) | `var(--cl-warning)` |
| `#ffebee` | `var(--cl-bg-danger-light)` |
| `#f1f8e9` | `var(--cl-bg-success-light)` |
| `#f0f7ff` | `var(--cl-bg-info-light)` |
| `#fff9c4` | `var(--cl-canvas-highlight)` |

### 邊框色

| 硬編碼 | CSS 變數 |
|--------|----------|
| `#ddd`, `#dddddd` | `var(--cl-border)` |
| `#e0e0e0` | `var(--cl-border)` |
| `#eee`, `#eeeeee` | `var(--cl-border-light)` |
| `#ccc`, `#cccccc` | `var(--cl-border-dark)` |
| `#dee2e6` | `var(--cl-border-medium)` |
| `#e9ecef` | `var(--cl-border-subtle)` |
| `#CFD8DC` | `var(--cl-border-muted)` |

---

## 非色碼 Token

### 圓角

| 硬編碼 | CSS 變數 |
|--------|----------|
| `border-radius: 2px` | `var(--cl-radius-xs)` |
| `border-radius: 3px` | `var(--cl-radius-xs)` |
| `border-radius: 4px` | `var(--cl-radius-sm)` |
| `border-radius: 6px` | `var(--cl-radius-md)` |
| `border-radius: 8px` | `var(--cl-radius-lg)` |
| `border-radius: 10px` | `var(--cl-radius-pill)` |
| `border-radius: 12px` | `var(--cl-radius-xl)` |
| `border-radius: 50%` | `var(--cl-radius-round)` |

複合圓角使用變數組合：`var(--cl-radius-xl) var(--cl-radius-xl) 0 0`

### 陰影

| 硬編碼 | CSS 變數 |
|--------|----------|
| `0 1px 3px rgba(0,0,0,0.1)` | `var(--cl-shadow-sm)` |
| `0 2px 4px rgba(0,0,0,0.05)` | `var(--cl-shadow-sm)` |
| `0 4px 12px rgba(0,0,0,0.15)` | `var(--cl-shadow-md)` |
| `0 4px 8px rgba(0,0,0,0.1)` | `var(--cl-shadow-md)` |
| `0 8px 24px rgba(0,0,0,0.2)` | `var(--cl-shadow-lg)` |

### 字型大小

| 硬編碼 | CSS 變數 |
|--------|----------|
| `font-size: 10px` | `var(--cl-font-size-2xs)` |
| `font-size: 11px` | `var(--cl-font-size-xs)` |
| `font-size: 12px` | `var(--cl-font-size-sm)` |
| `font-size: 13px` | `var(--cl-font-size-md)` |
| `font-size: 14px` | `var(--cl-font-size-lg)` |
| `font-size: 16px` | `var(--cl-font-size-xl)` |
| `font-size: 18px` | `var(--cl-font-size-2xl)` |
| `font-size: 24px` | `var(--cl-font-size-3xl)` |
| `font-size: 28px` | `var(--cl-font-size-4xl)` |
| `font-size: 36px` | `var(--cl-font-size-5xl)` |

### 過渡

| 硬編碼 | CSS 變數 |
|--------|----------|
| `0.1s ...` | `var(--cl-transition-fast)` |
| `0.15s ease` | `var(--cl-transition-fast)` |
| `0.2s ease` | `var(--cl-transition)` |
| `0.3s ease` | `var(--cl-transition-slow)` |
| `0.2s cubic-bezier(0.4, 0, 0.2, 1)` | `var(--cl-transition-material)` |

保留其他非標準 easing（如自訂 `cubic-bezier(...)`）。

### 字型

| 硬編碼 | CSS 變數 |
|--------|----------|
| `-apple-system, BlinkMacSystemFont, ...` | `var(--cl-font-family)` |
| `font-family: inherit` | 保留 |

---

## 保留不替換的值

- `transparent` — 保留
- `currentColor` — 保留
- `inherit` — 保留
- `rgba(0,0,0,0.08)` 等低透明度 hover 效果 — 保留（無對應變數）
- `rgba(255,255,255,0.15)` 等反色 hover 效果 — 保留
- `border-bottom-color: #fff` 用於 tab active 技巧 — 改為 `var(--cl-bg)`
- `linear-gradient(...)` 中的色碼 — 也應換成 CSS 變數

---

## 深色主題規則

1. `theme.css` 已在 `[data-theme="dark"]` 中覆寫所有 `--cl-*` 變數
2. 元件使用 CSS 變數後，切換主題自動生效
3. **移除**所有元件中的 `@media (prefers-color-scheme: dark) { ... }` 區塊
4. **移除**元件中用硬編碼色碼實作的深色模式邏輯

---

## 元件結構規範

### 原子化設計（Atomic Design）

元件按複雜度分為三層：

1. **原子（Atoms）** — 最小可重用單位，不依賴其他元件
   - 按鈕類：BasicButton, ActionButton, EditorButton, AuthButton, SortButton, UploadButton, DownloadButton
   - 輸入類：TextInput, NumberInput, Checkbox, Radio, ToggleSwitch, ColorPicker, Dropdown
   - 展示類：Badge, Tag, Tooltip, Progress, Divider, LoadingSpinner, Avatar
   - 回饋類：Notification

2. **分子（Molecules）** — 由原子組合而成
   - FormField（Label + 任意輸入原子）
   - SearchForm（TextInput + BasicButton）
   - DatePicker / TimePicker / DateTimeInput
   - Breadcrumb, Pagination, ButtonGroup

3. **有機體（Organisms）** — 由分子/原子組合的複雜元件
   - DataTable, TabContainer, FunctionMenu, SideMenu
   - Panel 系統（PanelManager + 多種 Panel 變體）
   - InfoPanel, WorkflowPanel, DocumentWall, PhotoWall

### 新元件開發規範

1. **每個元件一個資料夾**：`{category}/{ComponentName}/`
2. **必要檔案**：
   - `ComponentName.js` — 主元件類別
   - `index.js` — 重新匯出（`export { X } from './X.js';`）
   - `demo.html` — 完整展示頁面（含深色主題切換）
3. **註冊流程**：
   - 加入 `{category}/index.js` 匯出
   - 加入 `binding/ComponentFactory.js` 工廠註冊
4. **樣式方式**：使用 `_injectStyles()` 動態注入 CSS，或獨立 `.css` 檔案
5. **所有樣式值必須使用 `--cl-*` CSS 變數**，零硬編碼
