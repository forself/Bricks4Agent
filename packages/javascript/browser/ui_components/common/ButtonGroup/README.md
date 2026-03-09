# ButtonGroup

按鈕群組容器，將相關按鈕組織在一起，支援水平/垂直佈局、分隔線、多種主題。另含 `EditorToolbar` 類別可組合多個 ButtonGroup。

## API

### ButtonGroup Constructor

```js
new ButtonGroup(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.buttons` | `Array` | `[]` | 按鈕陣列（具 `element` 屬性的物件或 HTMLElement） |
| `options.direction` | `string` | `'horizontal'` | 佈局方向：`'horizontal'`/`'vertical'` |
| `options.gap` | `string` | `'4px'` | 按鈕間距 |
| `options.showSeparator` | `boolean` | `false` | 是否在群組末端顯示分隔線 |
| `options.separatorColor` | `string` | `null` | 分隔線顏色（預設依主題） |
| `options.theme` | `string` | `'light'` | 主題：`'light'`/`'dark'`/`'gradient'` |
| `options.align` | `string` | `'start'` | 對齊：`'start'`/`'center'`/`'end'` |
| `options.wrap` | `boolean` | `false` | 是否允許換行 |

### ButtonGroup 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `addButton(button)` | `this` | 新增按鈕 |
| `removeButton(index)` | `this` | 依索引移除按鈕 |
| `clear()` | `this` | 清空所有按鈕 |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

### EditorToolbar Constructor

```js
new EditorToolbar(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.groups` | `Array` | `[]` | ButtonGroup 實例陣列 |
| `options.theme` | `string` | `'light'` | 主題 |
| `options.position` | `string` | `'top'` | 位置：`'top'`/`'bottom'` |
| `options.sticky` | `boolean` | `false` | 是否黏著 |
| `options.background` | `string` | `null` | 自訂背景色 |
| `options.padding` | `string` | `'8px 12px'` | 內邊距 |

### EditorToolbar 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `addGroup(group)` | `this` | 新增群組 |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

## 使用範例

```js
import { ButtonGroup, EditorToolbar } from './ButtonGroup.js';

// 建立按鈕群組
const group = new ButtonGroup({
    buttons: [btn1, btn2, btn3],
    showSeparator: true
});
group.mount('#toolbar');

// 組合為工具列
const toolbar = new EditorToolbar({
    groups: [group1, group2],
    theme: 'dark',
    sticky: true
});
toolbar.mount('#editor');
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
