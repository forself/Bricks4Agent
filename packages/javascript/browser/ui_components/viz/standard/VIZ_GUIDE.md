# 統一視覺化引擎使用指南 (Unified Visualization Engine Guide)

## 數據協議：JSON 物件陣列 (JSON Array Protocol)

為了兼顧開發效率與直觀性，本引擎採用 **JSON 物件陣列** 作為標準輸入格式。
This engine adopts the **Array of JSON Objects** as the standard input format for balance between efficiency and intuitiveness.

### 解決了什麼問題？ (Problem Solved)

*   **無需 Mapping**: 欄位名稱直接寫在 JSON Key 中 (如 `id`, `label`)，通常情況下如果不需轉名，連 `mapping` 設定都可省略。
*   **保持扁平 (Flat)**: 後端不需組裝複雜的樹狀結構 (`children: []`)，只需回傳扁平的列表 (`List`)。

---

## 1. 怎麼用 (How to Use)

### 範例：組織圖 (Org Chart) - 樹狀結構

**後端回傳數據 (Backend Data):**
扁平的物件陣列，只需包含 `id` 與 `parentId`。

```javascript
const employees = [
    { id: "1001", parentId: null, label: "CEO John" },
    { id: "1002", parentId: "1001", label: "CTO Jane" },
    { id: "1003", parentId: "1002", label: "Dev Manager" }
];
```

**前端渲染 (Frontend Render):**

```javascript
viz.render({
    type: 'org',
    container: '#chart',
    data: employees
    // 無需 mapping，因為 key 已經是標準的 id/parentId/label
});
```

*若後端欄位名稱不同 (例如 `emp_id`)，可使用 mapping 進行別名轉換：*

```javascript
viz.render({
    // ...
    mapping: { id: 'emp_id', parentId: 'manager_id' }
});
```

---

### 範例：關聯圖 (Relation Chart) - 網絡結構

**後端回傳數據 (Backend Data):**
每一列代表一條「連線 (Link)」。

```javascript
const connections = [
    { source: "UserA", target: "UserB", value: 5 },
    { source: "UserB", target: "UserC", value: 10 }
];
```

**前端渲染 (Frontend Render):**

```javascript
viz.render({
    type: 'relation',
    container: '#chart',
    data: connections
});
```

*引擎會自動從連線中提取不重複的節點 (Nodes)。*

---

### 範例：時間軸 (Timeline) - 列表

**後端回傳數據 (Backend Data):**

```javascript
const tasks = [
    { label: "Task 1", group: "Server", start: 1680000000, end: 1680001000 },
    { label: "Task 2", group: "Client", start: 1680000500, end: 1680002000 }
];
```

**前端渲染 (Frontend Render):**

```javascript
viz.render({
    type: 'timeline',
    container: '#chart',
    data: tasks
});
```

---

## 2. 自動化轉換 (Automatic Transformation)

引擎內建了強大的轉換邏輯：

1.  **Flat-to-Tree**: 對於 `org`, `hierarchy`, `flame` 等圖表，引擎會自動掃描數據中的 `parentId` 欄位，將扁平列表重組為樹狀結構。
2.  **別名系統 (Aliasing)**: 透過 `mapping` 參數，您可以輕鬆對接任何後端既有的欄位命名，而無需修改後端 API。

---

## 3. 事件與互動 (Events)

事件回調將直接回傳該列的 JSON 物件，方便您直接取用數據。

```javascript
viz.render({
    // ...
    events: {
        click: (action) => alert(`Clicked on ${action.label}`) 
        // action: { id: "1001", label: "CEO John", ... }
    }
});
```

---

## 4. 資安與驗證 (Security & Authentication)

### 4.1 JWT 整合 (JWT Integration)

引擎內建 `fetch` 方法，支援 JWT Bearer Token 驗證，並符合資安規範（不將 Token 暴露於 Log）。

```javascript
// 1. 設定 Token (通常在登入後執行)
viz.setAuth('eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...');

// 2. 使用安全 Fetch 獲取數據
viz.fetch('/api/v1/employees')
   .then(data => {
       viz.render({
           type: 'org',
           container: '#chart',
           data: data
       });
   })
   .catch(err => {
       // 錯誤處理 (Error Handling)
   });
```

### 4.2 XSS 防護 (XSS Prevention)

*   **自動過濾**: 引擎內部所有 Detail Card 與 Tooltip 渲染皆已強制實作 `escapeHtml()`。
*   **數據內容**: 即便後端回傳含有 `<script>` 的惡意字串，在前端也會被轉義為純文字顯示 (`&lt;script&gt;`)，確保不會執行惡意代碼。

