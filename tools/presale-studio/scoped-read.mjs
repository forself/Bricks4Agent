import { PrototypeError } from './prototype-rules.mjs';

export function objectKeys(value, allowed, required = []) {
    return value && typeof value === 'object' && !Array.isArray(value) &&
        Object.keys(value).every(key => allowed.includes(key)) &&
        required.every(key => Object.hasOwn(value, key));
}

export function fieldFor(context, code) {
    const index = context.mapping.fields.findIndex(field => field.field_code === code);
    if (index < 0) throw new PrototypeError(400, 'Unknown field');
    return { field: context.resource.fields[index], column: `"${context.mapping.fields[index].column_code}"` };
}

export function compileWhere(context, filters = []) {
    if (!Array.isArray(filters) || filters.length > 12)
        throw new PrototypeError(400, 'Invalid filter list');
    const clauses = [`(${context.where.sql})`], values = [...context.where.values];
    for (const filter of filters) {
        if (!objectKeys(filter, ['field', 'op', 'value'], ['field', 'op', 'value']) ||
            Object.keys(filter).length !== 3)
            throw new PrototypeError(400, 'Invalid filter');
        const { field, column } = fieldFor(context, filter.field);
        const value = filter.value;
        if (filter.op === 'eq') {
            if (value === null) { clauses.push(`${column} IS NULL`); continue; }
            if (!typeMatches(field, value)) throw new PrototypeError(400, 'Filter type mismatch');
            clauses.push(`${column} = ?`);
            values.push(field.type === 'toggle' ? Number(value) : value);
        } else if (filter.op === 'contains') {
            if (['number', 'toggle'].includes(field.type) || typeof value !== 'string' || value.length > 100)
                throw new PrototypeError(400, 'Contains needs a text field');
            clauses.push(`LOWER(${column}) LIKE LOWER(?) ESCAPE '\\'`);
            values.push(`%${value.replace(/[\\%_]/g, character => `\\${character}`)}%`);
        } else if (filter.op === 'gte' || filter.op === 'lte') {
            if (!['number', 'date'].includes(field.type) || !typeMatches(field, value))
                throw new PrototypeError(400, 'Range needs a number or date');
            clauses.push(`${column} ${filter.op === 'gte' ? '>=' : '<='} ?`);
            values.push(value);
        } else throw new PrototypeError(400, 'Unsupported filter operator');
    }
    return { sql: `WHERE ${clauses.join(' AND ')}`, values };
}

function typeMatches(field, value) {
    if (field.type === 'number') return typeof value === 'number' && Number.isFinite(value);
    if (field.type === 'toggle') return typeof value === 'boolean';
    if (typeof value !== 'string' || value.length > 100) return false;
    if (field.type !== 'date') return true;
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
    const date = new Date(`${value}T00:00:00Z`);
    return !Number.isNaN(date.getTime()) && date.toISOString().slice(0, 10) === value;
}

export function compileSort(context, sort) {
    if (sort === undefined) return 'id ASC';
    if (!objectKeys(sort, ['field', 'direction'], ['field', 'direction']) ||
        Object.keys(sort).length !== 2 || !['asc', 'desc'].includes(sort.direction))
        throw new PrototypeError(400, 'Invalid sort');
    const column = sort.field === 'id' ? 'id' : fieldFor(context, sort.field).column;
    return `${column} ${sort.direction.toUpperCase()}, id ASC`;
}

export function toRecord(context, row) {
    return { id: row.id, ...Object.fromEntries(context.mapping.fields.map((field, index) => {
        const value = row[field.column_code];
        return [field.field_code,
            context.resource.fields[index].type === 'toggle' && value !== null ? Boolean(value) : value];
    })) };
}
