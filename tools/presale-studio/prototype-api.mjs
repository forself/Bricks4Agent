import { randomBytes } from 'node:crypto';
import { PrototypeError } from './prototype-rules.mjs';
import { resolvePrototypeRoute } from './prototype-router.mjs';
import { guardRoute, inspectRequest, RouteError, sendJson, sendRouteError } from './native-router.mjs';

const SESSION_SECONDS = 60 * 60;

export class DemoSessions {
    constructor(config) { this.config = config; this.tokens = new Map(); }
    issue(roleCode) {
        const role = this.config.roles.find(item => item.code === roleCode);
        if (!role) throw new PrototypeError(400, 'Unknown demo role');
        const token = randomBytes(32).toString('hex');
        this.tokens.set(token, { roleCode, expires: Date.now() + SESSION_SECONDS * 1000 });
        return { token, role };
    }
    role(req) {
        const token = /(?:^|;\s*)b4a_demo=([a-f0-9]{64})(?:;|$)/.exec(req.headers.cookie || '')?.[1];
        const session = token && this.tokens.get(token);
        if (!session || session.expires < Date.now()) throw new PrototypeError(401, 'Demo session required');
        return this.config.roles.find(role => role.code === session.roleCode);
    }
    principal(req) { return { principal_id: 'demo', subject: this.config.subject, roles: [this.role(req)] }; }
    clear() { this.tokens.clear(); }
}

export async function routePrototypeApi(req, res, { base, backend, sessions, context }) {
    const request = context || inspectRequest(req, res);
    const route = resolvePrototypeRoute(req.method, request.pathname, base);
    if (!route) return false;
    try {
        if (route.kind === 'method-not-allowed') {
            sendJson(res, 405, { error: 'Method not allowed' }, { Allow: route.allow });
            return true;
        }
        if (route.kind === 'not-found') throw new PrototypeError(404, 'Unknown prototype endpoint');
        if (!(sessions instanceof DemoSessions) && ['demo-session', 'reset'].includes(route.kind))
            throw new PrototypeError(404, 'Unknown endpoint');
        if (sessions instanceof DemoSessions && ['auth-exchange', 'auth-logout'].includes(route.kind))
            throw new PrototypeError(404, 'Unknown endpoint');
        const { actor: principal, body } = await guardRoute(req, request, {
            query: route.query,
            body: route.body,
            authorize: route.session ? () => sessions.principal(req) : null
        });
        if (route.kind === 'demo-session') {
            if (!body || typeof body !== 'object' || Array.isArray(body) ||
                Object.keys(body).join('|') !== 'role_code') throw new PrototypeError(400, 'Invalid demo role request');
            const { token, role } = sessions.issue(body.role_code);
            sendJson(res, 200, { role }, { 'Set-Cookie': `b4a_demo=${token}; HttpOnly; SameSite=Strict; Path=${base}; Max-Age=${SESSION_SECONDS}` });
            return true;
        }
        if (route.kind === 'auth-exchange') {
            if (!body || Object.keys(body).join('|') !== 'scoped_token' ||
                typeof body.scoped_token !== 'string') throw new PrototypeError(400, 'Broker token required');
            const { token, principal: authenticated } = await sessions.issue(body.scoped_token);
            sendJson(res, 200, { role: displayRole(authenticated) },
                { 'Set-Cookie': `b4a_formal=${token}; HttpOnly; SameSite=Strict; Path=${base}; Max-Age=${SESSION_SECONDS}` });
            return true;
        }
        if (route.kind === 'auth-logout') {
            if (Object.keys(body || {}).length) throw new PrototypeError(400, 'Invalid logout request');
            sessions.revoke(req);
            sendJson(res, 200, { ok: true },
                { 'Set-Cookie': `b4a_formal=; HttpOnly; SameSite=Strict; Path=${base}; Max-Age=0` });
            return true;
        }
        if (route.kind === 'session') { sendJson(res, 200, { role: displayRole(principal) }); return true; }
        if (route.kind === 'reset') {
            if (!body || typeof body !== 'object' || Array.isArray(body) || Object.keys(body).length)
                throw new PrototypeError(400, 'Invalid reset request');
            backend.reset(); sendJson(res, 200, { ok: true }); return true;
        }
        if (route.kind === 'read-module') {
            const api = route.path.slice(0, route.path.lastIndexOf('/'));
            if (route.params.module === 'query') sendJson(res, 200, backend.query(api, body, principal));
            else if (route.params.module === 'lookup') sendJson(res, 200, backend.lookup(api, body, principal));
            else if (route.params.module === 'summary') sendJson(res, 200, backend.summary(api, body, principal));
            else {
                const result = backend.export(api, body, principal);
                res.writeHead(200, { 'Content-Type': 'text/csv; charset=utf-8',
                    'Content-Disposition': 'attachment; filename="export.csv"',
                    'Content-Length': Buffer.byteLength(result.csv) });
                res.end(result.csv);
            }
            return true;
        }
        if (route.kind === 'batch') {
            const api = route.path.slice(0, -'/batch'.length);
            const result = backend.batchCreate(api, body, principal);
            sendJson(res, result.dry_run || result.replayed ? 200 : 201, result);
            return true;
        }
        const target = route.path + request.search;
        const value = req.method === 'GET' ? backend.get(target, principal)
            : req.method === 'POST' ? backend.post(target, body, principal)
                : req.method === 'PUT' ? backend.put(target, body, principal)
                    : backend.delete(target, principal);
        sendJson(res, req.method === 'POST' ? 201 : 200, value);
    } catch (error) {
        if (error instanceof PrototypeError) sendJson(res, error.status, { error: error.message });
        else sendRouteError(res, error instanceof RouteError ? error : new Error('Prototype backend failure'));
    }
    return true;
}

function displayRole(principal) {
    const roles = principal.roles;
    return { code: roles.map(role => role.code).join(','),
        label: roles.map(role => role.label || role.code).join('、'),
        actions: [...new Set(roles.flatMap(role => role.actions))] };
}
