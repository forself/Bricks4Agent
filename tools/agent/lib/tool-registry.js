'use strict';

const { readFile, writeFile, searchFiles, searchContent } = require('./tools/file-tools');
const { listDirectory } = require('./tools/dir-tools');
const { runCommand } = require('./tools/command-tool');

// ─── 工具定義（Ollama JSON Schema 格式） ───

const TOOL_DEFINITIONS = [
    {
        type: 'function',
        function: {
            name: 'read_file',
            description: '讀取檔案內容。回傳含行號的文字內容。',
            parameters: {
                type: 'object',
                properties: {
                    path: { type: 'string', description: '檔案路徑（相對或絕對）' },
                    offset: { type: 'number', description: '起始行號（0 起算），預設 0' },
                    limit: { type: 'number', description: '最多讀取行數，預設 500' },
                },
                required: ['path'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'write_file',
            description: '寫入檔案。若檔案不存在則自動建立（含目錄）。',
            parameters: {
                type: 'object',
                properties: {
                    path: { type: 'string', description: '檔案路徑' },
                    content: { type: 'string', description: '要寫入的內容' },
                    mode: { type: 'string', enum: ['rewrite', 'append'], description: '寫入模式：rewrite（覆寫，預設）或 append（追加）' },
                },
                required: ['path', 'content'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'list_directory',
            description: '列出目錄內容，含 [FILE] 和 [DIR] 前綴。',
            parameters: {
                type: 'object',
                properties: {
                    path: { type: 'string', description: '目錄路徑，預設為專案根目錄' },
                    depth: { type: 'number', description: '最大遍歷深度，預設 2' },
                },
                required: [],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'run_command',
            description: '在專案目錄中執行 shell 指令並回傳輸出。',
            parameters: {
                type: 'object',
                properties: {
                    command: { type: 'string', description: '要執行的指令' },
                    cwd: { type: 'string', description: '工作目錄（預設為專案根目錄）' },
                },
                required: ['command'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'search_files',
            description: '依檔名搜尋檔案（支援 glob 模式如 *.js、test-*.ts）。',
            parameters: {
                type: 'object',
                properties: {
                    pattern: { type: 'string', description: '檔名 glob 模式' },
                    directory: { type: 'string', description: '搜尋目錄，預設為專案根目錄' },
                },
                required: ['pattern'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'search_content',
            description: '在檔案內容中搜尋文字（正規表達式）。回傳匹配的行及行號。',
            parameters: {
                type: 'object',
                properties: {
                    pattern: { type: 'string', description: '搜尋文字或正規表達式' },
                    directory: { type: 'string', description: '搜尋目錄，預設為專案根目錄' },
                    file_pattern: { type: 'string', description: '限定搜尋的檔案模式（如 *.js）' },
                },
                required: ['pattern'],
            },
        },
    },
];

const TOOL_TO_CAPABILITY = {
    read_file: 'file.read',
    write_file: 'file.write',
    list_directory: 'file.list',
    search_files: 'file.search_name',
    search_content: 'file.search_content',
    run_command: 'command.execute',
};

// ─── 工具分派 ───

const TOOL_HANDLERS = {
    read_file: readFile,
    write_file: writeFile,
    list_directory: listDirectory,
    run_command: runCommand,
    search_files: searchFiles,
    search_content: searchContent,
};

/**
 * 執行工具呼叫
 * @param {string} name - 工具名稱
 * @param {Object} args - 工具參數
 * @param {Object} context - 執行上下文 { projectRoot, noConfirm, verbose }
 * @returns {Promise<string>} 執行結果
 */
async function executeTool(name, args, context) {
    const handler = TOOL_HANDLERS[name];
    if (!handler) {
        return `❌ 未知工具: ${name}。可用工具: ${Object.keys(TOOL_HANDLERS).join(', ')}`;
    }

    try {
        return await handler(args || {}, context);
    } catch (e) {
        return `❌ 工具執行錯誤 [${name}]: ${e.message}`;
    }
}

function getToolDefinitions(options = {}) {
    const { names = null, capabilityIds = null } = options;

    if (!Array.isArray(names) && !Array.isArray(capabilityIds)) {
        return TOOL_DEFINITIONS.slice();
    }

    const allowedNames = Array.isArray(names)
        ? new Set(names)
        : new Set(getToolNamesForCapabilities(capabilityIds));

    return TOOL_DEFINITIONS.filter((def) => allowedNames.has(def.function.name));
}

function getToolNamesForCapabilities(capabilityIds = []) {
    const allowedCapabilities = new Set((capabilityIds || []).filter(Boolean));
    const seen = new Set();

    return TOOL_DEFINITIONS
        .map((def) => def.function.name)
        .filter((toolName) => {
            const capabilityId = TOOL_TO_CAPABILITY[toolName];
            if (!capabilityId || !allowedCapabilities.has(capabilityId) || seen.has(toolName)) {
                return false;
            }
            seen.add(toolName);
            return true;
        });
}

function capabilityIdForTool(name) {
    return TOOL_TO_CAPABILITY[name] || null;
}

/**
 * 取得工具的純文字描述（用於 ReAct 提示詞）
 * @returns {string}
 */
function getToolDescriptions(options = {}) {
    const definitions = getToolDefinitions(options);
    if (definitions.length === 0) {
        return 'No broker-approved tool capabilities are currently available.';
    }

    return definitions.map((def, i) => {
        const fn = def.function;
        const params = fn.parameters.properties;
        const required = fn.parameters.required || [];
        const paramList = Object.entries(params).map(([name, schema]) => {
            const req = required.includes(name) ? '' : '?';
            return `${name}${req}: ${schema.type} — ${schema.description}`;
        }).join('\n      ');
        return `${i + 1}. ${fn.name}\n   ${fn.description}\n   參數:\n      ${paramList}`;
    }).join('\n\n');
}

module.exports = {
    TOOL_DEFINITIONS,
    TOOL_TO_CAPABILITY,
    capabilityIdForTool,
    executeTool,
    getToolDefinitions,
    getToolDescriptions,
    getToolNamesForCapabilities,
};
