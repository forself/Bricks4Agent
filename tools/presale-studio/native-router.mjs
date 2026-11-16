// Dependency-free HTTP request gate shared by the workbench and exported prototype.
// Route declarations remain explicit in prototype-router.mjs and server.mjs.
const LOOPBACK_HOST = /^(?:localhost|127\.0\.0\.1):([1-9]\d{0,4})$/;
const CONTROL = /[\u0000-\u001f\u007f]/;
const MAX_URL_BYTES = 4096;
const MAX_QUERY_PAIRS = 24;
const MAX_QUERY_VALUE = 256;
const DEFAULT_BODY_BYTES = 256 * 1024;
const DEFAULT_JSON_NODES = 10_000;
const MAX_JSON_NODES = 2_000_000;
const STRUCTURE_LIMIT = 'JSON structure limit exceeded';

export class RouteError extends Error {
    constructor(status, message) { super(message); this.status = status; }
}

export class NativeRouter {
    constructor() { this.routes = []; }
    register({ path, methods, kind, body = 'none', query = [], session = false }) {
        if (!(typeof path === 'string' && path.startsWith('/') && !path.includes('//')) &&
            !(path instanceof RegExp && path.source.startsWith('^') && path.source.endsWith('$') && !path.flags))
            throw new TypeError('Route path must be exact or an anchored regular expression');
        if (!Array.isArray(methods) || !methods.length || methods.some(method =>
            !['GET', 'POST', 'PUT', 'DELETE'].includes(method)) ||
            typeof kind !== 'string' || !['none', 'json'].includes(body) && typeof body !== 'function' &&
            !(body && typeof body === 'object' && body.type === 'json' &&
                Number.isSafeInteger(body.maxBytes) && body.maxBytes > 0 && body.maxBytes <= 8 * 1024 * 1024 &&
                (body.maxNodes === undefined || Number.isSafeInteger(body.maxNodes) &&
                    body.maxNodes > 0 && body.maxNodes <= MAX_JSON_NODES) &&
                ['maxBytes|type', 'maxBytes|maxNodes|type'].includes(Object.keys(body).sort().join('|'))))
            throw new TypeError('Invalid route declaration');
        this.routes.push({ path, methods: [...new Set(methods)], kind, body, query, session });
        return this;
    }
    resolve(method, pathname) {
        const matching = this.routes.flatMap(route => {
            const match = typeof route.path === 'string' ? route.path === pathname ? [pathname] : null
                : route.path.exec(pathname);
            return match ? [{ route, match }] : [];
        });
        if (!matching.length) return { kind: 'not-found' };
        const selected = matching.find(({ route }) => route.methods.includes(method));
        if (!selected) return { kind: 'method-not-allowed',
            allow: [...new Set(matching.flatMap(({ route }) => route.methods))].join(', ') };
        const { route, match } = selected;
        return { kind: route.kind, path: pathname, params: match.groups || {},
            body: typeof route.body === 'function' ? route.body({ method, match }) : route.body,
            query: typeof route.query === 'function' ? route.query({ method, match }) : route.query,
            session: route.session };
    }
}

export function applySecurityHeaders(res) {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Referrer-Policy', 'no-referrer');
    res.setHeader('X-Frame-Options', 'SAMEORIGIN');
    res.setHeader('Cross-Origin-Resource-Policy', 'same-origin');
    res.setHeader('Permissions-Policy', 'camera=(), microphone=(), geolocation=()');
    res.setHeader('Cache-Control', 'no-store');
}

function oneHeader(req, name) {
    const headers = req.rawHeaders || [];
    let count = 0;
    for (let index = 0; index < headers.length; index += 2)
        if (headers[index].toLowerCase() === name) count++;
    if (count > 1) throw new RouteError(400, `Repeated ${name} header`);
}

export function inspectRequest(req, res) {
    applySecurityHeaders(res);
    for (const name of ['host', 'origin', 'authorization', 'cookie', 'content-type', 'content-length', 'transfer-encoding'])
        oneHeader(req, name);
    if (req.headers['content-length'] && req.headers['transfer-encoding'])
        throw new RouteError(400, 'Conflicting request framing');
    const host = req.headers.host || '';
    const hostMatch = LOOPBACK_HOST.exec(host);
    if (!hostMatch || Number(hostMatch[1]) > 65535) throw new RouteError(403, 'Loopback host required');
    const raw = req.url;
    if (typeof raw !== 'string' || !raw.startsWith('/') || raw.startsWith('//') ||
        Buffer.byteLength(raw) > MAX_URL_BYTES) throw new RouteError(414, 'Invalid request target');
    if (CONTROL.test(raw) || raw.includes('\\') || raw.includes('#'))
        throw new RouteError(400, 'Invalid request target');
    const question = raw.indexOf('?');
    const rawPath = question < 0 ? raw : raw.slice(0, question);
    const rawQuery = question < 0 ? '' : raw.slice(question + 1);
    const segments = rawPath.split('/');
    if (segments.slice(1, -1).some(segment => segment === ''))
        throw new RouteError(400, 'Empty path segment');
    const decoded = segments.map(segment => {
        let value;
        try { value = decodeURIComponent(segment); }
        catch { throw new RouteError(400, 'Malformed path encoding'); }
        if (value === '.' || value === '..' || value.includes('/') || value.includes('\\') ||
            CONTROL.test(value)) throw new RouteError(400, 'Unsafe path segment');
        return value;
    });
    const pathname = decoded.join('/');
    const query = new URLSearchParams(rawQuery);
    if ([...query].length > MAX_QUERY_PAIRS) throw new RouteError(400, 'Too many query values');
    const seen = new Set();
    for (const [key, value] of query) {
        if (seen.has(key) || !key || key.length > 64 || value.length > MAX_QUERY_VALUE ||
            CONTROL.test(key) || CONTROL.test(value)) throw new RouteError(400, 'Invalid query value');
        seen.add(key);
    }
    // URLSearchParams replaces malformed UTF-8; reject the raw escape sequence first.
    for (const pair of rawQuery.split('&')) {
        try { decodeURIComponent(pair.replace(/\+/g, ' ')); }
        catch { throw new RouteError(400, 'Malformed query encoding'); }
    }
    return { host, pathname, query, search: rawQuery ? `?${rawQuery}` : '' };
}

export function checkSameOrigin(req, host) {
    if (req.method === 'GET' || req.method === 'HEAD') return;
    const origin = req.headers.origin;
    if (origin && origin !== `http://${host}`) throw new RouteError(403, 'Origin mismatch');
    const fetchSite = req.headers['sec-fetch-site'];
    if (fetchSite && !['same-origin', 'none'].includes(fetchSite))
        throw new RouteError(403, 'Cross-site request rejected');
}

export function validateQuery(query, allowed = []) {
    for (const [key, value] of query) {
        const rule = allowed.find(entry => {
            const name = typeof entry === 'string' || entry instanceof RegExp ? entry : entry.name;
            return typeof name === 'string' ? name === key : name instanceof RegExp && name.test(key);
        });
        if (!rule) throw new RouteError(400, 'Unknown query parameter');
        if (typeof rule === 'string' || rule instanceof RegExp) continue;
        if (rule.type === 'integer') {
            const number = Number(value);
            if (!/^(0|[1-9]\d*)$/.test(value) || !Number.isSafeInteger(number) ||
                number < (rule.min ?? 0) || number > (rule.max ?? Number.MAX_SAFE_INTEGER))
                throw new RouteError(400, 'Invalid integer query parameter');
        } else if (rule.type === 'string') {
            if (value.length > (rule.maxLength ?? MAX_QUERY_VALUE) ||
                (rule.pattern && !rule.pattern.test(value)))
                throw new RouteError(400, 'Invalid text query parameter');
        } else throw new TypeError('Unknown query rule type');
    }
}

export function requireNoBody(req) {
    if ((req.headers['content-length'] && req.headers['content-length'] !== '0') ||
        req.headers['transfer-encoding']) throw new RouteError(400, 'Request body is not allowed');
}

function rejectDuplicateJsonKeys(source, maxNodes) {
    let index = 0, nodes = 0;
    const fail = () => { throw new RouteError(400, 'Invalid or ambiguous JSON'); };
    const spaces = () => { while (/[ \t\r\n]/.test(source[index] || '\0')) index++; };
    const readString = () => {
        if (source[index] !== '"') fail();
        const start = index++;
        while (index < source.length) {
            if (source[index] === '\\') { index += 2; continue; }
            if (source[index++] === '"') {
                try { return JSON.parse(source.slice(start, index)); }
                catch { fail(); }
            }
        }
        fail();
    };
    const value = depth => {
        if (depth > 32 || ++nodes > maxNodes) throw new RouteError(400, STRUCTURE_LIMIT);
        spaces();
        if (source[index] === '{') {
            index++; spaces();
            const keys = new Set();
            if (source[index] === '}') { index++; return; }
            while (index < source.length) {
                const key = readString();
                if (keys.has(key)) fail();
                keys.add(key);
                spaces();
                if (source[index++] !== ':') fail();
                value(depth + 1); spaces();
                const delimiter = source[index++];
                if (delimiter === '}') return;
                if (delimiter !== ',') fail();
                spaces();
            }
            fail();
        }
        if (source[index] === '[') {
            index++; spaces();
            if (source[index] === ']') { index++; return; }
            while (index < source.length) {
                value(depth + 1); spaces();
                const delimiter = source[index++];
                if (delimiter === ']') return;
                if (delimiter !== ',') fail();
            }
            fail();
        }
        if (source[index] === '"') { readString(); return; }
        const start = index;
        while (index < source.length && !/[,\]} \t\r\n]/.test(source[index])) index++;
        if (index === start) fail();
    };
    value(0); spaces();
    if (index !== source.length) fail();
}

export async function readJsonBody(req, { maxBytes = DEFAULT_BODY_BYTES, maxNodes = DEFAULT_JSON_NODES } = {}) {
    const contentType = req.headers['content-type'] || '';
    if (!/^application\/json(?:;\s*charset=utf-8)?$/i.test(contentType))
        throw new RouteError(415, 'JSON content type required');
    if (req.headers['content-encoding'] && req.headers['content-encoding'] !== 'identity')
        throw new RouteError(415, 'Encoded request body is not supported');
    const declared = req.headers['content-length'];
    if (declared !== undefined && (!/^\d+$/.test(declared) || Number(declared) > maxBytes))
        throw new RouteError(413, 'JSON body too large');
    let bytes = 0;
    const chunks = [];
    for await (const chunk of req) {
        bytes += chunk.length;
        if (bytes > maxBytes) throw new RouteError(413, 'JSON body too large');
        chunks.push(chunk);
    }
    if (declared !== undefined && bytes !== Number(declared))
        throw new RouteError(400, 'JSON body length mismatch');
    let value;
    try {
        const source = new TextDecoder('utf-8', { fatal: true }).decode(Buffer.concat(chunks));
        rejectDuplicateJsonKeys(source, maxNodes);
        value = JSON.parse(source);
    }
    catch (error) {
        // 結構上限與語法錯誤分開回報，否則合法但過大的本文會被說成「不是 JSON」。
        if (error instanceof RouteError && error.message === STRUCTURE_LIMIT) throw error;
        throw new RouteError(400, 'Invalid JSON');
    }
    if (!value || typeof value !== 'object' || Array.isArray(value))
        throw new RouteError(400, 'JSON object required');
    const visit = (item, depth, budget) => {
        if (depth > 32 || --budget.remaining < 0) throw new RouteError(400, STRUCTURE_LIMIT);
        if (typeof item === 'number' && !Number.isFinite(item)) throw new RouteError(400, 'Non-finite JSON number');
        if (!item || typeof item !== 'object') return;
        for (const [key, child] of Object.entries(item)) {
            if (['__proto__', 'constructor', 'prototype'].includes(key) || CONTROL.test(key))
                throw new RouteError(400, 'Unsafe JSON key');
            visit(child, depth + 1, budget);
        }
    };
    visit(value, 0, { remaining: maxNodes });
    return value;
}

export async function guardRoute(req, context, { query = [], body = 'none', authorize } = {}) {
    checkSameOrigin(req, context.host);
    validateQuery(context.query, query);
    const actor = authorize ? await authorize(req) : null;
    if (body === 'none') { requireNoBody(req); return { actor, body: null }; }
    if (body === 'json') return { actor, body: await readJsonBody(req) };
    if (body && typeof body === 'object' && body.type === 'json')
        return { actor, body: await readJsonBody(req, { maxBytes: body.maxBytes, maxNodes: body.maxNodes }) };
    throw new TypeError('Unknown route body policy');
}

export function sendJson(res, status, value, extra = {}) {
    res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', ...extra });
    res.end(JSON.stringify(value));
}

export function sendRouteError(res, error) {
    const status = error instanceof RouteError ? error.status : 500;
    sendJson(res, status, { error: status === 500 ? 'Internal server error' : error.message });
}

export class RequestLimiter {
    constructor({ windowMs = 60_000, maxRequests = 600 } = {}) {
        this.windowMs = windowMs;
        this.maxRequests = maxRequests;
        this.buckets = new Map();
    }
    check(req) {
        const address = req.socket?.remoteAddress || 'unknown';
        const now = Date.now();
        const bucket = this.buckets.get(address);
        const next = !bucket || now >= bucket.until ? { count: 1, until: now + this.windowMs }
            : { count: bucket.count + 1, until: bucket.until };
        this.buckets.set(address, next);
        if (next.count > this.maxRequests) throw new RouteError(429, 'Request rate limit reached');
        if (this.buckets.size > 1024)
            for (const [key, value] of this.buckets) if (now >= value.until) this.buckets.delete(key);
    }
    clear() { this.buckets.clear(); }
}
