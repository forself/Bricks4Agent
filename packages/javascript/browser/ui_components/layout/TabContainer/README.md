# TabContainer - 頁籤容器組件

功能完整的頁籤容器組件，支援多頁籤介面、動態添加/移除、徽章顯示、圖示支援等功能。

## 特性

- ✅ **多種位置**: 支援頂部、底部、左側、右側四種頁籤位置
- ✅ **動態管理**: 動態添加、移除、更新頁籤
- ✅ **徽章顯示**: 顯示通知數量徽章
- ✅ **圖示支援**: 支援 Font Awesome 等圖示庫
- ✅ **可關閉**: 可配置頁籤是否可關閉
- ✅ **動畫效果**: 平滑的切換動畫
- ✅ **回調函數**: 提供切換和關閉事件回調
- ✅ **響應式設計**: 適配各種螢幕尺寸
- ✅ **深色模式**: 支援深色模式

## 使用方法

### 基本使用

```html
<!DOCTYPE html>
<html>
<head>
    <link rel="stylesheet" href="TabContainer.css">
</head>
<body>
    <div id="my-tabs"></div>

    <script src="TabContainer.js"></script>
    <script>
        const tabs = new TabContainer({
            containerId: 'my-tabs',
            position: 'top',
            tabs: [
                {
                    id: 'tab1',
                    title: '頁籤 1',
                    icon: 'fas fa-home',
                    content: '<h3>頁籤 1 內容</h3>'
                },
                {
                    id: 'tab2',
                    title: '頁籤 2',
                    icon: 'fas fa-user',
                    badge: 5,
                    content: '<h3>頁籤 2 內容</h3>'
                }
            ]
        });
    </script>
</body>
</html>
```

### 配置選項

```javascript
const tabs = new TabContainer({
    // 容器元素 ID（必填）
    containerId: 'my-tabs',

    // 頁籤位置: 'top', 'bottom', 'left', 'right'（預設: 'top'）
    position: 'top',

    // 頁籤是否可關閉（預設: true）
    closable: true,

    // 是否啟用動畫（預設: true）
    animated: true,

    // 初始頁籤配置（選填）
    tabs: [
        {
            id: 'tab1',              // 頁籤唯一 ID（必填）
            title: '頁籤標題',        // 頁籤標題（必填）
            content: '<div>內容</div>', // 頁籤內容（選填）
            icon: 'fas fa-home',     // 圖示類名（選填）
            badge: 5,                // 徽章數字（選填）
            closable: true           // 是否可關閉（選填，覆蓋全域設定）
        }
    ],

    // 頁籤切換回調
    onTabChange: (event) => {
        console.log('切換到頁籤:', event.tabId);
        console.log('前一個頁籤:', event.previousTabId);
        console.log('頁籤資料:', event.tab);
    },

    // 頁籤關閉回調
    onTabClose: (event) => {
        console.log('關閉頁籤:', event.tabId);
        // 返回 false 可取消關閉
        return confirm('確定要關閉嗎？');
    }
});
```

## API 方法

### addTab(tab)

添加新頁籤

```javascript
tabs.addTab({
    id: 'new-tab',
    title: '新頁籤',
    icon: 'fas fa-plus',
    badge: 3,
    content: '<h3>新頁籤內容</h3>',
    closable: true
});
```

### activateTab(tabId)

激活指定頁籤

```javascript
tabs.activateTab('tab1');
```

### closeTab(tabId)

關閉指定頁籤

```javascript
tabs.closeTab('tab1');
```

### updateBadge(tabId, badge)

更新頁籤徽章

```javascript
// 設定徽章數字
tabs.updateBadge('tab1', 10);

// 移除徽章
tabs.updateBadge('tab1', 0);
```

### updateTitle(tabId, title)

更新頁籤標題

```javascript
tabs.updateTitle('tab1', '新標題');
```

### updateContent(tabId, content)

更新頁籤內容

```javascript
// 使用 HTML 字串
tabs.updateContent('tab1', '<h3>新內容</h3>');

// 使用 DOM 元素
const element = document.createElement('div');
element.innerHTML = '<h3>新內容</h3>';
tabs.updateContent('tab1', element);
```

### getActiveTabId()

取得當前激活的頁籤 ID

```javascript
const activeId = tabs.getActiveTabId();
console.log('當前頁籤:', activeId);
```

### getAllTabIds()

取得所有頁籤 ID

```javascript
const allIds = tabs.getAllTabIds();
console.log('所有頁籤:', allIds); // ['tab1', 'tab2', 'tab3']
```

### getTabCount()

取得頁籤數量

```javascript
const count = tabs.getTabCount();
console.log('頁籤數量:', count);
```

### removeAllTabs()

移除所有頁籤

```javascript
tabs.removeAllTabs();
```

### destroy()

銷毀容器

```javascript
tabs.destroy();
```

## 使用範例

### 範例 1: 基本頁籤容器

```javascript
const basicTabs = new TabContainer({
    containerId: 'basic-tabs',
    position: 'top',
    tabs: [
        {
            id: 'home',
            title: '首頁',
            icon: 'fas fa-home',
            content: '<h3>歡迎來到首頁</h3>'
        },
        {
            id: 'profile',
            title: '個人資料',
            icon: 'fas fa-user',
            content: '<h3>個人資料頁面</h3>'
        }
    ]
});
```

### 範例 2: 帶徽章的頁籤

```javascript
const notificationTabs = new TabContainer({
    containerId: 'notification-tabs',
    tabs: [
        {
            id: 'inbox',
            title: '收件匣',
            icon: 'fas fa-inbox',
            badge: 12,
            content: '<h3>您有 12 封未讀郵件</h3>'
        },
        {
            id: 'sent',
            title: '寄件備份',
            icon: 'fas fa-paper-plane',
            content: '<h3>寄件備份</h3>'
        }
    ]
});

// 更新徽章數字
notificationTabs.updateBadge('inbox', 15);
```

### 範例 3: 動態添加頁籤

```javascript
const dynamicTabs = new TabContainer({
    containerId: 'dynamic-tabs',
    tabs: [
        {
            id: 'dashboard',
            title: '儀表板',
            content: '<h3>儀表板</h3>'
        }
    ]
});

// 動態添加頁籤
document.getElementById('add-tab-btn').addEventListener('click', () => {
    const tabId = `tab-${Date.now()}`;
    dynamicTabs.addTab({
        id: tabId,
        title: `新頁籤 ${dynamicTabs.getTabCount() + 1}`,
        content: `<h3>動態添加的頁籤 ${tabId}</h3>`,
        closable: true
    });
});
```

### 範例 4: 左側垂直頁籤

```javascript
const verticalTabs = new TabContainer({
    containerId: 'vertical-tabs',
    position: 'left',
    tabs: [
        {
            id: 'general',
            title: '一般設定',
            icon: 'fas fa-cog',
            content: '<h3>一般設定</h3>'
        },
        {
            id: 'security',
            title: '安全性',
            icon: 'fas fa-shield-alt',
            content: '<h3>安全性設定</h3>'
        },
        {
            id: 'privacy',
            title: '隱私權',
            icon: 'fas fa-lock',
            content: '<h3>隱私權設定</h3>'
        }
    ]
});
```

### 範例 5: 不可關閉的頁籤

```javascript
const fixedTabs = new TabContainer({
    containerId: 'fixed-tabs',
    closable: false, // 全域設定為不可關閉
    tabs: [
        {
            id: 'overview',
            title: '總覽',
            content: '<h3>總覽頁面</h3>'
        },
        {
            id: 'details',
            title: '詳細資訊',
            content: '<h3>詳細資訊</h3>'
        }
    ]
});
```

### 範例 6: 帶回調函數

```javascript
const callbackTabs = new TabContainer({
    containerId: 'callback-tabs',
    tabs: [/* ... */],
    onTabChange: (event) => {
        console.log(`從 ${event.previousTabId} 切換到 ${event.tabId}`);
        // 可在此載入頁籤內容
        loadTabContent(event.tabId);
    },
    onTabClose: (event) => {
        // 關閉前確認
        const confirmed = confirm(`確定要關閉「${event.tab.title}」嗎？`);
        if (confirmed) {
            // 執行清理工作
            cleanupTab(event.tabId);
        }
        return confirmed; // 返回 false 取消關閉
    }
});
```

## 樣式自訂

### 自訂顏色

```css
/* 自訂激活頁籤顏色 */
.tab-item.active .tab-button {
    color: #ff6b6b;
}

/* 自訂徽章顏色 */
.tab-badge {
    background: #4ecdc4;
}

/* 自訂容器背景 */
.tab-content {
    background: #f8f9fa;
}
```

### 自訂尺寸

```css
/* 調整頁籤大小 */
.tab-button {
    padding: 12px 20px;
    font-size: 16px;
}

/* 調整內容區域高度 */
.tab-container {
    min-height: 500px;
}
```

## 注意事項

1. **頁籤 ID 必須唯一**: 每個頁籤的 `id` 必須是唯一的
2. **圖示依賴**: 如使用圖示，需引入 Font Awesome 或其他圖示庫
3. **內容格式**: 頁籤內容可以是 HTML 字串或 DOM 元素
4. **響應式**: 在小螢幕上，頁籤列表會自動顯示滾動條
5. **事件回調**: `onTabClose` 返回 `false` 可取消關閉操作

## 瀏覽器支援

- Chrome 90+
- Firefox 88+
- Safari 14+
- Edge 90+

## 依賴

- 無外部依賴（純 JavaScript 實作）
- 可選：Font Awesome（用於圖示）

## 授權

MIT License

## 更新日誌

### v1.0.0 (2026-01-23)
- 初始版本
- 支援四種頁籤位置
- 動態添加/移除頁籤
- 徽章顯示功能
- 圖示支援
- 切換動畫
- 響應式設計
