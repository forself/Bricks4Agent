'use strict';

const { MAX_ARGUMENT_DEPTH, exceedsJsonDepth } = require('./tool-arguments');

const MAX_RESPONSE_LENGTH = 4096;
const RESPONSE_CODES = Object.freeze({
    final: new Set(['completed']),
    refusal: new Set(['policy_denied', 'unsupported_request', 'invalid_request']),
    failure: new Set(['generation_failed', 'tool_failed', 'limit_reached', 'invalid_response']),
});

/** Parse the only permitted governed non-tool response. Arbitrary prose and extra fields are rejected. */
function parseGovernedResponse(content) {
    if (typeof content !== 'string' || content.length === 0 || content.length > MAX_RESPONSE_LENGTH) {
        return { ok: false, code: 'invalid_response' };
    }

    let value;
    try {
        value = JSON.parse(content);
    } catch (_) {
        return { ok: false, code: 'invalid_response' };
    }

    if (!isPlainObject(value) || exceedsJsonDepth(value, MAX_ARGUMENT_DEPTH)) {
        return { ok: false, code: 'invalid_response' };
    }
    if (!hasUniqueStringMembers(content)) return { ok: false, code: 'invalid_response' };
    const keys = Object.keys(value);
    if (keys.length !== 2 || !keys.includes('type') || !keys.includes('code')) {
        return { ok: false, code: 'invalid_response' };
    }
    if (typeof value.type !== 'string' || typeof value.code !== 'string'
        || !Object.prototype.hasOwnProperty.call(RESPONSE_CODES, value.type)
        || !RESPONSE_CODES[value.type].has(value.code)) {
        return { ok: false, code: 'invalid_response' };
    }
    return { ok: true, response: { type: value.type, code: value.code } };
}

/** JSON.parse accepts duplicate object members; reject them in this flat, string-only contract. */
function hasUniqueStringMembers(text) {
    let index = 0;
    const skipSpace = () => { while (/\s/.test(text[index] || '') && index < text.length) index++; };
    const readString = () => {
        if (text[index] !== '"') return null;
        const start = index++;
        let escaped = false;
        while (index < text.length) {
            const char = text[index++];
            if (escaped) escaped = false;
            else if (char === '\\') escaped = true;
            else if (char === '"') {
                try { return JSON.parse(text.slice(start, index)); } catch (_) { return null; }
            }
        }
        return null;
    };
    skipSpace();
    if (text[index++] !== '{') return false;
    const keys = new Set();
    skipSpace();
    while (index < text.length && text[index] !== '}') {
        const key = readString();
        if (key === null || keys.has(key)) return false;
        keys.add(key);
        skipSpace();
        if (text[index++] !== ':') return false;
        skipSpace();
        if (readString() === null) return false;
        skipSpace();
        if (text[index] === ',') {
            index++;
            skipSpace();
            continue;
        }
        break;
    }
    if (text[index++] !== '}') return false;
    skipSpace();
    return index === text.length;
}

function serializeGovernedResponse(type, code) {
    if (!Object.prototype.hasOwnProperty.call(RESPONSE_CODES, type) || !RESPONSE_CODES[type].has(code)) {
        throw new TypeError('Invalid governed response type/code pair');
    }
    return JSON.stringify({ type, code });
}

function isPlainObject(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

module.exports = { MAX_RESPONSE_LENGTH, RESPONSE_CODES, parseGovernedResponse, serializeGovernedResponse };
