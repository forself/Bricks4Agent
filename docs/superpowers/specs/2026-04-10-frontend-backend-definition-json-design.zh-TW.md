# 前後端 Definition JSON 設計

日期：2026-04-10  
狀態：draft  
來源：由 `templates/spa` 會員商務網站 proof 反推

## 1. 目標

本設計定義一組可機械化處理的 JSON contract，用來描述：

- 前端 SPA 如何以既有元件庫與 generator/runtime 建立頁面
- 後端如何以 `N1/N2/N3` 模板與白名單模組 materialize
- 前後端如何在技術模型上解耦，但仍可作為單一整包交付

本設計明確不追求：

- 讓前端全站都自動由單一 page form/list/detail JSON 自然長出
- 讓後端由自由文字 spec 直接產生
- 讓 Markdown 擔任 canonical source

canonical source 應為：

- `architecture.json`
- `frontend-definition.json`
- `backend-definition.json`

人類可讀文件如 `ARCHITECTURE.md`、`IMPLEMENTATION_PLAN.md`、`DEPLOYMENT.md` 應由上述 JSON 與固定規則衍生，不反向作為真相來源。

## 2. 設計依據

此設計不是憑空發明，而是由目前已可運作的 proof 反推而來。

已驗證的 proof 具備：

- 會員註冊 / 登入
- 商品列表
- 內嵌下單表單
- 訂單歷史
- 管理員登入
- 後台商品列表
- 後台新增商品
- 資料持久化

對應現有檔案：

- 前端頁面：
  - `templates/spa/frontend/pages/HomePage.js`
  - `templates/spa/frontend/pages/LoginPage.js`
  - `templates/spa/frontend/pages/RegisterPage.js`
  - `templates/spa/frontend/pages/products/ProductListPage.js`
  - `templates/spa/frontend/pages/orders/OrderListPage.js`
  - `templates/spa/frontend/pages/admin/AdminProductPage.js`
  - `templates/spa/frontend/pages/admin/AdminProductCreatePage.js`
  - `templates/spa/frontend/pages/admin/AdminProductEditPage.js`
- runtime 定義頁：
  - `templates/spa/frontend/runtime/DefinitionRuntimePage.js`
  - `templates/spa/frontend/pages/admin/productFormDefinition.js`
- 後端：
  - `templates/spa/backend/Program.cs`
  - `templates/spa/backend/Data/AppDbContext.cs`
  - `templates/spa/backend/Data/DbInitializer.cs`

此 proof 已證明：

- 前端不是全站都能只靠目前 `form/list/detail` page definition
- 但後台表單類頁面已能由 definition runtime 支撐
- 後端已能以簡單模板與 BaseOrm 完成 N2 類型的小型商務站

因此 v1 JSON contract 必須同時容納：

- 前端的 `route + surfaces` 模型
- 後端的 `tier + template + modules` 模型

## 3. Canonical 文件集合

### 3.1 `architecture.json`

用途：

- 定義專案交付的最外層邊界
- 指出是否有前端 / 後端
- 指出對應 definition 檔案

最小格式：

```json
{
  "schema_version": 1,
  "project": {
    "id": "commerce-proof",
    "name": "Bricks Commerce",
    "mode": "frontend_backend_bundle"
  },
  "frontend": {
    "enabled": true,
    "definition_file": "frontend-definition.json"
  },
  "backend": {
    "enabled": true,
    "definition_file": "backend-definition.json"
  },
  "docs": {
    "deployment": true,
    "implementation_plan": true
  }
}
```

固定規則：

- `architecture.json` 不承載頁面、欄位、ORM、API 細節
- 細節下沉到 frontend/backend definition

### 3.2 `frontend-definition.json`

用途：

- 定義前端 shell、navigation、routes、各頁 surfaces、shared enums/messages

### 3.3 `backend-definition.json`

用途：

- 定義後端是否存在、使用哪個 `N1/N2/N3` 模板、啟用哪些白名單模組、持久化與安全基線

## 4. 前端 Definition 模型

### 4.1 核心原則

前端 definition 不應再侷限於單一 `form/list/detail/dashboard` page type。  
從 proof 反推，真正需要的是：

- `system_shell`
- `content_page`
- `auth_form_page`
- `resource_list_page`
- `resource_form_page`
- `embedded_action_form`

因此 v1 frontend definition 採：

- `app`
- `auth`
- `navigation`
- `shared_resources`
- `routes[]`

### 4.2 最外層格式

```json
{
  "schema_version": 1,
  "app": {
    "title": "Bricks Commerce",
    "subtitle": "小型會員商務網站驗證",
    "shell": "default_commerce_shell"
  },
  "auth": {
    "token_storage": "local_storage",
    "login_route": "/login",
    "logout_action": "auth.logout",
    "roles": ["guest", "member", "admin"]
  },
  "navigation": {
    "items": []
  },
  "shared_resources": {
    "enums": {},
    "messages": {}
  },
  "routes": []
}
```

### 4.3 `navigation.items[]`

最小格式：

```json
{
  "label": "商品商城",
  "route": "/products",
  "visible_for": ["guest", "member", "admin"]
}
```

固定規則：

- `visible_for` 必須明確列出 role
- shell renderer 不自行猜測

### 4.4 `routes[]`

每個 route 最小欄位：

```json
{
  "path": "/products",
  "page_id": "shop_products",
  "page_kind": "resource_list_page",
  "title": "商品商城",
  "access": {
    "roles": ["guest", "member", "admin"]
  },
  "data_sources": {},
  "state": {},
  "surfaces": []
}
```

#### `page_kind` 固定枚舉

- `content_page`
- `auth_form_page`
- `resource_list_page`
- `resource_form_page`
- `detail_page`

`embedded_action_form` 不是獨立 route，而是 surface kind。

### 4.5 `surfaces[]`

`surfaces` 是本設計的核心。  
因為像商品列表頁不是單一 widget，而是數個可獨立 materialize 的 UI 區塊。

`surface_kind` v1 固定枚舉：

- `message_panel`
- `search_form`
- `data_table`
- `detail_panel`
- `embedded_action_form`
- `content_block`

#### 4.5.1 `message_panel`

```json
{
  "surface_id": "feedback",
  "surface_kind": "message_panel"
}
```

#### 4.5.2 `search_form`

```json
{
  "surface_id": "search",
  "surface_kind": "search_form",
  "fields": [
    {
      "name": "keyword",
      "field_type": "text",
      "label": "關鍵字"
    },
    {
      "name": "categoryId",
      "field_type": "select",
      "label": "分類",
      "options_ref": "category_options"
    }
  ]
}
```

#### 4.5.3 `data_table`

```json
{
  "surface_id": "product_table",
  "surface_kind": "data_table",
  "source": "primary_list",
  "columns": [
    { "key": "id", "hidden": true },
    { "key": "name", "label": "商品名稱" },
    { "key": "categoryName", "label": "分類" },
    { "key": "price", "label": "價格", "display": "currency_twd" }
  ],
  "row_actions": [
    {
      "action_id": "buy",
      "label_template": "購買 {name}",
      "action_type": "set_selected_record"
    }
  ]
}
```

#### 4.5.4 `embedded_action_form`

```json
{
  "surface_id": "order_form",
  "surface_kind": "embedded_action_form",
  "visible_when": {
    "selected_item_key": "selectedProductId"
  },
  "submit": {
    "method": "POST",
    "endpoint": "/shop/orders"
  },
  "fields": [
    {
      "name": "quantity",
      "field_type": "number",
      "label": "數量",
      "required": true
    },
    {
      "name": "shippingAddress",
      "field_type": "text",
      "label": "收件地址",
      "required": true
    }
  ]
}
```

### 4.6 `shared_resources`

用途：

- 提供 options、enum label mapping、固定訊息等共用定義

最小格式：

```json
{
  "enums": {
    "category_options": [
      { "label": "Digital Goods", "value": "1" },
      { "label": "Member Services", "value": "2" }
    ],
    "status_options": [
      { "label": "啟用", "value": "active" },
      { "label": "停用", "value": "inactive" }
    ]
  },
  "messages": {
    "product_created": "商品已建立"
  }
}
```

## 5. 後端 Definition 模型

### 5.1 核心原則

後端不是 JSON runtime 渲染，而是：

- 選 `N1/N2/N3`
- 選 template
- 套白名單模組
- materialize 成專案骨架

### 5.2 tier taxonomy

#### `N1`

- 無持久化
- 單層
- 固定帶最小公開安全基線：
  - validation
  - exception handling
  - audit/logging
  - rate limit

#### `N2`

- 有持久化
- 微型兩層
- `BO + DAO` 壓在一起
- 固定帶完整高資安基線：
  - controller/API
  - service
  - repository/dao
  - model/schema
  - config
  - auth
  - validation
  - security middleware

#### `N3`

- 一般三層
- 與 `N2` 相同安全基線，但分層更完整

### 5.3 最外層格式

```json
{
  "schema_version": 1,
  "enabled": true,
  "tier": "N2",
  "template": "base_n2_commerce",
  "persistence": {
    "enabled": true,
    "orm": "BaseOrm",
    "database": "sqlite"
  },
  "security": {
    "auth_required": true,
    "auth_mode": "local_jwt",
    "role_model": ["member", "admin"]
  },
  "entities": [],
  "modules": [],
  "seed": {}
}
```

### 5.4 `orm` 規則

固定規則：

- 預設 ORM：`BaseOrm`
- 僅在業務邏輯足夠複雜且經明確判定時，才允許其他 ORM
- 因此一般後端模組不應反覆審批 ORM 選型

### 5.5 `modules[]`

`modules` 為白名單集合，不接受自由字串語意發明。  
此 proof 最小集合：

- `auth`
- `shop_catalog`
- `shop_order`
- `admin_product`

### 5.6 `entities[]`

此 proof 的 N2 commerce 後端至少需要：

- `User`
- `Category`
- `Product`
- `Order`
- `OrderItem`

### 5.7 `seed`

```json
{
  "admin_account": true,
  "sample_products": true,
  "sample_categories": true
}
```

## 6. 會員商務網站實例

### 6.1 `architecture.json`

```json
{
  "schema_version": 1,
  "project": {
    "id": "commerce-proof",
    "name": "Bricks Commerce",
    "mode": "frontend_backend_bundle"
  },
  "frontend": {
    "enabled": true,
    "definition_file": "frontend-definition.json"
  },
  "backend": {
    "enabled": true,
    "definition_file": "backend-definition.json"
  },
  "docs": {
    "deployment": true,
    "implementation_plan": true
  }
}
```

### 6.2 `frontend-definition.json`

```json
{
  "schema_version": 1,
  "app": {
    "title": "Bricks Commerce",
    "subtitle": "小型會員商務網站驗證",
    "shell": "default_commerce_shell"
  },
  "auth": {
    "token_storage": "local_storage",
    "login_route": "/login",
    "logout_action": "auth.logout",
    "roles": ["guest", "member", "admin"]
  },
  "navigation": {
    "items": [
      { "label": "首頁", "route": "/", "visible_for": ["guest", "member", "admin"] },
      { "label": "商品商城", "route": "/products", "visible_for": ["guest", "member", "admin"] },
      { "label": "我的訂單", "route": "/orders", "visible_for": ["member", "admin"] },
      { "label": "後台商品", "route": "/admin/products", "visible_for": ["admin"] },
      { "label": "註冊", "route": "/register", "visible_for": ["guest"] },
      { "label": "登入", "route": "/login", "visible_for": ["guest"] }
    ]
  },
  "shared_resources": {
    "enums": {
      "category_options": [
        { "label": "Digital Goods", "value": "1" },
        { "label": "Member Services", "value": "2" }
      ],
      "status_options": [
        { "label": "啟用", "value": "active" },
        { "label": "停用", "value": "inactive" }
      ]
    },
    "messages": {
      "product_created": "商品已建立",
      "order_created": "訂單已建立"
    }
  },
  "routes": [
    {
      "path": "/products",
      "page_id": "shop_products",
      "page_kind": "resource_list_page",
      "title": "商品商城",
      "access": { "roles": ["guest", "member", "admin"] },
      "data_sources": {
        "primary_list": {
          "method": "GET",
          "endpoint": "/shop/products"
        }
      },
      "state": {
        "selected_item_key": "selectedProductId",
        "flash_message_key": "message",
        "error_message_key": "error"
      },
      "surfaces": [
        { "surface_id": "feedback", "surface_kind": "message_panel" },
        {
          "surface_id": "search",
          "surface_kind": "search_form",
          "fields": [
            { "name": "keyword", "field_type": "text", "label": "關鍵字" },
            { "name": "categoryId", "field_type": "select", "label": "分類", "options_ref": "category_options" },
            { "name": "status", "field_type": "select", "label": "狀態", "options_ref": "status_options" }
          ]
        },
        {
          "surface_id": "product_table",
          "surface_kind": "data_table",
          "source": "primary_list",
          "columns": [
            { "key": "id", "hidden": true },
            { "key": "name", "label": "商品名稱" },
            { "key": "categoryName", "label": "分類" },
            { "key": "price", "label": "價格", "display": "currency_twd" },
            { "key": "stock", "label": "庫存" },
            { "key": "statusLabel", "label": "狀態" }
          ],
          "row_actions": [
            { "action_id": "buy", "label_template": "購買 {name}", "action_type": "set_selected_record" }
          ]
        },
        {
          "surface_id": "order_form",
          "surface_kind": "embedded_action_form",
          "visible_when": { "selected_item_key": "selectedProductId" },
          "submit": { "method": "POST", "endpoint": "/shop/orders" },
          "fields": [
            { "name": "quantity", "field_type": "number", "label": "數量", "required": true },
            { "name": "shippingAddress", "field_type": "text", "label": "收件地址", "required": true },
            { "name": "note", "field_type": "textarea", "label": "備註" }
          ]
        }
      ]
    }
  ]
}
```

### 6.3 `backend-definition.json`

```json
{
  "schema_version": 1,
  "enabled": true,
  "tier": "N2",
  "template": "base_n2_commerce",
  "persistence": {
    "enabled": true,
    "orm": "BaseOrm",
    "database": "sqlite"
  },
  "security": {
    "auth_required": true,
    "auth_mode": "local_jwt",
    "role_model": ["member", "admin"]
  },
  "entities": [
    "User",
    "Category",
    "Product",
    "Order",
    "OrderItem"
  ],
  "modules": [
    "auth",
    "shop_catalog",
    "shop_order",
    "admin_product"
  ],
  "seed": {
    "admin_account": true,
    "sample_products": true,
    "sample_categories": true
  }
}
```

## 7. 與現有 generator/runtime 的關係

### 7.1 可直接沿用的部分

- `resource_form_page`
  - 可映射到既有 `DefinitionRuntimePage`
- 現有元件庫的 field types
- metadata 正規化後的 component catalog

### 7.2 需要補強的部分

- `resource_list_page` 目前不是完整 JSON runtime page
  - 仍需要 `search_form + data_table + row_actions` 的 surface materializer
- `embedded_action_form`
  - 目前是手寫頁面邏輯，需要 JSON 化
- `system_shell`
  - 目前由 layout/page JS 寫死，需要抽成可描述的 shell contract

## 8. 不採用的方向

### 8.1 不採單一 page JSON 包打天下

理由：

- proof 已顯示真實頁面是多 surface，而不是單一 form/list/detail
- 若硬塞進單一 page type，只會把 schema 變得又亂又假

### 8.2 不採 Markdown 作 canonical source

理由：

- 難以機械驗證
- 容易漂移
- 不適合 deterministic materialization

### 8.3 不讓 backend 走 JSON runtime render

理由：

- 後端更適合 template + module composition
- 與現有 `templates/spa/backend` 的真實能力一致

## 9. 下一步

下一步應分成三件事：

1. 凍結 `architecture.json` / `frontend-definition.json` / `backend-definition.json` schema
2. 寫 `resource_list_page` 與 `embedded_action_form` 的 deterministic materializer
3. 讓這次會員商務站 proof 可以直接改由這三份 JSON materialize 出來，而不是混合手寫頁面

## 10. 評價

這套 contract 的價值在於：

- 它不是理想化 DSL
- 它是從已能跑通的會員商務站 proof 反推
- 它保留前後端非對稱模型
- 它給未來代理一個真實可落地的結構化目標

它目前的弱點也很清楚：

- 前端 list/shell/action 還需要補 deterministic materializer
- 現有 runtime 仍偏 form-oriented

## Implementation Status

- `architecture.json` 已作為 proof app 的 bootstrap 入口，前端在啟動時會先載入 architecture 與 frontend definition。
- `frontend-definition.json` 已接入 proof app 的 shell、導航、route bootstrap 與 shared resources。
- `backend-definition.json` 已接入 backend startup，會被 materialize 後註冊進 DI，並參與 seed/bootstrap 基線。
- 目前 proof 已驗證前後端定義檔可支撐小型會員商務網站，但 frontend `route + surfaces` 仍是 proof 導向的最小 materialization，尚未覆蓋完整通用頁型。

但即使如此，這套 JSON contract 已比目前單純 `PageDefinition` 更接近真實網站開發需求。
