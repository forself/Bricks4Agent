// Entry point shipped with each generated presale package: node backend/standalone-server.mjs
import { createServer } from 'node:http';
import { readFileSync, existsSync, realpathSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { SqlitePrototypeBackend } from './sqlite-backend.mjs';
import { DemoSessions, routePrototypeApi } from './prototype-api.mjs';
import { BrokerPrincipalProvider, FormalSessions } from './broker-principal.mjs';
import { inspectRequest, RequestLimiter, requireNoBody, RouteError, sendJson, validateQuery } from './native-router.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.webp': 'image/webp',
    '.woff': 'font/woff', '.woff2': 'font/woff2' };

export function createStandaloneServer(root = ROOT, options = {}) {
    const mode = options.mode ?? process.env.B4A_PROTOTYPE_MODE ?? 'demo';
    if (!['demo', 'formal'].includes(mode)) throw new TypeError('Invalid prototype mode');
    const site = path.join(root, 'site');
    const config = JSON.parse(readFileSync(path.join(site, 'prototype-config.json'), 'utf8'));
    const mapping = JSON.parse(readFileSync(path.join(root, 'review', 'field-mapping.json'), 'utf8'));
    const seed = JSON.parse(readFileSync(path.join(site, 'demo-seed.json'), 'utf8'));
    const policyPath = options.policyPath ?? process.env.B4A_FORMAL_POLICY;
    const verifyUrl = options.verifyUrl ?? process.env.B4A_BROKER_VERIFY_URL;
    const policy = mode === 'formal' && policyPath ? JSON.parse(readFileSync(policyPath, 'utf8')) : null;
    const sessions = mode === 'formal'
        ? new FormalSessions(new BrokerPrincipalProvider({ verifyUrl, policy, fetcher: options.fetcher }))
        : new DemoSessions(config);
    const backend = new SqlitePrototypeBackend({ dbPath: path.join(root, 'data', mode === 'formal'
        ? 'formal.sqlite' : 'prototype.sqlite'), config, mapping, seed, mode });
    const limiter = new RequestLimiter();
    const assetLimiter = new RequestLimiter({ maxRequests: 2400 });
    const server = createServer(async (req, res) => {
        try {
            const request = inspectRequest(req, res);
            if (request.pathname.startsWith('/api/prototype')) limiter.check(req);
            else assetLimiter.check(req);
            if (await routePrototypeApi(req, res, { base: '/api/prototype', backend, sessions,
                context: request })) return;
            validateQuery(request.query, [{ name: 'v', type: 'string', maxLength: 32,
                pattern: /^[A-Za-z0-9._-]+$/ }]);
            if (req.method !== 'GET') throw new RouteError(405, 'Method not allowed');
            requireNoBody(req);
            const relative = request.pathname === '/' ? 'index.html' : request.pathname.slice(1);
            if (!relative || relative.split('/').some(segment => !segment || segment === '.' || segment === '..' || segment.includes('\\')))
                throw new RouteError(404, 'File not found');
            const file = path.resolve(site, ...relative.split('/'));
            const mime = MIME[path.extname(file).toLowerCase()];
            if (!file.startsWith(`${site}${path.sep}`) || !mime || !existsSync(file) || !statSync(file).isFile())
                throw new RouteError(404, 'File not found');
            const realSite = realpathSync(site), realFile = realpathSync(file);
            if (!realFile.startsWith(`${realSite}${path.sep}`))
                throw new RouteError(404, 'File not found');
            res.writeHead(200, { 'Content-Type': mime });
            if (mode === 'formal' && relative === 'site.json') {
                const index = JSON.parse(readFileSync(realFile, 'utf8'));
                index.prototype_backend = 'sqlite-formal';
                res.end(JSON.stringify(index));
            } else if (mode === 'formal' && relative === 'prototype-config.json') {
                res.end(JSON.stringify({ ...config, subject: null, roles: [],
                    resources: config.resources.map(({ sample_scopes: _sample, ...resource }) => resource) }));
            } else if (mode === 'formal' && relative === 'demo-seed.json') {
                res.end('{}');
            } else res.end(readFileSync(realFile));
        } catch (error) {
            const status = error instanceof RouteError ? error.status : 500;
            sendJson(res, status, { error: status === 500 ? 'Internal server error' : error.message });
        }
    });
    server.on('close', () => { limiter.clear(); assetLimiter.clear(); sessions.clear(); backend.close(); });
    return server;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const port = Number.parseInt(process.env.B4A_PROTOTYPE_PORT || '3092', 10);
    if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid port');
    createStandaloneServer().listen(port, '127.0.0.1', () =>
        process.stdout.write(`B4A SQLite prototype: http://127.0.0.1:${port}/\n`));
}
