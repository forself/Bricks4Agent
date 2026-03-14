#!/usr/bin/env node
/**
 * page-gen.js - 頁面生成 CLI 工具
 *
 * 讀取新格式（AI 格式）的頁面定義 JSON，並可：
 * 1. 生成靜態 .js 頁面檔案（經轉換後透過 PageGenerator）
 * 2. 輸出動態渲染定義 JSON
 * 3. 驗證定義
 * 4. 列出可用欄位類型
 *
 * 所有輸出皆為 JSON 格式（stdout），錯誤訊息輸出至 stderr。
 *
 * @module page-gen
 */

const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const {
    assertValidDefinitionTemplate,
    resolveTemplateEnvelope,
    extractPageEntry
} = require('./lib/definition-template.js');

// ============================================================
// 常數定義
// ============================================================

/** 新格式（AI 格式）允許的欄位類型 */
const VALID_FIELD_TYPES = [
    // 基本類型
    'text', 'email', 'password', 'number', 'textarea',
    // 日期時間
    'date', 'time', 'datetime',
    // 選擇類型
    'select', 'multiselect', 'checkbox', 'toggle', 'radio',
    // 進階類型
    'richtext', 'canvas', 'color', 'image', 'file',
    // 服務類型
    'geolocation', 'weather',
    // 複合輸入元件
    'address', 'addresslist', 'chained', 'list',
    'personinfo', 'phonelist', 'socialmedia',
    'organization', 'student',
    // 隱藏
    'hidden'
];

/** 觸發器允許的 on 值 */
const VALID_TRIGGER_ON = ['change', 'check', 'uncheck', 'upload'];

/** 觸發器允許的 action 值 */
const VALID_TRIGGER_ACTIONS = [
    'reloadOptions', 'show', 'hide',
    'setReadonly', 'setRequired',
    'reload', 'setValue', 'clear'
];

/** optionsSource.type 允許值 */
const VALID_OPTIONS_SOURCE_TYPES = ['static', 'api'];

/** 舊格式頁面類型對應（view → type） */
const VIEW_TO_PAGE_TYPE = {
    form: 'form',
    create: 'form',
    edit: 'form',
    list: 'list',
    detail: 'detail',
    dashboard: 'dashboard'
};

// ============================================================
// CLI 參數解析
// ============================================================

/**
 * 手動解析 CLI 參數（不依賴外部套件）
 * @param {string[]} argv - process.argv
 * @returns {Object} 解析後的參數
 */
function parseArgs(argv) {
    const args = {
        def: null,
        page: null,
        mode: 'static',
        output: null,
        validate: false,
        listTypes: false,
        help: false
    };

    const rawArgs = argv.slice(2);

    for (let i = 0; i < rawArgs.length; i++) {
        const arg = rawArgs[i];

        switch (arg) {
            case '--def':
                args.def = rawArgs[++i];
                break;
            case '--page':
                args.page = rawArgs[++i];
                break;
            case '--mode':
                args.mode = rawArgs[++i];
                break;
            case '--output':
                args.output = rawArgs[++i];
                break;
            case '--validate':
                args.validate = true;
                break;
            case '--list-types':
                args.listTypes = true;
                break;
            case '--help':
            case '-h':
                args.help = true;
                break;
            default:
                // 忽略未知參數
                break;
        }
    }

    return args;
}

// ============================================================
// 說明訊息
// ============================================================

function printHelp() {
    const help = `
page-gen.js - 頁面生成 CLI 工具

用法：
  node tools/page-gen.js --def <path> --mode <mode> --output <dir>
  echo '{"page":{...},"fields":[...]}' | node tools/page-gen.js --mode static --output ./output/

選項：
  --def <path>       頁面定義 JSON 檔案路徑
  --page <id>        DefinitionTemplate 內要選用的 page id
  --mode <mode>      生成模式：static | dynamic | both（預設：static）
  --output <dir>     輸出目錄
  --validate         僅驗證定義，不生成檔案
  --list-types       列出所有可用的欄位類型、觸發器事件與動作
  --help, -h         顯示此說明

範例：
  # 生成靜態頁面
  node tools/page-gen.js --def employee.json --mode static --output ./output/

  # 從 DefinitionTemplate 選取單一 page
  node tools/page-gen.js --def site-definition.json --page products-list --mode static --output ./output/

  # 生成動態定義 JSON
  node tools/page-gen.js --def employee.json --mode dynamic --output ./output/

  # 同時生成兩種
  node tools/page-gen.js --def employee.json --mode both --output ./output/

  # 從 stdin 讀取（適用於 AI 代理管線）
  echo '{"page":{...},"fields":[...]}' | node tools/page-gen.js --mode static --output ./output/

  # 驗證定義
  node tools/page-gen.js --validate --def employee.json

  # 列出可用類型
  node tools/page-gen.js --list-types
`;
    process.stderr.write(help.trim() + '\n');
}

// ============================================================
// 定義驗證（新 AI 格式）
// ============================================================

/**
 * 驗證新格式的頁面定義
 * @param {Object} definition - 新格式定義 { page, fields }
 * @returns {{ valid: boolean, errors: string[] }}
 */
function validateNewDefinition(definition) {
    const errors = [];

    // === page 區塊驗證 ===
    if (!definition.page) {
        errors.push('缺少 page 區塊');
        return { valid: false, errors };
    }

    const { page } = definition;

    if (!page.pageName) {
        errors.push('缺少頁面名稱 (page.pageName)');
    }

    if (!page.entity) {
        errors.push('缺少實體名稱 (page.entity)');
    }

    if (!page.view) {
        errors.push('缺少頁面檢視類型 (page.view)');
    }

    // === fields 區塊驗證 ===
    if (!definition.fields || !Array.isArray(definition.fields)) {
        errors.push('缺少 fields 陣列');
        return { valid: errors.length === 0, errors };
    }

    const fieldNames = new Set();

    for (let i = 0; i < definition.fields.length; i++) {
        const field = definition.fields[i];
        const prefix = `fields[${i}]`;

        // 必要屬性
        if (!field.fieldName) {
            errors.push(`${prefix}: 缺少欄位名稱 (fieldName)`);
        } else {
            // 重複欄位名稱檢查
            if (fieldNames.has(field.fieldName)) {
                errors.push(`${prefix}: 欄位名稱重複 (${field.fieldName})`);
            }
            fieldNames.add(field.fieldName);
        }

        if (!field.label) {
            errors.push(`${prefix}: 缺少顯示標籤 (label)`);
        }

        if (!field.fieldType) {
            errors.push(`${prefix}: 缺少欄位類型 (fieldType)`);
        } else if (!VALID_FIELD_TYPES.includes(field.fieldType)) {
            errors.push(`${prefix}: 無效的欄位類型 "${field.fieldType}"，允許值：${VALID_FIELD_TYPES.join(', ')}`);
        }

        // 觸發器驗證
        if (field.triggers && Array.isArray(field.triggers)) {
            for (let j = 0; j < field.triggers.length; j++) {
                const trigger = field.triggers[j];
                const tPrefix = `${prefix}.triggers[${j}]`;

                if (trigger.on && !VALID_TRIGGER_ON.includes(trigger.on)) {
                    errors.push(`${tPrefix}: 無效的觸發事件 "${trigger.on}"，允許值：${VALID_TRIGGER_ON.join(', ')}`);
                }

                if (trigger.action && !VALID_TRIGGER_ACTIONS.includes(trigger.action)) {
                    errors.push(`${tPrefix}: 無效的觸發動作 "${trigger.action}"，允許值：${VALID_TRIGGER_ACTIONS.join(', ')}`);
                }
            }
        }

        // optionsSource 驗證
        if (field.optionsSource) {
            if (field.optionsSource.type && !VALID_OPTIONS_SOURCE_TYPES.includes(field.optionsSource.type)) {
                errors.push(`${prefix}.optionsSource: 無效的來源類型 "${field.optionsSource.type}"，允許值：${VALID_OPTIONS_SOURCE_TYPES.join(', ')}`);
            }
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

// ============================================================
// 格式轉換：新格式 → 舊格式（PageDefinition）
// ============================================================

/**
 * 將新 AI 格式轉換為 PageGenerator 可接受的舊格式
 * @param {Object} newDef - 新格式 { page, fields }
 * @returns {Object} 舊格式 PageDefinition
 */
function convertToOldFormat(newDef) {
    const { page, fields } = newDef;

    // 決定頁面類型
    const viewLower = (page.view || '').toLowerCase();
    const pageType = viewLower.includes('list') ? 'list'
        : viewLower.includes('detail') ? 'detail'
        : VIEW_TO_PAGE_TYPE[page.view] || 'form';

    // 從 entity 推導 PascalCase 頁面名稱（PageGenerator 要求 PascalCase + Page 結尾）
    const entity = page.entity || 'unnamed';
    const pageName = entity.charAt(0).toUpperCase() + entity.slice(1) + 'Page';

    // 轉換欄位
    const oldFields = (fields || []).map(field => {
        const oldField = {
            name: field.fieldName,
            type: field.fieldType === 'multiselect' ? 'select' : field.fieldType,
            label: field.label
        };

        if (field.isRequired) oldField.required = true;
        if (field.defaultValue !== undefined && field.defaultValue !== null) oldField.default = field.defaultValue;

        // 轉換 options（來自 optionsSource.items）
        if (field.optionsSource && field.optionsSource.type === 'static' && field.optionsSource.items) {
            oldField.options = field.optionsSource.items;
        }

        // 驗證規則
        if (field.validation) {
            oldField.validation = field.validation;
        }

        return oldField;
    });

    // 推斷元件
    const components = [];
    const componentSet = new Set();
    const fieldTypeToComponent = {
        date: 'DatePicker',
        color: 'ColorPicker',
        image: 'ImageViewer'
    };

    for (const field of fields || []) {
        const comp = fieldTypeToComponent[field.fieldType];
        if (comp && !componentSet.has(comp)) {
            componentSet.add(comp);
            components.push(comp);
        }
    }

    // 推斷 API
    const api = {};
    if (page.apiEndpoint) {
        api.create = page.apiEndpoint;
        api.update = page.apiEndpoint;
        api.get = page.apiEndpoint;
        api.delete = page.apiEndpoint;
    }
    if (page.api) {
        Object.assign(api, page.api);
    }

    // 建構行為定義
    const behaviors = {};
    if (page.behaviors) {
        Object.assign(behaviors, page.behaviors);
    }

    // 建構欄位觸發器（從新格式的 field.triggers 轉為舊格式的 behaviors.fieldTriggers）
    const fieldTriggers = {};
    for (const field of fields || []) {
        if (field.triggers && field.triggers.length > 0) {
            fieldTriggers[field.fieldName] = `handle_${field.fieldName}_trigger`;
        }
    }
    if (Object.keys(fieldTriggers).length > 0) {
        behaviors.fieldTriggers = fieldTriggers;
    }

    return {
        name: pageName,
        type: pageType,
        description: page.pageName || pageName.replace(/Page$/, ''),
        components,
        services: [],
        fields: oldFields,
        api,
        behaviors,
        styles: {
            layout: page.layout || 'single',
            theme: page.theme || 'default'
        }
    };
}

// ============================================================
// 讀取定義
// ============================================================

/**
 * 從檔案讀取 JSON 定義
 * @param {string} filePath - 檔案路徑
 * @returns {Object} 解析後的 JSON
 */
function readDefinitionFromFile(filePath) {
    const resolved = path.resolve(filePath);
    if (!fs.existsSync(resolved)) {
        throw new Error(`檔案不存在: ${resolved}`);
    }
    const content = fs.readFileSync(resolved, 'utf-8');
    return JSON.parse(content);
}

/**
 * 從 stdin 讀取 JSON 定義
 * @returns {Promise<Object>} 解析後的 JSON
 */
function readDefinitionFromStdin() {
    return new Promise((resolve, reject) => {
        let data = '';
        process.stdin.setEncoding('utf-8');
        process.stdin.on('data', chunk => { data += chunk; });
        process.stdin.on('end', () => {
            try {
                resolve(JSON.parse(data));
            } catch (e) {
                reject(new Error(`stdin JSON 解析失敗: ${e.message}`));
            }
        });
        process.stdin.on('error', reject);
    });
}

/**
 * 偵測是否有 stdin 管線輸入
 * @returns {boolean}
 */
function hasStdinPipe() {
    return !process.stdin.isTTY;
}

let pageDefinitionModulesPromise = null;

async function loadPageDefinitionModules() {
    if (!pageDefinitionModulesPromise) {
        const adapterPath = pathToFileURL(
            path.resolve(__dirname, '../packages/javascript/browser/page-generator/PageDefinitionAdapter.js')
        ).href;
        const definitionPath = pathToFileURL(
            path.resolve(__dirname, '../packages/javascript/browser/page-generator/PageDefinition.js')
        ).href;

        pageDefinitionModulesPromise = Promise.all([
            import(adapterPath),
            import(definitionPath)
        ]).then(([adapterModule, definitionModule]) => ({
            PageDefinitionAdapter: adapterModule.PageDefinitionAdapter || adapterModule.default,
            validatePageDefinition: definitionModule.validateDefinition
        }));
    }

    return pageDefinitionModulesPromise;
}

async function normalizeDefinitionInput(definition, pageId) {
    const envelope = resolveTemplateEnvelope(definition, pageId);
    if (!envelope) {
        return {
            source: 'legacy-page-definition',
            definition,
            pageId: null,
            oldDefinition: null,
            templateStats: null
        };
    }

    const templateValidation = await assertValidDefinitionTemplate(envelope.template);
    const { pageId: selectedPageId, pageDefinition } = extractPageEntry(envelope.template, envelope.pageId);
    const { PageDefinitionAdapter } = await loadPageDefinitionModules();
    const newDefinition = PageDefinitionAdapter.toNewFormat(pageDefinition);

    if (!newDefinition) {
        throw new Error(`無法將 page ${selectedPageId} 轉為 CLI 可用格式`);
    }

    return {
        source: 'definition-template',
        definition: newDefinition,
        pageId: selectedPageId,
        oldDefinition: pageDefinition,
        templateStats: templateValidation.stats
    };
}

async function validateNormalizedDefinition(normalized) {
    const errors = [];

    if (normalized.oldDefinition) {
        const { validatePageDefinition } = await loadPageDefinitionModules();
        const pageValidation = validatePageDefinition(normalized.oldDefinition);
        if (!pageValidation.valid) {
            errors.push(...pageValidation.errors);
        }
    }

    const newValidation = validateNewDefinition(normalized.definition);
    if (!newValidation.valid) {
        errors.push(...newValidation.errors);
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

// ============================================================
// 生成功能
// ============================================================

/**
 * 生成靜態 .js 頁面檔案
 * @param {Object} newDef - 新格式定義
 * @param {string} outputDir - 輸出目錄
 * @returns {Promise<Object>} 生成結果 { path, type, size }
 */
async function generateStatic(newDef, outputDir) {
    // 轉換為舊格式
    const oldDef = convertToOldFormat(newDef);
    return generateStaticFromOldDefinition(oldDef, outputDir);
}

async function generateStaticFromOldDefinition(oldDef, outputDir) {
    // 動態載入 PageGenerator
    const generatorPath = pathToFileURL(
        path.resolve(__dirname, '../packages/javascript/browser/page-generator/PageGenerator.js')
    ).href;
    const { PageGenerator } = await import(generatorPath);

    // 生成程式碼
    const generator = new PageGenerator();
    const result = generator.generate(oldDef);

    if (result.errors && result.errors.length > 0) {
        throw new Error(`生成失敗: ${result.errors.join('; ')}`);
    }

    // 確保輸出目錄存在
    fs.mkdirSync(outputDir, { recursive: true });

    // 寫入檔案
    const fileName = `${oldDef.name}.js`;
    const filePath = path.join(outputDir, fileName);
    fs.writeFileSync(filePath, result.code, 'utf-8');

    const stats = fs.statSync(filePath);
    return {
        path: filePath,
        type: 'static',
        size: stats.size
    };
}

/**
 * 生成動態渲染定義 JSON
 * @param {Object} newDef - 新格式定義
 * @param {string} outputDir - 輸出目錄
 * @returns {Object} 生成結果 { path, type, size }
 */
function generateDynamic(newDef, outputDir, fileBaseName = null) {
    // 驗證
    const validation = validateNewDefinition(newDef);
    if (!validation.valid) {
        throw new Error(`定義驗證失敗: ${validation.errors.join('; ')}`);
    }

    // 確保輸出目錄存在
    fs.mkdirSync(outputDir, { recursive: true });

    // 決定檔名
    const entity = newDef.page.entity || newDef.page.pageName || 'unknown';
    const baseName = fileBaseName || entity.toLowerCase();
    const fileName = `${baseName}-definition.json`;
    const filePath = path.join(outputDir, fileName);

    // 寫入 JSON
    const jsonContent = JSON.stringify(newDef, null, 2);
    fs.writeFileSync(filePath, jsonContent, 'utf-8');

    const stats = fs.statSync(filePath);
    return {
        path: filePath,
        type: 'dynamic',
        size: stats.size
    };
}

// ============================================================
// 列出可用類型
// ============================================================

function listTypes() {
    const result = {
        fieldTypes: VALID_FIELD_TYPES,
        triggerOn: VALID_TRIGGER_ON,
        triggerActions: VALID_TRIGGER_ACTIONS,
        optionsSourceTypes: VALID_OPTIONS_SOURCE_TYPES
    };

    outputJSON({ success: true, ...result });
}

// ============================================================
// 輸出工具
// ============================================================

/**
 * 輸出 JSON 至 stdout
 * @param {Object} data
 */
function outputJSON(data) {
    process.stdout.write(JSON.stringify(data, null, 2) + '\n');
}

/**
 * 輸出錯誤 JSON 至 stdout，訊息至 stderr
 * @param {string[]} errors
 */
function outputError(errors) {
    outputJSON({ success: false, errors });
}

// ============================================================
// 主程式
// ============================================================

async function main() {
    const args = parseArgs(process.argv);

    // 說明
    if (args.help) {
        printHelp();
        process.exit(0);
    }

    // 列出類型
    if (args.listTypes) {
        listTypes();
        process.exit(0);
    }

    // 讀取定義
    let definition;
    try {
        if (args.def) {
            definition = readDefinitionFromFile(args.def);
        } else if (hasStdinPipe()) {
            definition = await readDefinitionFromStdin();
        } else {
            process.stderr.write('錯誤：請提供 --def <path> 或透過 stdin 管線輸入定義\n');
            printHelp();
            process.exit(1);
        }
    } catch (e) {
        outputError(Array.isArray(e.errors) ? e.errors : [e.message]);
        process.exit(1);
    }

    let normalized;
    try {
        normalized = await normalizeDefinitionInput(definition, args.page);
    } catch (e) {
        outputError(Array.isArray(e.errors) ? e.errors : [e.message]);
        process.exit(1);
    }

    // 僅驗證模式
    if (args.validate) {
        const validation = await validateNormalizedDefinition(normalized);
        if (validation.valid) {
            outputJSON({
                success: true,
                message: '定義驗證通過',
                source: normalized.source,
                pageId: normalized.pageId,
                templateStats: normalized.templateStats
            });
        } else {
            outputError(validation.errors);
        }
        process.exit(validation.valid ? 0 : 1);
    }

    // 檢查模式與輸出目錄
    const validModes = ['static', 'dynamic', 'both'];
    if (!validModes.includes(args.mode)) {
        outputError([`無效的模式 "${args.mode}"，允許值：${validModes.join(', ')}`]);
        process.exit(1);
    }

    if (!args.output) {
        outputError(['缺少輸出目錄，請使用 --output <dir>']);
        process.exit(1);
    }

    const outputDir = path.resolve(args.output);

    // 先驗證定義
    const validation = await validateNormalizedDefinition(normalized);
    if (!validation.valid) {
        outputError(validation.errors);
        process.exit(1);
    }

    // 生成檔案
    const files = [];

    try {
        if (args.mode === 'static' || args.mode === 'both') {
            const result = normalized.oldDefinition
                ? await generateStaticFromOldDefinition(normalized.oldDefinition, outputDir)
                : await generateStatic(normalized.definition, outputDir);
            files.push(result);
        }

        if (args.mode === 'dynamic' || args.mode === 'both') {
            const result = generateDynamic(normalized.definition, outputDir, normalized.pageId);
            files.push(result);
        }

        outputJSON({
            success: true,
            source: normalized.source,
            pageId: normalized.pageId,
            templateStats: normalized.templateStats,
            files
        });
    } catch (e) {
        outputError(Array.isArray(e.errors) ? e.errors : [e.message]);
        process.exit(1);
    }
}

main().catch(e => {
    outputError([e.message || '未預期的錯誤']);
    process.exit(1);
});
