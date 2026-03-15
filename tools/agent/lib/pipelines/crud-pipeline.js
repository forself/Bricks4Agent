'use strict';

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execSync } = require('child_process');
const { CompletionContract, State } = require('../state-machine');

/**
 * CRUD 功能生成管線
 *
 * 將「生成一個 CRUD 功能」拆分為 3 個狀態:
 *
 * 狀態流程:
 *   1. generate-model       → LLM 產生 Entity Model + DTO
 *   2. generate-backend     → 確定性處理器：generate-api.js 函式生成 DB/Service/Endpoints
 *   3. generate-frontend    → 確定性處理器：page-gen.js CLI 生成前端頁面
 */

// ============================================================
// 允許的引用清單（Backend）— 框架固定部分
// ============================================================

const ALLOWED_BACKEND_REFS_BASE = [
    // 框架
    'System', 'System.Text', 'System.Text.Json',
    'System.Text.RegularExpressions', 'System.Threading.RateLimiting',
    'Microsoft.AspNetCore.Authentication.JwtBearer',
    'Microsoft.AspNetCore.RateLimiting',
    'Microsoft.IdentityModel.Tokens',
    // ORM
    'BaseOrm',
    // System sub-namespaces（常見引用）
    'System.ComponentModel.DataAnnotations',
    'System.Security.Cryptography',
];

/**
 * 從專案目錄偵測 C# namespace
 * 優先從 User.cs（builtin 參考實體）讀取，fallback 到其他 .cs 檔
 *
 * @param {string} projectRoot
 * @param {string} projectPath
 * @returns {string} e.g. "PhotoDiary" or "WebEditor"
 */
function detectProjectNamespace(projectRoot, projectPath) {
    const modelsDir = path.join(projectRoot, projectPath, 'backend', 'Models');
    try {
        // 優先讀 User.cs（skeleton 中的 builtin entity，namespace 最可靠）
        const userPath = path.join(modelsDir, 'User.cs');
        if (fs.existsSync(userPath)) {
            const content = fs.readFileSync(userPath, 'utf-8');
            const match = content.match(/namespace\s+(\w+)\.Models/);
            if (match) return match[1];
        }

        // Fallback: 搜尋其他 .cs 檔
        const files = fs.readdirSync(modelsDir).filter(f => f.endsWith('.cs'));
        for (const file of files) {
            const content = fs.readFileSync(path.join(modelsDir, file), 'utf-8');
            const match = content.match(/namespace\s+(\w+)\.Models/);
            if (match) return match[1];
        }
    } catch { /* directory may not exist yet */ }
    return 'PhotoDiary'; // fallback
}

/**
 * 建構完整的允許引用清單（框架 + 專案 namespace）
 *
 * @param {string} namespace - 專案 namespace (e.g. "PhotoDiary", "WebEditor")
 * @returns {string[]}
 */
function buildAllowedBackendRefs(namespace) {
    return [
        ...ALLOWED_BACKEND_REFS_BASE,
        `${namespace}.Data`,
        `${namespace}.Models`,
        `${namespace}.Services`,
    ];
}

// 明確禁止的引用
const FORBIDDEN_BACKEND_PATTERNS = [
    'Microsoft\\.EntityFrameworkCore',
    'Microsoft\\.Extensions\\.DependencyInjection',
    'System\\.Data\\.Entity',
    'Dapper',
    'NHibernate',
];

// ============================================================
// 允許的前端元件
// ============================================================

const ALLOWED_FRONTEND_COMPONENTS = [
    // Core
    'BasePage', 'DefinedPage', 'NestedPage', 'Router', 'Store', 'ApiService',
    // Panel 系統
    'BasePanel', 'ModalPanel', 'ToastPanel', 'PanelManager',
    // Common
    'ActionButton', 'BasicButton', 'Breadcrumb', 'ButtonGroup', 'Dialog',
    'LoadingSpinner', 'Notification', 'Pagination', 'SortButton',
    // Form
    'TextInput', 'NumberInput', 'Dropdown', 'ToggleSwitch', 'DatePicker',
    'DateTimeInput', 'Checkbox', 'Radio', 'FormField',
    // Layout
    'DataTable', 'FormRow', 'InfoPanel', 'TabContainer',
    // 自定義頁面內的相對引用也允許
];

// ============================================================
// 允許的 C# 型別映射
// ============================================================

const CSHARP_TYPE_MAP = {
    'string': 'string', 'text': 'string',
    'int': 'int', 'integer': 'int',
    'long': 'long',
    'decimal': 'decimal', 'float': 'float', 'double': 'double',
    'bool': 'bool', 'boolean': 'bool',
    'date': 'DateTime', 'datetime': 'DateTime',
    'guid': 'Guid',
};

// ============================================================
// Deterministic helpers (adapted from generate-api.js)
// ============================================================

/**
 * Patch a project file by inserting content before a marker comment.
 * Idempotent: if dupCheckString is already present, the patch is skipped.
 *
 * @param {string} filePath - absolute path to the file to patch
 * @param {string} marker - anchor comment (e.g. '// --- BRICKS:TABLE_SQL ---')
 * @param {string} insertion - content to insert before the marker
 * @param {string} [dupCheckString] - if this string already exists in the file, skip
 * @returns {boolean} whether the file was patched (or already up-to-date)
 */
function patchProjectFile(filePath, marker, insertion, dupCheckString) {
    if (!fs.existsSync(filePath)) {
        throw new Error(`patchProjectFile: file not found: ${filePath}`);
    }

    let content = fs.readFileSync(filePath, 'utf8');
    const markerIndex = content.indexOf(marker);
    if (markerIndex === -1) {
        throw new Error(`patchProjectFile: marker not found: ${marker} in ${filePath}`);
    }

    // Idempotency check
    if (dupCheckString && content.includes(dupCheckString)) {
        return true; // already patched
    }

    const newContent = content.replace(marker, insertion + '\n\n' + marker);
    fs.writeFileSync(filePath, newContent, 'utf8');
    return true;
}

/**
 * Ensure required using statements are present in a C# file.
 *
 * @param {string} filePath - absolute path to the .cs file
 * @param {string} namespace - project namespace (e.g. "PhotoDiary")
 */
function ensureProjectUsings(filePath, namespace) {
    let content = fs.readFileSync(filePath, 'utf8');
    const requiredUsings = [
        `using ${namespace}.Models;`,
        `using ${namespace}.Services;`,
    ];
    let changed = false;
    for (const u of requiredUsings) {
        if (!content.includes(u)) {
            // Insert after last existing using statement
            const lastUsing = content.lastIndexOf('using ');
            const lineEnd = content.indexOf('\n', lastUsing);
            content = content.substring(0, lineEnd + 1) + u + '\n' + content.substring(lineEnd + 1);
            changed = true;
        }
    }
    if (changed) fs.writeFileSync(filePath, content, 'utf8');
}

function ensureGeneratedRoutesFile(routesFilePath) {
    const routesDir = path.dirname(routesFilePath);
    if (!fs.existsSync(routesDir)) {
        fs.mkdirSync(routesDir, { recursive: true });
    }

    if (!fs.existsSync(routesFilePath)) {
        fs.writeFileSync(
            routesFilePath,
            'export const generatedRoutes = [\n];\n\nexport default generatedRoutes;\n',
            'utf8'
        );
    }
}

function registerGeneratedCrudRoutes(routesFilePath, pageDir, entityName, plural) {
    ensureGeneratedRoutesFile(routesFilePath);

    let content = fs.readFileSync(routesFilePath, 'utf8');
    const listImport = `import ${entityName}ListPage from '../${pageDir}/${entityName}ListPage.js';`;
    const detailImport = `import ${entityName}DetailPage from '../${pageDir}/${entityName}DetailPage.js';`;

    for (const importLine of [listImport, detailImport]) {
        if (!content.includes(importLine)) {
            const importMatches = [...content.matchAll(/^import .*;$/gm)];
            if (importMatches.length > 0) {
                const lastMatch = importMatches[importMatches.length - 1];
                const insertPos = lastMatch.index + lastMatch[0].length;
                content = content.slice(0, insertPos) + '\n' + importLine + content.slice(insertPos);
            } else {
                content = importLine + '\n\n' + content;
            }
        }
    }

    const listRoutePath = `/${toKebabCase(plural)}`;
    const detailRoutePath = `/${toKebabCase(plural)}/:id`;
    const hasListRoute = content.includes(`path: '${listRoutePath}'`) || content.includes(`component: ${entityName}ListPage`);
    const hasDetailRoute = content.includes(`path: '${detailRoutePath}'`) || content.includes(`component: ${entityName}DetailPage`);

    if (!hasListRoute || !hasDetailRoute) {
        const entries = [];
        if (!hasListRoute) {
            entries.push(
                `    {\n`
                + `        path: '${listRoutePath}',\n`
                + `        component: ${entityName}ListPage,\n`
                + `        meta: {\n`
                + `            title: '${plural}',\n`
                + `            generated: true\n`
                + `        }\n`
                + `    }`
            );
        }
        if (!hasDetailRoute) {
            entries.push(
                `    {\n`
                + `        path: '${detailRoutePath}',\n`
                + `        component: ${entityName}DetailPage,\n`
                + `        meta: {\n`
                + `            title: '${entityName} Detail',\n`
                + `            generated: true\n`
                + `        }\n`
                + `    }`
            );
        }

        const arrayCloseIndex = content.lastIndexOf('];');
        if (arrayCloseIndex === -1) {
            throw new Error(`registerGeneratedCrudRoutes: invalid routes file ${routesFilePath}`);
        }

        const hasExistingRoutes = content.slice(0, arrayCloseIndex).includes('path:');
        const insertion = `${hasExistingRoutes ? ',\n' : ''}${entries.join(',\n')}\n`;
        content = content.slice(0, arrayCloseIndex) + insertion + content.slice(arrayCloseIndex);
    }

    fs.writeFileSync(routesFilePath, content, 'utf8');
}

// ============================================================
// Pipeline Builder
// ============================================================

/**
 * PascalCase → kebab-case
 * e.g. "DiaryEntry" → "diary-entry"
 */
function toKebabCase(str) {
    return str.replace(/([a-z0-9])([A-Z])/g, '$1-$2').toLowerCase();
}

/**
 * 簡易英語複數化
 * 處理常見模式: y→ies, s/x/ch/sh→es, 其他→s
 */
function pluralize(word) {
    if (word.endsWith('y') && !/[aeiou]y$/i.test(word)) {
        return word.slice(0, -1) + 'ies';
    }
    if (/(?:s|x|ch|sh)$/i.test(word)) {
        return word + 'es';
    }
    return word + 's';
}

/**
 * 建構 CRUD 管線
 *
 * @param {Object} config
 * @param {string} config.entityName - 實體名稱 (PascalCase, e.g. "DiaryEntry")
 * @param {Object[]} config.fields - 欄位定義 [{name, type, nullable?}]
 * @param {string} config.projectPath - 專案子目錄 (相對於 projectRoot)
 * @param {string} config.plural - 自定義複數形 (e.g. "DiaryEntries")
 * @param {string} config.apiPath - 自定義 API 路徑 (e.g. "/api/diary-entries")
 * @param {Object} config.referenceEntity - 參考實體 {name, modelPath, servicePath}
 * @returns {State[]}
 */
function buildCrudPipeline(config) {
    const {
        entityName,
        fields,
        projectPath = '',
        projectRoot = process.cwd(),
        referenceEntity = { name: 'User', modelPath: 'backend/Models/User.cs', servicePath: 'backend/Services/UserService.cs' },
    } = config;

    const plural = config.plural || pluralize(entityName);
    const lower = entityName.charAt(0).toLowerCase() + entityName.slice(1);
    const apiPath = config.apiPath || `/api/${toKebabCase(plural)}`;
    const pageDir = plural.toLowerCase(); // 前端頁面目錄: diaryentries, users, products

    const be = (rel) => projectPath ? `${projectPath}/${rel}` : rel;

    // 動態偵測專案 namespace
    const projectNamespace = detectProjectNamespace(projectRoot, projectPath);
    const ALLOWED_BACKEND_REFS = buildAllowedBackendRefs(projectNamespace);

    // Bricks4Agent repo root — resolve from this file's location
    const bricks4agentRoot = path.resolve(__dirname, '..', '..', '..', '..');

    // ─── State 1: Generate Model (LLM) ───

    const modelContract = new CompletionContract({
        id: 'model',
        description: `${entityName} Model: 欄位必須 1:1 對應 schema, 型別從允許集合選取`,
        category: 1,
        expectedFields: fields,
        requiredOutputs: [be(`backend/Models/${entityName}.cs`)],
        requiredPatterns: [
            `class\\s+${entityName}\\b`,
            `record\\s+Create${entityName}Request`,
            `record\\s+Update${entityName}Request`,
            `record\\s+${entityName}Response`,
        ],
        fileChecks: [{
            path: be(`backend/Models/${entityName}.cs`),
            schemaCheck: true,
            validators: [
                // 驗證每個 field 的型別是否合法
                (content) => {
                    const errors = [];
                    for (const f of fields) {
                        const csharpType = CSHARP_TYPE_MAP[f.type] || f.type;
                        const nullable = f.nullable ? '\\??' : '';
                        const pattern = new RegExp(
                            `public\\s+${csharpType}${nullable}\\s+${f.name}\\s*\\{`
                        );
                        if (!pattern.test(content)) {
                            errors.push(`[型別不符] 欄位 ${f.name} 應為 ${csharpType}，但未找到匹配宣告`);
                        }
                    }
                    return { errors, warnings: [] };
                },
            ],
        }],
        contextFiles: [be(referenceEntity.modelPath)],
        constraints: [
            `實體名稱必須是 ${entityName}`,
            `必須包含 Create${entityName}Request, Update${entityName}Request, ${entityName}Response record`,
            `型別只能從以下選擇: ${Object.keys(CSHARP_TYPE_MAP).join(', ')}`,
        ],
    });

    const stateModel = new State({
        id: 'generate-model',
        name: `產生 ${entityName} Model`,
        contract: modelContract,
        maxRetries: 2,
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是為 "${entityName}" 建立資料模型。\n\n`;
            prompt += `## 預期產出檔案\n`;
            prompt += `- ${be(`backend/Models/${entityName}.cs`)}\n\n`;
            prompt += `## Schema 定義\n`;
            prompt += `namespace: ${projectNamespace}.Models\n`;
            prompt += `| 欄位名 | 型別 | 可空 |\n|---|---|---|\n`;
            for (const f of fields) {
                prompt += `| ${f.name} | ${f.type} | ${f.nullable ? '是' : '否'} |\n`;
            }
            prompt += `\n此外，還有自動欄位: Id (int, 主鍵), CreatedAt (DateTime), UpdatedAt (DateTime?)\n\n`;
            prompt += `## 必須包含\n`;
            prompt += `- class ${entityName} — 資料實體\n`;
            prompt += `- record Create${entityName}Request — 建立用 DTO\n`;
            prompt += `- record Update${entityName}Request — 更新用 DTO（所有欄位可空）\n`;
            prompt += `- record ${entityName}Response — 回應用 DTO\n\n`;

            if (ctx.referenceCode[be(referenceEntity.modelPath)]) {
                prompt += `## 參考範例 (${referenceEntity.name} Model)\n`;
                prompt += '```csharp\n' + ctx.referenceCode[be(referenceEntity.modelPath)] + '\n```\n\n';
            }

            prompt += `## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請直接用 write_file 寫入檔案。`;
            return prompt;
        },
    });

    // ─── State 2: Generate Backend (deterministic handler) ───

    const expectedRoutes = [
        { method: 'Get', path: apiPath },
        { method: 'Get', path: `${apiPath}/{id:int}` },
        { method: 'Post', path: apiPath },
        { method: 'Put', path: `${apiPath}/{id:int}` },
        { method: 'Delete', path: `${apiPath}/{id:int}` },
    ];

    // 路由路徑轉 regex: {param} 匹配 {param} 或 {param:type}
    function routePathToRegex(routePath) {
        return routePath.replace(/\{(\w+)(?::(\w+))?\}/g, (match, name, type) => {
            return `\\{${name}(?::\\w+)?\\}`;
        });
    }

    const backendContract = new CompletionContract({
        id: 'backend-gen',
        description: `Backend generation for ${entityName}: Service + DB + Endpoints via deterministic handler`,
        category: 1,
        forbiddenPatterns: FORBIDDEN_BACKEND_PATTERNS,
        requiredPatterns: [
            // Model checks (from previous state, still present)
            `class\\s+${entityName}\\b`,
            // AppDb checks
            `GetAll${plural}`,
            `Get${entityName}ById`,
            `Create${entityName}`,
            `Update${entityName}`,
            `Delete${entityName}`,
            // Service checks
            `class\\s+${entityName}Service\\s*:\\s*I${entityName}Service`,
            // Endpoint checks
            ...expectedRoutes.map(r =>
                `app\\.Map${r.method}\\s*\\(\\s*"${routePathToRegex(r.path)}"`
            ),
        ],
        fileChecks: [
            {
                path: be(`backend/Models/${entityName}.cs`),
                schemaCheck: true,
                validators: [
                    (content) => {
                        const errors = [];
                        for (const f of fields) {
                            const csharpType = CSHARP_TYPE_MAP[f.type] || f.type;
                            const nullable = f.nullable ? '\\??' : '';
                            const pattern = new RegExp(
                                `public\\s+${csharpType}${nullable}\\s+${f.name}\\s*\\{`
                            );
                            if (!pattern.test(content)) {
                                errors.push(`[型別不符] 欄位 ${f.name} 應為 ${csharpType}，但未找到匹配宣告`);
                            }
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                path: be(`backend/Services/${entityName}Service.cs`),
                validators: [
                    (content) => {
                        const errors = [];
                        const methods = ['GetAllAsync', 'GetByIdAsync', 'CreateAsync', 'UpdateAsync', 'DeleteAsync'];
                        for (const m of methods) {
                            if (!content.includes(m)) {
                                errors.push(`[方法缺失] ${entityName}Service 缺少 ${m} 方法`);
                            }
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                path: be('backend/Data/AppDbContext.cs'),
                validators: [
                    (content) => {
                        const errors = [];
                        const methods = [
                            `GetAll${plural}`, `Get${entityName}ById`,
                            `Create${entityName}`, `Update${entityName}`, `Delete${entityName}`,
                        ];
                        for (const m of methods) {
                            if (!content.includes(m)) {
                                errors.push(`[方法缺失] AppDb 缺少 ${m} 方法`);
                            }
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                path: be('backend/Program.cs'),
                validators: [
                    (content) => {
                        const errors = [];
                        for (const r of expectedRoutes) {
                            const regexPath = routePathToRegex(r.path);
                            const pattern = new RegExp(`\\.Map${r.method}\\s*\\(\\s*"${regexPath}"`);
                            if (!pattern.test(content)) {
                                errors.push(`[路由缺失] ${r.method.toUpperCase()} ${r.path}`);
                            }
                        }
                        if (!content.includes(`I${entityName}Service`)) {
                            errors.push(`[DI 缺失] 未註冊 I${entityName}Service`);
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
        ],
        constraints: [
            `Deterministic backend generation for ${entityName} via generate-api.js functions`,
        ],
    });

    const stateBackend = new State({
        id: 'generate-backend',
        name: `確定性生成 ${entityName} 後端（Service + DB + Endpoints）`,
        contract: backendContract,
        maxRetries: 1,
        handler: async (context, taskContext, projRoot) => {
            const genApiPath = path.resolve(bricks4agentRoot, 'templates', 'spa', 'scripts', 'generate-api.js');
            const genApi = require(genApiPath);

            const ns = projectNamespace;
            const backendDir = path.join(projRoot, projectPath, 'backend');

            // 1. Generate Service file
            const service = genApi.generateService(entityName, fields, ns);
            const serviceDir = path.join(backendDir, 'Services');
            if (!fs.existsSync(serviceDir)) fs.mkdirSync(serviceDir, { recursive: true });
            const servicePath = path.join(serviceDir, `${entityName}Service.cs`);
            fs.writeFileSync(servicePath, service.content, 'utf8');

            // 2. Generate & patch DB methods
            const db = genApi.generateDbMethods(entityName, fields);
            const dbContextPath = path.join(backendDir, 'Data', 'AppDbContext.cs');
            patchProjectFile(dbContextPath, '// --- BRICKS:TABLE_SQL ---', '\n' + db.tableSql, `CREATE TABLE IF NOT EXISTS ${plural}`);
            patchProjectFile(dbContextPath, '// --- BRICKS:DB_METHODS ---', '\n' + db.methods, `#region ${entityName} Operations`);

            // 3. Patch Program.cs with service registration + endpoints
            const programPath = path.join(backendDir, 'Program.cs');
            const serviceReg = `builder.Services.AddScoped<I${entityName}Service, ${entityName}Service>();`;
            patchProjectFile(programPath, '// --- BRICKS:SERVICES ---', serviceReg, `I${entityName}Service, ${entityName}Service`);

            const endpoints = genApi.generateEndpoints(entityName, fields);
            patchProjectFile(programPath, '// --- BRICKS:ENDPOINTS ---', endpoints.content, `"Get${entityName}ById"`);

            // 4. Ensure using statements
            ensureProjectUsings(programPath, ns);

            return `Deterministic backend generation completed for ${entityName}`;
        },
    });

    // ─── State 3: Generate Frontend (deterministic handler via page-gen.js) ───

    // C# type → PageDefinition fieldType mapping
    const FIELD_TYPE_MAP = {
        'string': 'text',
        'int': 'number', 'integer': 'number', 'long': 'number',
        'decimal': 'number', 'float': 'number', 'double': 'number',
        'bool': 'toggle', 'boolean': 'toggle',
        'datetime': 'date', 'date': 'date',
        'text': 'textarea',
        'guid': 'text',
    };

    const frontendContract = new CompletionContract({
        id: 'frontend-gen',
        description: `Frontend pages for ${entityName}: generated via page-gen.js deterministic handler`,
        category: 1,
        requiredOutputs: [
            be(`frontend/pages/${pageDir}/${entityName}ListPage.js`),
            be(`frontend/pages/${pageDir}/${entityName}DetailPage.js`),
        ],
        requiredPatterns: [],
        fileChecks: [
            {
                path: be(`frontend/pages/${pageDir}/${entityName}ListPage.js`),
                validators: [
                    (content) => {
                        const errors = [];
                        if (!content.includes('BasePage') && !content.includes('DefinedPage')) {
                            errors.push(`[基類缺失] ListPage 必須繼承 BasePage 或 DefinedPage`);
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                path: be(`frontend/pages/${pageDir}/${entityName}DetailPage.js`),
                validators: [
                    (content) => {
                        const errors = [];
                        if (!content.includes('BasePage') && !content.includes('DefinedPage')) {
                            errors.push(`[基類缺失] DetailPage 必須繼承 BasePage 或 DefinedPage`);
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                path: be('frontend/pages/generated/routes.generated.js'),
                mustExist: false,
                validators: [
                    (content) => {
                        const errors = [];
                        if (content && !content.includes(`${entityName}ListPage`)) {
                            errors.push(`[路由缺失] routes.js 未包含 ${entityName}ListPage import`);
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
        ],
        constraints: [
            `Deterministic frontend page generation for ${entityName} via page-gen.js`,
        ],
    });

    const statePages = new State({
        id: 'generate-frontend',
        name: `確定性生成 ${entityName} 前端頁面（List + Detail）`,
        contract: frontendContract,
        maxRetries: 1,
        handler: async (context, taskContext, projRoot) => {
            const pageGenPath = path.resolve(bricks4agentRoot, 'tools', 'page-gen.js');
            const pageOutputDir = path.join(projRoot, projectPath, 'frontend', 'pages', pageDir);

            // Build PageDefinition fields
            const pageDefFields = fields.map(f => ({
                fieldName: f.name.charAt(0).toLowerCase() + f.name.slice(1),
                label: f.name.replace(/([a-z])([A-Z])/g, '$1 $2'),
                fieldType: FIELD_TYPE_MAP[f.type.toLowerCase()] || 'text',
            }));

            // Build API info
            const apiInfo = {
                baseUrl: apiPath,
                endpoints: {
                    list: `GET ${apiPath}`,
                    detail: `GET ${apiPath}/{id}`,
                },
            };

            // --- List page ---
            const listPageDef = {
                page: { pageName: `${entityName}ListPage`, entity: lower, view: 'list' },
                fields: pageDefFields,
                api: apiInfo,
            };

            const tmpListFile = path.join(os.tmpdir(), `bricks-pagedef-${entityName}-list.json`);
            fs.writeFileSync(tmpListFile, JSON.stringify(listPageDef, null, 2), 'utf8');
            try {
                execSync(`node "${pageGenPath}" --def "${tmpListFile}" --mode static --output "${pageOutputDir}"`, {
                    cwd: bricks4agentRoot,
                    stdio: 'pipe',
                });
            } finally {
                try { fs.unlinkSync(tmpListFile); } catch { /* ignore */ }
            }

            // --- Detail page ---
            const detailPageDef = {
                page: { pageName: `${entityName}DetailPage`, entity: lower, view: 'form' },
                fields: pageDefFields,
                api: apiInfo,
            };

            const tmpDetailFile = path.join(os.tmpdir(), `bricks-pagedef-${entityName}-detail.json`);
            fs.writeFileSync(tmpDetailFile, JSON.stringify(detailPageDef, null, 2), 'utf8');
            try {
                execSync(`node "${pageGenPath}" --def "${tmpDetailFile}" --mode static --output "${pageOutputDir}"`, {
                    cwd: bricks4agentRoot,
                    stdio: 'pipe',
                });
            } finally {
                try { fs.unlinkSync(tmpDetailFile); } catch { /* ignore */ }
            }

            // --- Update routes.js ---
            const routesFilePath = path.join(projRoot, projectPath, 'frontend', 'pages', 'generated', 'routes.generated.js');
            ensureGeneratedRoutesFile(routesFilePath);
            if (fs.existsSync(routesFilePath)) {
                let routesContent = fs.readFileSync(routesFilePath, 'utf8');

                // Add imports if not already present
                const listImport = `import ${entityName}ListPage from '../${pageDir}/${entityName}ListPage.js';`;
                const detailImport = `import ${entityName}DetailPage from '../${pageDir}/${entityName}DetailPage.js';`;

                if (!routesContent.includes(`${entityName}ListPage`)) {
                    // Insert imports after the last existing import statement
                    const lastImportIdx = routesContent.lastIndexOf('import ');
                    if (lastImportIdx !== -1) {
                        const lineEnd = routesContent.indexOf('\n', lastImportIdx);
                        const insertPos = lineEnd + 1;
                        routesContent = routesContent.substring(0, insertPos)
                            + listImport + '\n'
                            + detailImport + '\n'
                            + routesContent.substring(insertPos);
                    } else {
                        // No existing imports, prepend
                        routesContent = listImport + '\n' + detailImport + '\n' + routesContent;
                    }

                    // Add route entries — look for the routes array closing bracket
                    // Try to find a pattern like `];` that closes the routes array
                    const listRoute = `    { path: '/${toKebabCase(plural)}', component: ${entityName}ListPage, meta: { generated: true } }`;
                    const detailRoute = `    { path: '/${toKebabCase(plural)}/:id', component: ${entityName}DetailPage, meta: { generated: true } }`;

                    // Insert before the last '];' in the file (assumed to close the routes array)
                    const lastArrayClose = routesContent.lastIndexOf('];');
                    if (lastArrayClose !== -1) {
                        const hasExistingRoutes = routesContent.slice(0, lastArrayClose).includes('path:');
                        const routeEntries = `${hasExistingRoutes ? ',\n' : ''}${listRoute},\n${detailRoute}\n`;
                        routesContent = routesContent.substring(0, lastArrayClose)
                            + routeEntries
                            + routesContent.substring(lastArrayClose);
                    }

                    fs.writeFileSync(routesFilePath, routesContent, 'utf8');
                }
            }

            return `Deterministic frontend generation completed for ${entityName}`;
        },
    });

    return [stateModel, stateBackend, statePages];
}

module.exports = { buildCrudPipeline, pluralize, toKebabCase, detectProjectNamespace, buildAllowedBackendRefs, ALLOWED_BACKEND_REFS_BASE, FORBIDDEN_BACKEND_PATTERNS, CSHARP_TYPE_MAP };
