'use strict';

/**
 * XML ReAct 回退解析器
 * 從模型文字輸出中解析 <tool_call> 區塊
 */

const TOOL_CALL_REGEX = /<tool_call>\s*([\s\S]*?)\s*<\/tool_call>/g;

/**
 * 從文字中解析工具呼叫
 * @param {string} text - 模型回應文字
 * @returns {Object[]} 解析出的工具呼叫陣列 [{name, arguments}]
 */
function parseToolCalls(text) {
    if (!text || !text.includes('<tool_call>')) return [];

    const calls = [];
    let match;
    const regex = new RegExp(TOOL_CALL_REGEX.source, 'g');

    while ((match = regex.exec(text)) !== null) {
        const inner = match[1].trim();
        try {
            const parsed = JSON.parse(inner);
            if (parsed.name) {
                calls.push({
                    function: {
                        name: parsed.name,
                        arguments: parsed.arguments || {},
                    },
                });
            }
        } catch (e) {
            // JSON 解析失敗，嘗試修復常見問題
            try {
                // 嘗試移除尾部逗號
                const fixed = inner.replace(/,\s*([}\]])/g, '$1');
                const parsed = JSON.parse(fixed);
                if (parsed.name) {
                    calls.push({
                        function: {
                            name: parsed.name,
                            arguments: parsed.arguments || {},
                        },
                    });
                }
            } catch (_) {
                // 真的解析不了，跳過
            }
        }
    }

    return calls;
}

/**
 * 檢查文字中是否有工具呼叫
 * @param {string} text
 * @returns {boolean}
 */
function hasToolCalls(text) {
    return text && text.includes('<tool_call>') && text.includes('</tool_call>');
}

/**
 * 去除回應文字中的工具呼叫區塊，只留純文字
 * @param {string} text
 * @returns {string}
 */
function stripToolCalls(text) {
    if (!text) return '';
    return text.replace(/<tool_call>[\s\S]*?<\/tool_call>/g, '').trim();
}

/**
 * 格式化工具結果為 <tool_result> 區塊
 * @param {string} toolName
 * @param {string} result
 * @returns {string}
 */
function formatToolResult(toolName, result) {
    return `<tool_result>\n{"tool": "${toolName}", "result": ${JSON.stringify(result)}}\n</tool_result>`;
}

module.exports = {
    parseToolCalls,
    hasToolCalls,
    stripToolCalls,
    formatToolResult,
};
