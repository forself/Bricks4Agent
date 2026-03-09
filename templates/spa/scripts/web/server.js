#!/usr/bin/env node
/**
 * SPA Generator Web Server
 *
 * 用法:
 *   node server.js
 *   node server.js --port 8080
 *
 * @module server
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const url = require('url');
const { spawn, execSync } = require('child_process');

// ===== 配置 =====
const PORT = parseInt(process.argv.find(a => a.startsWith('--port='))?.split('=')[1] || '3080');
const SCRIPTS_DIR = path.join(__dirname, '..');
const WEB_DIR = __dirname;
const TEMPLATE_DIR = path.join(SCRIPTS_DIR, '..');

// ===== MIME 類型 =====
const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon'
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

function runScript(scriptName, args = []) {
    return new Promise((resolve, reject) => {
        const scriptPath = path.join(SCRIPTS_DIR, scriptName);
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

// ===== API 處理器 =====

const apiHandlers = {
    // 取得系統資訊
    'GET /api/info': async (req, res) => {
        const info = {
            templateDir: TEMPLATE_DIR,
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
    'POST /api/project/create': async (req, res) => {
        try {
            const config = await parseBody(req);

            // 驗證必要欄位
            if (!config.project?.name) {
                return sendError(res, '專案名稱為必填');
            }

            // 設定預設值
            config.project.displayName = config.project.displayName || config.project.name;
            config.project.description = config.project.description || '基於 SPA 範本建立的應用程式';
            config.project.outputDir = config.project.outputDir || path.join(TEMPLATE_DIR, '..', '..', 'projects');

            config.backend = config.backend || {};
            config.backend.dbName = config.backend.dbName || `${config.project.name}.db`;
            config.backend.apiPort = config.backend.apiPort || '5001';

            config.frontend = config.frontend || {};
            config.frontend.devPort = config.frontend.devPort || '3000';
            config.frontend.apiBaseUrl = config.frontend.apiBaseUrl || `https://localhost:${config.backend.apiPort}/api`;

            config.security = config.security || {};
            config.security.jwtKey = config.security.jwtKey || generateRandomString(64);
            config.security.jwtIssuer = config.security.jwtIssuer || config.project.name;
            config.security.corsOrigins = config.security.corsOrigins || [`http://localhost:${config.frontend.devPort}`];

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
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type');

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
            sendError(res, error.message, 500);
        }
        return;
    }

    // 靜態檔案
    let filePath = pathname === '/' ? '/index.html' : pathname;
    filePath = path.join(WEB_DIR, filePath);

    // 安全性檢查
    if (!filePath.startsWith(WEB_DIR)) {
        res.writeHead(403);
        res.end('Forbidden');
        return;
    }

    // 讀取檔案
    fs.readFile(filePath, (err, content) => {
        if (err) {
            if (err.code === 'ENOENT') {
                res.writeHead(404);
                res.end('Not Found');
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
    console.log('╔════════════════════════════════════════╗');
    console.log('║      SPA Generator Web Interface       ║');
    console.log('╚════════════════════════════════════════╝');
    console.log('');
    console.log(`伺服器運行中: http://localhost:${PORT}`);
    console.log('');
    console.log('按 Ctrl+C 停止伺服器');
    console.log('');
});
