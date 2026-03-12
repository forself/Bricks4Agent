'use strict';

const fs = require('fs');
const path = require('path');
const { CompletionContract, State } = require('../state-machine');
const {
    ALLOWED_BACKEND_REFS_BASE,
    FORBIDDEN_BACKEND_PATTERNS,
    detectProjectNamespace,
} = require('./crud-pipeline');

/**
 * Service Pipeline — Batch B 擴展服務管線
 *
 * 與 CRUD Pipeline 的差異：
 *   - 無 DB 表，改用檔案系統 / KV 快取
 *   - 多重 DI 注入（不只 AppDb）
 *   - Value Object 而非 Entity Model
 *   - 結構化回報模板取代 Agent 自由回報
 *
 * 狀態流程（最多 5 個，可選）:
 *   1. generate-models         → 產生 Value Objects
 *   2. generate-interface      → 產生 I{Service}.cs
 *   3. generate-implementation → 產生 {Service}.cs（多重 DI）
 *   4. generate-endpoints      → 建立 {Service}Endpoints.cs（Convention 自動掛載）
 *   5. generate-pages          → 產生前端頁面（可選，含元件 preCheck）
 *
 * DI 註冊由 Convention 自動處理（ServiceRegistration.cs 掃描 IXxx→Xxx 配對）
 * Endpoints 由 Convention 自動掛載（EndpointRegistration.cs 掃描 Map() 方法）
 * Agent 只建立新檔案，永遠不碰 Program.cs
 */

// ============================================================
// 結構化回報模板 — 6 種失敗報告類型
// ============================================================

/**
 * 模板 1: 模組缺口
 * 某個必要的基礎模組不存在，需要先建立
 */
const REPORT_MODULE_GAP = {
    type: 'module_gap',
    service: '',
    state: '',
    missingModule: '',
    currentAlternative: '',
    requiredMethods: [],
    actionRequired: '',
};

/**
 * 模板 2: 依賴缺失
 * 服務依賴的其他介面/模組尚未生成
 */
const REPORT_DEPENDENCY_MISSING = {
    type: 'dependency_missing',
    service: '',
    missingDependencies: [],
    blockingServices: [],
    actionRequired: '',
};

/**
 * 模板 3: 元件缺失
 * 前端頁面所需的 UI 元件尚未建立
 */
const REPORT_COMPONENT_MISSING = {
    type: 'component_missing',
    page: '',
    missingComponents: [],
    availableComponents: [],
    actionRequired: '',
};

/**
 * 模板 4: 合約擴展需求
 * 現有的合約/驗證能力不足以覆蓋新的需求
 */
const REPORT_CONTRACT_EXTENSION = {
    type: 'contract_extension_needed',
    service: '',
    state: '',
    currentCapability: '',
    requiredCapability: '',
    validationGap: '',
    actionRequired: '',
};

/**
 * 模板 5: 驗證失敗
 * Agent 多次重試後仍未通過 postcondition
 */
const REPORT_VALIDATION_FAILED = {
    type: 'validation_failed',
    service: '',
    state: '',
    errors: [],
    retryCount: 0,
    maxRetries: 0,
    actionRequired: '',
};

/**
 * 模板 6: API 速率限制
 * 模型達到速率限制，需要切換模型或等待
 */
const REPORT_RATE_LIMIT = {
    type: 'rate_limit',
    service: '',
    state: '',
    model: '',
    retryAfter: null,
    suggestedModels: [],
    actionRequired: '',
};

/** 所有模板的快速查找表 */
const REPORT_TEMPLATES = {
    module_gap: REPORT_MODULE_GAP,
    dependency_missing: REPORT_DEPENDENCY_MISSING,
    component_missing: REPORT_COMPONENT_MISSING,
    contract_extension_needed: REPORT_CONTRACT_EXTENSION,
    validation_failed: REPORT_VALIDATION_FAILED,
    rate_limit: REPORT_RATE_LIMIT,
};

// ============================================================
// 模板工具函數
// ============================================================

/**
 * 填充回報模板
 * @param {string} templateType - 模板類型 key
 * @param {Object} data - 要填入的資料
 * @returns {Object} 填充後的回報物件
 */
function fillTemplate(templateType, data) {
    const template = REPORT_TEMPLATES[templateType];
    if (!template) throw new Error(`Unknown report template: ${templateType}`);
    return { ...JSON.parse(JSON.stringify(template)), ...data };
}

/**
 * 從錯誤字串中解析 [code] message 格式
 */
function parseErrorCode(errorStr) {
    const match = errorStr.match(/^\[([^\]]+)\]\s*(.+)/);
    if (match) {
        return { code: match[1], message: match[2] };
    }
    return { code: 'unknown', message: errorStr };
}

/**
 * 自動分類錯誤並填充對應的回報模板
 *
 * 分類優先順序：
 *   1. 元件缺失 → component_missing
 *   2. 依賴缺失 → dependency_missing
 *   3. 檔案/模組缺失 → module_gap
 *   4. DI 多重注入問題 → contract_extension_needed
 *   5. 預設 → validation_failed
 *
 * @param {Object} validationResult - { passed, errors, warnings }
 * @param {Object} serviceConfig - { name, dependencies?, methods? }
 * @param {string} stateId - 失敗的狀態 ID
 * @param {Object} context - { attempt?, maxRetries?, pageName?, availableComponents? }
 * @returns {Object} 填充後的模板化回報
 */
function classifyAndFillReport(validationResult, serviceConfig, stateId, context) {
    const errors = validationResult.errors || [];

    // 最高優先: Rate Limit（由 state-machine 注入 [rate_limit] 標記）
    const rlErrors = errors.filter(e => e.startsWith('[rate_limit]'));
    if (rlErrors.length > 0) {
        return fillTemplate('rate_limit', {
            service: serviceConfig.name,
            state: stateId,
            model: context.model || '',
            retryAfter: context.retryAfter || null,
            suggestedModels: context.suggestedModels || [],
            actionRequired: `模型達到速率限制，建議改用: ${(context.suggestedModels || []).join(', ') || '其他模型'}`,
        });
    }

    // 分類 1: 元件缺失
    const compErrors = errors.filter(e => e.startsWith('[component'));
    if (compErrors.length > 0) {
        return fillTemplate('component_missing', {
            page: context.pageName || '',
            missingComponents: compErrors.map(e => {
                const m = e.match(/\[component missing\]\s*(.+)/);
                return { name: m ? m[1] : e, expectedPath: '', category: '', priority: 'HIGH' };
            }),
            availableComponents: context.availableComponents || [],
            actionRequired: '建立缺失元件後重新執行管線',
        });
    }

    // 分類 2: 依賴缺失
    const depErrors = errors.filter(e => e.startsWith('[dependency'));
    if (depErrors.length > 0) {
        return fillTemplate('dependency_missing', {
            service: serviceConfig.name,
            missingDependencies: depErrors.map(e => {
                const m = e.match(/\[dependency missing\]\s*(\S+)/);
                return { name: m ? m[1] : e, expectedPath: '', searchedPaths: [] };
            }),
            blockingServices: [],
            actionRequired: '先執行依賴服務的管線',
        });
    }

    // 分類 3: 檔案/模組缺失
    const fileErrors = errors.filter(e =>
        e.startsWith('[檔案缺失]') || e.startsWith('[interface')
    );
    if (fileErrors.length > 0) {
        return fillTemplate('module_gap', {
            service: serviceConfig.name,
            state: stateId,
            missingModule: fileErrors.map(e => parseErrorCode(e).message).join(', '),
            currentAlternative: '',
            requiredMethods: serviceConfig.methods || [],
            actionRequired: '建立缺失的模組/介面',
        });
    }

    // 分類 4: DI 多重注入問題
    const diErrors = errors.filter(e =>
        e.includes('DI') || e.includes('注入') || e.includes('readonly')
    );
    if (diErrors.length > 0 && (serviceConfig.dependencies || []).length > 1) {
        return fillTemplate('contract_extension_needed', {
            service: serviceConfig.name,
            state: stateId,
            currentCapability: '單一 AppDb 注入',
            requiredCapability: `多重注入: ${(serviceConfig.dependencies || []).join(', ')}`,
            validationGap: diErrors.map(e => parseErrorCode(e).message).join('; '),
            actionRequired: '擴展 DI 合約以支援多重注入',
        });
    }

    // 預設: 一般驗證失敗
    return fillTemplate('validation_failed', {
        service: serviceConfig.name,
        state: stateId,
        errors: errors.map(e => parseErrorCode(e)),
        retryCount: context.attempt || 0,
        maxRetries: context.maxRetries || 2,
        actionRequired: '檢查錯誤列表並手動修復',
    });
}

// ============================================================
// 白名單擴展
// ============================================================

/** 服務共通：async 方法 + 集合型別必需 */
const SERVICE_COMMON_REFS = [
    'System.Threading.Tasks',       // Task<T>, async/await
    'System.Collections.Generic',   // List<T>, Dictionary<K,V>
    'System.Linq',                  // LINQ 查詢
];

/** Repository 類型額外允許 System.IO */
const REPOSITORY_EXTRA_REFS = ['System.IO'];

/** KV Store 類型額外允許 BaseCache */
const CACHE_EXTRA_REFS = ['BaseCache'];

/**
 * 判斷 serviceConfig.storage 是否包含指定 target
 */
function storageIncludes(serviceConfig, target) {
    const storage = serviceConfig.storage;
    if (Array.isArray(storage)) return storage.includes(target);
    return storage === target;
}

/**
 * 為服務建構完整的允許引用清單
 *
 * 基底 = ALLOWED_BACKEND_REFS_BASE + 專案 namespace 子空間
 * + fileSystem → System.IO
 * + kvStore → BaseCache
 */
function buildAllowedServiceRefs(namespace, serviceConfig) {
    const refs = [
        ...ALLOWED_BACKEND_REFS_BASE,
        ...SERVICE_COMMON_REFS,
        `${namespace}.Data`,
        `${namespace}.Models`,
        `${namespace}.Services`,
        `${namespace}.Repositories`,
    ];
    if (storageIncludes(serviceConfig, 'fileSystem')) refs.push(...REPOSITORY_EXTRA_REFS);
    if (storageIncludes(serviceConfig, 'kvStore'))    refs.push(...CACHE_EXTRA_REFS);
    return refs;
}

// ============================================================
// 服務註冊表 (V1: 硬編碼，後續可遷移至 project.json)
// ============================================================

const SERVICE_REGISTRY = {
    ProjectFileRepository: {
        valueObjects: [
            {
                name: 'FileTreeNode',
                isEnum: false,
                fields: [
                    { name: 'Name', type: 'string' },
                    { name: 'Path', type: 'string' },
                    { name: 'IsDirectory', type: 'bool' },
                    { name: 'Children', type: 'List<FileTreeNode>?', nullable: true },
                    { name: 'Size', type: 'long?', nullable: true },
                    { name: 'LastModified', type: 'DateTime?', nullable: true },
                ],
            },
        ],
        interfaceMethods: [
            'Task<string> ReadFile(int projectId, string relativePath)',
            'Task WriteFile(int projectId, string relativePath, string content)',
            'Task DeleteFile(int projectId, string relativePath)',
            'Task<FileTreeNode> ListTree(int projectId)',
            'Task CreateDirectory(int projectId, string relativePath)',
            'Task<bool> FileExists(int projectId, string relativePath)',
        ],
        endpoints: [
            { method: 'Get', path: '/api/projects/{projectId:int}/files', desc: 'ListTree' },
            { method: 'Get', path: '/api/projects/{projectId:int}/files/{*path}', desc: 'ReadFile' },
            { method: 'Put', path: '/api/projects/{projectId:int}/files/{*path}', desc: 'WriteFile' },
            { method: 'Delete', path: '/api/projects/{projectId:int}/files/{*path}', desc: 'DeleteFile' },
        ],
        pages: [],
        implConstraints: [
            '所有路徑操作必須使用 Path.Combine，禁止字串拼接',
            '必須驗證 relativePath 不包含 ".." 防止路徑穿越攻擊',
            '使用 System.IO.File 和 System.IO.Directory 操作檔案',
            '專案根目錄由配置決定，不可硬編碼',
        ],
    },

    EditorService: {
        valueObjects: [
            {
                name: 'LockResult',
                isEnum: false,
                fields: [
                    { name: 'Success', type: 'bool' },
                    { name: 'LockedBy', type: 'string?', nullable: true },
                    { name: 'ExpiresAt', type: 'DateTime?', nullable: true },
                ],
            },
            {
                name: 'SessionState',
                isEnum: false,
                fields: [
                    { name: 'SessionId', type: 'string' },
                    { name: 'ProjectId', type: 'int' },
                    { name: 'OpenFiles', type: 'List<string>' },
                    { name: 'ActiveFile', type: 'string?', nullable: true },
                    { name: 'LastActivity', type: 'DateTime' },
                ],
            },
        ],
        interfaceMethods: [
            'Task<string> OpenFile(int projectId, string relativePath)',
            'Task SaveFile(int projectId, string relativePath, string content)',
            'Task<FileTreeNode> GetFileTree(int projectId)',
            'Task<LockResult> AcquireLock(int projectId, string relativePath, string userId)',
            'Task ReleaseLock(int projectId, string relativePath, string userId)',
            'Task SaveSession(SessionState session)',
            'Task<SessionState?> LoadSession(string sessionId)',
        ],
        endpoints: [],
        pages: ['EditorPage'],
        implConstraints: [
            '透過 IProjectFileRepository 操作檔案，不直接存取 System.IO',
            '透過 BaseCache 管理檔案鎖和 session 狀態',
            '鎖的過期時間預設 5 分鐘',
            '鎖的 key 格式: lock:{projectId}:{relativePath}',
            'session 的 key 格式: session:{sessionId}',
        ],
    },

    GeneratorService: {
        valueObjects: [
            {
                name: 'GenerationResult',
                isEnum: false,
                fields: [
                    { name: 'Success', type: 'bool' },
                    { name: 'GeneratedFiles', type: 'List<string>' },
                    { name: 'Errors', type: 'List<string>?', nullable: true },
                    { name: 'Duration', type: 'TimeSpan' },
                ],
            },
            {
                name: 'GenerationStatus',
                isEnum: true,
                values: ['Idle', 'Running', 'Completed', 'Failed'],
            },
        ],
        interfaceMethods: [
            'Task<GenerationResult> GenerateProject(int projectId)',
            'Task<GenerationResult> GenerateEntity(int projectId, int entityId)',
            'Task<GenerationStatus> GetGenerationStatus(int projectId)',
        ],
        endpoints: [
            { method: 'Post', path: '/api/projects/{projectId:int}/generate', desc: 'GenerateProject' },
            { method: 'Post', path: '/api/projects/{projectId:int}/entities/{entityId:int}/generate', desc: 'GenerateEntity' },
            { method: 'Get', path: '/api/projects/{projectId:int}/generate/status', desc: 'GetGenerationStatus' },
        ],
        pages: [],
        implConstraints: [
            '透過 IProjectFileRepository 寫入生成的檔案',
            '透過 AppDb 讀取 ProjectEntity 定義',
            'GenerationStatus 是 enum: Idle, Running, Completed, Failed',
            '生成過程中的狀態追蹤用 static ConcurrentDictionary 或 BaseCache',
        ],
    },
};

// ============================================================
// 元件路徑映射（前端元件存在性檢查用）
// ============================================================

const COMPONENT_PATH_MAP = {
    // Layout
    SplitPane: 'frontend/components/layout/SplitPane',
    TreeList: 'frontend/components/layout/TreeList',
    TabContainer: 'frontend/components/layout/TabContainer',
    DataTable: 'frontend/components/layout/DataTable',
    // Form
    CodeEditor: 'frontend/components/form/CodeEditor',
    // Common
    PreviewFrame: 'frontend/components/common/PreviewFrame',
    ButtonGroup: 'frontend/components/common/ButtonGroup',
    TerminalOutput: 'frontend/components/common/TerminalOutput',
    ActionButton: 'frontend/components/common/ActionButton',
    LoadingSpinner: 'frontend/components/common/LoadingSpinner',
    Notification: 'frontend/components/common/Notification',
};

const COMPONENT_CATEGORY_MAP = {
    SplitPane: 'layout', TreeList: 'layout', TabContainer: 'layout', DataTable: 'layout',
    CodeEditor: 'form',
    PreviewFrame: 'common', ButtonGroup: 'common', TerminalOutput: 'common',
    ActionButton: 'common', LoadingSpinner: 'common', Notification: 'common',
};

const COMPONENT_PRIORITY_MAP = {
    CodeEditor: 'CRITICAL',
    SplitPane: 'HIGH', PreviewFrame: 'HIGH', TerminalOutput: 'HIGH',
    TreeList: 'MEDIUM', TabContainer: 'MEDIUM',
    ButtonGroup: 'LOW', ActionButton: 'LOW', DataTable: 'LOW',
    LoadingSpinner: 'LOW', Notification: 'LOW',
};

// ============================================================
// 頁面註冊表（從 project.json 的 pages 硬編碼，V1）
// ============================================================

const PAGE_REGISTRY = {
    EditorPage: {
        route: '/projects/:id/editor',
        components: ['SplitPane', 'TreeList', 'CodeEditor', 'PreviewFrame', 'TabContainer', 'ButtonGroup', 'TerminalOutput'],
        missingComponents: ['SplitPane', 'CodeEditor', 'PreviewFrame', 'TerminalOutput'],
    },
    DeploymentPage: {
        route: '/projects/:id/deployments',
        components: ['DataTable', 'ActionButton', 'Notification', 'TerminalOutput', 'LoadingSpinner'],
        missingComponents: ['TerminalOutput'],
    },
};

// ============================================================
// 路由正則擴展（支援 catch-all {*path}）
// ============================================================

/**
 * 將路由路徑轉換為驗證用正則
 *
 * 擴展 crud-pipeline 的版本，增加支援:
 *   - {*path} catch-all 參數 → \{\*path\}
 *   - {param:type} typed 參數 → \{param(?::\w+)?\}
 */
function routePathToRegex(routePath) {
    return routePath
        .replace(/\{\*(\w+)\}/g, '\\{\\*$1\\}')
        .replace(/\{(\w+)(?::(\w+))?\}/g, '\\{$1(?::\\w+)?\\}');
}

// ============================================================
// 輔助函數
// ============================================================

function escapeRegex(str) {
    return str.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// ============================================================
// 狀態建構器 — S1: Value Objects
// ============================================================

/**
 * 產生 Value Objects（非 DB entity 的資料類別 + enum）
 * 所有 VO 放在同一個 {ServiceName}Models.cs 檔案中
 */
function buildVoState(config) {
    const {
        serviceName, valueObjects, projectPath, projectNamespace, allowedRefs,
    } = config;
    const be = (rel) => projectPath ? `${projectPath}/${rel}` : rel;

    const voFileName = `${serviceName}Models.cs`;
    const voFilePath = be(`backend/Models/${voFileName}`);

    // class 或 enum 的必要模式
    const requiredPatterns = valueObjects.map(vo =>
        vo.isEnum
            ? `enum\\s+${vo.name}\\b`
            : `class\\s+${vo.name}\\b`
    );

    const contract = new CompletionContract({
        id: `${serviceName}-models`,
        description: `${serviceName} Value Objects: ${valueObjects.map(v => v.name).join(', ')}`,
        category: 1,
        allowedRefs,
        requiredOutputs: [voFilePath],
        requiredPatterns,
        fileChecks: [{
            path: voFilePath,
            schemaCheck: false,
            validators: [
                (content) => {
                    const errors = [];
                    for (const vo of valueObjects) {
                        if (vo.isEnum) {
                            // enum 檢查
                            const enumPattern = new RegExp(`enum\\s+${vo.name}\\b`);
                            if (!enumPattern.test(content)) {
                                errors.push(`[enum 缺失] 未找到 enum ${vo.name}`);
                                continue;
                            }
                            for (const val of (vo.values || [])) {
                                if (!content.includes(val)) {
                                    errors.push(`[enum 值缺失] ${vo.name} 缺少 ${val}`);
                                }
                            }
                        } else {
                            // class 檢查
                            const classPattern = new RegExp(`class\\s+${vo.name}\\b`);
                            if (!classPattern.test(content)) {
                                errors.push(`[類別缺失] 未找到 class ${vo.name}`);
                                continue;
                            }
                            for (const field of (vo.fields || [])) {
                                const propPattern = new RegExp(
                                    `public\\s+\\S+\\s+${field.name}\\s*\\{`, 'i'
                                );
                                if (!propPattern.test(content)) {
                                    errors.push(`[欄位缺失] ${vo.name}.${field.name} 未找到`);
                                }
                            }
                        }
                    }
                    return { errors, warnings: [] };
                },
            ],
        }],
        constraints: [
            `命名空間必須是 ${projectNamespace}.Models`,
            '這些不是 DB 實體，不需要 Id/CreatedAt/UpdatedAt',
            '每個 class 的所有屬性都必須有 public getter/setter',
        ],
    });

    return new State({
        id: 'generate-models',
        name: `產生 ${serviceName} Value Objects`,
        contract,
        maxRetries: 2,
        reportBuilder: (validation, taskCtx) =>
            classifyAndFillReport(validation, { name: serviceName }, 'generate-models', taskCtx),
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是為 ${serviceName} 建立 Value Object 類別和 enum。\n\n`;
            prompt += `## 預期產出檔案\n- ${voFilePath}\n\n`;
            prompt += `## 命名空間\n${projectNamespace}.Models\n\n`;
            prompt += `## 類別/enum 定義\n`;

            for (const vo of valueObjects) {
                if (vo.isEnum) {
                    prompt += `\n### enum ${vo.name}\n`;
                    prompt += `值: ${(vo.values || []).join(', ')}\n`;
                } else {
                    prompt += `\n### class ${vo.name}\n`;
                    prompt += `| 屬性名 | 型別 |\n|---|---|\n`;
                    for (const f of (vo.fields || [])) {
                        prompt += `| ${f.name} | ${f.type} |\n`;
                    }
                }
            }

            prompt += `\n## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請用 write_file 寫入檔案。`;
            return prompt;
        },
    });
}

// ============================================================
// 狀態建構器 — S2: Interface
// ============================================================

/**
 * 產生服務介面 (I{ServiceName}.cs)
 */
function buildInterfaceState(config) {
    const {
        serviceName, serviceType, interfaceMethods,
        projectPath, projectNamespace, allowedRefs,
    } = config;
    const be = (rel) => projectPath ? `${projectPath}/${rel}` : rel;

    const interfaceName = `I${serviceName}`;
    const dir = serviceType === 'repository' ? 'Repositories' : 'Services';
    const interfacePath = be(`backend/${dir}/${interfaceName}.cs`);

    const contract = new CompletionContract({
        id: `${serviceName}-interface`,
        description: `${interfaceName}: 定義 ${interfaceMethods.length} 個方法簽章`,
        category: 1,
        allowedRefs,
        requiredOutputs: [interfacePath],
        requiredPatterns: [
            `interface\\s+${interfaceName}\\b`,
        ],
        fileChecks: [{
            path: interfacePath,
            whitelistCheck: true,
            validators: [
                (content) => {
                    const errors = [];
                    for (const method of interfaceMethods) {
                        const methodName = method.match(/(\w+)\s*\(/)?.[1];
                        if (methodName && !content.includes(methodName)) {
                            errors.push(`[方法缺失] ${interfaceName} 缺少 ${methodName} 方法宣告`);
                        }
                    }
                    return { errors, warnings: [] };
                },
            ],
        }],
        constraints: [
            `命名空間: ${projectNamespace}.${dir}`,
            `介面名稱: ${interfaceName}`,
            `必須宣告所有方法簽章`,
        ],
    });

    return new State({
        id: 'generate-interface',
        name: `產生 ${interfaceName}`,
        contract,
        maxRetries: 2,
        reportBuilder: (validation, taskCtx) =>
            classifyAndFillReport(validation, { name: serviceName }, 'generate-interface', taskCtx),
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是建立 ${interfaceName} 介面。\n\n`;
            prompt += `## 預期產出檔案\n- ${interfacePath}\n\n`;
            prompt += `## 命名空間\n${projectNamespace}.${dir}\n\n`;
            prompt += `## 方法簽章\n`;
            for (const m of interfaceMethods) {
                prompt += `- ${m}\n`;
            }
            prompt += `\n## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請用 write_file 寫入檔案。`;
            return prompt;
        },
    });
}

// ============================================================
// 狀態建構器 — S3: Implementation（多重 DI 核心）
// ============================================================

/**
 * 產生實作類別 ({ServiceName}.cs)
 *
 * 與 CRUD 的關鍵差異：驗證多個 DI 注入（非僅 AppDb）
 * 建構子必須包含所有 dependencies 的 private readonly 宣告與注入
 */
function buildImplementationState(config) {
    const {
        serviceName, serviceType, dependencies, interfaceMethods, implConstraints,
        projectPath, projectNamespace, allowedRefs,
    } = config;
    const be = (rel) => projectPath ? `${projectPath}/${rel}` : rel;

    const interfaceName = `I${serviceName}`;
    const dir = serviceType === 'repository' ? 'Repositories' : 'Services';
    const implPath = be(`backend/${dir}/${serviceName}.cs`);
    const interfaceFilePath = be(`backend/${dir}/${interfaceName}.cs`);

    const diDependencies = dependencies || [];

    // 必要模式: class 宣告 + 每個依賴的 private readonly
    const requiredPatterns = [
        `class\\s+${serviceName}\\s*:\\s*${escapeRegex(interfaceName)}`,
    ];
    for (const dep of diDependencies) {
        requiredPatterns.push(`private\\s+readonly\\s+${escapeRegex(dep)}\\s+`);
    }

    const contract = new CompletionContract({
        id: `${serviceName}-impl`,
        description: `${serviceName}: 實作 ${interfaceName}, 注入 ${diDependencies.length > 0 ? diDependencies.join(' + ') : '無外部依賴'}`,
        category: 2,
        allowedRefs,
        forbiddenPatterns: FORBIDDEN_BACKEND_PATTERNS,
        requiredOutputs: [implPath],
        requiredPatterns,
        fileChecks: [
            {
                path: implPath,
                whitelistCheck: true,
                validators: [
                    (content) => {
                        const errors = [];
                        // 驗證建構子包含所有 DI 依賴
                        if (diDependencies.length > 0) {
                            const ctorPattern = new RegExp(
                                `${escapeRegex(serviceName)}\\s*\\([^)]*\\)`, 's'
                            );
                            const ctorMatch = content.match(ctorPattern);
                            if (ctorMatch) {
                                const ctorArgs = ctorMatch[0];
                                for (const dep of diDependencies) {
                                    if (!ctorArgs.includes(dep)) {
                                        errors.push(`[DI 缺失] 建構子未注入 ${dep}`);
                                    }
                                }
                            } else {
                                errors.push(`[建構子缺失] 未找到 ${serviceName} 建構子`);
                            }
                        }
                        // 驗證所有介面方法已實作
                        for (const method of interfaceMethods) {
                            const methodName = method.match(/(\w+)\s*\(/)?.[1];
                            if (methodName && !content.includes(methodName)) {
                                errors.push(`[方法缺失] ${serviceName} 缺少 ${methodName} 實作`);
                            }
                        }
                        return { errors, warnings: [] };
                    },
                ],
            },
            {
                // 介面檔案: 可選讀取（用於 context 注入）
                path: interfaceFilePath,
                mustExist: false,
            },
        ],
        contextFiles: [interfaceFilePath],
        constraints: [
            `命名空間: ${projectNamespace}.${dir}`,
            `類別: ${serviceName} : ${interfaceName}`,
            ...(diDependencies.length > 0
                ? [`建構子注入: ${diDependencies.join(', ')}`]
                : []),
            ...(implConstraints || []),
        ],
    });

    return new State({
        id: 'generate-implementation',
        name: `實作 ${serviceName}`,
        contract,
        maxRetries: 2,
        reportBuilder: (validation, taskCtx) =>
            classifyAndFillReport(
                validation,
                { name: serviceName, dependencies: diDependencies, methods: interfaceMethods },
                'generate-implementation',
                taskCtx
            ),
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是實作 ${serviceName}。\n\n`;
            prompt += `## 預期產出檔案\n- ${implPath}\n\n`;
            prompt += `## 命名空間\n${projectNamespace}.${dir}\n\n`;
            prompt += `## 類別宣告\npublic class ${serviceName} : ${interfaceName}\n\n`;

            if (diDependencies.length > 0) {
                prompt += `## DI 注入（建構子必須接受以下所有依賴）\n`;
                for (const dep of diDependencies) {
                    prompt += `- private readonly ${dep} _${dep.replace(/^I/, '').charAt(0).toLowerCase() + dep.replace(/^I/, '').slice(1)}\n`;
                }
                prompt += `\n`;
            }

            prompt += `## 需要實作的方法\n`;
            for (const m of interfaceMethods) {
                prompt += `- ${m}\n`;
            }
            prompt += `\n`;

            if (ctx.referenceCode[interfaceFilePath]) {
                prompt += `## 介面定義（必須完全實作）\n`;
                prompt += '```csharp\n' + ctx.referenceCode[interfaceFilePath] + '\n```\n\n';
            }

            prompt += `## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請用 write_file 寫入檔案。`;
            return prompt;
        },
    });
}

// ============================================================
// 狀態建構器 — S4: Endpoints（獨立檔案，不碰 Program.cs）
// ============================================================

/**
 * 建立 {ServiceName}Endpoints.cs 獨立端點檔案
 *
 * Convention 架構: 每個服務的端點放在獨立檔案中。
 * EndpointRegistration.cs 會自動掃描所有 static class 的 Map() 方法並掛載。
 * Agent 只建立新檔案，永遠不修改 Program.cs。
 */
function buildEndpointsState(config) {
    const {
        serviceName, endpoints, projectPath, projectNamespace, allowedRefs,
    } = config;
    const be = (rel) => projectPath ? `${projectPath}/${rel}` : rel;

    const endpointFileName = `${serviceName}Endpoints.cs`;
    const endpointFilePath = be(`backend/Endpoints/${endpointFileName}`);
    const interfaceName = `I${serviceName}`;

    // 路由正則（驗證用）
    const routePatterns = endpoints.map(r =>
        `\\.Map${r.method}\\s*\\(\\s*"${routePathToRegex(r.path)}"`
    );

    const contract = new CompletionContract({
        id: `${serviceName}-endpoints`,
        description: `API 端點: ${endpoints.length} 個路由 → ${endpointFileName}`,
        category: 1,
        allowedRefs: [...(allowedRefs || []), `${projectNamespace}.Endpoints`],
        requiredOutputs: [endpointFilePath],
        requiredPatterns: [
            `static\\s+class\\s+${serviceName}Endpoints`,
            `static\\s+void\\s+Map\\s*\\(\\s*WebApplication`,
            ...routePatterns,
        ],
        fileChecks: [{
            path: endpointFilePath,
            whitelistCheck: true,
            validators: [
                (content) => {
                    const errors = [];
                    for (const r of endpoints) {
                        const regexPath = routePathToRegex(r.path);
                        const pattern = new RegExp(`\\.Map${r.method}\\s*\\(\\s*"${regexPath}"`);
                        if (!pattern.test(content)) {
                            errors.push(`[路由缺失] ${r.method.toUpperCase()} ${r.path} (${r.desc})`);
                        }
                    }
                    return { errors, warnings: [] };
                },
            ],
        }],
        constraints: [
            `命名空間: ${projectNamespace}.Endpoints`,
            `類別: public static class ${serviceName}Endpoints`,
            `方法: public static void Map(WebApplication app)`,
            `${endpoints.length} 個端點:`,
            ...endpoints.map(r => `  ${r.method.toUpperCase()} "${r.path}" → ${r.desc}`),
            '每個端點需要 .RequireAuthorization().RequireRateLimiting("api")',
            `透過參數注入 ${interfaceName}`,
        ],
    });

    return new State({
        id: 'generate-endpoints',
        name: `新增 ${serviceName} API 端點`,
        contract,
        maxRetries: 2,
        reportBuilder: (validation, taskCtx) =>
            classifyAndFillReport(validation, { name: serviceName }, 'generate-endpoints', taskCtx),
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是建立 ${serviceName} 的 API 端點檔案。\n\n`;
            prompt += `## 預期產出檔案\n- ${endpointFilePath}\n\n`;
            prompt += `## 格式\n`;
            prompt += `建立一個 public static class，包含 public static void Map(WebApplication app) 方法。\n`;
            prompt += `所有端點定義在 Map() 方法內。\n\n`;
            prompt += `## 端點\n`;
            for (const r of endpoints) {
                prompt += `- ${r.method.toUpperCase()} "${r.path}" → ${r.desc}\n`;
            }
            prompt += `\n## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請用 write_file 建立新檔案。**不要修改 Program.cs**，端點會由 Convention 自動掛載。`;
            return prompt;
        },
    });
}

// ============================================================
// 狀態建構器 — S6: Pages（含元件 preCheck）
// ============================================================

/**
 * 產生前端頁面
 *
 * 特殊機制: preCheck
 *   在 Agent 執行前先檢查所需元件是否存在。
 *   若缺失元件，直接產生 component_missing 回報，不呼叫 Agent。
 */
function buildPageState(config) {
    const {
        serviceName, pageName, projectPath,
    } = config;
    const be = (rel) => projectPath ? `${projectPath}/${rel}` : rel;

    const pageInfo = PAGE_REGISTRY[pageName];
    if (!pageInfo) throw new Error(`Unknown page: ${pageName}`);

    const pageDir = pageName.replace(/Page$/, '').toLowerCase();
    const pageFilePath = be(`frontend/pages/${pageDir}/${pageName}.js`);

    const contract = new CompletionContract({
        id: `${serviceName}-pages`,
        description: `前端頁面: ${pageName}`,
        category: 2,
        requiredOutputs: [pageFilePath],
        requiredPatterns: [
            `class\\s+${pageName}`,
        ],
        fileChecks: [{
            path: pageFilePath,
            validators: [
                (content) => {
                    const errors = [];
                    if (!content.includes('BasePage') && !content.includes('DefinedPage')) {
                        errors.push(`[基類缺失] ${pageName} 必須繼承 BasePage 或 DefinedPage`);
                    }
                    return { errors, warnings: [] };
                },
            ],
        }],
        constraints: [
            `頁面必須繼承 BasePage 或 DefinedPage`,
            `路由: ${pageInfo.route}`,
            `使用元件: ${pageInfo.components.join(', ')}`,
        ],
    });

    // preCheck: 在呼叫 Agent 前先檢查元件是否存在
    const preCheck = (rootDir) => {
        const missing = [];
        const available = [];

        for (const comp of pageInfo.components) {
            const compRelPath = COMPONENT_PATH_MAP[comp];
            if (!compRelPath) {
                available.push(comp); // 未在映射中 → 假設存在（或為內建）
                continue;
            }

            const fullPath = path.join(rootDir, projectPath || '', compRelPath);
            // 檢查目錄或 .js 檔是否存在
            if (fs.existsSync(fullPath) ||
                fs.existsSync(fullPath + '.js') ||
                fs.existsSync(path.join(fullPath, 'index.js'))) {
                available.push(comp);
            } else {
                missing.push(comp);
            }
        }

        if (missing.length > 0) {
            return {
                passed: false,
                errors: missing.map(c => `[component missing] ${c}`),
                report: fillTemplate('component_missing', {
                    page: pageName,
                    missingComponents: missing.map(c => ({
                        name: c,
                        expectedPath: COMPONENT_PATH_MAP[c] || 'unknown',
                        category: COMPONENT_CATEGORY_MAP[c] || 'unknown',
                        priority: COMPONENT_PRIORITY_MAP[c] || 'MEDIUM',
                    })),
                    availableComponents: available,
                    actionRequired: '建立缺失元件後重新執行管線',
                }),
            };
        }
        return { passed: true };
    };

    return new State({
        id: 'generate-pages',
        name: `產生 ${pageName}`,
        contract,
        maxRetries: 2,
        preCheck,
        reportBuilder: (validation, taskCtx) =>
            classifyAndFillReport(
                validation,
                { name: serviceName },
                'generate-pages',
                { ...taskCtx, pageName }
            ),
        promptBuilder: (ctx, taskCtx) => {
            let prompt = `你的任務是建立 ${pageName} 前端頁面。\n\n`;
            prompt += `## 預期產出\n- ${pageFilePath}\n\n`;
            prompt += `## 路由\n${pageInfo.route}\n\n`;
            prompt += `## 使用元件\n`;
            for (const comp of pageInfo.components) {
                prompt += `- ${comp}\n`;
            }

            prompt += `\n## 約束\n`;
            ctx.constraints.forEach(c => prompt += `- ${c}\n`);
            prompt += `\n請用 write_file 寫入檔案。`;
            return prompt;
        },
    });
}

// ============================================================
// 主建構函式
// ============================================================

/**
 * 建構服務管線
 *
 * 根據 project.json 中的 extendedService 定義 + SERVICE_REGISTRY，
 * 自動組合出對應的狀態列表。
 *
 * @param {Object} config
 * @param {Object} config.serviceConfig - project.json 中的 extendedService 定義
 * @param {string} config.projectPath - 專案子路徑
 * @param {string} config.projectRoot - 專案根目錄
 * @returns {State[]}
 */
function buildServicePipeline(config) {
    const { serviceConfig, projectPath = '', projectRoot = process.cwd() } = config;
    const serviceName = serviceConfig.name;
    const serviceType = serviceConfig.type || 'service';
    const dependencies = serviceConfig.dependencies || [];

    // 從 registry 取得詳細定義
    const registry = SERVICE_REGISTRY[serviceName];
    if (!registry) {
        throw new Error(`Service "${serviceName}" 不在 SERVICE_REGISTRY 中，請先新增註冊`);
    }

    // 偵測 namespace
    const projectNamespace = detectProjectNamespace(projectRoot, projectPath);
    const allowedRefs = buildAllowedServiceRefs(projectNamespace, serviceConfig);

    const shared = {
        serviceName,
        serviceType,
        dependencies,
        projectPath,
        projectRoot,
        projectNamespace,
        allowedRefs,
        interfaceMethods: registry.interfaceMethods,
        implConstraints: registry.implConstraints,
    };

    const states = [];

    // S1: Value Objects (可選)
    if (registry.valueObjects && registry.valueObjects.length > 0) {
        states.push(buildVoState({
            ...shared,
            valueObjects: registry.valueObjects,
        }));
    }

    // S2: Interface (必要)
    states.push(buildInterfaceState(shared));

    // S3: Implementation (必要)
    states.push(buildImplementationState(shared));

    // DI 註冊: 由 Convention 自動處理（ServiceRegistration.cs 掃描 IXxx→Xxx 配對）
    // 不需要 Agent 修改 Program.cs

    // S4: Endpoints (可選) — 建立獨立的 {Service}Endpoints.cs
    if (registry.endpoints && registry.endpoints.length > 0) {
        states.push(buildEndpointsState({
            ...shared,
            endpoints: registry.endpoints,
        }));
    }

    // S5: Pages (可選，含 preCheck)
    if (registry.pages && registry.pages.length > 0) {
        for (const pageName of registry.pages) {
            states.push(buildPageState({
                ...shared,
                pageName,
            }));
        }
    }

    return states;
}

// ============================================================
// Exports
// ============================================================

module.exports = {
    // 主建構
    buildServicePipeline,

    // 回報模板
    REPORT_TEMPLATES,
    fillTemplate,
    classifyAndFillReport,
    parseErrorCode,

    // 註冊表
    SERVICE_REGISTRY,
    PAGE_REGISTRY,
    COMPONENT_PATH_MAP,
    COMPONENT_CATEGORY_MAP,
    COMPONENT_PRIORITY_MAP,

    // 白名單
    buildAllowedServiceRefs,
    SERVICE_COMMON_REFS,
    REPOSITORY_EXTRA_REFS,
    CACHE_EXTRA_REFS,

    // 路由工具
    routePathToRegex,
};
