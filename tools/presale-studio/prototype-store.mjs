// A deliberately isolated, browser-persisted test store. Role selection is a test
// identity, not authentication; production permissions must be enforced server-side.
import { MAX_ROWS, PrototypeError, scopeAllowed, validateFields, parseFilters } from './prototype-rules.mjs';
export { PrototypeError } from './prototype-rules.mjs';

export class PrototypeStore {
    constructor(config, seed, storage = globalThis.localStorage) {
        if (config?.schema_version !== 1 || !Array.isArray(config.resources) ||
            !Array.isArray(config.roles) || typeof config.storage_key !== 'string' ||
            !config.storage_key.startsWith('b4a-presale-')) throw new TypeError('Invalid prototype config');
        this.config = config;
        this.storage = storage;
        this.resources = new Map(config.resources.map(resource => [resource.api, resource]));
        this.roleKey = `${config.storage_key}:role`;
        const persisted = this.#load();
        this.rows = persisted || Object.fromEntries(config.resources.map(resource => [
            resource.api, (seed[resource.api] || []).map((row, index) => ({
                ...structuredClone(row), __scope: resource.sample_scopes[index]
            }))
        ]));
        if (!persisted) this.#save();
    }

    get role() {
        const code = this.storage.getItem(this.roleKey);
        return this.config.roles.find(role => role.code === code) || this.config.roles[0];
    }

    setRole(code) {
        if (!this.config.roles.some(role => role.code === code)) throw new PrototypeError(400, 'Unknown test role');
        this.storage.setItem(this.roleKey, code);
    }

    resetData() { this.storage.removeItem(this.config.storage_key); }

    #load() {
        try {
            const value = JSON.parse(this.storage.getItem(this.config.storage_key));
            if (!value || Object.keys(value).length !== this.resources.size) return null;
            for (const resource of this.resources.values()) {
                const rows = value[resource.api];
                if (!Array.isArray(rows) || rows.length > MAX_ROWS || rows.some(row =>
                    !row || !Number.isSafeInteger(row.id) || row.id < 1 ||
                    !row.__scope || typeof row.__scope.owner !== 'string' ||
                    typeof row.__scope.unit !== 'string' || typeof row.__scope.function !== 'string' ||
                    Object.keys(row).some(key => key !== 'id' && key !== '__scope' &&
                        !resource.fields.some(field => field.code === key)))) return null;
            }
            return value;
        } catch { return null; }
    }

    #save() { this.storage.setItem(this.config.storage_key, JSON.stringify(this.rows)); }

    #target(url) {
        const parsed = new URL(url, 'http://prototype.invalid');
        for (const [api, resource] of this.resources) {
            if (parsed.pathname === api) return { resource, id: null, query: parsed.searchParams };
            if (parsed.pathname.startsWith(`${api}/`)) {
                const id = Number(parsed.pathname.slice(api.length + 1));
                if (Number.isSafeInteger(id) && id > 0) return { resource, id, query: parsed.searchParams };
            }
        }
        throw new PrototypeError(404, 'Unknown prototype resource');
    }

    #allowed(action, scope) {
        return scopeAllowed(this.role, this.config.subject, action, scope);
    }

    #readable(row) {
        const { __scope, ...data } = row;
        return structuredClone(data);
    }

    #row(resource, id, action) {
        const row = this.rows[resource.api].find(item => item.id === id);
        if (!row || !this.#allowed(action, row.__scope)) throw new PrototypeError(404, 'Record unavailable in this role');
        return row;
    }

    #fields(resource, body, create) {
        return validateFields(resource, body, create);
    }

    async get(url) {
        const { resource, id, query } = this.#target(url);
        if (!this.role.actions.includes('read')) throw new PrototypeError(403, 'Read not granted');
        if (id !== null) return this.#readable(this.#row(resource, id, 'read'));
        const { filters, page, pageSize } = parseFilters(resource, query);
        const rows = this.rows[resource.api].filter(row => this.#allowed('read', row.__scope) &&
            filters.every(([key, value]) => String(row[key] ?? '').toLocaleLowerCase().includes(value.toLocaleLowerCase())));
        return { items: rows.slice((page - 1) * pageSize, page * pageSize).map(row => this.#readable(row)), total: rows.length };
    }

    async post(url, body) {
        const { resource, id } = this.#target(url);
        if (id !== null || !this.role.actions.includes('create')) throw new PrototypeError(403, 'Create not granted');
        const values = this.#fields(resource, body, true);
        const rows = this.rows[resource.api];
        if (rows.length >= MAX_ROWS) throw new PrototypeError(413, 'Prototype record limit reached');
        const idValue = Math.max(0, ...rows.map(row => row.id)) + 1;
        const subject = this.config.subject;
        const scope = { owner: subject.owner, unit: subject.unit, function: subject.function };
        if (!this.#allowed('create', scope)) throw new PrototypeError(403, 'Create scope denied');
        const row = { id: idValue, ...values, __scope: scope };
        rows.push(row);
        this.#save();
        return this.#readable(row);
    }

    async put(url, body) {
        const { resource, id } = this.#target(url);
        if (id === null) throw new PrototypeError(405, 'Record id required');
        const row = this.#row(resource, id, 'update');
        Object.assign(row, this.#fields(resource, body, false));
        this.#save();
        return this.#readable(row);
    }

    async delete(url) {
        const { resource, id } = this.#target(url);
        if (id === null) throw new PrototypeError(405, 'Record id required');
        this.#row(resource, id, 'delete');
        this.rows[resource.api] = this.rows[resource.api].filter(row => row.id !== id);
        this.#save();
        return { ok: true };
    }
}

export function testStorage() {
    const values = new Map();
    return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value),
        removeItem: key => values.delete(key) };
}
