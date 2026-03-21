'use strict';

const { logInfo, logWarn, logError, colorize, bold } = require('./utils');

/**
 * LINE Listener — 持續輪詢 LINE 訊息並透過 LLM 回應
 *
 * 流程：
 * 1. Agent 初始化（governed mode: ECDH → session → capabilities）
 * 2. 每 N 秒呼叫 read_line_messages（透過 Broker → LINE Worker）
 * 3. 收到訊息 → 組合對話歷史 → 送 LLM
 * 4. LLM 回覆（可能包含工具呼叫 → Agent 自動執行）
 * 5. 最終文字回覆 → send_line_message（透過 Broker → LINE Worker）
 * 6. 迴圈繼續
 */
class LineListener {
    constructor(agent, options = {}) {
        this.agent = agent;
        this.pollIntervalMs = options.pollIntervalMs || 3000;
        this.maxConversationHistory = options.maxConversationHistory || 20;
        this.verbose = options.verbose || false;
        this.running = false;
        this._pollCount = 0;

        // 每個使用者的對話歷史（userId → messages[]）
        this._conversations = new Map();
    }

    async start(ct) {
        if (!this.agent.initialized) {
            await this.agent.init();
        }

        this.running = true;
        logInfo(bold('=== LINE Listener Mode ==='));
        logInfo(`Poll interval: ${this.pollIntervalMs}ms`);

        // Layer 1：從 Broker 載入對話日誌
        await this._loadConvLog();
        logInfo('Waiting for LINE messages...\n');

        while (this.running) {
            try {
                await this._pollOnce();
            } catch (e) {
                if (e.message === 'cancelled') break;
                logError(`Poll error: ${e.message}`);
            }

            // 等待下次輪詢
            await this._sleep(this.pollIntervalMs);
        }

        // Layer 1 已即時寫入，無需批次儲存
        logInfo('LINE Listener stopped.');
    }

    stop() {
        this.running = false;
    }

    async _pollOnce() {
        this._pollCount++;

        // 透過 governed executor 呼叫 read_line_messages
        const result = await this.agent.governedExecutor.executeTool(
            'read_line_messages',
            { consume: true, limit: 10 },
            { projectRoot: this.agent.projectRoot, noConfirm: true, verbose: this.verbose }
        );

        const messages = this._parseMessages(result);

        if (messages.length === 0) {
            if (this._pollCount % 20 === 0) {
                logInfo(colorize(`  ... listening (${this._pollCount} polls, ${new Date().toLocaleTimeString()})`, 'gray'));
            }
            return;
        }

        // 處理每則訊息
        for (const msg of messages) {
            const text = (msg.text || '').trim();
            if (!text) continue;

            const userId = msg.user_id || msg.userId || 'unknown';
            logInfo(colorize(`  ← [${userId.substring(0, 8)}...] ${text}`, 'cyan'));

            try {
                const response = await this._processMessage(userId, text);
                logInfo(colorize(`  → ${this._truncate(response, 150)}`, 'green'));

                // 發送回覆
                await this.agent.governedExecutor.executeTool(
                    'send_line_message',
                    { text: response },
                    { projectRoot: this.agent.projectRoot, noConfirm: true, verbose: this.verbose }
                );
            } catch (e) {
                logError(`  Process error: ${e.message}`);

                // 嘗試發送錯誤通知
                try {
                    await this.agent.governedExecutor.executeTool(
                        'send_line_message',
                        { text: `處理時發生錯誤: ${e.message}` },
                        { projectRoot: this.agent.projectRoot, noConfirm: true, verbose: this.verbose }
                    );
                } catch (_) { /* best effort */ }
            }
        }
    }

    /**
     * 處理一則使用者訊息：維護對話歷史 → 送 LLM → 回傳最終回覆
     */
    async _processMessage(userId, text) {
        // 特殊指令
        if (text === '/clear' || text === '/reset') {
            this._conversations.delete(userId);
            return '對話歷史已清除。';
        }

        if (text === '/help') {
            return [
                '可用指令：',
                '/clear - 清除對話歷史',
                '/memory - 查看已儲存的記憶',
                '/help - 顯示此說明',
                '',
                '你也可以直接輸入問題，我會透過 AI 回答。',
                '我可以讀取/搜尋檔案、搜尋網路、記憶重要資訊等。',
            ].join('\n');
        }

        if (text === '/memory') {
            try {
                const result = await this.agent.governedExecutor.executeTool(
                    'memory_retrieve', {},
                    { projectRoot: this.agent.projectRoot, noConfirm: true }
                );
                const parsed = JSON.parse(result);
                const keys = parsed.keys || [];
                if (keys.length === 0) return '目前沒有儲存的記憶。';
                return '已儲存的記憶：\n' + keys.map(k => `• ${k.key} (v${k.version})`).join('\n');
            } catch (e) {
                return `查詢記憶失敗: ${e.message}`;
            }
        }

        // 取得或從日誌載入對話歷史（首次 lazy-load）
        if (!this._conversations.has(userId)) {
            await this._loadUserConvLog(userId);
            if (!this._conversations.has(userId)) {
                this._conversations.set(userId, []);
            }
        }
        const history = this._conversations.get(userId);

        // 加入使用者訊息
        history.push({ role: 'user', content: text });

        // Layer 1：自動記錄使用者訊息到日誌
        await this._logMessage(userId, 'user', text);

        // 修剪歷史（保持最近 N 則）
        while (history.length > this.maxConversationHistory) {
            history.shift();
        }

        // 呼叫 LLM（Layer 2 的 memory_store/memory_retrieve 由 LLM 自主判斷使用）
        const response = await this._sendToLlm(history);

        // 記錄助理回覆
        history.push({ role: 'assistant', content: response });

        // Layer 1：自動記錄助理回覆到日誌
        await this._logMessage(userId, 'assistant', response);

        // 修剪歷史
        while (history.length > this.maxConversationHistory) {
            history.shift();
        }

        // LINE 訊息限制 5000 字元
        if (response.length > 4990) {
            return response.substring(0, 4990) + '\n...[截斷]';
        }

        return response;
    }

    /**
     * 將對話歷史送入 LLM（複用 agent 的 governed executor）
     */
    async _sendToLlm(userHistory) {
        // 重建 agent 的訊息歷史：system prompt + 使用者對話
        // 保留原始 system prompt（第一則訊息）
        const systemMsg = this.agent.messages[0];
        const lineContext = {
            role: 'system',
            content: [
                '你正在 LINE 即時通訊中與使用者對話。',
                '請用繁體中文回答，語氣自然友善。',
                '回覆盡量簡潔（LINE 訊息不適合太長的文字）。',
                '',
                '=== 記憶架構（雙層分離） ===',
                '',
                '【Layer 1 — 對話日誌】（系統自動處理，你不需要操心）',
                '所有對話訊息已自動逐則記錄到 conv_log，你無需手動記錄聊天內容。',
                '',
                '【Layer 2 — 智慧記憶】（由你主動判斷）',
                '• memory_store — 儲存你認為重要的知識、事實、偏好、結論',
                '• memory_retrieve — 回憶之前儲存的智慧記憶',
                '• memory_delete — 刪除過時的記憶',
                '',
                '使用時機：',
                '- 使用者告訴你他的偏好、重要事實、專案狀態 → memory_store',
                '- 你推導出重要結論、摘要、決策 → memory_store',
                '- 需要回憶跨對話的知識 → memory_retrieve',
                '- 不要儲存日常閒聊，只儲存有長期價值的資訊',
                '',
                '=== RAG 檢索（知識查詢最佳工具） ===',
                '• rag_retrieve — 混合檢索（BM25 全文 + 向量語意，RRF 融合）',
                '• memory_fulltext_search — BM25 全文檢索（精確關鍵字匹配）',
                '• memory_semantic_search — 向量語意搜尋（模糊概念匹配）',
                '',
                '當使用者詢問「你還記得...」「之前提過...」「關於XX的資料」→ 優先用 rag_retrieve',
                '需要精確關鍵字 → memory_fulltext_search',
                '需要模糊語意 → memory_semantic_search',
                '',
                '=== 其他能力 ===',
                '• 搜尋網路（web_search）— 查詢即時資訊',
                '• 瀏覽網頁（web_fetch）— 讀取網頁內容',
                '• 檔案操作（read_file/list_directory/search_files 等）',
                '• LINE 通訊（send_line_message/send_line_notification 等）',
                '',
                '如果使用者的問題不需要工具，直接回答即可。',
            ].join('\n'),
        };

        // 設定 agent 訊息為：system + LINE context + user history
        this.agent.messages = [systemMsg, lineContext, ...userHistory];

        // 透過 agent 的 send 機制處理（含工具呼叫循環）
        // 但我們不能直接用 agent.send() 因為它會再加一次 user message
        // 所以直接驅動 agent 的內部循環

        let iterations = 0;
        const maxIter = this.agent.maxIterations;

        while (iterations < maxIter) {
            iterations++;

            if (typeof this.agent.provider.resolveModel === 'function') {
                this.agent.model = this.agent.provider.resolveModel(this.agent.model);
            }

            const chatParams = {
                model: this.agent.model,
                messages: this.agent.messages,
                stream: false, // LINE 模式不需要 streaming
            };

            if (this.agent.useNativeTools && this.agent.toolDefinitions.length > 0) {
                chatParams.tools = this.agent.toolDefinitions;
            }

            let result;
            try {
                result = await this.agent.provider.chat(chatParams);
            } catch (e) {
                return `LLM 錯誤: ${e.message}`;
            }

            // 檢查是否有工具呼叫
            let toolCalls = [];
            if (this.agent.useNativeTools) {
                toolCalls = result.toolCalls || [];
            } else {
                const { parseToolCalls } = require('./react-parser');
                toolCalls = parseToolCalls(result.content);
            }

            // 沒有工具呼叫 → 最終回覆
            if (toolCalls.length === 0) {
                const content = result.content || '';
                this.agent.messages.push({ role: 'assistant', content });
                return content;
            }

            // 有工具呼叫 → 執行並繼續
            if (this.agent.useNativeTools) {
                this.agent.messages.push({
                    role: 'assistant',
                    content: result.content || '',
                    tool_calls: toolCalls,
                });
            } else {
                this.agent.messages.push({ role: 'assistant', content: result.content });
            }

            for (const call of toolCalls) {
                const fn = call.function;
                const toolName = fn.name;
                const toolArgs = fn.arguments || {};

                if (this.verbose) {
                    logInfo(colorize(`  [tool] ${toolName}(${JSON.stringify(toolArgs).substring(0, 100)})`, 'yellow'));
                }

                // 透過 governed executor 執行（走 Broker pipeline）
                const toolResult = await this.agent.governedExecutor.executeTool(
                    toolName, toolArgs,
                    { projectRoot: this.agent.projectRoot, noConfirm: true, verbose: this.verbose }
                );

                if (this.agent.useNativeTools) {
                    const toolMsg = { role: 'tool', content: toolResult };
                    if (call.id) toolMsg.tool_call_id = call.id;
                    this.agent.messages.push(toolMsg);
                } else {
                    const { formatToolResult } = require('./react-parser');
                    this.agent.messages.push({
                        role: 'user',
                        content: formatToolResult(toolName, toolResult),
                    });
                }
            }
        }

        return '已達到最大處理次數，請重新發送訊息。';
    }

    /**
     * 解析 read_line_messages 的結果
     */
    _parseMessages(result) {
        if (!result || typeof result !== 'string') return [];

        try {
            // GovernedExecutor.executeTool 回傳 result_payload 字串
            // 內容為 { count, messages: [...] }
            const parsed = JSON.parse(result);

            // 直接是 { messages: [...] }
            if (Array.isArray(parsed.messages)) {
                return parsed.messages;
            }

            // 可能包在 data 裡
            if (parsed.data && Array.isArray(parsed.data.messages)) {
                return parsed.data.messages;
            }
        } catch (_) {
            // 可能是 [Governed] 前綴的錯誤訊息
            if (this.verbose && result.startsWith('[Governed]')) {
                logWarn(`  read_line_messages: ${result}`);
            }
        }

        return [];
    }

    _truncate(s, max) {
        return s.length <= max ? s : s.substring(0, max) + '...';
    }

    _sleep(ms) {
        return new Promise((resolve) => {
            if (!this.running) return resolve();
            const timer = setTimeout(resolve, ms);
            if (timer.unref) timer.unref();
        });
    }

    // ═══════════════════════════════════════════
    // Layer 1：對話日誌（自動、機械式）
    // ═══════════════════════════════════════════

    /**
     * 記錄一則訊息到 Broker 對話日誌（conv_log_append）
     * 每則訊息即時寫入，不批次
     */
    async _logMessage(userId, role, content) {
        try {
            await this.agent.governedExecutor.executeTool(
                'conv_log_append',
                { user_id: userId, role, content },
                { projectRoot: this.agent.projectRoot, noConfirm: true, verbose: this.verbose }
            );
        } catch (e) {
            if (this.verbose) logWarn(`  Conv log write failed: ${e.message}`);
        }
    }

    /**
     * 啟動時從 Broker 對話日誌載入歷史（conv_log_read）
     */
    async _loadConvLog() {
        // 目前不預載所有使用者的日誌（效能考量）
        // 改為：首次收到某使用者訊息時才載入
        // 這裡只記錄功能就緒
        logInfo('  Conv Log (Layer 1): ready — messages will be auto-logged');
        logInfo('  Smart Memory (Layer 2): ready — LLM judges what to remember');
    }

    /**
     * 從 Broker 載入特定使用者的對話日誌作為初始上下文
     */
    async _loadUserConvLog(userId) {
        try {
            const result = await this.agent.governedExecutor.executeTool(
                'conv_log_read',
                { user_id: userId, limit: this.maxConversationHistory },
                { projectRoot: this.agent.projectRoot, noConfirm: true, verbose: this.verbose }
            );
            const parsed = JSON.parse(result);
            const messages = parsed.messages || [];

            if (messages.length > 0) {
                const history = messages.map(m => ({
                    role: m.role || 'user',
                    content: m.content || '',
                }));
                this._conversations.set(userId, history);
                logInfo(`  Loaded ${history.length} log entries for user ${userId.substring(0, 8)}...`);
                return history;
            }
        } catch (e) {
            if (this.verbose) logWarn(`  Conv log read failed: ${e.message}`);
        }
        return [];
    }

    // Layer 2（智慧記憶）由 LLM 在對話中自主調用 memory_store / memory_retrieve
    // 不需要 line-listener 額外處理
}

module.exports = { LineListener };
