import { createHash } from 'node:crypto';
import { MAX_ROWS, PrototypeError, validateFields } from './prototype-rules.mjs';
import { objectKeys } from './scoped-read.mjs';

const REQUEST_ID = /^[a-f0-9]{32}$/;

export function batchCreateRecords(context, body) {
    if (!objectKeys(body, ['request_id', 'items', 'dry_run'], ['request_id', 'items']) ||
        !REQUEST_ID.test(body.request_id) || !Array.isArray(body.items) ||
        !body.items.length || body.items.length > 100 ||
        (body.dry_run !== undefined && typeof body.dry_run !== 'boolean'))
        throw new PrototypeError(400, 'Invalid batch request');
    const fields = context.resource.fields;
    const records = body.items.map(item => validateFields(context.resource, item, true));
    if (body.dry_run) return { accepted: records.length, dry_run: true };
    const fingerprint = createHash('sha256').update(JSON.stringify({
        resource: context.resource.api, principal_id: context.principal_id, scope: context.scope,
        rows: records.map(record => fields.map(field => record[field.code] ?? null))
    })).digest('hex');
    context.db.exec('BEGIN IMMEDIATE');
    try {
        const previous = context.db.prepare('SELECT fingerprint, result_json FROM prototype_batch_requests WHERE request_id = ?')
            .get(body.request_id);
        if (previous) {
            if (previous.fingerprint !== fingerprint) throw new PrototypeError(409, 'Batch request id reused with different content');
            const result = JSON.parse(previous.result_json);
            context.db.exec('COMMIT');
            return { ...result, replayed: true };
        }
        if (context.mode === 'demo' && context.db.prepare(`SELECT COUNT(*) AS count FROM ${context.table}`).get().count + records.length > MAX_ROWS)
            throw new PrototypeError(413, 'Prototype record limit reached');
        const columns = ['scope_owner', 'scope_unit', 'scope_function', ...context.mapping.fields.map(field => `"${field.column_code}"`)];
        const statement = context.db.prepare(`INSERT INTO ${context.table} (${columns.join(', ')}) VALUES (${columns.map(() => '?').join(', ')})`);
        const ids = records.map(record => Number(statement.run(context.scope.owner, context.scope.unit,
            context.scope.function, ...fields.map(field => field.type === 'toggle' && record[field.code] !== null
                ? Number(record[field.code]) : record[field.code] ?? null)).lastInsertRowid));
        const result = { accepted: ids.length, ids };
        context.db.prepare('INSERT INTO prototype_batch_requests (request_id, fingerprint, result_json) VALUES (?, ?, ?)')
            .run(body.request_id, fingerprint, JSON.stringify(result));
        context.db.exec('COMMIT');
        return { ...result, replayed: false };
    } catch (error) {
        context.db.exec('ROLLBACK');
        throw error;
    }
}
