'use strict';

const fs = require('fs');
const path = require('path');
const { CompletionContract, State } = require('../state-machine');

/**
 * CRUD 功能生成管線
 *
 * 將「生成一個 CRUD 功能」拆分為 5 個狀態，
 * 每個狀態有對應的 completion contract，
 * 同一份 contract 生成 precondition（就源規範）與 postcondition（驗證）。
 *
 * 狀態流程:
 *   1. generate-model     → 產生 Entity Model + DTO
 *   2. generate-db-layer   → 在 AppDb 中新增 CRUD 方法
 *   3. generate-service    → 產生 Service 介面與實作
 *   4. generate-endpoints  → 在 Program.cs 新增 API 端點
 *   5. generate-pages      → 產生前端列表 + 詳細頁
 *
 * 任務分類:
 *   狀態 1, 4, 5 — Category 1（強可驗證：集合成員、結構相等）
 *   狀態 2, 3   — Category 1+2 混合（白名單 + 需要編譯輔助驗證）
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

    // ─── State 1: Generate Model ───

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

    // ─── State 2: Generate DB Layer ───

    const dbContract = new CompletionContract({
        id: 'db-layer',
        description: `AppDb ${entityName} 方法: 必須新增 CRUD 方法 + 建表 SQL`,
        category: 1,
        allowedRefs: ALLOWED_BACKEND_REFS,
        forbiddenPatterns: FORBIDDEN_BACKEND_PATTERNS,
        requiredPatterns: [
            `GetAll${plural}`,
            `Get${entityName}ById`,
            `Create${entityName}`,
            `Update${entityName}`,
            `Delete${entityName}`,
            `CREATE TABLE IF NOT EXISTS ${plural}`,
        ],
        fileChecks: [{
            path: be('backend/Data/AppDbContext.cs'),
            whitelistCheck: true,
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
        }],
        contextFiles: [be('backend/Data/AppDbContext.cs')],
        constraints: [
            '只能使用 BaseOrm 的方法: Query<T>, QueryFirst<T>, Insert, Update, Delete<T>, Execute, Scalar<T>',
            '禁止使用 EntityFrameworkCore',
            `建表 SQL 必須包含 ${plural} 的所有欄位`,
            '必須新增 5 個方法: GetAll, GetById, Create, Update, Delete',
        ],
    });

    const stateDb = new State({
        id: 'generate-db-layer',
        name: `新增 AppDb ${entityName} 方法`,
        contract: dbContract,
        maxRetries: 2,
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是在 AppDbContext.cs 中新增 ${entityName} 的 CRUD 方法和建表 SQL。\n\n`;
            prompt += `## 目標檔案\n- ${be('backend/Data/AppDbContext.cs')}\n\n`;
            prompt += `## 要新增的內容\n`;
            prompt += `1. 在 EnsureCreated() 方法中加入 CREATE TABLE IF NOT EXISTS ${plural}\n`;
            prompt += `2. 新增 region "${entityName} Operations" 包含 5 個方法:\n`;
            prompt += `   - List<${entityName}> GetAll${plural}()\n`;
            prompt += `   - ${entityName}? Get${entityName}ById(int id)\n`;
            prompt += `   - long Create${entityName}(${entityName} entry)\n`;
            prompt += `   - int Update${entityName}(${entityName} entry)\n`;
            prompt += `   - int Delete${entityName}(int id)\n\n`;

            prompt += `## 可用的 BaseOrm API（只能用這些）\n`;
            prompt += `- Query<T>(sql, param?) → List<T>\n`;
            prompt += `- QueryFirst<T>(sql, param?) → T?\n`;
            prompt += `- Insert(entity) → long\n`;
            prompt += `- Update(entity) → int\n`;
            prompt += `- Delete<T>(id) → int\n`;
            prompt += `- Execute(sql, param?) → void\n`;
            prompt += `- Scalar<T>(sql, param?) → T\n\n`;

            if (ctx.referenceCode[be('backend/Data/AppDbContext.cs')]) {
                prompt += `## 現有檔案內容（先讀取確認結構再修改）\n`;
                prompt += '```csharp\n' + ctx.referenceCode[be('backend/Data/AppDbContext.cs')] + '\n```\n\n';
            }

            prompt += `## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n注意: 先用 read_file 讀取現有檔案確認結構，再用 write_file 寫入完整檔案（保留所有既有內容）。`;
            return prompt;
        },
    });

    // ─── State 3: Generate Service ───

    const serviceContract = new CompletionContract({
        id: 'service',
        description: `${entityName}Service: 必須實作介面, 只能引用 AppDb`,
        category: 1,
        allowedRefs: ALLOWED_BACKEND_REFS,
        forbiddenPatterns: FORBIDDEN_BACKEND_PATTERNS,
        // 注意: interface 可能在獨立檔案 (I{Name}Service.cs) 或內嵌在 Service 檔案中
        // 所以 requiredPatterns 只檢查 Service 類別宣告
        requiredPatterns: [
            `class\\s+${entityName}Service\\s*:\\s*I${entityName}Service`,
            'private readonly AppDb _db',
        ],
        requiredOutputs: [be(`backend/Services/${entityName}Service.cs`)],
        fileChecks: [
            {
                path: be(`backend/Services/${entityName}Service.cs`),
                whitelistCheck: true,
                validators: [
                    (content) => {
                        const errors = [];
                        // 必須有 5 個 CRUD 方法
                        const methods = ['GetAllAsync', 'GetByIdAsync', 'CreateAsync', 'UpdateAsync', 'DeleteAsync'];
                        for (const m of methods) {
                            if (!content.includes(m)) {
                                errors.push(`[方法缺失] ${entityName}Service 缺少 ${m} 方法`);
                            }
                        }
                        // 必須使用 AppDb 而非 DbContext
                        if (content.includes('DbContext') || content.includes('_context')) {
                            errors.push(`[違規引用] 不得使用 DbContext，必須使用 AppDb`);
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                // 介面檔案: 可能是獨立檔案 (I{Name}Service.cs) 或內嵌在 Service 檔案中
                // 如果獨立檔案不存在就跳過（interface 可能內嵌在 Service 檔案中）
                path: be(`backend/Services/I${entityName}Service.cs`),
                mustExist: false,
                whitelistCheck: true,
            },
        ],
        // 跨檔案驗證: interface 必須存在於 Service 檔案或獨立 Interface 檔案
        crossFileValidators: [
            (filesContent) => {
                const errors = [];
                const serviceFile = filesContent[be(`backend/Services/${entityName}Service.cs`)] || '';
                const interfaceFile = filesContent[be(`backend/Services/I${entityName}Service.cs`)] || '';
                const combined = serviceFile + '\n' + interfaceFile;
                const ifacePattern = new RegExp(`interface\\s+I${entityName}Service`);
                if (!ifacePattern.test(combined)) {
                    errors.push(`[介面缺失] 必須定義 I${entityName}Service 介面（可在 ${entityName}Service.cs 內或獨立 I${entityName}Service.cs）`);
                }
                return { errors, warnings: [] };
            },
        ],
        contextFiles: [
            be(referenceEntity.servicePath),
            be(`backend/Services/I${referenceEntity.name}Service.cs`),
            be(`backend/Models/${entityName}.cs`),
        ],
        constraints: [
            `只能注入 AppDb，不能使用 DbContext 或 EntityFrameworkCore`,
            `必須實作 I${entityName}Service 介面（可放在同檔或獨立 I${entityName}Service.cs）`,
            `使用 Task.FromResult 包裝（因為 BaseOrm 是同步的）`,
            `包含 ToResponse 私有方法將 Entity 轉為 Response DTO`,
        ],
    });

    const stateService = new State({
        id: 'generate-service',
        name: `產生 ${entityName}Service`,
        contract: serviceContract,
        maxRetries: 2,
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是建立 ${entityName} 的服務層。\n\n`;
            prompt += `## 預期產出檔案\n`;
            prompt += `- ${be(`backend/Services/${entityName}Service.cs`)}\n\n`;

            prompt += `## 可用的 AppDb 方法（只能用這些）\n`;
            prompt += `- _db.GetAll${plural}() → List<${entityName}>\n`;
            prompt += `- _db.Get${entityName}ById(int id) → ${entityName}?\n`;
            prompt += `- _db.Create${entityName}(${entityName} entry) → long\n`;
            prompt += `- _db.Update${entityName}(${entityName} entry) → int\n`;
            prompt += `- _db.Delete${entityName}(int id) → int\n\n`;

            if (ctx.referenceCode[be(referenceEntity.servicePath)]) {
                prompt += `## 參考範例 (${referenceEntity.name}Service — 必須完全遵循此模式)\n`;
                prompt += '```csharp\n' + ctx.referenceCode[be(referenceEntity.servicePath)] + '\n```\n\n';
            }

            prompt += `## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請直接用 write_file 寫入檔案。`;
            return prompt;
        },
    });

    // ─── State 4: Generate Endpoints ───

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
            // {param} → \{param(?::\w+)?\}  — 接受有或沒有型別約束
            return `\\{${name}(?::\\w+)?\\}`;
        });
    }

    const endpointContract = new CompletionContract({
        id: 'endpoints',
        description: `API 端點: 5 個 CRUD 路由必須完全符合合約`,
        category: 1,
        requiredPatterns: expectedRoutes.map(r =>
            `app\\.Map${r.method}\\s*\\(\\s*"${routePathToRegex(r.path)}"`
        ),
        fileChecks: [{
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
                    // DI 註冊檢查
                    if (!content.includes(`I${entityName}Service`)) {
                        errors.push(`[DI 缺失] 未註冊 I${entityName}Service`);
                    }
                    return { errors, warnings: [] };
                },
            ],
        }],
        contextFiles: [be('backend/Program.cs')],
        constraints: [
            `新增 5 個端點: GET ${apiPath}, GET ${apiPath}/{id}, POST ${apiPath}, PUT ${apiPath}/{id}, DELETE ${apiPath}/{id}`,
            `新增 DI 註冊: builder.Services.AddScoped<I${entityName}Service, ${entityName}Service>()`,
            '保留所有既有端點和程式碼',
            '每個端點需要 RequireAuthorization() 和 RequireRateLimiting("api")',
        ],
    });

    const stateEndpoints = new State({
        id: 'generate-endpoints',
        name: `新增 ${entityName} API 端點`,
        contract: endpointContract,
        maxRetries: 2,
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是在 Program.cs 中新增 ${entityName} 的 API 端點。\n\n`;
            prompt += `## 目標檔案\n- ${be('backend/Program.cs')}\n\n`;

            prompt += `## 要新增的端點\n`;
            for (const r of expectedRoutes) {
                prompt += `- ${r.method.toUpperCase()} "${r.path}"\n`;
            }
            prompt += `\n## 要新增的 DI 註冊\n`;
            prompt += `- builder.Services.AddScoped<I${entityName}Service, ${entityName}Service>();\n\n`;

            if (ctx.referenceCode[be('backend/Program.cs')]) {
                prompt += `## 現有 Program.cs（先讀取確認再修改，保留所有既有內容）\n`;
                prompt += '```csharp\n' + ctx.referenceCode[be('backend/Program.cs')] + '\n```\n\n';
            }

            prompt += `## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n注意: 先用 read_file 讀取現有檔案，然後用 write_file 寫入完整修改後的檔案。`;
            return prompt;
        },
    });

    // ─── State 5: Generate Frontend Pages ───

    // API 路徑檢查：接受精確字串或 JS template literal 變體
    // e.g. "/api/projects/{projectId}/entities" 也接受 "/api/projects/${projectId}/entities"
    const apiPathVariants = [apiPath];
    // 將 {param} 轉為 ${param} 變體（前端 JS 常用 template literal）
    const templateLiteralPath = apiPath.replace(/\{(\w+)\}/g, '${$1}');
    if (templateLiteralPath !== apiPath) {
        apiPathVariants.push(templateLiteralPath);
    }
    // 也接受去掉路徑參數的基底路徑 (e.g. "/api/projects")
    const basePath = apiPath.replace(/\/\{[^}]+\}.*$/, '');
    if (basePath !== apiPath) {
        apiPathVariants.push(basePath);
    }

    function checkApiPath(content, pageName) {
        for (const variant of apiPathVariants) {
            if (content.includes(variant)) return null;
        }
        return `[API 路徑不符] ${pageName} 未使用正確的 API 路徑 ${apiPath} (也接受 template literal 形式)`;
    }

    const pageContract = new CompletionContract({
        id: 'frontend-pages',
        description: `前端頁面: 使用組件庫元件, 正確調用 API`,
        category: 1,
        requiredOutputs: [
            be(`frontend/pages/${pageDir}/${entityName}ListPage.js`),
            be(`frontend/pages/${pageDir}/${entityName}DetailPage.js`),
        ],
        // 注意: class 名稱檢查移到 per-file validators，避免跨檔案誤判
        requiredPatterns: [],
        fileChecks: [
            {
                path: be(`frontend/pages/${pageDir}/${entityName}ListPage.js`),
                validators: [
                    (content) => {
                        const errors = [];
                        const classPattern = new RegExp(`class\\s+${entityName}ListPage`);
                        if (!classPattern.test(content)) {
                            errors.push(`[類別缺失] ListPage 未包含 class ${entityName}ListPage`);
                        }
                        const apiErr = checkApiPath(content, 'ListPage');
                        if (apiErr) errors.push(apiErr);
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
                        const classPattern = new RegExp(`class\\s+${entityName}DetailPage`);
                        if (!classPattern.test(content)) {
                            errors.push(`[類別缺失] DetailPage 未包含 class ${entityName}DetailPage`);
                        }
                        const apiErr = checkApiPath(content, 'DetailPage');
                        if (apiErr) errors.push(apiErr);
                        return { errors, warnings: [] };
                    },
                ],
            },
        ],
        contextFiles: [
            be('frontend/pages/users/UserListPage.js'),
            be('frontend/pages/users/UserDetailPage.js'),
        ],
        constraints: [
            `頁面必須繼承 BasePage 或 DefinedPage`,
            `API 路徑必須是 ${apiPath}`,
            `列表頁 class 名稱: ${entityName}ListPage`,
            `詳細頁 class 名稱: ${entityName}DetailPage`,
        ],
    });

    const statePages = new State({
        id: 'generate-pages',
        name: `產生 ${entityName} 前端頁面`,
        contract: pageContract,
        maxRetries: 2,
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是建立 ${entityName} 的前端頁面。\n\n`;
            prompt += `## 預期產出\n`;
            prompt += `- ${be(`frontend/pages/${pageDir}/${entityName}ListPage.js`)} — 列表頁\n`;
            prompt += `- ${be(`frontend/pages/${pageDir}/${entityName}DetailPage.js`)} — 詳細頁/編輯頁\n\n`;
            prompt += `## API 端點\n`;
            prompt += `- GET ${apiPath} — 取得所有\n`;
            prompt += `- GET ${apiPath}/{id} — 取得單筆\n`;
            prompt += `- POST ${apiPath} — 新增\n`;
            prompt += `- PUT ${apiPath}/{id} — 更新\n`;
            prompt += `- DELETE ${apiPath}/{id} — 刪除\n\n`;

            prompt += `## 欄位\n`;
            for (const f of fields) {
                prompt += `- ${f.name}: ${f.type}\n`;
            }
            prompt += `\n`;

            const listRef = ctx.referenceCode[be('frontend/pages/users/UserListPage.js')];
            const detailRef = ctx.referenceCode[be('frontend/pages/users/UserDetailPage.js')];
            if (listRef) {
                prompt += `## 參考範例 (UserListPage.js — 必須遵循此模式)\n`;
                prompt += '```javascript\n' + listRef + '\n```\n\n';
            }
            if (detailRef) {
                prompt += `## 參考範例 (UserDetailPage.js)\n`;
                prompt += '```javascript\n' + detailRef + '\n```\n\n';
            }

            prompt += `## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請用 write_file 寫入檔案。`;
            return prompt;
        },
    });

    return [stateModel, stateDb, stateService, stateEndpoints, statePages];
}

module.exports = { buildCrudPipeline, pluralize, toKebabCase, detectProjectNamespace, buildAllowedBackendRefs, ALLOWED_BACKEND_REFS_BASE, FORBIDDEN_BACKEND_PATTERNS, CSHARP_TYPE_MAP };
