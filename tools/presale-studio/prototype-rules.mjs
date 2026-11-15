// Shared, deterministic validation for the browser test store and SQLite backend.
export const MAX_ROWS = 500;
const CODE = /^[EFR]_[A-F0-9]{12}$/;

export class PrototypeError extends Error {
    constructor(status, message) { super(message); this.status = status; }
}

export function scopeAllowed(role, subject, action, scope) {
    if (!role?.actions.includes(action)) return false;
    const vertical = role.vertical === 'all' ||
        ((role.vertical === 'self' || role.vertical === 'self_descendants') && scope.owner === subject.owner) ||
        ((role.vertical === 'descendants' || role.vertical === 'self_descendants') &&
            subject.descendants.includes(scope.owner));
    const unit = scope.unit === subject.unit;
    const func = scope.function === subject.function;
    const horizontal = role.horizontal === 'all' ||
        (role.horizontal === 'same_unit' && unit) ||
        (role.horizontal === 'same_function' && func) ||
        (role.horizontal === 'unit_or_function' && (unit || func)) ||
        (role.horizontal === 'unit_and_function' && unit && func);
    return vertical && horizontal;
}

export function validateFields(resource, body, create) {
    if (!body || typeof body !== 'object' || Array.isArray(body)) throw new PrototypeError(400, 'Invalid record');
    const result = {};
    for (const [key, value] of Object.entries(body)) {
        if (!CODE.test(key)) throw new PrototypeError(400, 'Unknown field');
        const field = resource.fields.find(item => item.code === key);
        if (!field) throw new PrototypeError(400, 'Unknown field');
        if (value !== null && ((field.type === 'number' && (typeof value !== 'number' || !Number.isFinite(value))) ||
            (field.type === 'toggle' && typeof value !== 'boolean') ||
            (!['number', 'toggle'].includes(field.type) && (typeof value !== 'string' || value.length > 100))))
            throw new PrototypeError(400, 'Field type mismatch');
        if (field.required && (value === null || value === '')) throw new PrototypeError(400, `${field.label} is required`);
        result[key] = value;
    }
    if (create && resource.fields.some(field => field.required && !Object.hasOwn(result, field.code)))
        throw new PrototypeError(400, 'Required field missing');
    return result;
}

export function parseFilters(resource, query) {
    const filters = [...query.entries()].filter(([key, value]) => !['page', 'pageSize'].includes(key) && value !== '');
    if (filters.some(([key]) => !resource.fields.some(field => field.code === key)))
        throw new PrototypeError(400, 'Unknown filter');
    const parsedPage = Number.parseInt(query.get('page') || '1', 10);
    const parsedSize = Number.parseInt(query.get('pageSize') || '20', 10);
    const page = Number.isSafeInteger(parsedPage) ? Math.min(1_000_000, Math.max(1, parsedPage)) : 1;
    const pageSize = Number.isSafeInteger(parsedSize) ? Math.min(100, Math.max(1, parsedSize)) : 20;
    return { filters, page, pageSize };
}
