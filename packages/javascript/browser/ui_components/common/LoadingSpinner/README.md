# LoadingSpinner

載入指示器元件，提供多種動畫樣式（spinner、dots、pulse、bar），支援全螢幕遮罩和行內模式。

## API

### Constructor

```js
new LoadingSpinner(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|---|---|---|---|
| `options.variant` | `string` | `'spinner'` | 樣式：`'spinner'`/`'dots'`/`'pulse'`/`'bar'` |
| `options.size` | `string` | `'medium'` | 尺寸：`'small'`/`'medium'`/`'large'` |
| `options.color` | `string` | `'var(--cl-primary)'` | 主色彩。目前行為：只接受 hex、`rgb()`/`rgba()`/`hsl()`/`hsla()` 或具名色；傳入 `var(--cl-*)` 會被拒絕並回退為 `var(--cl-primary)` |
| `options.text` | `string` | `''` | 載入文字 |
| `options.overlay` | `boolean` | `false` | 是否顯示全螢幕遮罩 |
| `options.visible` | `boolean` | `true` | 初始可見狀態 |
| `options.zIndex` | `number` | `9999` | 遮罩層級 |

### 靜態常數

- `LoadingSpinner.VARIANTS` — `{ SPINNER, DOTS, PULSE, BAR }`

- `LoadingSpinner.SIZES` — `{ SMALL, MEDIUM, LARGE }`

### 方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `show()` | `this` | 顯示 |
| `hide()` | `this` | 隱藏 |
| `toggle()` | `this` | 切換顯示/隱藏 |
| `isVisible()` | `boolean` | 是否可見 |
| `setText(text)` | `this` | 更新載入文字；只有建構時已給非空 `text`（文字元素已存在）才有作用，否則不做任何事 |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

### 靜態方法

| 方法 | 回傳 | 說明 |
|---|---|---|
| `LoadingSpinner.showOverlay(text?, options?)` | `LoadingSpinner` | 快速顯示全螢幕載入遮罩 |

## 使用範例

```js
import { LoadingSpinner } from './LoadingSpinner.js';

// 行內使用
const spinner = new LoadingSpinner({ variant: 'dots', text: '載入中...' });
spinner.mount('#container');

// 全螢幕遮罩（靜態方法）
const overlay = LoadingSpinner.showOverlay('資料載入中...');
// 完成後銷毀
overlay.destroy();
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
