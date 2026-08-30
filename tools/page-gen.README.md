# page-gen CLI

`tools/page-gen.js` 會把 PageDefinition 轉成靜態頁面程式碼，或輸出動態定義 JSON。

## Usage

```bash
node tools/page-gen.js [options]
```

## Common Commands

### Validate a definition

```bash
node tools/page-gen.js --validate --def employee.json
```

### Generate a static page

```bash
node tools/page-gen.js --def employee.json --mode static --output ./output/
```

### List supported types

```bash
node tools/page-gen.js --list-types
```

### Generate from a DefinitionTemplate

單頁需以 `--page` 指定；批次則用 `--pages` 或 `--all`，在同一個 Node 行程內完成
（模板只解析與驗證一次，輸出與逐頁執行逐位相同），並輸出彙總 JSON。

```bash
node tools/page-gen.js --def site-definition.json --page products-list --mode static --output ./output/
node tools/page-gen.js --def site-definition.json --pages products-list,orders-list --mode static --output ./output/
node tools/page-gen.js --def site-definition.json --all --mode both --output ./output/
```

## Options

| Option | Description | Default |
|---|---|---|
| `--def <path>` | Input definition file | - |
| `--page <id>` | DefinitionTemplate 內要選用的單一 page id | - |
| `--pages <ids>` | DefinitionTemplate 內要批次處理的 page id（逗號分隔） | - |
| `--all` | 批次處理 DefinitionTemplate 內所有 pages | `false` |
| `--mode <mode>` | `static`, `dynamic`, or `both` | `static` |
| `--output <dir>` | Output directory | - |
| `--validate` | Validate only, do not generate files | `false` |
| `--list-types` | Print supported field, trigger, and optionsSource types | `false` |
| `--help`, `-h` | Show help | - |

## Modes

| Mode | Output |
|---|---|
| `static` | `<Entity>Page.js` |
| `dynamic` | `<entity>-definition.json` |
| `both` | Both outputs |

## stdin Support

```bash
cat employee.json | node tools/page-gen.js --mode static --output ./output/
```

## Validation and Test Entry Points

目前 repo 內可直接執行的測試入口在 `packages/javascript/browser/page-generator/examples/`。

```bash
npm test
node packages/javascript/browser/page-generator/examples/test-all.js
node packages/javascript/browser/page-generator/examples/test-generator.js
```

## Example Definition

可參考：

- `packages/javascript/browser/page-generator/examples/EmployeeDefinition.js`

- `packages/javascript/browser/page-generator/examples/DiaryEditorDefinition.js`

- `packages/javascript/browser/page-generator/examples/ContactFormDefinition.js`

## Related Files

- `packages/javascript/browser/page-generator/README.md`

- `packages/javascript/browser/page-generator/PageDefinitionAdapter.js`

- `packages/javascript/browser/page-generator/examples/`
