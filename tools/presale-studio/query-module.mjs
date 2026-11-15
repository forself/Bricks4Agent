import { PrototypeError } from './prototype-rules.mjs';
import { compileSort, compileWhere, fieldFor, objectKeys, toRecord } from './scoped-read.mjs';

export function queryRecords(context, body) {
    if (!objectKeys(body, ['filters', 'sort', 'page', 'page_size']))
        throw new PrototypeError(400, 'Invalid query');
    const page = body.page ?? 1, pageSize = body.page_size ?? 20;
    if (!Number.isSafeInteger(page) || page < 1 || page > 1_000_000 ||
        !Number.isSafeInteger(pageSize) || pageSize < 1 || pageSize > 100)
        throw new PrototypeError(400, 'Invalid page');
    const where = compileWhere(context, body.filters);
    const sort = compileSort(context, body.sort);
    const total = context.db.prepare(`SELECT COUNT(*) AS total FROM ${context.table} ${where.sql}`)
        .get(...where.values).total;
    const rows = context.db.prepare(`SELECT * FROM ${context.table} ${where.sql} ORDER BY ${sort} LIMIT ? OFFSET ?`)
        .all(...where.values, pageSize, (page - 1) * pageSize);
    return { items: rows.map(row => toRecord(context, row)), total, page, page_size: pageSize };
}

export function lookupValues(context, body) {
    if (!objectKeys(body, ['field', 'filters', 'limit'], ['field']))
        throw new PrototypeError(400, 'Invalid lookup');
    const limit = body.limit ?? 50;
    if (!Number.isSafeInteger(limit) || limit < 1 || limit > 100)
        throw new PrototypeError(400, 'Invalid lookup limit');
    const column = fieldFor(context, body.field).column;
    const where = compileWhere(context, body.filters);
    const rows = context.db.prepare(`SELECT ${column} AS value, COUNT(*) AS count FROM ${context.table} ${where.sql}
        GROUP BY ${column} ORDER BY ${column} COLLATE NOCASE LIMIT ?`)
        .all(...where.values, limit + 1);
    const field = fieldFor(context, body.field).field;
    return { items: rows.slice(0, limit).map(row => ({
        value: field.type === 'toggle' && row.value !== null ? Boolean(row.value) : row.value,
        count: row.count
    })), truncated: rows.length > limit };
}
