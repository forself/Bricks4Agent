# FunctionMenu - 功能選單容器組件

功能完整的功能選單容器組件，支援多種布局、尺寸、顏色主題，適用於工具欄、快捷功能、操作選單。

## 特性

- ✅ **多種布局**: Horizontal（水平）、Vertical（垂直）、Grid（網格）
- ✅ **三種尺寸**: Small、Medium、Large
- ✅ **顏色主題**: 支援 5 種預設主題顏色
- ✅ **徽章顯示**: 顯示通知數量徽章
- ✅ **圖示支援**: 支援 Font Awesome 等圖示庫
- ✅ **禁用狀態**: 支援單個項目的啟用/禁用
- ✅ **工具提示**: 支援 tooltip 提示文字
- ✅ **響應式設計**: 自動適配不同螢幕尺寸
- ✅ **深色模式**: 支援深色模式

## 使用方法

### 基本使用

```html
<!DOCTYPE html>
<html>
<head>
    <link rel="stylesheet" href="FunctionMenu.css">
</head>
<body>
    <div id="my-menu"></div>

    <script src="FunctionMenu.js"></script>
    <script>
        const menu = new FunctionMenu({
            containerId: 'my-menu',
            layout: 'horizontal',
            items: [
                {
                    id: 'new',
                    label: '新增',
                    icon: 'fas fa-plus',
                    onClick: () => alert('新增')
                },
                {
                    id: 'edit',
                    label: '編輯',
                    icon: 'fas fa-edit',
                    badge: 5,
                    onClick: () => alert('編輯')
                }
            ]
        });
    </script>
</body>
</html>
```

### 配置選項

```javascript
const menu = new FunctionMenu({
    // 容器元素 ID（必填）
    containerId: 'my-menu',

    // 布局方式: 'horizontal', 'vertical', 'grid'（預設: 'horizontal'）
    layout: 'horizontal',

    // 欄位數量（grid 模式，預設: 4）
    columns: 4,

    // 按鈕尺寸: 'small', 'medium', 'large'（預設: 'medium'）
    size: 'medium',

    // 是否顯示標籤（預設: true）
    showLabels: true,

    // 是否顯示徽章（預設: true）
    showBadges: true,

    // 初始選單項目
    items: [
        {
            id: 'item1',            // 項目唯一 ID（必填）
            label: '項目標籤',       // 標籤文字（選填）
            icon: 'fas fa-home',    // 圖示類名（選填）
            color: 'primary',       // 顏色主題（選填）
            badge: 5,               // 徽章數字（選填）
            disabled: false,        // 是否禁用（選填）
            tooltip: '提示文字',    // 工具提示（選填）
            onClick: () => {}       // 點擊回調（選填）
        }
    ],

    // 項目點擊回調（全域）
    onItemClick: (event) => {
        console.log('點擊項目:', event.itemId);
    }
});
```

## API 方法

### addItem(item)

添加新選單項目

```javascript
menu.addItem({
    id: 'new-item',
    label: '新項目',
    icon: 'fas fa-star',
    onClick: () => alert('點擊新項目')
});
```

### removeItem(itemId)

移除選單項目

```javascript
menu.removeItem('item1');
```

### updateBadge(itemId, badge)

更新項目徽章

```javascript
// 設定徽章數字
menu.updateBadge('item1', 10);

// 移除徽章
menu.updateBadge('item1', 0);
```

### enableItem(itemId)

啟用項目

```javascript
menu.enableItem('item1');
```

### disableItem(itemId)

禁用項目

```javascript
menu.disableItem('item1');
```

### updateLabel(itemId, label)

更新項目標籤

```javascript
menu.updateLabel('item1', '新標籤');
```

### setLayout(layout, columns)

改變布局

```javascript
// 切換為 Grid 布局
menu.setLayout('grid', 4);

// 切換為 Vertical 布局
menu.setLayout('vertical');
```

### setSize(size)

改變尺寸

```javascript
menu.setSize('large');
```

### getItemCount()

取得項目數量

```javascript
const count = menu.getItemCount();
```

### clearAllItems()

清空所有項目

```javascript
menu.clearAllItems();
```

### destroy()

銷毀容器

```javascript
menu.destroy();
```

## 顏色主題

支援以下預設主題顏色：

```javascript
{ color: 'primary' }   // 紫色漸變
{ color: 'success' }   // 綠色漸變
{ color: 'warning' }   // 粉紅色漸變
{ color: 'danger' }    // 紅色漸變
{ color: 'info' }      // 藍色漸變
```

## 使用範例

### 範例 1: 工具欄

```javascript
const toolbar = new FunctionMenu({
    containerId: 'toolbar',
    layout: 'horizontal',
    size: 'medium',
    items: [
        {
            id: 'new',
            label: '新增',
            icon: 'fas fa-plus',
            onClick: () => createNew()
        },
        {
            id: 'save',
            label: '儲存',
            icon: 'fas fa-save',
            onClick: () => save()
        },
        {
            id: 'delete',
            label: '刪除',
            icon: 'fas fa-trash',
            color: 'danger',
            onClick: () => confirmDelete()
        }
    ]
});
```

### 範例 2: 側邊欄導航

```javascript
const sidebar = new FunctionMenu({
    containerId: 'sidebar',
    layout: 'vertical',
    size: 'large',
    items: [
        {
            id: 'dashboard',
            label: '儀表板',
            icon: 'fas fa-tachometer-alt',
            onClick: () => navigate('/dashboard')
        },
        {
            id: 'users',
            label: '用戶管理',
            icon: 'fas fa-users',
            badge: 12,
            onClick: () => navigate('/users')
        },
        {
            id: 'settings',
            label: '設定',
            icon: 'fas fa-cog',
            onClick: () => navigate('/settings')
        }
    ]
});
```

### 範例 3: 網格功能選單

```javascript
const functions = new FunctionMenu({
    containerId: 'functions',
    layout: 'grid',
    columns: 4,
    items: [
        {
            id: 'reports',
            label: '報表',
            icon: 'fas fa-chart-bar',
            color: 'primary'
        },
        {
            id: 'export',
            label: '匯出',
            icon: 'fas fa-download',
            color: 'success'
        },
        {
            id: 'import',
            label: '匯入',
            icon: 'fas fa-upload',
            color: 'info'
        },
        {
            id: 'delete',
            label: '刪除',
            icon: 'fas fa-trash',
            color: 'danger'
        }
    ]
});
```

### 範例 4: 動態更新

```javascript
// 定時更新徽章
setInterval(() => {
    const count = getNotificationCount();
    menu.updateBadge('notifications', count);
}, 30000);

// 根據權限啟用/禁用
if (!hasPermission('delete')) {
    menu.disableItem('delete');
}
```

## 樣式自訂

### 自訂顏色

```css
/* 自訂按鈕背景 */
.menu-item-button {
    background: #f0f0f0;
}

/* 自訂懸停效果 */
.menu-item-button:hover {
    background: #e0e0e0;
}

/* 自訂圖示顏色 */
.menu-item-icon i {
    color: #ff6b6b;
}
```

### 自訂尺寸

```css
/* 調整間距 */
.function-menu {
    gap: 20px;
}

/* 調整按鈕內邊距 */
.menu-item-button {
    padding: 20px 24px;
}
```

## 注意事項

1. **項目 ID 必須唯一**: 每個選單項目的 `id` 必須是唯一的
2. **圖示依賴**: 使用圖示需引入 Font Awesome
3. **響應式**: 在小螢幕上自動調整布局
4. **點擊事件**: 支援單個項目回調和全域回調

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
