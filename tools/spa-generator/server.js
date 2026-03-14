#!/usr/bin/env node
/**
 * SPA Generator Web Server
 *
 * 提供 SPA Generator 的 Web 介面，包含：
 * - 靜態檔案服務 (前端)
 * - API 端點 (呼叫生成器腳本)
 *
 * 用法:
 *   node server.js
 *   node server.js --port 3080
 *
 * @module server
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const url = require('url');
const { spawn, execSync } = require('child_process');
const { pathToFileURL } = require('url');
const {
    isDefinitionTemplate,
    assertValidDefinitionTemplate,
    validateDefinitionTemplate,
    resolveTemplateEnvelope,
    extractPageEntry,
    extractAppEntry
} = require('../lib/definition-template.js');
const {
    validateAppGenerationSupport,
    materializeAppProject
} = require('../lib/app-generator.js');

// ===== 配置 =====
const PORT = parseInt(process.argv.find(a => a.startsWith('--port='))?.split('=')[1] || '3080');
const FRONTEND_DIR = path.join(__dirname, 'frontend');
const LIBRARY_DIR = path.join(__dirname, '..', '..'); // Bricks4Agent 根目錄
const SCRIPTS_DIR = path.join(__dirname, '..', '..', 'templates', 'spa', 'scripts');

// 允許從 Bricks4Agent 根目錄載入的路徑前綴
const LIBRARY_PREFIXES = ['/templates/', '/packages/'];

// ===== MIME 類型 =====
const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.woff': 'font/woff',
    '.woff2': 'font/woff2'
};

// ===== 工具函數 =====

function parseBody(req) {
    return new Promise((resolve, reject) => {
        let body = '';
        req.on('data', chunk => body += chunk);
        req.on('end', () => {
            try {
                resolve(body ? JSON.parse(body) : {});
            } catch (e) {
                reject(new Error('Invalid JSON'));
            }
        });
        req.on('error', reject);
    });
}

function sendJson(res, data, status = 200) {
    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify(data, null, 2));
}

function sendError(res, message, status = 400) {
    sendJson(res, { success: false, error: message }, status);
}

function sendValidationErrors(res, errors, status = 400) {
    sendJson(res, { success: false, errors }, status);
}

function runScript(scriptName, args = []) {
    return new Promise((resolve, reject) => {
        const scriptPath = path.join(SCRIPTS_DIR, scriptName);

        if (!fs.existsSync(scriptPath)) {
            reject(new Error(`Script not found: ${scriptPath}`));
            return;
        }

        let stdout = '';
        let stderr = '';

        const proc = spawn('node', [scriptPath, ...args], {
            cwd: SCRIPTS_DIR,
            env: { ...process.env, FORCE_COLOR: '0' }
        });

        proc.stdout.on('data', data => stdout += data);
        proc.stderr.on('data', data => stderr += data);

        proc.on('close', code => {
            if (code === 0) {
                resolve({ stdout, stderr });
            } else {
                reject(new Error(stderr || stdout || `Exit code ${code}`));
            }
        });

        proc.on('error', reject);
    });
}

function generateRandomString(length = 32) {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*';
    let result = '';
    for (let i = 0; i < length; i++) {
        result += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return result;
}

function toPascalCase(str) {
    return str
        .split(/[-_\/\s]/)
        .map(part => part.charAt(0).toUpperCase() + part.slice(1))
        .join('');
}

function toKebabCase(str) {
    return str
        .replace(/([a-z])([A-Z])/g, '$1-$2')
        .replace(/[\s_]+/g, '-')
        .toLowerCase();
}

function pluralize(word) {
    if (word.endsWith('y')) return word.slice(0, -1) + 'ies';
    if (word.endsWith('s') || word.endsWith('x') || word.endsWith('ch') || word.endsWith('sh')) return word + 'es';
    return word + 's';
}

function resolveTemplateDocument(payload) {
    if (isDefinitionTemplate(payload)) {
        return payload;
    }

    if (!payload || typeof payload !== 'object') {
        return null;
    }

    const envelope = resolveTemplateEnvelope(payload);
    if (envelope?.template) {
        return envelope.template;
    }

    const candidates = ['definitionTemplate', 'template', 'definition'];
    for (const key of candidates) {
        if (isDefinitionTemplate(payload[key])) {
            return payload[key];
        }
    }

    return null;
}

let pageDefinitionAdapterPromise = null;

async function loadPageDefinitionAdapter() {
    if (!pageDefinitionAdapterPromise) {
        const adapterPath = pathToFileURL(
            path.resolve(__dirname, '../../packages/javascript/browser/page-generator/PageDefinitionAdapter.js')
        ).href;
        pageDefinitionAdapterPromise = import(adapterPath).then(module => module.PageDefinitionAdapter || module.default);
    }

    return pageDefinitionAdapterPromise;
}

async function normalizeTemplateToOldPageDefinition(payload, pageIdOverride = null) {
    const envelope = resolveTemplateEnvelope(payload, pageIdOverride);
    if (!envelope) {
        return null;
    }

    const validation = await assertValidDefinitionTemplate(envelope.template);
    const extracted = extractPageEntry(envelope.template, envelope.pageId);

    return {
        ...extracted,
        templateStats: validation.stats
    };
}

async function normalizeTemplateToNewPageDefinition(payload, pageIdOverride = null) {
    const extracted = await normalizeTemplateToOldPageDefinition(payload, pageIdOverride);
    if (!extracted) {
        return null;
    }

    const PageDefinitionAdapter = await loadPageDefinitionAdapter();
    const newDefinition = PageDefinitionAdapter.toNewFormat(extracted.pageDefinition);

    if (!newDefinition) {
        throw new Error(`無法將 page ${extracted.pageId} 轉為 page-builder 格式`);
    }

    return {
        pageId: extracted.pageId,
        oldDefinition: extracted.pageDefinition,
        definition: newDefinition,
        templateStats: extracted.templateStats
    };
}

async function normalizeTemplateToAppDefinition(payload, appIdOverride = null) {
    const template = resolveTemplateDocument(payload);
    if (!template) {
        return null;
    }

    const validation = await assertValidDefinitionTemplate(template);
    const appId = appIdOverride || payload?.appId || null;
    const appEntry = extractAppEntry(template, appId);
    const support = validateAppGenerationSupport(appEntry, template);

    return {
        template,
        appEntry,
        templateStats: validation.stats,
        support
    };
}

// ===== 頁面建構器輔助函數 =====

const VALID_FIELD_TYPES = ['text', 'email', 'password', 'number', 'textarea', 'date', 'time', 'datetime', 'select', 'multiselect', 'checkbox', 'toggle', 'radio', 'richtext', 'canvas', 'color', 'image', 'file', 'geolocation', 'weather', 'address', 'addresslist', 'chained', 'list', 'personinfo', 'phonelist', 'socialmedia', 'organization', 'student', 'hidden'];
const VALID_TRIGGER_ONS = ['change', 'check', 'uncheck', 'upload'];
const VALID_TRIGGER_ACTIONS = ['reloadOptions', 'show', 'hide', 'setReadonly', 'setRequired', 'reload', 'setValue', 'clear'];

/**
 * 驗證新格式頁面定義
 * @param {Object} def - 頁面定義
 * @returns {string[]} 錯誤陣列
 */
function validatePageDefinition(def) {
    const errors = [];
    if (!def || typeof def !== 'object') { errors.push('定義必須是 JSON 物件'); return errors; }
    if (!def.page?.pageName) errors.push('缺少 page.pageName');
    if (!def.page?.entity) errors.push('缺少 page.entity');
    if (!def.page?.view) errors.push('缺少 page.view');
    if (!Array.isArray(def.fields) || def.fields.length === 0) {
        errors.push('fields 必須是非空陣列');
        return errors;
    }
    const names = new Set();
    for (const f of def.fields) {
        if (!f.fieldName) { errors.push('欄位缺少 fieldName'); continue; }
        if (names.has(f.fieldName)) errors.push(`重複的 fieldName: ${f.fieldName}`);
        names.add(f.fieldName);
        if (!f.fieldType) errors.push(`欄位 ${f.fieldName} 缺少 fieldType`);
        else if (!VALID_FIELD_TYPES.includes(f.fieldType)) errors.push(`欄位 ${f.fieldName} 的 fieldType 無效: ${f.fieldType}`);
        if (f.triggers && Array.isArray(f.triggers)) {
            for (const t of f.triggers) {
                if (t.on && !VALID_TRIGGER_ONS.includes(t.on)) errors.push(`觸發器 on 值無效: ${t.on}`);
                if (t.action && !VALID_TRIGGER_ACTIONS.includes(t.action)) errors.push(`觸發器 action 值無效: ${t.action}`);
            }
        }
    }
    return errors;
}

/**
 * 新格式 → 舊格式轉換（伺服器端簡易版）
 */
function convertToOldFormatForServer(newDef) {
    const entity = newDef.page.entity;
    const name = entity.charAt(0).toUpperCase() + entity.slice(1) + 'Page';
    const view = (newDef.page.view || '').toLowerCase();
    const type = view.includes('list') ? 'list' : view.includes('detail') ? 'detail' : 'form';

    const fields = (newDef.fields || []).map(f => ({
        name: f.fieldName,
        type: f.fieldType === 'multiselect' ? 'select' : f.fieldType,
        label: f.label,
        required: f.isRequired || false,
        default: f.defaultValue != null ? f.defaultValue : undefined,
        options: f.optionsSource?.type === 'static' ? f.optionsSource.items : undefined,
        validation: f.validation || undefined,
        dependsOn: f.dependsOn || undefined,
        component: f.component || undefined
    }));

    return { name, type, description: newDef.page.pageName, fields, api: {}, behaviors: {}, styles: { layout: 'single' } };
}

/**
 * 生成靜態頁面程式碼（伺服器端簡易版）
 */
function generateStaticPageCode(def) {
    const formFields = def.fields.filter(f => f.type !== 'hidden');
    const fieldHtml = formFields.map(f => {
        const req = f.required ? ' *' : '';
        const label = f.label || f.name;
        switch (f.type) {
            case 'textarea': return `                    <div class="form-group full-width">\n                        <label for="${f.name}">${label}${req}</label>\n                        <textarea id="${f.name}" name="${f.name}" rows="5" ${f.required ? 'required' : ''}>\${this.esc(this._data.form.${f.name})}</textarea>\n                    </div>`;
            case 'select': return `                    <div class="form-group">\n                        <label for="${f.name}">${label}${req}</label>\n                        <select id="${f.name}" name="${f.name}" ${f.required ? 'required' : ''}>\n                            <option value="">請選擇</option>\n${(f.options || []).map(o => `                            <option value="${o.value}">${o.label}</option>`).join('\n')}\n                        </select>\n                    </div>`;
            case 'checkbox':
            case 'toggle': return `                    <div class="form-group">\n                        <label class="checkbox-label"><input type="checkbox" id="${f.name}" name="${f.name}" \${this._data.form.${f.name} ? 'checked' : ''}> ${label}</label>\n                    </div>`;
            case 'date': return `                    <div class="form-group">\n                        <label for="${f.name}">${label}${req}</label>\n                        <div id="${f.name}-picker"></div>\n                    </div>`;
            default: return `                    <div class="form-group">\n                        <label for="${f.name}">${label}${req}</label>\n                        <input type="${f.type === 'number' ? 'number' : f.type === 'email' ? 'email' : 'text'}" id="${f.name}" name="${f.name}" value="\${this.escAttr(this._data.form.${f.name})}" ${f.required ? 'required' : ''}>\n                    </div>`;
        }
    }).join('\n\n');

    const formDefaults = {};
    for (const f of def.fields) {
        if (f.default !== undefined) formDefaults[f.name] = f.default;
        else if (f.type === 'checkbox' || f.type === 'toggle') formDefaults[f.name] = false;
        else if (f.type === 'number') formDefaults[f.name] = 0;
        else formDefaults[f.name] = '';
    }

    return `/**
 * ${def.name} - ${def.description || '自動生成的頁面'}
 *
 * 頁面類型: ${def.type}
 * 生成時間: ${new Date().toISOString()}
 */

import { BasePage } from '../core/BasePage.js';

export class ${def.name} extends BasePage {
    async onInit() {
        this._data = {
            form: ${JSON.stringify(formDefaults, null, 12).replace(/\n/g, '\n        ')},
            loading: false,
            submitting: false
        };
    }

    template() {
        return \`
            <div class="${toKebabCase(def.name)}">
                <header class="page-header">
                    <h1>${def.description || def.name.replace(/Page$/, '')}</h1>
                </header>

                <form id="main-form" class="form-container">
${fieldHtml}

                    <div class="form-actions">
                        <button type="submit" class="btn btn-primary" \${this._data.submitting ? 'disabled' : ''}>
                            \${this._data.submitting ? '處理中...' : '儲存'}
                        </button>
                        <button type="button" class="btn btn-secondary" data-action="cancel">取消</button>
                    </div>
                </form>
            </div>
        \`;
    }

    events() {
        return [
            { el: '#main-form', on: 'submit', do: '_onSubmit' }
        ];
    }

    async _onSubmit(e) {
        e.preventDefault();
        this._data.submitting = true;
        this.render();
        // TODO: API 呼叫
        this._data.submitting = false;
        this.render();
    }
}

export default ${def.name};
`;
}

// ===== API 處理器 =====

const apiHandlers = {
    // 取得系統資訊
    'GET /api/info': async (req, res) => {
        const info = {
            scriptsDir: SCRIPTS_DIR,
            nodeVersion: process.version,
            platform: process.platform
        };

        // 檢查 dotnet
        try {
            const dotnetVersion = execSync('dotnet --version', { encoding: 'utf8' }).trim();
            info.dotnetVersion = dotnetVersion;
        } catch {
            info.dotnetVersion = null;
        }

        sendJson(res, { success: true, data: info });
    },

    // 建立專案
    'POST /api/generator/project': async (req, res) => {
        try {
            const config = await parseBody(req);

            // 驗證必要欄位
            if (!config.project?.name) {
                return sendError(res, '專案名稱為必填');
            }

            if (!config.project?.outputDir) {
                return sendError(res, '輸出目錄為必填');
            }

            // 設定預設值
            config.project.displayName = config.project.displayName || config.project.name;
            config.project.description = config.project.description || '基於 SPA 範本建立的應用程式';

            config.backend = config.backend || {};
            config.backend.dbName = config.backend.dbName || `${config.project.name}.db`;
            config.backend.apiPort = config.backend.apiPort || '5001';

            config.frontend = config.frontend || {};
            config.frontend.devPort = config.frontend.devPort || '3000';
            config.frontend.apiBaseUrl = config.frontend.apiBaseUrl || `https://localhost:${config.backend.apiPort}/api`;

            config.security = config.security || {};
            config.security.jwtKey = config.security.jwtKey || generateRandomString(64);
            config.security.jwtIssuer = config.security.jwtIssuer || config.project.name;
            if (Array.isArray(config.security.corsOrigins)) {
                // 已經是陣列
            } else if (typeof config.security.corsOrigins === 'string') {
                config.security.corsOrigins = config.security.corsOrigins.split(',').map(s => s.trim()).filter(s => s);
            } else {
                config.security.corsOrigins = [`http://localhost:${config.frontend.devPort}`];
            }

            config.admin = config.admin || {};
            config.admin.email = config.admin.email || 'admin@example.com';
            config.admin.password = config.admin.password || 'Admin@123';
            config.admin.name = config.admin.name || 'Admin';

            // 寫入臨時配置檔
            const configPath = path.join(SCRIPTS_DIR, `_temp_${Date.now()}.json`);
            fs.writeFileSync(configPath, JSON.stringify(config, null, 2));

            try {
                // 執行建立腳本
                const result = await runScript('create-project.js', ['--config', configPath]);

                // 刪除臨時檔
                fs.unlinkSync(configPath);

                const projectPath = path.join(config.project.outputDir, config.project.name);

                sendJson(res, {
                    success: true,
                    message: '專案建立成功',
                    data: {
                        projectPath,
                        config: {
                            ...config,
                            security: { jwtIssuer: config.security.jwtIssuer },
                            admin: { email: config.admin.email, name: config.admin.name }
                        }
                    },
                    output: result.stdout
                });
            } catch (error) {
                // 刪除臨時檔
                if (fs.existsSync(configPath)) fs.unlinkSync(configPath);
                throw error;
            }
        } catch (error) {
            sendError(res, error.message, 500);
        }
    },

    // 生成頁面 (預覽)
    'POST /api/page/preview': async (req, res) => {
        try {
            const { pageName, isDetail } = await parseBody(req);

            if (!pageName) {
                return sendError(res, '頁面名稱為必填');
            }

            const parts = pageName.split('/');
            const fileName = parts[parts.length - 1];
            const className = toPascalCase(fileName) + 'Page';
            const subDir = parts.length > 1 ? parts.slice(0, -1).join('/') : '';
            const routePath = subDir ? `${subDir}/${toKebabCase(fileName)}` : toKebabCase(fileName);

            sendJson(res, {
                success: true,
                data: {
                    className,
                    fileName: `${className}.js`,
                    directory: subDir || 'pages/',
                    routePath: `/${routePath}`,
                    importPath: subDir ? `./${subDir}/${className}.js` : `./${className}.js`,
                    isDetail: isDetail || fileName.toLowerCase().includes('detail')
                }
            });
        } catch (error) {
            sendError(res, error.message);
        }
    },

    // 生成頁面
    'POST /api/page/generate': async (req, res) => {
        try {
            const { pageName, isDetail } = await parseBody(req);

            if (!pageName) {
                return sendError(res, '頁面名稱為必填');
            }

            const args = [pageName];
            if (isDetail) args.push('--detail');

            const result = await runScript('generate-page.js', args);

            sendJson(res, {
                success: true,
                message: '頁面生成成功',
                output: result.stdout
            });
        } catch (error) {
            sendError(res, error.message, 500);
        }
    },

    // 生成 API (預覽)
    'POST /api/endpoint/preview': async (req, res) => {
        try {
            const { entityName, fields } = await parseBody(req);

            if (!entityName) {
                return sendError(res, '實體名稱為必填');
            }

            const className = toPascalCase(entityName);
            const pluralName = pluralize(className);
            const routePath = toKebabCase(pluralName);

            // 解析欄位
            const parsedFields = fields ? fields.split(',').map(f => {
                const [name, type] = f.split(':');
                return { name: name.trim(), type: type?.trim() || 'string' };
            }) : [{ name: 'Name', type: 'string' }];

            sendJson(res, {
                success: true,
                data: {
                    className,
                    pluralName,
                    routePath: `/api/${routePath}`,
                    modelFile: `${className}.cs`,
                    serviceFile: `${className}Service.cs`,
                    fields: parsedFields,
                    endpoints: [
                        { method: 'GET', path: `/api/${routePath}`, description: '取得所有' },
                        { method: 'GET', path: `/api/${routePath}/{id}`, description: '取得單一' },
                        { method: 'POST', path: `/api/${routePath}`, description: '新增' },
                        { method: 'PUT', path: `/api/${routePath}/{id}`, description: '更新' },
                        { method: 'DELETE', path: `/api/${routePath}/{id}`, description: '刪除' }
                    ]
                }
            });
        } catch (error) {
            sendError(res, error.message);
        }
    },

    // 生成 API
    'POST /api/endpoint/generate': async (req, res) => {
        try {
            const { entityName, fields } = await parseBody(req);

            if (!entityName) {
                return sendError(res, '實體名稱為必填');
            }

            const args = [entityName];
            if (fields) args.push('--fields', fields);

            const result = await runScript('generate-api.js', args);

            sendJson(res, {
                success: true,
                message: 'API 生成成功',
                output: result.stdout
            });
        } catch (error) {
            sendError(res, error.message, 500);
        }
    },

    // ===== PageDefinitionEditor API =====

    'POST /api/definition-template/validate': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const envelope = resolveTemplateEnvelope(payload, payload.pageId);
            const template = envelope?.template || payload;
            const validation = await validateDefinitionTemplate(template);

            sendJson(res, {
                success: validation.valid,
                errors: validation.errors,
                data: validation.valid ? {
                    message: 'DefinitionTemplate 驗證通過',
                    stats: validation.stats
                } : null
            });
        } catch (error) {
            sendError(res, error.message);
        }
    },

    // 從 PageDefinition 生成頁面程式碼
    'POST /api/definition-template/app/validate': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const normalized = await normalizeTemplateToAppDefinition(payload, payload.appId);
            if (!normalized) {
                return sendError(res, 'Input must be a DefinitionTemplate document');
            }

            if (!normalized.support.valid) {
                return sendJson(res, {
                    success: false,
                    errors: normalized.support.errors,
                    data: null
                });
            }

            sendJson(res, {
                success: true,
                errors: [],
                data: {
                    message: 'DefinitionTemplate app is valid for minimal backend generation',
                    appId: normalized.appEntry.id,
                    stats: normalized.templateStats
                }
            });
        } catch (error) {
            if (Array.isArray(error.errors)) {
                return sendValidationErrors(res, error.errors);
            }
            sendError(res, error.message);
        }
    },

    'POST /api/generator/page-definition': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const { definition } = payload;
            if (!definition || typeof definition !== 'object') {
                return sendError(res, '缺少 definition 物件');
            }

            const normalized = await normalizeTemplateToOldPageDefinition(definition, payload.pageId);
            if (normalized) {
                const oldDef = normalized.pageDefinition;
                const code = generateStaticPageCode(oldDef);

                return sendJson(res, {
                    success: true,
                    data: {
                        code,
                        className: oldDef.name,
                        fileName: `${oldDef.name}.js`,
                        pageId: normalized.pageId,
                        source: 'definition-template',
                        templateStats: normalized.templateStats
                    }
                });
            }

            // 將 PageDefinition 格式轉為 server 端的舊格式並生成
            const name = definition.name || 'GeneratedPage';
            const type = (definition.type || 'form').toLowerCase();
            const fields = (definition.fields || []).map(f => ({
                name: f.name,
                type: f.type || 'text',
                label: f.label || f.name,
                required: f.required || false,
                default: f.default,
                options: f.options,
                validation: f.validation
            }));

            const oldDef = { name, type, description: definition.description || name, fields, api: {}, behaviors: {}, styles: { layout: 'single' } };
            const code = generateStaticPageCode(oldDef);

            sendJson(res, {
                success: true,
                data: {
                    code,
                    className: name,
                    fileName: `${name}.js`
                }
            });
        } catch (error) {
            if (Array.isArray(error.errors)) {
                return sendValidationErrors(res, error.errors);
            }
            sendError(res, error.message, 500);
        }
    },

    // ===== 頁面建構器 API =====

    // 驗證頁面定義
    'POST /api/generator/app-definition': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const normalized = await normalizeTemplateToAppDefinition(payload, payload.appId);
            if (!normalized) {
                return sendError(res, 'Input must be a DefinitionTemplate document');
            }

            if (!normalized.support.valid) {
                return sendValidationErrors(res, normalized.support.errors);
            }

            if (!payload.outputDir || typeof payload.outputDir !== 'string') {
                return sendError(res, 'Missing outputDir');
            }

            const result = materializeAppProject(
                normalized.template,
                normalized.appEntry.id,
                payload.outputDir
            );

            sendJson(res, {
                success: true,
                data: {
                    appId: result.appId,
                    templateStats: normalized.templateStats,
                    projectRoot: result.projectRoot,
                    backendDir: result.backendDir,
                    csprojPath: result.csprojPath,
                    generatedFilePath: result.generatedFilePath,
                    frontendDir: result.frontendDir,
                    routesFilePath: result.routesFilePath,
                    generatedPagePaths: result.generatedPagePaths,
                    projectJsonPath: result.projectJsonPath,
                    readmePath: result.readmePath
                }
            });
        } catch (error) {
            if (Array.isArray(error.errors)) {
                return sendValidationErrors(res, error.errors);
            }
            sendError(res, error.message, 500);
        }
    },

    'POST /api/page-builder/validate': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const normalized = await normalizeTemplateToNewPageDefinition(payload, payload.pageId);
            const definition = normalized?.definition || payload;
            const errors = validatePageDefinition(definition);

            sendJson(res, {
                success: errors.length === 0,
                errors,
                data: errors.length === 0 ? {
                    message: '定義驗證通過',
                    pageId: normalized?.pageId || null,
                    source: normalized ? 'definition-template' : 'legacy-page-definition',
                    templateStats: normalized?.templateStats || null
                } : null
            });
        } catch (error) {
            if (Array.isArray(error.errors)) {
                return sendJson(res, { success: false, errors: error.errors, data: null });
            }
            sendError(res, error.message);
        }
    },

    // 生成靜態 .js 頁面
    'POST /api/page-builder/generate': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const normalized = await normalizeTemplateToNewPageDefinition(payload, payload.pageId);
            const definition = normalized?.definition || payload;
            const errors = validatePageDefinition(definition);

            if (errors.length > 0) {
                return sendJson(res, { success: false, errors });
            }

            // 將新格式轉為舊格式
            const oldDef = convertToOldFormatForServer(definition);

            // 使用簡化的靜態程式碼生成
            const code = generateStaticPageCode(oldDef);

            sendJson(res, {
                success: true,
                data: {
                    code,
                    className: oldDef.name,
                    fileName: `${oldDef.name}.js`,
                    pageId: normalized?.pageId || null,
                    source: normalized ? 'definition-template' : 'legacy-page-definition',
                    templateStats: normalized?.templateStats || null
                }
            });
        } catch (error) {
            if (Array.isArray(error.errors)) {
                return sendValidationErrors(res, error.errors);
            }
            sendError(res, error.message, 500);
        }
    },

    // 匯出定義 JSON
    'POST /api/page-builder/export': async (req, res) => {
        try {
            const payload = await parseBody(req);
            const normalized = await normalizeTemplateToNewPageDefinition(payload, payload.pageId);
            const definition = normalized?.definition || payload;
            const errors = validatePageDefinition(definition);

            if (errors.length > 0) {
                return sendJson(res, { success: false, errors });
            }

            sendJson(res, {
                success: true,
                data: {
                    definition,
                    pageId: normalized?.pageId || null,
                    source: normalized ? 'definition-template' : 'legacy-page-definition',
                    templateStats: normalized?.templateStats || null,
                    exportedAt: new Date().toISOString()
                }
            });
        } catch (error) {
            if (Array.isArray(error.errors)) {
                return sendValidationErrors(res, error.errors);
            }
            sendError(res, error.message);
        }
    },

    // 生成完整功能
    'POST /api/feature/generate': async (req, res) => {
        try {
            const { featureName, fields } = await parseBody(req);

            if (!featureName) {
                return sendError(res, '功能名稱為必填');
            }

            const results = [];

            // 生成 API
            const apiArgs = [featureName];
            if (fields) apiArgs.push('--fields', fields);
            const apiResult = await runScript('generate-api.js', apiArgs);
            results.push({ type: 'api', output: apiResult.stdout });

            // 生成頁面
            const folderName = featureName.toLowerCase() + 's';

            const listResult = await runScript('generate-page.js', [`${folderName}/${featureName}List`]);
            results.push({ type: 'listPage', output: listResult.stdout });

            const detailResult = await runScript('generate-page.js', [`${folderName}/${featureName}Detail`, '--detail']);
            results.push({ type: 'detailPage', output: detailResult.stdout });

            sendJson(res, {
                success: true,
                message: '功能生成成功',
                results
            });
        } catch (error) {
            sendError(res, error.message, 500);
        }
    }
};

// ===== HTTP 伺服器 =====

const server = http.createServer(async (req, res) => {
    const parsedUrl = url.parse(req.url, true);
    const pathname = parsedUrl.pathname;

    // CORS
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, PUT, DELETE, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

    if (req.method === 'OPTIONS') {
        res.writeHead(204);
        res.end();
        return;
    }

    // API 路由
    const routeKey = `${req.method} ${pathname}`;
    if (apiHandlers[routeKey]) {
        try {
            await apiHandlers[routeKey](req, res);
        } catch (error) {
            console.error(`[API Error] ${routeKey}:`, error);
            sendError(res, error.message, 500);
        }
        return;
    }

    // 靜態檔案
    let filePath = pathname === '/' ? '/index.html' : pathname;

    // 判斷是否為 Bricks4Agent 根目錄下的路徑（templates/, packages/）
    const isLibraryPath = LIBRARY_PREFIXES.some(prefix => pathname.startsWith(prefix));

    if (isLibraryPath) {
        filePath = path.join(LIBRARY_DIR, pathname);
    } else {
        filePath = path.join(FRONTEND_DIR, filePath);
    }

    // 安全性檢查
    const normalizedPath = path.normalize(filePath);
    const allowedRoot = isLibraryPath ? LIBRARY_DIR : FRONTEND_DIR;
    if (!normalizedPath.startsWith(allowedRoot)) {
        res.writeHead(403);
        res.end('Forbidden');
        return;
    }

    // 讀取檔案
    fs.readFile(filePath, (err, content) => {
        if (err) {
            if (err.code === 'ENOENT') {
                // SPA fallback - 回傳 index.html（僅限非 library 路徑）
                if (!isLibraryPath) {
                    fs.readFile(path.join(FRONTEND_DIR, 'index.html'), (err2, indexContent) => {
                        if (err2) {
                            res.writeHead(404);
                            res.end('Not Found');
                        } else {
                            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
                            res.end(indexContent);
                        }
                    });
                } else {
                    res.writeHead(404);
                    res.end('Not Found');
                }
            } else {
                res.writeHead(500);
                res.end('Internal Server Error');
            }
            return;
        }

        const ext = path.extname(filePath);
        const contentType = MIME_TYPES[ext] || 'application/octet-stream';

        res.writeHead(200, { 'Content-Type': contentType });
        res.end(content);
    });
});

// ===== 啟動伺服器 =====

server.listen(PORT, () => {
    console.log('');
    console.log('\x1b[36m' + '╔════════════════════════════════════════╗' + '\x1b[0m');
    console.log('\x1b[36m' + '║      SPA Generator Web Interface       ║' + '\x1b[0m');
    console.log('\x1b[36m' + '╚════════════════════════════════════════╝' + '\x1b[0m');
    console.log('');
    console.log(`\x1b[32m伺服器運行中:\x1b[0m http://localhost:${PORT}`);
    console.log('');
    console.log(`\x1b[33m腳本目錄:\x1b[0m ${SCRIPTS_DIR}`);
    console.log(`\x1b[33m前端目錄:\x1b[0m ${FRONTEND_DIR}`);
    console.log('');
    console.log('按 Ctrl+C 停止伺服器');
    console.log('');
});
