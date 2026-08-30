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
const crypto = require('node:crypto');
const fs = require('fs');
const path = require('path');
const url = require('url');
const { spawn, execSync } = require('child_process');

// ===== 配置 =====
const PORT = parseInt(process.argv.find(a => a.startsWith('--port='))?.split('=')[1] || '3080');
const SCRIPTS_DIR = path.join(__dirname, '..');
const WEB_DIR = __dirname;
const TEMPLATE_DIR = path.join(SCRIPTS_DIR, '..');

// ===== 本機安全設定 =====
// 這是無認證的開發伺服器：預設只綁 loopback。指定非 loopback 位址等同自願開放區網，
// 屆時 Host 白名單會放寬（仍保留同源檢查），請只在可信網段使用。
const BIND_HOST = process.argv.find(a => a.startsWith('--host='))?.split('=')[1]
    || process.env.SPA_GENERATOR_HOST
    || '127.0.0.1';
const MAX_BODY_BYTES = 8 * 1024 * 1024;
const HEADERS_TIMEOUT_MS = 10 * 1000;
const REQUEST_TIMEOUT_MS = 30 * 1000;

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

// ===== 來源與路徑安全 =====

// 逐段檢查八位元，/^127\.\d{1,3}\./ 會把 127.0.0.999、127.0.0.010 這類無效位址當成本機
const LOOPBACK_IPV4 = /^127\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)$/;

function normalizeHostname(value) {
    if (typeof value !== 'string') return '';
    const trimmed = value.trim().toLowerCase();
    if (trimmed.startsWith('[')) {
        const end = trimmed.indexOf(']');
        return end === -1 ? '' : trimmed.slice(1, end);
    }
    const colon = trimmed.indexOf(':');
    return colon === -1 ? trimmed : trimmed.slice(0, colon);
}

function isLoopbackHostname(hostname) {
    if (!hostname) return false;
    if (hostname === 'localhost') return true;
    if (hostname === '::1' || hostname === '0:0:0:0:0:0:0:1') return true;
    if (hostname.startsWith('::ffff:')) return LOOPBACK_IPV4.test(hostname.slice(7));
    return LOOPBACK_IPV4.test(hostname);
}

const ALLOW_REMOTE_HOSTS = !isLoopbackHostname(normalizeHostname(BIND_HOST));

function isTrustedHost(hostHeader) {
    if (ALLOW_REMOTE_HOSTS) return true;
    return isLoopbackHostname(normalizeHostname(hostHeader));
}

function isAllowedOrigin(originHeader, hostHeader) {
    if (!originHeader) return false;
    let parsed;
    try {
        parsed = new URL(originHeader);
    } catch {
        return false;
    }
    if (isLoopbackHostname(normalizeHostname(parsed.hostname))) return true;
    return parsed.host.toLowerCase() === String(hostHeader || '').toLowerCase();
}

// 少數情境（部分 no-cors 導覽、舊瀏覽器）只給 Referer，取其 origin 當備援來源
function isAllowedReferer(refererHeader, hostHeader) {
    if (!refererHeader) return false;
    try {
        return isAllowedOrigin(new URL(refererHeader).origin, hostHeader);
    } catch {
        return false;
    }
}

const STATE_CHANGING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

function isWithinRoot(root, target) {
    const resolvedRoot = path.resolve(root);
    const resolvedTarget = path.resolve(target);
    const prefix = resolvedRoot.endsWith(path.sep) ? resolvedRoot : resolvedRoot + path.sep;
    if (process.platform === 'win32') {
        const lowerTarget = resolvedTarget.toLowerCase();
        return lowerTarget === resolvedRoot.toLowerCase() || lowerTarget.startsWith(prefix.toLowerCase());
    }
    return resolvedTarget === resolvedRoot || resolvedTarget.startsWith(prefix);
}

// ===== 工具函數 =====

const rawBodies = new WeakMap();

function readRawBody(req) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        let size = 0;
        let rejected = false;

        req.on('data', chunk => {
            if (rejected) return;
            size += chunk.length;
            if (size > MAX_BODY_BYTES) {
                rejected = true;
                const error = new Error(`Request body too large (max ${MAX_BODY_BYTES} bytes)`);
                error.statusCode = 413;
                reject(error);
                return;
            }
            chunks.push(chunk);
        });
        req.on('end', () => {
            if (!rejected) resolve(Buffer.concat(chunks).toString('utf8'));
        });
        req.on('error', err => {
            if (!rejected) reject(err);
        });
    });
}

async function parseBody(req) {
    const body = rawBodies.has(req) ? rawBodies.get(req) : await readRawBody(req);
    try {
        return body ? JSON.parse(body) : {};
    } catch (e) {
        throw new Error('Invalid JSON');
    }
}

function sendJson(res, data, status = 200) {
    if (res.headersSent) return;
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
    // 這串會成為專案的 JWT 簽章金鑰：必須用 CSPRNG，Math.random 可由少數輸出反推
    return crypto.randomBytes(length).toString('base64').slice(0, length);
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

    // Host 必須指向本機，擋掉 DNS rebinding
    if (!isTrustedHost(req.headers.host)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('Forbidden');
        return;
    }

    // CORS：只回應本機或同源的 Origin，不再開放萬用字元
    const origin = req.headers.origin;
    const originAllowed = isAllowedOrigin(origin, req.headers.host);
    res.setHeader('Vary', 'Origin');
    if (originAllowed) {
        res.setHeader('Access-Control-Allow-Origin', origin);
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
    }

    // 跨站的狀態變更請求（含 preflight）一律拒絕
    if (origin && !originAllowed && req.method !== 'GET' && req.method !== 'HEAD') {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('Forbidden');
        return;
    }

    // 兩種綁定模式刻意不對稱：loopback 預設仍放行沒有 Origin 的請求（本機 agent／curl 的既有用法），
    // 但開放遠端綁定後 Host 白名單已失效，Origin 白名單是唯一防線，狀態變更請求就必須帶可信來源
    if (ALLOW_REMOTE_HOSTS
        && STATE_CHANGING_METHODS.has(req.method)
        && !originAllowed
        && !isAllowedReferer(req.headers.referer, req.headers.host)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('Forbidden');
        return;
    }

    if (req.method === 'OPTIONS') {
        res.writeHead(204);
        res.end();
        return;
    }

    // API 路由
    const routeKey = `${req.method} ${pathname}`;
    if (apiHandlers[routeKey]) {
        try {
            if (req.method !== 'GET' && req.method !== 'HEAD') {
                rawBodies.set(req, await readRawBody(req));
            }
            await apiHandlers[routeKey](req, res);
        } catch (error) {
            sendError(res, error.message, error.statusCode || 500);
        }
        return;
    }

    // 靜態檔案
    let filePath = pathname === '/' ? '/index.html' : pathname;
    filePath = path.join(WEB_DIR, filePath);

    // 安全性檢查：只能落在 WEB_DIR 內（擋 /../ 這類點段跳脫與同前綴的兄弟目錄）
    if (!isWithinRoot(WEB_DIR, filePath)) {
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

// 限制標頭與整段請求的收取時間，避免 slowloris 把連線佔滿
server.headersTimeout = HEADERS_TIMEOUT_MS;
server.requestTimeout = REQUEST_TIMEOUT_MS;

server.listen(PORT, BIND_HOST, () => {
    console.log('');
    console.log('╔════════════════════════════════════════╗');
    console.log('║      SPA Generator Web Interface       ║');
    console.log('╚════════════════════════════════════════╝');
    console.log('');
    console.log(`伺服器運行中: http://localhost:${PORT}`);
    if (ALLOW_REMOTE_HOSTS) {
        console.log(`警告: 已綁定 ${BIND_HOST}（非 loopback），此伺服器無認證且可寫入任意路徑`);
    }
    console.log('');
    console.log('按 Ctrl+C 停止伺服器');
    console.log('');
});
