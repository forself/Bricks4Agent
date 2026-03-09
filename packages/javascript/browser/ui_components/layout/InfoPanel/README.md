# InfoPanel - 資訊面板容器組件

功能完整的資訊面板容器組件，支援統計數據、通知、圖表等多種面板類型展示。

## 特性

- ✅ **多種面板類型**: 統計（stat）、通知（notification）、圖表（chart）、卡片（card）
- ✅ **多種布局**: Grid（網格）、List（列表）、Masonry（瀑布流）
- ✅ **可收合**: 支援面板收合/展開
- ✅ **顏色主題**: 支援多種預設主題顏色
- ✅ **動態管理**: 動態添加、移除、更新面板
- ✅ **響應式設計**: 自動適配不同螢幕尺寸
- ✅ **深色模式**: 支援深色模式
- ✅ **動畫效果**: 平滑的動畫過渡

## 使用方法

### 基本使用

```html
<!DOCTYPE html>
<html>
<head>
    <link rel="stylesheet" href="InfoPanel.css">
</head>
<body>
    <div id="my-panels"></div>

    <script src="InfoPanel.js"></script>
    <script>
        const panels = new InfoPanel({
            containerId: 'my-panels',
            layout: 'grid',
            columns: 3,
            panels: [
                {
                    id: 'users',
                    type: 'stat',
                    title: '總用戶數',
                    icon: 'fas fa-users',
                    data: {
                        value: '12,345',
                        label: '註冊用戶',
                        change: 12.5
                    }
                }
            ]
        });
    </script>
</body>
</html>
```

### 配置選項

```javascript
const panels = new InfoPanel({
    // 容器元素 ID（必填）
    containerId: 'my-panels',

    // 布局方式: 'grid', 'list', 'masonry'（預設: 'grid'）
    layout: 'grid',

    // 欄位數量（grid 模式，預設: 3）
    columns: 3,

    // 面板是否可收合（預設: true）
    collapsible: true,

    // 面板是否可排序（預設: false）
    sortable: false,

    // 初始面板配置
    panels: [
        {
            id: 'panel1',           // 面板唯一 ID（必填）
            type: 'stat',           // 面板類型（必填）
            title: '面板標題',       // 標題（必填）
            icon: 'fas fa-home',    // 圖示類名（選填）
            color: 'primary',       // 主題顏色（選填）
            collapsible: true,      // 是否可收合（選填）
            collapsed: false,       // 初始是否收合（選填）
            data: { ... }           // 面板資料（必填）
        }
    ],

    // 面板點擊回調
    onPanelClick: (event) => {
        console.log('點擊面板:', event.panelId);
    },

    // 面板收合回調
    onPanelCollapse: (event) => {
        console.log('收合狀態:', event.collapsed);
    }
});
```

## 面板類型

### 1. 統計面板 (stat)

展示數值統計數據，支援變化趨勢

```javascript
{
    id: 'users',
    type: 'stat',
    title: '總用戶數',
    icon: 'fas fa-users',
    data: {
        value: '12,345',      // 統計值
        label: '註冊用戶',    // 標籤
        change: 12.5          // 變化百分比（正數↑ 負數↓）
    }
}
```

### 2. 通知面板 (notification)

展示通知列表

```javascript
{
    id: 'notifications',
    type: 'notification',
    title: '系統通知',
    icon: 'fas fa-bell',
    data: {
        items: [
            {
                time: '5 分鐘前',
                message: '系統更新完成'
            },
            {
                time: '1 小時前',
                message: '有新訂單'
            }
        ]
    }
}
```

### 3. 圖表面板 (chart)

展示圖表（佔位符，可整合圖表庫）

```javascript
{
    id: 'chart',
    type: 'chart',
    title: '銷售趨勢',
    icon: 'fas fa-chart-line',
    data: {
        chartType: '折線圖',
        height: '300px'
    }
}
```

### 4. 卡片面板 (card)

展示自訂內容

```javascript
{
    id: 'card',
    type: 'card',
    title: '自訂卡片',
    icon: 'fas fa-file-alt',
    data: {
        html: '<h3>自訂內容</h3><p>可放置任何 HTML</p>'
        // 或 text: '純文字內容'
        // 或直接傳入 DOM 元素
    }
}
```

## API 方法

### addPanel(panel)

添加新面板

```javascript
panels.addPanel({
    id: 'new-panel',
    type: 'stat',
    title: '新面板',
    data: { value: '100', label: '數量' }
});
```

### removePanel(panelId)

移除面板

```javascript
panels.removePanel('panel1');
```

### updatePanel(panelId, newData)

更新面板資料

```javascript
panels.updatePanel('users', {
    data: {
        value: '13,000',
        label: '註冊用戶',
        change: 15.2
    }
});
```

### toggleCollapse(panelId)

切換面板收合狀態

```javascript
panels.toggleCollapse('panel1');
```

### setLayout(layout, columns)

改變布局

```javascript
// 切換為 List 布局
panels.setLayout('list');

// 切換為 4 欄 Grid 布局
panels.setLayout('grid', 4);
```

### getPanelCount()

取得面板數量

```javascript
const count = panels.getPanelCount();
```

### clearAllPanels()

清空所有面板

```javascript
panels.clearAllPanels();
```

### destroy()

銷毀容器

```javascript
panels.destroy();
```

## 顏色主題

支援以下預設主題顏色：

```javascript
{
    color: 'primary'   // 紫色漸變
}
{
    color: 'success'   // 綠色漸變
}
{
    color: 'warning'   // 粉紅色漸變
}
{
    color: 'info'      // 藍色漸變
}
```

## 使用範例

### 範例 1: 儀表板統計

```javascript
const dashboard = new InfoPanel({
    containerId: 'dashboard',
    layout: 'grid',
    columns: 3,
    panels: [
        {
            id: 'total-users',
            type: 'stat',
            title: '總用戶數',
            icon: 'fas fa-users',
            data: {
                value: '12,345',
                label: '註冊用戶',
                change: 12.5
            }
        },
        {
            id: 'revenue',
            type: 'stat',
            title: '本月營收',
            icon: 'fas fa-dollar-sign',
            color: 'success',
            data: {
                value: '$98,765',
                label: '美元',
                change: 8.3
            }
        },
        {
            id: 'orders',
            type: 'stat',
            title: '訂單數量',
            icon: 'fas fa-shopping-cart',
            color: 'info',
            data: {
                value: '2,580',
                label: '本月訂單',
                change: -3.2
            }
        }
    ]
});
```

### 範例 2: 通知中心

```javascript
const notifications = new InfoPanel({
    containerId: 'notifications',
    layout: 'list',
    panels: [
        {
            id: 'sys-notif',
            type: 'notification',
            title: '系統通知',
            icon: 'fas fa-bell',
            data: {
                items: [
                    {
                        time: '5 分鐘前',
                        message: '系統更新完成'
                    },
                    {
                        time: '1 小時前',
                        message: '資料庫備份完成'
                    }
                ]
            }
        }
    ]
});
```

### 範例 3: 動態更新

```javascript
// 定時更新統計數據
setInterval(() => {
    const newValue = Math.floor(Math.random() * 10000);
    dashboard.updatePanel('total-users', {
        data: {
            value: newValue.toLocaleString(),
            label: '註冊用戶',
            change: (Math.random() * 20 - 10).toFixed(1)
        }
    });
}, 5000);
```

## 樣式自訂

### 自訂顏色

```css
/* 自訂面板背景 */
.info-panel {
    background: #f8f9fa;
}

/* 自訂統計值顏色 */
.stat-value {
    color: #ff6b6b;
}

/* 自訂通知項目樣式 */
.notification-item {
    background: #e3f2fd;
    border-left-color: #2196f3;
}
```

### 自訂尺寸

```css
/* 調整面板間距 */
.info-panel-container {
    gap: 30px;
}

/* 調整面板內邊距 */
.info-panel-content {
    padding: 25px;
}
```

## 注意事項

1. **面板 ID 必須唯一**: 每個面板的 `id` 必須是唯一的
2. **圖示依賴**: 使用圖示需引入 Font Awesome
3. **圖表整合**: chart 類型提供佔位符，需整合 Chart.js 等圖表庫
4. **響應式**: 在小螢幕上自動切換為單欄布局

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
