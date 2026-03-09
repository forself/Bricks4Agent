# Pagination

分頁器元件，提供頁碼導航、每頁數量選擇、快速跳頁功能。

## API

### Constructor

```js
new Pagination(options?)
```

| 參數 | 型別 | 預設值 | 說明 |
|------|------|--------|------|
| `options.total` | `number` | `0` | 總資料筆數 |
| `options.page` | `number` | `1` | 當前頁碼 (1-based) |
| `options.pageSize` | `number` | `20` | 每頁筆數 |
| `options.pageSizeOptions` | `number[]` | `[10,20,50,100]` | 每頁筆數選項 |
| `options.visiblePages` | `number` | `5` | 顯示頁碼數量 |
| `options.showTotal` | `boolean` | `true` | 顯示總筆數 |
| `options.showPageSize` | `boolean` | `true` | 顯示每頁筆數選擇器 |
| `options.showJumper` | `boolean` | `true` | 顯示跳頁輸入框 |
| `options.onChange` | `Function` | `null` | 頁碼/筆數變更回調 `(page, pageSize)` |

### 方法

| 方法 | 回傳 | 說明 |
|------|------|------|
| `goTo(page)` | `this` | 跳至指定頁碼 |
| `setPageSize(pageSize)` | `this` | 設定每頁筆數 |
| `setTotal(total)` | `this` | 設定總筆數 |
| `getState()` | `Object` | 取得目前狀態 `{page, pageSize, total, totalPages}` |
| `mount(container)` | `this` | 掛載至容器 |
| `destroy()` | `void` | 銷毀元件 |

### 屬性

| 屬性 | 說明 |
|------|------|
| `totalPages` | 計算屬性，總頁數 |

## 使用範例

```js
import { Pagination } from './Pagination.js';

const pager = new Pagination({
    total: 200,
    pageSize: 20,
    onChange: (page, pageSize) => {
        console.log(`第 ${page} 頁，每頁 ${pageSize} 筆`);
    }
});
pager.mount('#pagination');
```

## Demo

開啟 `demo.html` 直接在瀏覽器測試。
