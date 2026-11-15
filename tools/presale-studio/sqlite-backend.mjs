import { DatabaseSync } from 'node:sqlite';
import { createHash } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import path from 'node:path';
import { MAX_ROWS, PrototypeError, scopeAllowed, validateFields, parseFilters } from './prototype-rules.mjs';
import { queryRecords, lookupValues } from './query-module.mjs';
import { summarizeRecords, exportCsv } from './report-module.mjs';
import { batchCreateRecords } from './batch-module.mjs';

const IDENTIFIER = /^[TC]_[A-F0-9]{12}$/;
const sqlType = type => type === 'number' ? 'REAL' : type === 'toggle' ? 'INTEGER' : 'TEXT';
const quoted = value => {
    if (!IDENTIFIER.test(value)) throw new TypeError('Invalid database mapping identifier');
    return `"${value}"`;
};
const stored = (field, value) => field.type === 'toggle' && value !== null ? Number(value) : value;
const restored = (field, value) => field.type === 'toggle' && value !== null ? Boolean(value) : value;
const escapedLike = value => `%${value.replace(/[\\%_]/g, character => `\\${character}`)}%`;

function roleWhere(role, subject) {
    const parts = [], values = [];
    if (role.vertical !== 'all') {
        const owners = [];
        if (['self', 'self_descendants'].includes(role.vertical)) owners.push(subject.owner);
        if (['descendants', 'self_descendants'].includes(role.vertical)) owners.push(...subject.descendants);
        if (!owners.length) return { sql: '0', values: [] };
        parts.push(`scope_owner IN (${owners.map(() => '?').join(',')})`);
        values.push(...owners);
    }
    switch (role.horizontal) {
        case 'all': break;
        case 'same_unit': parts.push('scope_unit = ?'); values.push(subject.unit); break;
        case 'same_function': parts.push('scope_function = ?'); values.push(subject.function); break;
        case 'unit_or_function':
            parts.push('(scope_unit = ? OR scope_function = ?)');
            values.push(subject.unit, subject.function); break;
        case 'unit_and_function':
            parts.push('(scope_unit = ? AND scope_function = ?)');
            values.push(subject.unit, subject.function); break;
        default: return { sql: '0', values: [] };
    }
    return { sql: parts.length ? parts.join(' AND ') : '1', values };
}

function accessWhere(principal, action) {
    if (!principal || !principal.subject || !Array.isArray(principal.roles))
        throw new PrototypeError(401, 'Verified principal required');
    const granted = principal.roles.filter(role => role?.actions?.includes(action));
    if (!granted.length) throw new PrototypeError(403, `${action} not granted`);
    const scopes = granted.map(role => roleWhere(role, principal.subject));
    return { sql: `(${scopes.map(scope => `(${scope.sql})`).join(' OR ')})`,
        values: scopes.flatMap(scope => scope.values) };
}

export class SqlitePrototypeBackend {
    constructor({ dbPath, config, mapping, seed, mode = 'demo' }) {
        if (config?.schema_version !== 1 || !Array.isArray(config.resources) || !Array.isArray(config.roles) ||
            !Array.isArray(mapping) || !seed || typeof seed !== 'object') throw new TypeError('Invalid backend definition');
        mkdirSync(path.dirname(dbPath), { recursive: true });
        this.db = new DatabaseSync(dbPath);
        this.config = config;
        if (!['demo', 'formal'].includes(mode)) throw new TypeError('Invalid backend mode');
        this.mode = mode;
        this.db.exec('PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000');
        if (mode === 'formal' && dbPath !== ':memory:')
            this.db.exec('PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL');
        this.seed = structuredClone(seed);
        this.resources = new Map();
        for (const resource of config.resources) {
            const entry = mapping.find(item => item.entity_code === resource.entity_code);
            if (!entry || !IDENTIFIER.test(entry.table_code) || entry.fields.length !== resource.fields.length ||
                entry.fields.some((field, index) => field.field_code !== resource.fields[index].code ||
                    field.frontend_key !== field.field_code || field.type !== resource.fields[index].type ||
                    !IDENTIFIER.test(field.column_code))) throw new TypeError('Field mapping mismatch');
            this.resources.set(resource.api, { resource, mapping: entry });
        }
        if (this.resources.size !== config.resources.length || mapping.length !== config.resources.length)
            throw new TypeError('Resource mapping mismatch');
        this.#initialize(seed);
    }

    #initialize(seed) {
        const signature = createHash('sha256').update(JSON.stringify({ resources: this.config.resources, mapping: [...this.resources.values()].map(x => x.mapping) })).digest('hex');
        this.db.exec('BEGIN IMMEDIATE');
        try {
            this.db.exec('CREATE TABLE IF NOT EXISTS prototype_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)');
            this.db.exec('CREATE TABLE IF NOT EXISTS prototype_batch_requests (request_id TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, result_json TEXT NOT NULL)');
            const batchColumns = this.db.prepare('PRAGMA table_info(prototype_batch_requests)').all();
            const batchExpected = ['request_id', 'fingerprint', 'result_json'];
            if (batchColumns.length !== batchExpected.length || batchColumns.some((column, index) =>
                column.name !== batchExpected[index] || column.type !== 'TEXT' ||
                column.pk !== Number(index === 0) || column.notnull !== Number(index > 0)))
                throw new TypeError('SQLite batch request schema mismatch');
            const current = this.db.prepare('SELECT value FROM prototype_meta WHERE key = ?').get('schema_signature');
            if (current && current.value !== signature) throw new TypeError('SQLite schema does not match field mapping');
            for (const { resource, mapping } of this.resources.values()) {
                const columns = mapping.fields.map((field, index) =>
                    `${quoted(field.column_code)} ${sqlType(field.type)}${resource.fields[index].required ? ' NOT NULL' : ''}`);
                this.db.exec(`CREATE TABLE IF NOT EXISTS ${quoted(mapping.table_code)} (
                    id INTEGER PRIMARY KEY, scope_owner TEXT NOT NULL, scope_unit TEXT NOT NULL,
                    scope_function TEXT NOT NULL, ${columns.join(', ')})`);
                const reflected = this.db.prepare(`PRAGMA table_info(${quoted(mapping.table_code)})`).all();
                const expected = ['id', 'scope_owner', 'scope_unit', 'scope_function', ...mapping.fields.map(field => field.column_code)];
                if (reflected.length !== expected.length || reflected.some((column, index) =>
                    column.name !== expected[index] ||
                    (index === 0 && (column.type !== 'INTEGER' || column.pk !== 1)) ||
                    (index > 0 && index < 4 && (column.type !== 'TEXT' || column.notnull !== 1)) ||
                    (index > 3 && (column.type !== sqlType(mapping.fields[index - 4].type) ||
                        column.notnull !== Number(resource.fields[index - 4].required)))))
                    throw new TypeError('SQLite reflected columns do not match field mapping');
            }
            if (!current) {
                if (this.mode === 'demo')
                    for (const { resource, mapping } of this.resources.values()) this.#seedOne(resource, mapping, seed[resource.api] || []);
                this.db.prepare('INSERT INTO prototype_meta (key, value) VALUES (?, ?)').run('schema_signature', signature);
            }
            this.db.exec('COMMIT');
        } catch (error) { this.db.exec('ROLLBACK'); this.db.close(); throw error; }
    }

    #seedOne(resource, mapping, rows) {
        if (!Array.isArray(rows) || rows.length !== resource.sample_scopes.length || rows.length > MAX_ROWS)
            throw new TypeError('Invalid seed data');
        const columns = ['id', 'scope_owner', 'scope_unit', 'scope_function', ...mapping.fields.map(field => field.column_code)];
        const statement = this.db.prepare(`INSERT INTO ${quoted(mapping.table_code)} (${columns.map(name => name.startsWith('C_') ? quoted(name) : name).join(', ')}) VALUES (${columns.map(() => '?').join(', ')})`);
        rows.forEach((row, index) => {
            const values = validateFields(resource, Object.fromEntries(resource.fields.map(field => [field.code, row[field.code]])), true);
            const scope = resource.sample_scopes[index];
            statement.run(row.id, scope.owner, scope.unit, scope.function,
                ...resource.fields.map(field => stored(field, values[field.code])));
        });
    }

    close() { this.db.close(); }
    readContext(api, actor) {
        const item = this.#target(api);
        if (item.id !== null || item.query.size) throw new PrototypeError(400, 'Resource path required');
        return { resource: item.resource, mapping: item.mapping, db: this.db,
            table: quoted(item.mapping.table_code), where: accessWhere(this.#principal(actor), 'read') };
    }
    query(api, body, actor) { return queryRecords(this.readContext(api, actor), body); }
    lookup(api, body, actor) { return lookupValues(this.readContext(api, actor), body); }
    summary(api, body, actor) { return summarizeRecords(this.readContext(api, actor), body); }
    export(api, body, actor) { return exportCsv(this.readContext(api, actor), body); }
    writeContext(api, actor) {
        const item = this.#target(api);
        if (item.id !== null || item.query.size) throw new PrototypeError(400, 'Resource path required');
        const principal = this.#principal(actor);
        const scope = { owner: principal.subject.owner, unit: principal.subject.unit,
            function: principal.subject.function };
        if (!principal.roles.some(role => scopeAllowed(role, principal.subject, 'create', scope)))
            throw new PrototypeError(403, 'Create scope denied');
        return { resource: item.resource, mapping: item.mapping, db: this.db,
            table: quoted(item.mapping.table_code), mode: this.mode,
            principal_id: principal.principal_id || 'demo', scope };
    }
    batchCreate(api, body, actor) { return batchCreateRecords(this.writeContext(api, actor), body); }
    #principal(actor) {
        if (actor?.subject && Array.isArray(actor.roles)) return actor;
        if (this.mode === 'demo' && actor?.code && this.config.roles.some(role => role.code === actor.code))
            return { subject: this.config.subject, roles: [this.config.roles.find(role => role.code === actor.code)] };
        throw new PrototypeError(401, 'Verified principal required');
    }
    #target(url) {
        const parsed = new URL(url, 'http://prototype.invalid');
        for (const [api, item] of this.resources) {
            if (parsed.pathname === api) return { ...item, id: null, query: parsed.searchParams };
            if (parsed.pathname.startsWith(`${api}/`)) {
                const token = parsed.pathname.slice(api.length + 1);
                const id = Number(token);
                if (/^[1-9]\d*$/.test(token) && Number.isSafeInteger(id))
                    return { ...item, id, query: parsed.searchParams };
            }
        }
        throw new PrototypeError(404, 'Unknown prototype resource');
    }
    #readable(resource, mapping, row) {
        return { id: row.id, ...Object.fromEntries(mapping.fields.map((field, index) =>
            [field.field_code, restored(resource.fields[index], row[field.column_code])])) };
    }
    #one(item, principal, action) {
        let where;
        try { where = accessWhere(principal, action); }
        catch (error) { if (error.status === 403) throw new PrototypeError(404, 'Record unavailable'); throw error; }
        const row = this.db.prepare(`SELECT * FROM ${quoted(item.mapping.table_code)} WHERE id = ? AND ${where.sql}`)
            .get(item.id, ...where.values);
        if (!row) throw new PrototypeError(404, 'Record unavailable');
        return row;
    }

    get(url, role) {
        const item = this.#target(url);
        const principal = this.#principal(role);
        if (item.id !== null) return this.#readable(item.resource, item.mapping, this.#one(item, principal, 'read'));
        const { filters, page, pageSize } = parseFilters(item.resource, item.query);
        const where = accessWhere(principal, 'read');
        const clauses = [where.sql], values = [...where.values];
        for (const [code, value] of filters) {
            const field = item.mapping.fields.find(entry => entry.field_code === code);
            clauses.push(`LOWER(CAST(${quoted(field.column_code)} AS TEXT)) LIKE LOWER(?) ESCAPE '\\'`);
            values.push(escapedLike(value));
        }
        const sql = ` FROM ${quoted(item.mapping.table_code)} WHERE ${clauses.join(' AND ')}`;
        const total = this.db.prepare(`SELECT COUNT(*) AS total${sql}`).get(...values).total;
        const rows = this.db.prepare(`SELECT *${sql} ORDER BY id LIMIT ? OFFSET ?`)
            .all(...values, pageSize, (page - 1) * pageSize);
        return { items: rows.map(row => this.#readable(item.resource, item.mapping, row)), total };
    }

    post(url, body, role) {
        const item = this.#target(url);
        const principal = this.#principal(role);
        if (item.id !== null) throw new PrototypeError(405, 'Create needs resource path');
        const values = validateFields(item.resource, body, true);
        const scope = { owner: principal.subject.owner, unit: principal.subject.unit,
            function: principal.subject.function };
        if (!principal.roles.some(granted => scopeAllowed(granted, principal.subject, 'create', scope)))
            throw new PrototypeError(403, 'Create scope denied');
        const table = quoted(item.mapping.table_code);
        if (this.mode === 'demo' && this.db.prepare(`SELECT COUNT(*) AS count FROM ${table}`).get().count >= MAX_ROWS)
            throw new PrototypeError(413, 'Prototype record limit reached');
        const fields = item.mapping.fields;
        const columns = ['scope_owner', 'scope_unit', 'scope_function', ...fields.map(field => quoted(field.column_code))];
        const inserted = this.db.prepare(`INSERT INTO ${table} (${columns.join(', ')}) VALUES (${columns.map(() => '?').join(', ')})`)
            .run(scope.owner, scope.unit, scope.function,
                ...fields.map((field, index) => stored(item.resource.fields[index], values[field.field_code] ?? null)));
        return this.#readable(item.resource, item.mapping,
            this.db.prepare(`SELECT * FROM ${table} WHERE id = ?`).get(inserted.lastInsertRowid));
    }

    put(url, body, role) {
        const item = this.#target(url);
        const principal = this.#principal(role);
        if (item.id === null) throw new PrototypeError(405, 'Record id required');
        this.#one(item, principal, 'update');
        const values = validateFields(item.resource, body, false);
        if (Object.keys(values).length) {
            const fields = Object.keys(values).map(code => ({ code, index: item.mapping.fields.findIndex(field => field.field_code === code) }));
            const where = accessWhere(principal, 'update');
            this.db.prepare(`UPDATE ${quoted(item.mapping.table_code)} SET ${fields.map(({ index }) => `${quoted(item.mapping.fields[index].column_code)} = ?`).join(', ')} WHERE id = ? AND ${where.sql}`)
                .run(...fields.map(({ code, index }) => stored(item.resource.fields[index], values[code])), item.id, ...where.values);
        }
        if (!principal.roles.some(granted => granted.actions.includes('read'))) return { ok: true };
        try { return this.get(url, principal); }
        catch (error) { if (error.status === 404) return { ok: true }; throw error; }
    }

    delete(url, role) {
        const item = this.#target(url);
        const principal = this.#principal(role);
        if (item.id === null) throw new PrototypeError(405, 'Record id required');
        this.#one(item, principal, 'delete');
        const where = accessWhere(principal, 'delete');
        this.db.prepare(`DELETE FROM ${quoted(item.mapping.table_code)} WHERE id = ? AND ${where.sql}`)
            .run(item.id, ...where.values);
        return { ok: true };
    }

    reset() {
        if (this.mode !== 'demo') throw new PrototypeError(403, 'Reset is only available in demo mode');
        this.db.exec('BEGIN IMMEDIATE');
        try {
            for (const { mapping } of this.resources.values()) this.db.exec(`DELETE FROM ${quoted(mapping.table_code)}`);
            this.db.exec('DELETE FROM prototype_batch_requests');
            // Seed is kept in memory only for the explicitly isolated demo database.
            for (const { resource, mapping } of this.resources.values())
                this.#seedOne(resource, mapping, this.seed[resource.api] || []);
            this.db.exec('COMMIT');
        } catch (error) { this.db.exec('ROLLBACK'); throw error; }
    }
}
