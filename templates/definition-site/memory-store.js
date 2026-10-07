/**
 * 瀏覽器內的記憶體資料來源，介面對齊 DefinitionRuntimePage 使用的 api（get/post/put/delete）。
 * 每個定義中出現的 api 基底路徑是一個集合：
 *   GET    {base}?page=&pageSize=  → { items, total }
 *   GET    {base}/{id}             → 單筆
 *   POST   {base}                  → 新增（id 由此產生）
 *   PUT    {base}/{id}             → 更新
 *   DELETE {base}/{id}             → 刪除
 * 資料只存在目前分頁的記憶體中，重新整理即清空；原型不連任何後端。
 */

export const MAX_RECORDS_PER_COLLECTION = 500;
const MAX_PAGE_SIZE = 100;
const PAGING_KEYS = new Set(['page', 'pageSize']);

export class MemoryApiError extends Error {
    constructor(status, message) {
        super(message);
        this.name = 'MemoryApiError';
        this.status = status;
    }
}

function cloneValue(value) {
    if (typeof structuredClone === 'function') {
        try {
            return structuredClone(value);
        } catch {
            // 含函式等無法複製的值時退回 JSON 複製
        }
    }
    return value === undefined ? undefined : JSON.parse(JSON.stringify(value));
}

function normalizePath(path) {
    const trimmed = String(path || '').replace(/\/+$/, '');
    return trimmed === '' ? '/' : trimmed;
}

function stripId(body) {
    const result = {};
    if (body && typeof body === 'object' && !Array.isArray(body)) {
        for (const [key, value] of Object.entries(body)) {
            if (key === 'id' || key === '__proto__' || key === 'constructor' || key === 'prototype') continue;
            result[key] = value;
        }
    }
    return result;
}

function matchesFilter(value, expected) {
    if (value === null || value === undefined) return false;
    const text = typeof value === 'object' ? JSON.stringify(value) : String(value);
    return text.toLowerCase().includes(String(expected).toLowerCase());
}

export class MemoryStore {
    /**
     * @param {{ collections?: string[] }} options - 已知的 api 基底路徑
     */
    constructor({ collections = [] } = {}) {
        this._collections = new Map();
        for (const base of collections) {
            if (typeof base === 'string' && base !== '') {
                this._collections.set(normalizePath(base), { nextId: 1, rows: new Map() });
            }
        }
    }

    hasCollection(base) {
        return this._collections.has(normalizePath(base));
    }

    count(base) {
        return this._collections.get(normalizePath(base))?.rows.size ?? 0;
    }

    _resolve(url) {
        const [rawPath, rawQuery = ''] = String(url || '').split('?');
        const path = normalizePath(rawPath);
        const query = new URLSearchParams(rawQuery);
        if (this._collections.has(path)) {
            return { base: path, id: null, query };
        }
        const slash = path.lastIndexOf('/');
        if (slash > 0) {
            const parent = path.slice(0, slash);
            let id;
            try {
                id = decodeURIComponent(path.slice(slash + 1));
            } catch {
                throw new MemoryApiError(400, 'Invalid record id');
            }
            if (this._collections.has(parent) && id !== '') {
                return { base: parent, id, query };
            }
        }
        throw new MemoryApiError(404, 'Unknown endpoint');
    }

    _row(base, id) {
        const row = this._collections.get(base).rows.get(String(id));
        if (!row) throw new MemoryApiError(404, 'Record not found');
        return row;
    }

    async get(url) {
        const { base, id, query } = this._resolve(url);
        if (id !== null) return cloneValue(this._row(base, id));

        const rows = [...this._collections.get(base).rows.values()];
        const filters = [...query.entries()].filter(([key, value]) => !PAGING_KEYS.has(key) && value !== '');
        const matched = filters.length === 0
            ? rows
            : rows.filter(row => filters.every(([key, value]) => matchesFilter(row[key], value)));
        const pageSize = Math.min(Math.max(Number.parseInt(query.get('pageSize') || '20', 10) || 20, 1), MAX_PAGE_SIZE);
        const page = Math.max(Number.parseInt(query.get('page') || '1', 10) || 1, 1);
        const start = (page - 1) * pageSize;
        return {
            items: matched.slice(start, start + pageSize).map(cloneValue),
            total: matched.length
        };
    }

    async post(url, body) {
        const { base, id } = this._resolve(url);
        if (id !== null) throw new MemoryApiError(405, 'Create must target the collection');
        const collection = this._collections.get(base);
        if (collection.rows.size >= MAX_RECORDS_PER_COLLECTION) {
            throw new MemoryApiError(413, 'Prototype record limit reached');
        }
        const newId = collection.nextId;
        collection.nextId += 1;
        const record = { id: newId, ...cloneValue(stripId(body)) };
        collection.rows.set(String(newId), record);
        return cloneValue(record);
    }

    async put(url, body) {
        const { base, id } = this._resolve(url);
        if (id === null) throw new MemoryApiError(405, 'Update must target a record');
        const existing = this._row(base, id);
        const record = { ...existing, ...cloneValue(stripId(body)), id: existing.id };
        this._collections.get(base).rows.set(String(existing.id), record);
        return cloneValue(record);
    }

    async delete(url) {
        const { base, id } = this._resolve(url);
        if (id === null) throw new MemoryApiError(405, 'Delete must target a record');
        const existing = this._row(base, id);
        this._collections.get(base).rows.delete(String(existing.id));
        return { ok: true };
    }
}
