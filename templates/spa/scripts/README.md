# SPA Template CLI

這個目錄包含 Bricks4Agent SPA 範本的 CLI 腳本：

- `spa-cli.js`
- `create-project.js`
- `generate-page.js`
- `generate-api.js`

## Important Note

這份文件描述的是目前支援的 CLI 介面。

## Commands

### Create a Project

```bash
node spa-cli.js new
node spa-cli.js new --name my-app --output ./projects
node spa-cli.js new --config project.json
```

### Generate a Page

```bash
node spa-cli.js page ProductList
node spa-cli.js page products/ProductDetail
node spa-cli.js page orders/OrderView --detail
```

### Generate an API

```bash
node spa-cli.js api Product
node spa-cli.js api Order --fields "CustomerId:int,Total:decimal,Status:string"
```

Supported field type aliases include:

- `string`
- `int`, `integer`
- `long`
- `decimal`
- `float`, `double`
- `bool`, `boolean`
- `datetime`, `date`
- `guid`

### Generate a Feature

```bash
node spa-cli.js feature Product
node spa-cli.js feature Order --fields "CustomerId:int,Total:decimal,Status:string"
```

## Example Workflow

```bash
cd Bricks4Agent/templates/spa/scripts
node spa-cli.js new --name my-shop
node spa-cli.js feature Product --fields "Name:string,Price:decimal,Stock:int"
```

產生後仍需手動整合：

1. `backend/Data/AppDbContext.cs`
2. `backend/Program.cs`
3. `frontend/pages/routes.js`

## Config File

`project-config.example.json` 提供非互動式建專案的範例設定。

```bash
cp project-config.example.json my-project.json
node spa-cli.js new --config my-project.json
```
