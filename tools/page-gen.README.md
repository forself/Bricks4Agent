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

## Options

| Option | Description | Default |
| --- | --- | --- |
| `--def <path>` | Input definition file | - |
| `--mode <mode>` | `static`, `dynamic`, or `both` | `static` |
| `--output <dir>` | Output directory | - |
| `--validate` | Validate only, do not generate files | `false` |
| `--list-types` | Print supported field, trigger, and optionsSource types | `false` |
| `--help`, `-h` | Show help | - |

## Modes

| Mode | Output |
| --- | --- |
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
