# SPA Template CLI

This directory contains the CLI scripts used to generate projects from the SPA template.

Current scripts include:

- `spa-cli.js`
- `create-project.js`
- `generate-page.js`
- `generate-api.js`

## Scope

This CLI helps scaffold SPA-style projects from the template.

It does not describe the broker control plane, the LINE sidecar, or the production-style governed runtime.

## Commands

### Create a project

```bash
node spa-cli.js new
node spa-cli.js new --name my-app --output ./projects
node spa-cli.js new --config project.json
```

### Generate a page

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

Supported field aliases include:

- `string`
- `int`, `integer`
- `long`
- `decimal`
- `float`, `double`
- `bool`, `boolean`
- `datetime`, `date`
- `guid`

### Generate a feature

```bash
node spa-cli.js feature Product
node spa-cli.js feature Order --fields "CustomerId:int,Total:decimal,Status:string"
```

## Example workflow

```bash
cd templates/spa/scripts
node spa-cli.js new --name my-shop
node spa-cli.js feature Product --fields "Name:string,Price:decimal,Stock:int"
```

## Important limitation

Generated output still needs manual integration.

Typical follow-up work includes:

1. update backend schema/bootstrap in `backend/Data/AppDbContext.cs`
2. update backend routing in `backend/Program.cs`
3. update frontend routing in `frontend/pages/routes.js`

This CLI is a scaffold helper, not a full end-to-end product compiler.

## Config file

`project-config.example.json` provides a non-interactive project-creation example.

```bash
cp project-config.example.json my-project.json
node spa-cli.js new --config my-project.json
```
