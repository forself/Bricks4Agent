import { PrototypeError } from './prototype-rules.mjs';
import { randomBytes } from 'node:crypto';

const ROLE_CODE = /^R_[A-F0-9]{12}$/;
const ACTIONS = new Set(['create', 'read', 'update', 'delete']);
const VERTICAL = new Set(['all', 'self', 'descendants', 'self_descendants']);
const HORIZONTAL = new Set(['all', 'same_unit', 'same_function', 'unit_or_function', 'unit_and_function']);
const IDENT = /^[A-Za-z0-9_.:-]{1,128}$/;
const ownKeys = (value, required) => value && typeof value === 'object' && !Array.isArray(value) &&
    Object.keys(value).sort().join('|') === [...required].sort().join('|');
const identifier = value => typeof value === 'string' && IDENT.test(value);

function validateSubject(subject) {
    if (!ownKeys(subject, ['owner', 'unit', 'function', 'descendants']) ||
        ![subject.owner, subject.unit, subject.function].every(identifier) ||
        !Array.isArray(subject.descendants) || subject.descendants.length > 100 ||
        subject.descendants.some(value => !identifier(value)) ||
        new Set(subject.descendants).size !== subject.descendants.length)
        throw new TypeError('Invalid approved subject scope');
}

function validateRole(role) {
    if (!ownKeys(role, ['code', 'label', 'actions', 'vertical', 'horizontal']) ||
        !ROLE_CODE.test(role.code) || !Array.isArray(role.actions) || !role.actions.length ||
        typeof role.label !== 'string' || role.label.length < 1 || role.label.length > 40 ||
        new Set(role.actions).size !== role.actions.length || role.actions.some(action => !ACTIONS.has(action)) ||
        !VERTICAL.has(role.vertical) || !HORIZONTAL.has(role.horizontal))
        throw new TypeError('Invalid approved role policy');
}

// This policy is supplied and reviewed by an administrator. Interview roles and
// task scope descriptors are deliberately never promoted into formal grants.
export function validateFormalPolicy(policy) {
    if (!ownKeys(policy, ['schema_version', 'principals']) || policy.schema_version !== 1 ||
        !Array.isArray(policy.principals) || !policy.principals.length || policy.principals.length > 1000)
        throw new TypeError('Approved formal policy required');
    const keys = new Set();
    for (const entry of policy.principals) {
        if (!ownKeys(entry, ['principal_id', 'task_id', 'role_id', 'subject', 'roles']) ||
            ![entry.principal_id, entry.task_id, entry.role_id].every(identifier) ||
            !Array.isArray(entry.roles) || !entry.roles.length || entry.roles.length > 16)
            throw new TypeError('Invalid approved principal binding');
        validateSubject(entry.subject);
        entry.roles.forEach(validateRole);
        const key = `${entry.principal_id}\0${entry.task_id}\0${entry.role_id}`;
        if (keys.has(key)) throw new TypeError('Duplicate approved principal binding');
        keys.add(key);
    }
    return structuredClone(policy);
}

export class BrokerPrincipalProvider {
    constructor({ verifyUrl, policy, fetcher = fetch }) {
        const url = new URL(verifyUrl);
        if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' ||
            url.username || url.password || url.search || url.hash ||
            url.pathname !== '/api/v1/sessions/verify')
            throw new TypeError('Broker verification must use the local read-only endpoint');
        this.verifyUrl = url.href;
        this.policy = validateFormalPolicy(policy);
        this.fetcher = fetcher;
    }

    async principal(req) {
        const header = req.headers.authorization;
        if (typeof header !== 'string' || !/^Bearer [A-Za-z0-9._~-]{20,8192}$/.test(header))
            throw new PrototypeError(401, 'Broker bearer token required');
        let response;
        try {
            response = await this.fetcher(this.verifyUrl, { method: 'GET',
                headers: { Authorization: header, Accept: 'application/json' },
                cache: 'no-store', redirect: 'manual', signal: AbortSignal.timeout(3000) });
        } catch { throw new PrototypeError(503, 'Broker verification unavailable'); }
        if (response.status === 401 || response.status === 403) throw new PrototypeError(401, 'Broker token rejected');
        if (response.status !== 200) throw new PrototypeError(503, 'Broker verification unavailable');
        const contentLength = Number(response.headers.get('content-length'));
        if (Number.isFinite(contentLength) && contentLength > 4096)
            throw new PrototypeError(503, 'Broker response rejected');
        let envelope;
        try {
            const text = await response.text();
            if (text.length > 4096) throw new Error('Too large');
            envelope = JSON.parse(text);
        } catch { throw new PrototypeError(503, 'Broker response rejected'); }
        const data = envelope?.data ?? envelope?.Data;
        if ((envelope?.success ?? envelope?.Success) !== true ||
            !identifier(data?.principal_id) || !identifier(data?.task_id) ||
            !identifier(data?.role_id) || !identifier(data?.session_id))
            throw new PrototypeError(503, 'Broker response rejected');
        const binding = this.policy.principals.find(entry => entry.principal_id === data.principal_id &&
            entry.task_id === data.task_id && entry.role_id === data.role_id);
        if (!binding) throw new PrototypeError(403, 'Principal has no approved prototype grant');
        return { principal_id: data.principal_id, subject: structuredClone(binding.subject),
            roles: structuredClone(binding.roles) };
    }
}

// The browser receives an opaque HttpOnly cookie. The bearer stays only in
// server memory, and every request is checked against Broker again so a
// revoked token or closed container session stops immediately.
export class FormalSessions {
    constructor(provider) { this.provider = provider; this.tokens = new Map(); }
    async issue(scopedToken) {
        const principal = await this.provider.principal({ headers: { authorization: `Bearer ${scopedToken}` } });
        for (const [key, session] of this.tokens)
            if (session.expires <= Date.now()) this.tokens.delete(key);
        if (this.tokens.size >= 1024) throw new PrototypeError(429, 'Too many formal sessions');
        const token = randomBytes(32).toString('hex');
        this.tokens.set(token, { scopedToken, expires: Date.now() + 60 * 60 * 1000 });
        return { token, principal };
    }
    async principal(req) {
        const direct = req.headers.authorization;
        if (direct) return this.provider.principal(req);
        const token = /(?:^|;\s*)b4a_formal=([a-f0-9]{64})(?:;|$)/.exec(req.headers.cookie || '')?.[1];
        const session = token && this.tokens.get(token);
        if (!session || session.expires <= Date.now()) {
            if (token) this.tokens.delete(token);
            throw new PrototypeError(401, 'Broker session required');
        }
        return this.provider.principal({ headers: { authorization: `Bearer ${session.scopedToken}` } });
    }
    revoke(req) {
        const token = /(?:^|;\s*)b4a_formal=([a-f0-9]{64})(?:;|$)/.exec(req.headers.cookie || '')?.[1];
        if (token) this.tokens.delete(token);
    }
    clear() { this.tokens.clear(); }
}
