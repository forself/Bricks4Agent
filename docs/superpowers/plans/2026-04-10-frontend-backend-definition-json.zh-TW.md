# Frontend/Backend Definition JSON Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 讓 `architecture.json`、`frontend-definition.json`、`backend-definition.json` 成為可執行的 canonical source，並能 materialize 出與會員商務網站 proof 等價的前後端專案骨架。

**Architecture:** 前端採 `route + surfaces` 定義，經 deterministic frontend materializer 轉成既有 `templates/spa/frontend` 可執行頁面與 runtime definition；後端採 `N1/N2/N3 + template + modules` 定義，經 deterministic backend materializer 套用既有 `templates/spa/backend` 模板與白名單模組。JSON 只描述結構與選項，materializer 不接受自然語言，不做 AI 決策。

**Tech Stack:** JavaScript ESM、Vitest、Playwright、.NET 8、BaseOrm、既有 `templates/spa`、既有 `page-generator/runtime`

---

## File Structure

### Create

- `packages/javascript/browser/definition-json/architecture-schema.js`
- `packages/javascript/browser/definition-json/frontend-schema.js`
- `packages/javascript/browser/definition-json/backend-schema.js`
- `packages/javascript/browser/definition-json/validators.js`
- `packages/javascript/browser/definition-json/load-definition.mjs`
- `templates/spa/frontend/runtime/materializers/FrontendDefinitionMaterializer.js`
- `templates/spa/frontend/runtime/materializers/surfaces/MessagePanelSurface.js`
- `templates/spa/frontend/runtime/materializers/surfaces/SearchFormSurface.js`
- `templates/spa/frontend/runtime/materializers/surfaces/DataTableSurface.js`
- `templates/spa/frontend/runtime/materializers/surfaces/EmbeddedActionFormSurface.js`
- `templates/spa/frontend/runtime/pages/DefinitionListPage.js`
- `templates/spa/frontend/runtime/pages/DefinitionContentPage.js`
- `templates/spa/frontend/runtime/pages/DefinitionAuthFormPage.js`
- `templates/spa/backend/Generated/DefinitionBackendMaterializer.cs`
- `templates/spa/backend/Generated/DefinitionBackendModels.cs`
- `templates/spa/backend/Generated/DefinitionBackendModuleRegistry.cs`
- `templates/spa/frontend/definition/architecture.json`
- `templates/spa/frontend/definition/frontend-definition.json`
- `templates/spa/backend/definition/backend-definition.json`
- `packages/javascript/browser/__tests__/definition-json/DefinitionValidation.test.js`
- `packages/javascript/browser/__tests__/definition-json/FrontendDefinitionMaterializer.test.js`
- `templates/spa/backend.Tests/DefinitionBackendMaterializerTests.cs`

### Modify

- `templates/spa/frontend/runtime/DefinitionRuntimePage.js`
- `templates/spa/frontend/pages/routes.js`
- `templates/spa/frontend/core/App.js`
- `templates/spa/frontend/pages/admin/productFormDefinition.js`
- `tests/e2e/ui/spa-commerce-proof.spec.ts`
- `tests/e2e/playwright.config.ts`
- `templates/spa/backend/Program.cs`
- `templates/spa/backend/Data/DbInitializer.cs`
- `templates/spa/backend/Generated/DefinitionTemplateGeneratedComposition.cs`

### Reuse

- `packages/javascript/browser/page-generator/PageDefinitionAdapter.js`
- `packages/javascript/browser/page-generator/DynamicPageRenderer.js`
- `packages/javascript/browser/page-generator/DynamicFormRenderer.js`
- `packages/javascript/browser/ui_components/form/SearchForm/SearchForm.js`
- `packages/javascript/browser/ui_components/layout/DataTable/DataTable.js`
- `templates/spa/backend/Data/AppDbContext.cs`
- `templates/spa/backend/Services/AuthService.cs`

---

## Task 1: 凍結 Definition JSON schema 與載入器

**Files:**
- Create: `packages/javascript/browser/definition-json/architecture-schema.js`
- Create: `packages/javascript/browser/definition-json/frontend-schema.js`
- Create: `packages/javascript/browser/definition-json/backend-schema.js`
- Create: `packages/javascript/browser/definition-json/validators.js`
- Create: `packages/javascript/browser/definition-json/load-definition.mjs`
- Test: `packages/javascript/browser/__tests__/definition-json/DefinitionValidation.test.js`

- [ ] **Step 1: Write the failing validation tests**

```js
import { describe, it, expect } from 'vitest';
import { validateArchitecture, validateFrontendDefinition, validateBackendDefinition } from '../../definition-json/validators.js';

describe('Definition validation', () => {
    it('accepts the minimal architecture contract', () => {
        const result = validateArchitecture({
            schema_version: 1,
            project: { id: 'commerce-proof', name: 'Bricks Commerce', mode: 'frontend_backend_bundle' },
            frontend: { enabled: true, definition_file: 'frontend-definition.json' },
            backend: { enabled: true, definition_file: 'backend-definition.json' },
            docs: { deployment: true, implementation_plan: true }
        });

        expect(result.valid).toBe(true);
        expect(result.errors).toEqual([]);
    });

    it('rejects a route without surfaces', () => {
        const result = validateFrontendDefinition({
            schema_version: 1,
            app: { title: 'x', subtitle: 'y', shell: 'default' },
            auth: { token_storage: 'local_storage', login_route: '/login', logout_action: 'auth.logout', roles: ['guest'] },
            navigation: { items: [] },
            shared_resources: { enums: {}, messages: {} },
            routes: [{ path: '/products', page_id: 'shop_products', page_kind: 'resource_list_page', title: 'Products', access: { roles: ['guest'] } }]
        });

        expect(result.valid).toBe(false);
        expect(result.errors).toContain('Route /products must declare surfaces');
    });

    it('rejects unsupported backend tier', () => {
        const result = validateBackendDefinition({
            schema_version: 1,
            enabled: true,
            tier: 'N9',
            template: 'x',
            persistence: { enabled: true, orm: 'BaseOrm', database: 'sqlite' },
            security: { auth_required: true, auth_mode: 'local_jwt', role_model: ['member'] },
            entities: [],
            modules: [],
            seed: {}
        });

        expect(result.valid).toBe(false);
        expect(result.errors).toContain('Unsupported backend tier: N9');
    });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm.cmd --prefix packages/javascript/browser run test -- DefinitionValidation.test.js`  
Expected: FAIL with missing `definition-json` modules.

- [ ] **Step 3: Implement minimal schema validators**

```js
const FRONTEND_PAGE_KINDS = new Set([
    'content_page',
    'auth_form_page',
    'resource_list_page',
    'resource_form_page',
    'detail_page'
]);

const BACKEND_TIERS = new Set(['N1', 'N2', 'N3']);

export function validateArchitecture(input) {
    const errors = [];
    if (input?.schema_version !== 1) errors.push('architecture.schema_version must be 1');
    if (!input?.project?.id) errors.push('architecture.project.id is required');
    if (!input?.frontend?.definition_file) errors.push('architecture.frontend.definition_file is required');
    if (!input?.backend?.definition_file) errors.push('architecture.backend.definition_file is required');
    return { valid: errors.length === 0, errors };
}

export function validateFrontendDefinition(input) {
    const errors = [];
    if (input?.schema_version !== 1) errors.push('frontend.schema_version must be 1');
    for (const route of input?.routes || []) {
        if (!FRONTEND_PAGE_KINDS.has(route.page_kind)) {
            errors.push(`Unsupported page_kind: ${route.page_kind}`);
        }
        if (!Array.isArray(route.surfaces) || route.surfaces.length === 0) {
            errors.push(`Route ${route.path} must declare surfaces`);
        }
    }
    return { valid: errors.length === 0, errors };
}

export function validateBackendDefinition(input) {
    const errors = [];
    if (input?.schema_version !== 1) errors.push('backend.schema_version must be 1');
    if (!BACKEND_TIERS.has(input?.tier)) errors.push(`Unsupported backend tier: ${input?.tier}`);
    if (!input?.template) errors.push('backend.template is required');
    if (!input?.persistence?.orm) errors.push('backend.persistence.orm is required');
    return { valid: errors.length === 0, errors };
}
```

- [ ] **Step 4: Add a deterministic JSON loader**

```js
import fs from 'node:fs';
import path from 'node:path';
import { validateArchitecture, validateFrontendDefinition, validateBackendDefinition } from './validators.js';

export function loadDefinitionFile(rootDir, relativePath, validator) {
    const fullPath = path.resolve(rootDir, relativePath);
    const parsed = JSON.parse(fs.readFileSync(fullPath, 'utf8'));
    const result = validator(parsed);
    if (!result.valid) {
        throw new Error(`Invalid definition file ${relativePath}: ${result.errors.join('; ')}`);
    }
    return parsed;
}

export function loadDefinitionBundle(rootDir, architecturePath) {
    const architecture = loadDefinitionFile(rootDir, architecturePath, validateArchitecture);
    const frontend = architecture.frontend?.enabled
        ? loadDefinitionFile(rootDir, architecture.frontend.definition_file, validateFrontendDefinition)
        : null;
    const backend = architecture.backend?.enabled
        ? loadDefinitionFile(rootDir, architecture.backend.definition_file, validateBackendDefinition)
        : null;
    return { architecture, frontend, backend };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `npm.cmd --prefix packages/javascript/browser run test -- DefinitionValidation.test.js`  
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add packages/javascript/browser/definition-json packages/javascript/browser/__tests__/definition-json/DefinitionValidation.test.js
git commit -m "feat: add definition json schema validation"
```

---

## Task 2: 讓 frontend-definition 可 materialize form/content/list page

**Files:**
- Create: `templates/spa/frontend/runtime/materializers/FrontendDefinitionMaterializer.js`
- Create: `templates/spa/frontend/runtime/materializers/surfaces/MessagePanelSurface.js`
- Create: `templates/spa/frontend/runtime/materializers/surfaces/SearchFormSurface.js`
- Create: `templates/spa/frontend/runtime/materializers/surfaces/DataTableSurface.js`
- Create: `templates/spa/frontend/runtime/materializers/surfaces/EmbeddedActionFormSurface.js`
- Create: `templates/spa/frontend/runtime/pages/DefinitionListPage.js`
- Create: `templates/spa/frontend/runtime/pages/DefinitionContentPage.js`
- Create: `templates/spa/frontend/runtime/pages/DefinitionAuthFormPage.js`
- Modify: `templates/spa/frontend/runtime/DefinitionRuntimePage.js`
- Test: `packages/javascript/browser/__tests__/definition-json/FrontendDefinitionMaterializer.test.js`

- [ ] **Step 1: Write the failing materializer tests**

```js
import { describe, it, expect } from 'vitest';
import { FrontendDefinitionMaterializer } from '../../../../templates/spa/frontend/runtime/materializers/FrontendDefinitionMaterializer.js';

describe('FrontendDefinitionMaterializer', () => {
    const frontendDefinition = {
        schema_version: 1,
        app: { title: 'Bricks Commerce', subtitle: 'proof', shell: 'default' },
        auth: { token_storage: 'local_storage', login_route: '/login', logout_action: 'auth.logout', roles: ['guest', 'member', 'admin'] },
        navigation: { items: [] },
        shared_resources: {
            enums: {
                category_options: [{ label: 'Digital Goods', value: '1' }]
            },
            messages: {}
        },
        routes: [
            {
                path: '/products',
                page_id: 'shop_products',
                page_kind: 'resource_list_page',
                title: '商品商城',
                access: { roles: ['guest', 'member', 'admin'] },
                data_sources: { primary_list: { method: 'GET', endpoint: '/shop/products' } },
                state: { selected_item_key: 'selectedProductId' },
                surfaces: [
                    { surface_id: 'feedback', surface_kind: 'message_panel' },
                    { surface_id: 'search', surface_kind: 'search_form', fields: [{ name: 'keyword', field_type: 'text', label: '關鍵字' }] },
                    { surface_id: 'table', surface_kind: 'data_table', source: 'primary_list', columns: [{ key: 'name', label: '商品名稱' }], row_actions: [] }
                ]
            }
        ]
    };

    it('materializes a route map keyed by path', () => {
        const materializer = new FrontendDefinitionMaterializer(frontendDefinition);
        const result = materializer.materialize();

        expect(result.routes['/products']).toBeDefined();
        expect(result.routes['/products'].pageKind).toBe('resource_list_page');
    });

    it('marks unsupported surface kinds as errors', () => {
        const materializer = new FrontendDefinitionMaterializer({
            ...frontendDefinition,
            routes: [{
                ...frontendDefinition.routes[0],
                surfaces: [{ surface_id: 'x', surface_kind: 'workflow_grid' }]
            }]
        });

        expect(() => materializer.materialize()).toThrow('Unsupported surface_kind: workflow_grid');
    });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npm.cmd --prefix packages/javascript/browser run test -- FrontendDefinitionMaterializer.test.js`  
Expected: FAIL with missing materializer modules.

- [ ] **Step 3: Implement the minimal materializer**

```js
const SURFACE_KINDS = new Set([
    'message_panel',
    'search_form',
    'data_table',
    'embedded_action_form',
    'content_block'
]);

export class FrontendDefinitionMaterializer {
    constructor(definition) {
        this.definition = definition;
    }

    materialize() {
        const routes = {};

        for (const route of this.definition.routes || []) {
            for (const surface of route.surfaces || []) {
                if (!SURFACE_KINDS.has(surface.surface_kind)) {
                    throw new Error(`Unsupported surface_kind: ${surface.surface_kind}`);
                }
            }

            routes[route.path] = {
                pageId: route.page_id,
                pageKind: route.page_kind,
                title: route.title,
                access: route.access,
                state: route.state || {},
                dataSources: route.data_sources || {},
                surfaces: route.surfaces || []
            };
        }

        return {
            app: this.definition.app,
            auth: this.definition.auth,
            navigation: this.definition.navigation,
            sharedResources: this.definition.shared_resources,
            routes
        };
    }
}
```

- [ ] **Step 4: Add runtime page wrappers that reuse existing components**

```js
export class DefinitionListPage extends BasePage {
    constructor(options = {}) {
        super(options);
        this.routeDefinition = options.routeDefinition;
        this.appDefinition = options.appDefinition;
    }

    async onInit() {
        this._materialized = new FrontendDefinitionMaterializer({
            app: this.appDefinition,
            auth: { roles: [] },
            navigation: { items: [] },
            shared_resources: this.options.sharedResources || { enums: {}, messages: {} },
            routes: [this.routeDefinition]
        }).materialize();
    }

    template() {
        return `<div class="definition-list-page"><div data-surface-host></div></div>`;
    }
}
```

- [ ] **Step 5: Extend `DefinitionRuntimePage` to accept direct form definitions from the route materializer**

```js
_resolveDefinition() {
    if (this.options?.definitionOverride) {
        return this.options.definitionOverride;
    }

    const definition = this.constructor.definition;
    if (!definition || typeof definition !== 'object') {
        throw new Error(`${this.constructor.name} must declare a static definition object`);
    }

    return definition;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `npm.cmd --prefix packages/javascript/browser run test -- FrontendDefinitionMaterializer.test.js`  
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add templates/spa/frontend/runtime/materializers templates/spa/frontend/runtime/pages templates/spa/frontend/runtime/DefinitionRuntimePage.js packages/javascript/browser/__tests__/definition-json/FrontendDefinitionMaterializer.test.js
git commit -m "feat: add frontend definition materializer"
```

---

## Task 3: 將會員商務站 proof 改成由 frontend-definition 驅動

**Files:**
- Create: `templates/spa/frontend/definition/architecture.json`
- Create: `templates/spa/frontend/definition/frontend-definition.json`
- Modify: `templates/spa/frontend/pages/routes.js`
- Modify: `templates/spa/frontend/core/App.js`
- Modify: `templates/spa/frontend/pages/admin/productFormDefinition.js`
- Test: `tests/e2e/ui/spa-commerce-proof.spec.ts`

- [ ] **Step 1: Write the failing route bootstrap test by extending the existing Playwright proof**

```ts
test('loads products route from frontend-definition json', async ({ page }) => {
    await page.goto('/#/products');
    await expect(page.locator('body')).toContainText('商品商城');
    await expect(page.locator('body')).toContainText('Starter Membership');
});
```

- [ ] **Step 2: Run the E2E proof to verify the JSON route path is not wired yet**

Run: `npx.cmd playwright test tests/e2e/ui/spa-commerce-proof.spec.ts --config tests/e2e/playwright.config.ts`  
Expected: FAIL after route bootstrap starts reading frontend definitions without materialized route registration.

- [ ] **Step 3: Add canonical JSON fixtures for the proof app**

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
    "definition_file": "../backend/definition/backend-definition.json"
  },
  "docs": {
    "deployment": true,
    "implementation_plan": true
  }
}
```

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
      { "label": "後台商品", "route": "/admin/products", "visible_for": ["admin"] }
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
  "routes": []
}
```

- [ ] **Step 4: Load the JSON in `routes.js` and materialize route registrations**

```js
import frontendDefinition from '../definition/frontend-definition.json';
import { FrontendDefinitionMaterializer } from '../runtime/materializers/FrontendDefinitionMaterializer.js';

const materialized = new FrontendDefinitionMaterializer(frontendDefinition).materialize();

export const routes = [
    ...legacyRoutes,
    ...Object.entries(materialized.routes).map(([path, route]) => ({
        path,
        title: route.title,
        page: resolveDefinitionPage(route)
    }))
];
```

- [ ] **Step 5: Keep admin create/edit on form definition, but source their options from shared resources**

```js
import frontendDefinition from '../../definition/frontend-definition.json';

const enums = frontendDefinition.shared_resources.enums;
const CATEGORY_OPTIONS = enums.category_options;
const STATUS_OPTIONS = enums.status_options;
```

- [ ] **Step 6: Run the full Playwright proof to verify the JSON-backed routes are green**

Run: `npx.cmd playwright test tests/e2e/ui/spa-commerce-proof.spec.ts --config tests/e2e/playwright.config.ts`  
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add templates/spa/frontend/definition templates/spa/frontend/pages/routes.js templates/spa/frontend/core/App.js templates/spa/frontend/pages/admin/productFormDefinition.js tests/e2e/ui/spa-commerce-proof.spec.ts
git commit -m "feat: materialize commerce frontend from definition json"
```

---

## Task 4: 實作 backend-definition materializer 與 N2 commerce 白名單模組

**Files:**
- Create: `templates/spa/backend/definition/backend-definition.json`
- Create: `templates/spa/backend/Generated/DefinitionBackendMaterializer.cs`
- Create: `templates/spa/backend/Generated/DefinitionBackendModels.cs`
- Create: `templates/spa/backend/Generated/DefinitionBackendModuleRegistry.cs`
- Modify: `templates/spa/backend/Program.cs`
- Modify: `templates/spa/backend/Data/DbInitializer.cs`
- Modify: `templates/spa/backend/Generated/DefinitionTemplateGeneratedComposition.cs`
- Test: `templates/spa/backend.Tests/DefinitionBackendMaterializerTests.cs`

- [ ] **Step 1: Write the failing backend materializer tests**

```csharp
using FluentAssertions;

namespace SpaApi.Template.Tests;

public class DefinitionBackendMaterializerTests
{
    [Fact]
    public void Applies_n2_security_baseline_for_persistent_backend()
    {
        var definition = new BackendDefinition
        {
            SchemaVersion = 1,
            Enabled = true,
            Tier = "N2",
            Template = "base_n2_commerce",
            Persistence = new PersistenceDefinition { Enabled = true, Orm = "BaseOrm", Database = "sqlite" },
            Security = new SecurityDefinition { AuthRequired = true, AuthMode = "local_jwt", RoleModel = ["member", "admin"] },
            Modules = ["auth", "shop_catalog", "shop_order", "admin_product"]
        };

        var result = DefinitionBackendMaterializer.Materialize(definition);

        result.RequiresAuthentication.Should().BeTrue();
        result.RequiredModules.Should().Contain("admin_product");
        result.SecurityMiddleware.Should().Contain("jwt");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj --filter DefinitionBackendMaterializerTests -v minimal`  
Expected: FAIL with missing backend definition types/materializer.

- [ ] **Step 3: Implement backend definition models and materializer**

```csharp
public sealed class BackendDefinition
{
    public int SchemaVersion { get; set; }
    public bool Enabled { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public PersistenceDefinition Persistence { get; set; } = new();
    public SecurityDefinition Security { get; set; } = new();
    public List<string> Entities { get; set; } = [];
    public List<string> Modules { get; set; } = [];
    public SeedDefinition Seed { get; set; } = new();
}

public static class DefinitionBackendMaterializer
{
    public static MaterializedBackendDefinition Materialize(BackendDefinition definition)
    {
        if (definition.Tier is not ("N1" or "N2" or "N3"))
        {
            throw new InvalidOperationException($"Unsupported backend tier: {definition.Tier}");
        }

        var requiresAuth = definition.Tier is "N2" or "N3";
        return new MaterializedBackendDefinition(
            requiresAuth,
            definition.Modules,
            requiresAuth ? ["jwt", "validation", "audit"] : ["validation", "audit", "rate_limit"]
        );
    }
}
```

- [ ] **Step 4: Load `backend-definition.json` in the template bootstrap**

```csharp
var backendDefinitionPath = Path.Combine(app.Environment.ContentRootPath, "definition", "backend-definition.json");
var backendDefinition = File.Exists(backendDefinitionPath)
    ? JsonSerializer.Deserialize<BackendDefinition>(File.ReadAllText(backendDefinitionPath))
    : null;

var materializedBackend = backendDefinition is null
    ? null
    : DefinitionBackendMaterializer.Materialize(backendDefinition);
```

- [ ] **Step 5: Gate seed and module registration through the materialized definition**

```csharp
if (materializedBackend is not null && materializedBackend.RequiredModules.Contains("shop_catalog"))
{
    // keep existing product/category endpoints enabled
}
```

- [ ] **Step 6: Run backend template tests**

Run: `dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj -v minimal`  
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add templates/spa/backend/definition templates/spa/backend/Generated templates/spa/backend/Program.cs templates/spa/backend/Data/DbInitializer.cs templates/spa/backend.Tests/DefinitionBackendMaterializerTests.cs
git commit -m "feat: add backend definition materializer"
```

---

## Task 5: 回歸驗證與文件衍生

**Files:**
- Modify: `tests/e2e/playwright.config.ts`
- Modify: `tests/e2e/ui/spa-commerce-proof.spec.ts`
- Modify: `docs/superpowers/specs/2026-04-10-frontend-backend-definition-json-design.zh-TW.md`

- [ ] **Step 1: Add a proof assertion that the canonical JSON files exist and are used**

```ts
await expect(page.locator('body')).toContainText('Bricks Commerce');
await expect.poll(async () => page.evaluate(() => location.hash)).toContain('#/admin/products');
```

- [ ] **Step 2: Run browser unit tests**

Run: `npm.cmd --prefix packages/javascript/browser run test`  
Expected: PASS

- [ ] **Step 3: Run backend template tests**

Run: `dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj -v minimal`  
Expected: PASS

- [ ] **Step 4: Run proof E2E**

Run: `npx.cmd playwright test tests/e2e/ui/spa-commerce-proof.spec.ts --config tests/e2e/playwright.config.ts`  
Expected: PASS

- [ ] **Step 5: Update the spec with implementation status notes only if needed**

```md
## Implementation Status

- `architecture.json` loader implemented
- `frontend-definition.json` materializer implemented for content/form/list surfaces
- `backend-definition.json` materializer implemented for N2 proof modules
```

- [ ] **Step 6: Commit**

```bash
git add tests/e2e/playwright.config.ts tests/e2e/ui/spa-commerce-proof.spec.ts docs/superpowers/specs/2026-04-10-frontend-backend-definition-json-design.zh-TW.md
git commit -m "test: verify definition json commerce proof"
```

---

## Self-Review

### Spec coverage

- `architecture.json` canonical source: covered by Task 1 and Task 3
- `frontend-definition.json` route + surfaces model: covered by Task 2 and Task 3
- `backend-definition.json` tier + template + modules model: covered by Task 4
- commerce proof instance JSON: covered by Task 3 and Task 4
- deterministic materialization instead of AI: enforced across Tasks 2–4

### Placeholder scan

- No `TBD`, `TODO`, or “implement later”
- Each test step has an exact command
- Each implementation step contains concrete code shape

### Type consistency

- `architecture.json`, `frontend-definition.json`, `backend-definition.json` names are consistent across all tasks
- `resource_list_page`, `resource_form_page`, `embedded_action_form` names match the spec
- backend tier names are consistently `N1/N2/N3`

