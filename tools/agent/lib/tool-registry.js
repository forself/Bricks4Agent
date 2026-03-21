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

    // ── LINE 通訊工具 ──

    {
        type: 'function',
        function: {
            name: 'send_line_message',
            description: '透過 LINE 發送文字訊息給指定使用者。',
            parameters: {
                type: 'object',
                properties: {
                    to: { type: 'string', description: '接收者 LINE userId（省略則發給預設接收者）' },
                    text: { type: 'string', description: '訊息內容（最多 5000 字元）' },
                },
                required: ['text'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'send_line_notification',
            description: '透過 LINE 發送結構化通知（含標題、內容、等級）。',
            parameters: {
                type: 'object',
                properties: {
                    to: { type: 'string', description: '接收者 LINE userId（省略則發給預設接收者）' },
                    title: { type: 'string', description: '通知標題' },
                    body: { type: 'string', description: '通知內容' },
                    level: { type: 'string', enum: ['info', 'warning', 'error', 'success'], description: '通知等級，預設 info' },
                    actions: { type: 'array', items: { type: 'string' }, description: '可選的回覆選項，如 ["approve", "deny"]' },
                },
                required: ['title', 'body'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'send_line_audio',
            description: '透過 LINE 發送語音訊息。提供文字（TTS 轉語音）或音檔 URL。',
            parameters: {
                type: 'object',
                properties: {
                    to: { type: 'string', description: '接收者 LINE userId（省略則發給預設接收者）' },
                    text: { type: 'string', description: 'TTS 文字（與 audio_url 二擇一）' },
                    audio_url: { type: 'string', description: '音檔 URL（與 text 二擇一）' },
                    duration_ms: { type: 'number', description: '音訊時長毫秒，預設 5000' },
                },
                required: [],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'read_line_messages',
            description: '讀取尚未消費的 LINE 入站訊息。',
            parameters: {
                type: 'object',
                properties: {
                    limit: { type: 'number', description: '最多讀取幾則，預設 10' },
                    consume: { type: 'boolean', description: '讀取後是否標記為已消費，預設 true' },
                },
                required: [],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'request_line_approval',
            description: '透過 LINE 發送審批請求並等待人工回覆（approve/deny）。',
            parameters: {
                type: 'object',
                properties: {
                    to: { type: 'string', description: '審批者 LINE userId（省略則發給預設接收者）' },
                    description: { type: 'string', description: '審批內容描述' },
                    request_id: { type: 'string', description: '關聯的請求 ID' },
                    timeout_sec: { type: 'number', description: '等待超時秒數，預設 300' },
                },
                required: ['description'],
            },
        },
    },

    // ── 對話日誌工具（Layer 1：自動機械式紀錄） ──

    {
        type: 'function',
        function: {
            name: 'conv_log_append',
            description: '記錄一則對話訊息到日誌（自動調用，不需 LLM 判斷）。',
            parameters: {
                type: 'object',
                properties: {
                    user_id: { type: 'string', description: '使用者 ID' },
                    role: { type: 'string', enum: ['user', 'assistant'], description: '訊息角色' },
                    content: { type: 'string', description: '訊息內容' },
                    metadata: { type: 'string', description: '額外中繼資料（JSON）' },
                },
                required: ['user_id', 'role', 'content'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'conv_log_read',
            description: '讀取某使用者的對話歷史日誌。',
            parameters: {
                type: 'object',
                properties: {
                    user_id: { type: 'string', description: '使用者 ID' },
                    limit: { type: 'number', description: '最多回傳幾則，預設 50' },
                    before: { type: 'string', description: 'ISO 時間戳，只回傳此時間之前的訊息' },
                },
                required: ['user_id'],
            },
        },
    },

    // ── 智慧記憶工具（Layer 2：Agent 判斷式持久化） ──

    {
        type: 'function',
        function: {
            name: 'memory_store',
            description: '儲存資訊到持久化記憶。用於保存重要事實、使用者偏好、對話摘要等。跨對話可用。',
            parameters: {
                type: 'object',
                properties: {
                    key: { type: 'string', description: '記憶鍵名（如 "user_preference"、"project_summary"）' },
                    value: { type: 'string', description: '要記憶的內容' },
                    content_type: { type: 'string', description: '內容類型，預設 text/plain' },
                },
                required: ['key', 'value'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'memory_retrieve',
            description: '從持久化記憶讀取。可精確查詢 key、模糊搜尋、或列出所有記憶。',
            parameters: {
                type: 'object',
                properties: {
                    key: { type: 'string', description: '精確查詢的鍵名' },
                    search: { type: 'string', description: '模糊搜尋（搜尋 key 和 value）' },
                    limit: { type: 'number', description: '最多回傳幾筆，預設 20' },
                },
                required: [],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'memory_delete',
            description: '刪除持久化記憶條目。',
            parameters: {
                type: 'object',
                properties: {
                    key: { type: 'string', description: '要刪除的鍵名' },
                },
                required: ['key'],
            },
        },
    },

    // ── 搜尋工具（BM25 + 向量 + RAG） ──

    {
        type: 'function',
        function: {
            name: 'memory_fulltext_search',
            description: 'BM25 全文檢索。搜尋智慧記憶和/或對話日誌。適合關鍵字精確匹配。',
            parameters: {
                type: 'object',
                properties: {
                    query: { type: 'string', description: '搜尋文字' },
                    scope: { type: 'string', enum: ['memory', 'convlog', 'all'], description: '搜尋範圍，預設 memory' },
                    limit: { type: 'number', description: '最多回傳幾筆，預設 10' },
                },
                required: ['query'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'memory_semantic_search',
            description: '向量語意搜尋。透過嵌入向量找到語意相近的記憶。適合模糊概念搜尋。',
            parameters: {
                type: 'object',
                properties: {
                    query: { type: 'string', description: '語意查詢文字' },
                    limit: { type: 'number', description: '最多回傳幾筆，預設 5' },
                    threshold: { type: 'number', description: '最低相似度門檻（0~1），預設 0.3' },
                },
                required: ['query'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'rag_retrieve',
            description: 'RAG 混合檢索（BM25 + 向量語意，Reciprocal Rank Fusion）。支援查詢改寫、標籤過濾、LLM 重排序。',
            parameters: {
                type: 'object',
                properties: {
                    query: { type: 'string', description: '查詢文字' },
                    mode: { type: 'string', enum: ['hybrid', 'semantic', 'fulltext'], description: '檢索模式，預設 hybrid' },
                    limit: { type: 'number', description: '最多回傳幾筆，預設 5' },
                    threshold: { type: 'number', description: '向量相似度門檻，預設 0.2' },
                    include_convlog: { type: 'boolean', description: '是否包含對話日誌，預設 false' },
                    tags: { type: 'array', items: { type: 'string' }, description: '標籤過濾（只搜尋含指定標籤的文件）' },
                    rewrite: { type: 'boolean', description: '是否啟用 LLM 查詢改寫，預設 true' },
                    rerank: { type: 'boolean', description: '是否啟用 LLM 重排序，預設 true' },
                },
                required: ['query'],
            },
        },
    },

    // ── RAG 匯入工具 ──

    {
        type: 'function',
        function: {
            name: 'rag_import',
            description: 'RAG 資料匯入（CSV 或 JSON 格式）。自動分塊長文件、建立 FTS5 全文索引 + 向量嵌入。',
            parameters: {
                type: 'object',
                properties: {
                    format: { type: 'string', enum: ['json', 'csv'], description: '匯入格式：json 或 csv' },
                    tag: { type: 'string', description: '分類標籤（作為 source_key 前綴，例如 "產品手冊"）' },
                    data: {
                        type: 'string',
                        description: 'JSON 格式：[{"key":"標題","content":"內容","tag":"可選標籤","tags":["標籤1","標籤2"]}]。CSV 格式：首列為標題列（key,content,tag），tag 欄位可選。長文件（>800字）自動分塊。',
                    },
                    task_id: { type: 'string', description: '任務 ID，預設 global' },
                },
                required: ['format', 'tag', 'data'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'rag_import_web',
            description: 'RAG 網路匯入 — 根據搜尋關鍵字或指定 URL 從網路抓取內容，自動分段後寫入 RAG 資料庫。',
            parameters: {
                type: 'object',
                properties: {
                    query: { type: 'string', description: '搜尋關鍵字（用於搜尋引擎）' },
                    tag: { type: 'string', description: '分類標籤，預設使用 query' },
                    urls: { type: 'array', items: { type: 'string' }, description: '直接指定 URL 列表（可與 query 併用或單獨使用）' },
                    max_pages: { type: 'number', description: '最大抓取頁面數，預設 5' },
                    chunk_size: { type: 'number', description: '分段大小（字元數），預設 1000' },
                    chunk_overlap: { type: 'number', description: '分段重疊（字元數），預設 100' },
                    task_id: { type: 'string', description: '任務 ID，預設 global' },
                },
                required: ['query'],
            },
        },
    },

    // ── 網路工具 ──

    {
        type: 'function',
        function: {
            name: 'web_search',
            description: '搜尋網路。回傳連結、標題和摘要。',
            parameters: {
                type: 'object',
                properties: {
                    query: { type: 'string', description: '搜尋查詢文字' },
                    limit: { type: 'number', description: '最多回傳幾筆，預設 5' },
                },
                required: ['query'],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'web_fetch',
            description: '擷取網頁內容並轉為純文字。',
            parameters: {
                type: 'object',
                properties: {
                    url: { type: 'string', description: '網頁 URL（http/https）' },
                    max_length: { type: 'number', description: '最大回傳字數，預設 50000' },
                },
                required: ['url'],
            },
        },
    },

    // ── Agent 管理工具 ──

    {
        type: 'function',
        function: {
            name: 'list_agents',
            description: '列出所有已建立的 Agent 代理容器，包含狀態、角色、能力等資訊。',
            parameters: {
                type: 'object',
                properties: {
                    filter: { type: 'string', description: '篩選狀態（如 "Active"、"Completed"），省略則列出全部' },
                },
                required: [],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'create_agent',
            description: '建立新的 Agent 代理容器。可指定能力清單，未指定則使用預設低權限能力。',
            parameters: {
                type: 'object',
                properties: {
                    display_name: { type: 'string', description: 'Agent 顯示名稱' },
                    capability_ids: { type: 'array', items: { type: 'string' }, description: '要授予的能力 ID 清單（如 ["file.read", "line.message.send"]）' },
                    task_type: { type: 'string', description: '任務類型：analysis（分析，預設）、rag（RAG 檢索增強）、assistant（助理）、full（全能力）。設定 rag 會自動授予 memory/RAG 相關能力' },
                },
                required: [],
            },
        },
    },
    {
        type: 'function',
        function: {
            name: 'stop_agent',
            description: '停止並停用指定的 Agent 代理容器。',
            parameters: {
                type: 'object',
                properties: {
                    agent_id: { type: 'string', description: '要停止的 Agent ID' },
                },
                required: ['agent_id'],
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
    send_line_message: 'line.message.send',
    send_line_notification: 'line.notification.send',
    send_line_audio: 'line.audio.send',
    read_line_messages: 'line.message.read',
    request_line_approval: 'line.approval.request',
    conv_log_append: 'conv.log.write',
    conv_log_read: 'conv.log.read',
    memory_store: 'memory.write',
    memory_retrieve: 'memory.read',
    memory_delete: 'memory.delete',
    memory_fulltext_search: 'memory.fulltext_search',
    memory_semantic_search: 'memory.semantic_search',
    rag_retrieve: 'rag.retrieve',
    rag_import: 'rag.import',
    rag_import_web: 'rag.import_web',
    web_search: 'web.search',
    web_fetch: 'web.fetch',
    list_agents: 'agent.list',
    create_agent: 'agent.create',
    stop_agent: 'agent.stop',
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
