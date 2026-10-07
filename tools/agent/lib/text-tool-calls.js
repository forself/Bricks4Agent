'use strict';

/**
 * 模型把工具呼叫寫在回覆內文時的退回解析（只用在受治理的生成任務，見 agent-loop）。
 *
 * 有些模型（特別是小型的本機模型）不論 native 或 ReAct 模式，都把呼叫寫成 JSON 文字，而不是工具呼叫。
 * 這裡依序從內文找出：```json（或不標語言的 ```）區塊，缺少收尾 fence 也可以；<tool_call>、<tools>、
 * <function_call> 包裝；最後是整段或內文中的裸 JSON。只取同時帶字串 name 與物件 arguments 的物件
 * （也接受這種物件的陣列，以及 { tool_calls: [...] } 或 { function: {...} } 的包裝）。
 * arguments 的巢狀深度超過 MAX_ARGUMENT_DEPTH（與 broker 的 System.Text.Json 預設一致）時不當成呼叫：
 * JSON.parse 不受深度影響，但之後印出或序列化參數時會耗盡呼叫堆疊。
 * 名稱是否已授予由呼叫端判斷；這裡不執行任何東西。
 */

const { MAX_ARGUMENT_DEPTH, exceedsJsonDepth } = require('./tool-arguments');

const MAX_TEXT_LENGTH = 256 * 1024;
const MAX_CALLS = 8;
const FENCE_PATTERN = /```[ \t]*([A-Za-z0-9_-]*)[ \t]*\r?\n?([\s\S]*?)(?:```|$)/g;
const WRAPPER_PATTERN = /<(tool_call|tools|function_call)>([\s\S]*?)(?:<\/\1>|$)/g;

function isPlainObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** 一段文字裡的 JSON：整段、去掉前後文字的 {…} 或 […]，再試一次去掉結尾多餘的逗號。 */
function parseJsonFragment(text) {
    const trimmed = String(text || '').trim();
    if (trimmed === '') return undefined;
    const attempts = [trimmed];
    const start = trimmed.search(/[[{]/);
    const end = Math.max(trimmed.lastIndexOf('}'), trimmed.lastIndexOf(']'));
    if (start >= 0 && end > start) {
        attempts.push(trimmed.slice(start, end + 1));
    }
    for (const attempt of [...attempts, ...attempts.map((value) => value.replace(/,\s*([}\]])/g, '$1'))]) {
        try {
            return JSON.parse(attempt);
        } catch (_) {
            // 試下一種
        }
    }
    return undefined;
}

/** 解析出的 JSON 值中的呼叫：{ name, arguments }、它的陣列，或 tool_calls／function 包裝。 */
function collectCalls(value, calls, depth = 0) {
    if (calls.length >= MAX_CALLS || depth > 3) return;
    if (Array.isArray(value)) {
        for (const item of value) collectCalls(item, calls, depth + 1);
        return;
    }
    if (!isPlainObject(value)) return;
    if (typeof value.name === 'string' && value.name.trim() !== '' && isPlainObject(value.arguments)) {
        if (!exceedsJsonDepth(value.arguments, MAX_ARGUMENT_DEPTH)) {
            calls.push({ name: value.name.trim(), arguments: value.arguments });
        }
        return;
    }
    if (Array.isArray(value.tool_calls)) {
        collectCalls(value.tool_calls, calls, depth + 1);
        return;
    }
    if (isPlainObject(value.function)) {
        collectCalls(value.function, calls, depth + 1);
    }
}

function callsFromFragments(fragments) {
    const calls = [];
    for (const fragment of fragments) {
        const parsed = parseJsonFragment(fragment);
        if (parsed !== undefined) collectCalls(parsed, calls);
        if (calls.length >= MAX_CALLS) break;
    }
    return calls;
}

/**
 * 從回覆內文找出寫成文字的工具呼叫。
 * @param {string} text - 模型回覆的內文
 * @returns {{ name: string, arguments: object }[]}
 */
function parseTextToolCalls(text) {
    if (typeof text !== 'string' || text.trim() === '' || text.length > MAX_TEXT_LENGTH) return [];

    const fences = [];
    for (const match of text.matchAll(FENCE_PATTERN)) {
        const language = match[1].toLowerCase();
        if (language === '' || language === 'json' || language === 'jsonc' || language === 'javascript') {
            fences.push(match[2]);
        }
    }
    const fromFences = callsFromFragments(fences);
    if (fromFences.length > 0) return fromFences;

    const wrapped = [...text.matchAll(WRAPPER_PATTERN)].map((match) => match[2]);
    const fromWrappers = callsFromFragments(wrapped);
    if (fromWrappers.length > 0) return fromWrappers;

    return callsFromFragments([text]);
}

module.exports = { parseTextToolCalls };
