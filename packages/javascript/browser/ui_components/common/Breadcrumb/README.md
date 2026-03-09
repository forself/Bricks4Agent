# Breadcrumb

麵包屑導航元件，支援自訂分隔符號、首頁圖示、導航回調。

## API

### Constructor

```js
new Breadcrumb(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.items` | `Array` | `[]` | 導航項目 `[{text, href?, icon?, active?}]` |
| `options.separator` | `string` | `'/'` | 分隔符號 |
| `options.homeIcon` | `string` | `'🏠'` | 首頁圖示 |
| `options.showHome` | `boolean` | `true` | 是否顯示首頁 |
| `options.homeHref` | `string` | `'#/'` | 首頁連結 |
| `options.onNavigate` | `Function` | `null` | 導航回調 `(item, index)` |

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `setItems(items)` | `this` | 重設導航項目 |
| `push(item)` | `this` | 新增一層導航 |
| `pop()` | `this` | 移除最後一層導航 |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

## 使用範例

```js
import { Breadcrumb } from './Breadcrumb.js';

const bc = new Breadcrumb({
    items: [
        { text: '使用者管理', href: '#/users' },
        { text: '編輯使用者' }
    ],
    onNavigate: (item) => console.log('導航至:', item.text)
});
bc.mount('#breadcrumb');
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
