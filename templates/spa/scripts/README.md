# SPA Template CLI

This directory contains the CLI scripts used to generate projects from the SPA template.

Current scripts include:

- `spa-cli.js`

- `create-project.js`

- `generate-page.js`

- `generate-api.js`

## Scope

This CLI helps scaffold SPA-style projects from the template.

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

By default `generate-api.js` tries to patch `backend/Program.cs` and
`backend/Data/AppDbContext.cs` at `// --- BRICKS:* ---` markers, but those markers
were removed from the template, so it only prints `Marker not found`. Pass
`--no-patch` (also accepted by `spa-cli.js api` / `feature`) to get the service
registration and endpoint code printed instead, then:

1. update backend schema/bootstrap in `backend/Data/AppDbContext.cs`

2. update backend routing in `backend/Program.cs`

3. frontend routes are auto-registered by `generate-page.js` in
 `frontend/pages/generated/routes.generated.js` (pass `--no-register` to skip and
 wire them yourself)

This CLI is a scaffold helper, not a full end-to-end product compiler.

## Config file

`project-config.example.json` provides a non-interactive project-creation example.

```bash
cp project-config.example.json my-project.json
node spa-cli.js new --config my-project.json
```
