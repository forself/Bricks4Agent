# SideMenu

側邊選單元件，支援多層級、展開收合、手風琴模式、Badge 徽章。

## API

### Constructor

```js
new SideMenu(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.items` | `Array` | `[]` | 選單項目 `[{id, text, icon?, href?, children?, badge?, disabled?}]` |
| `options.activeId` | `string` | `null` | 當前選中項目 ID |
| `options.collapsed` | `boolean` | `false` | 是否收合模式 |
| `options.accordion` | `boolean` | `true` | 手風琴模式（同層只展開一個） |
| `options.width` | `string` | `'240px'` | 選單寬度 |
| `options.collapsedWidth` | `string` | `'64px'` | 收合時寬度 |
| `options.onSelect` | `Function` | `null` | 選擇回調 `(item)` |
| `options.onToggle` | `Function` | `null` | 收合回調 `(collapsed)` |

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `toggle()` | `this` | 切換展開/收合 |
| `expand()` | `this` | 展開選單 |
| `collapse()` | `this` | 收合選單 |
| `setActive(id)` | `this` | 設定選中項目 |
| `setItems(items)` | `this` | 重設選單項目 |
| `updateBadge(id, badge)` | `this` | 更新指定項目的 Badge（傳 `null`/`0` 移除） |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

## 使用範例

```js
import { SideMenu } from './SideMenu.js';

const menu = new SideMenu({
    items: [
        { id: 'home', text: '首頁', icon: '🏠' },
        { id: 'users', text: '使用者', icon: '👥', badge: 5, children: [
            { id: 'user-list', text: '使用者列表' },
            { id: 'user-add', text: '新增使用者' }
        ]}
    ],
    activeId: 'home',
    onSelect: (item) => console.log('選擇:', item.id)
});
menu.mount('#sidebar');
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
