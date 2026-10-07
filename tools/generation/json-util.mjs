import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';

export function sha256Hex(data) {
    return createHash('sha256').update(data).digest('hex');
}

export function fileSha256(filePath) {
    return sha256Hex(readFileSync(filePath));
}

export function readJsonFile(filePath) {
    return JSON.parse(readFileSync(filePath, 'utf8'));
}

/**
 * 鍵依字碼排序的 JSON 字串（同內容不同鍵序得到相同結果），供 digest 使用。
 * 只接受 JSON 值；呼叫端須先確認深度有上限（驗證第 1 層負責）。
 */
export function canonicalJson(value) {
    if (value === null || typeof value === 'boolean' || typeof value === 'string') {
        return JSON.stringify(value);
    }
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) throw new TypeError('non-finite number');
        return JSON.stringify(value);
    }
    if (Array.isArray(value)) {
        return `[${value.map(canonicalJson).join(',')}]`;
    }
    if (typeof value === 'object') {
        const keys = Object.keys(value).sort(compareCodeUnits);
        return `{${keys.map(key => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`;
    }
    throw new TypeError(`non-JSON value: ${typeof value}`);
}

/** 與平台語系無關的排序（不用 localeCompare） */
export function compareCodeUnits(a, b) {
    if (a < b) return -1;
    if (a > b) return 1;
    return 0;
}

export function prettyJson(value) {
    return `${JSON.stringify(value, null, 2)}\n`;
}

export function isPlainObject(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const proto = Object.getPrototypeOf(value);
    return proto === Object.prototype || proto === null;
}

export function hasOwn(target, key) {
    return Object.prototype.hasOwnProperty.call(target, key);
}
