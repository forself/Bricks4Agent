import { PrototypeError } from './prototype-rules.mjs';
import { compileSort, compileWhere, fieldFor, objectKeys, toRecord } from './scoped-read.mjs';

export function summarizeRecords(context, body) {
    if (!objectKeys(body, ['filters', 'group_by', 'metric'], ['metric']) ||
        !objectKeys(body.metric, ['op', 'field'], ['op']))
        throw new PrototypeError(400, 'Invalid summary');
    const metric = body.metric;
    let expression;
    if (metric.op === 'count' && !Object.hasOwn(metric, 'field')) expression = 'COUNT(*)';
    else if (metric.op === 'sum' && Object.hasOwn(metric, 'field') &&
        fieldFor(context, metric.field).field.type === 'number')
        expression = `SUM(${fieldFor(context, metric.field).column})`;
    else throw new PrototypeError(400, 'Unsupported summary metric');
    const group = body.group_by === undefined || body.group_by === null ? null
        : fieldFor(context, body.group_by).column;
    const where = compileWhere(context, body.filters);
    const sql = group
        ? `SELECT ${group} AS grouping, ${expression} AS value FROM ${context.table} ${where.sql}
            GROUP BY ${group} ORDER BY grouping LIMIT 101`
        : `SELECT NULL AS grouping, ${expression} AS value FROM ${context.table} ${where.sql}`;
    const rows = context.db.prepare(sql).all(...where.values);
    if (rows.length > 100) throw new PrototypeError(413, 'Too many summary groups');
    const groupField = group ? fieldFor(context, body.group_by).field : null;
    return { items: rows.map(row => ({ group: groupField?.type === 'toggle' && row.grouping !== null
        ? Boolean(row.grouping) : row.grouping, value: row.value ?? 0 })), metric: metric.op };
}

function csvCell(value) {
    let text = value === null || value === undefined ? '' : String(value);
    if (/^\s*[=+\-@]/.test(text)) text = `'${text}`;
    return `"${text.replaceAll('"', '""')}"`;
}

export function exportCsv(context, body) {
    if (!objectKeys(body, ['fields', 'filters', 'sort'], ['fields']) ||
        !Array.isArray(body.fields) || !body.fields.length || body.fields.length > 20 ||
        new Set(body.fields).size !== body.fields.length)
        throw new PrototypeError(400, 'Invalid export fields');
    const selected = body.fields.map(code => code === 'id'
        ? { code, label: 'ID' }
        : { code, label: fieldFor(context, code).field.label });
    const where = compileWhere(context, body.filters);
    const sort = compileSort(context, body.sort);
    const rows = context.db.prepare(`SELECT * FROM ${context.table} ${where.sql} ORDER BY ${sort} LIMIT 2001`)
        .all(...where.values);
    if (rows.length > 2000) throw new PrototypeError(413, 'Export exceeds 2000 rows');
    const lines = [selected.map(field => csvCell(field.label)).join(',')];
    for (const row of rows) {
        const record = toRecord(context, row);
        lines.push(selected.map(field => csvCell(record[field.code])).join(','));
    }
    return { csv: `${lines.join('\r\n')}\r\n`, row_count: rows.length };
}
