'use strict';

const { TOOL_DEFINITIONS, executeTool, getToolDescriptions } = require('./tool-registry');
const { buildSystemPrompt } = require('./system-prompt');
const { parseToolCalls, hasToolCalls, stripToolCalls, formatToolResult } = require('./react-parser');
const { colorize, bold, logInfo, logWarn, logError, logTool, formatDuration } = require('./utils');
const { GovernedExecutor } = require('./governed-executor');

/**
 * 核心 Agent 迴圈
 *
 * 透過 Provider 介面與 LLM 互動，支援：
 * - Ollama（本地）
 * - OpenAI 相容 API（雲端）
 * - 任何實作 BaseProvider 介面的後端
 */
class AgentLoop {
    /**
     * @param {Object} options
     * @param {string} options.model - 模型名稱
     * @param {import('./providers/base-provider').BaseProvider} options.provider - LLM provider
     * @param {string} options.projectRoot - 專案根目錄
     * @param {boolean} options.stream - 是否串流
     * @param {boolean} options.noConfirm - 跳過確認
     * @param {boolean} options.verbose - 顯示除錯
     * @param {number} options.maxIterations - 最大迭代數
     * @param {string} options.forceStrategy - 'react' | 'native' | null
     * @param {Object} [options.governed] - 受控模式配置
     * @param {string} options.governed.brokerUrl - Broker API URL
     * @param {string} options.governed.brokerPubKey - Broker ECDH 公鑰 (Base64)
     * @param {string} options.governed.principalId - 主體 ID
     * @param {string} options.governed.taskId - 任務 ID
     * @param {string} options.governed.roleId - 角色 ID
     */
    constructor(options) {
        this.model = options.model || 'llama3.1';
        this.provider = options.provider;
        this.projectRoot = options.projectRoot;
        this.stream = options.stream !== false;
        this.noConfirm = options.noConfirm || false;
        this.verbose = options.verbose || false;
        this.maxIterations = options.maxIterations || 20;
        this.forceStrategy = options.forceStrategy || null;

        // 受控模式（--governed）
        this.governedConfig = options.governed || null;
        this.governedExecutor = null;

        this.messages = [];
        this.useNativeTools = true;
        this.toolDefinitions = TOOL_DEFINITIONS.slice();
        this.toolDescriptions = getToolDescriptions();
        this.initialized = false;
    }

    /**
     * 初始化：健康檢查 + 策略偵測 + 建構系統提示詞
     */
    async init() {
        if (this.governedExecutor) {
            await this.governedExecutor.close();
            this.governedExecutor = null;
        }

        // 健康檢查
        const ok = await this.provider.healthCheck();
        if (!ok) {
            throw new Error(
                `無法連線到 ${this.provider.name} provider (${this.provider.baseUrl})\n` +
                `請確認伺服器已啟動。`
            );
        }

        // 策略偵測
        if (this.forceStrategy === 'react') {
            this.useNativeTools = false;
            if (this.verbose) logInfo('強制使用 ReAct 回退模式');
        } else if (this.forceStrategy === 'native') {
            this.useNativeTools = true;
            if (this.verbose) logInfo('強制使用原生工具呼叫');
        } else {
            this.useNativeTools = this.provider.supportsToolCalling(this.model);
            if (this.verbose) {
                logInfo(`模型 ${this.model}: ${this.useNativeTools ? '原生工具呼叫' : 'ReAct 回退'}`);
            }
        }

        // 受控模式初始化
        if (this.governedConfig) {
            this.governedExecutor = new GovernedExecutor({
                ...this.governedConfig,
                verbose: this.verbose,
            });
            await this.governedExecutor.init();
            this.toolDefinitions = this.governedExecutor.getAllowedToolDefinitions();
            this.toolDescriptions = this.governedExecutor.getAllowedToolDescriptions();
        } else {
            this.toolDefinitions = TOOL_DEFINITIONS.slice();
            this.toolDescriptions = getToolDescriptions();
        }

        // 建構系統提示詞
        const systemPrompt = buildSystemPrompt({
            projectRoot: this.projectRoot,
            useReact: !this.useNativeTools,
            verbose: this.verbose,
            toolDescriptions: this.toolDescriptions,
            governed: this.governedExecutor ? this.governedExecutor.getPromptContext() : null,
        });

        this.messages = [{ role: 'system', content: systemPrompt }];
        this.initialized = true;

        // 顯示策略
        const strategy = this.useNativeTools ? '原生 Tool Calling' : 'ReAct XML';
        const mode = this.governedExecutor ? ' | 模式: 🔒 受控' : '';
        logInfo(`模型: ${bold(this.model)} | Provider: ${this.provider.name} | 策略: ${strategy}${mode}`);
    }

    /**
     * 發送使用者訊息並取得完整回應（含工具呼叫迴圈）
     * @param {string} userMessage
     * @returns {Promise<string>} 最終回應文字
     */
    async send(userMessage) {
        if (!this.initialized) await this.init();

        this.messages.push({ role: 'user', content: userMessage });

        let iterations = 0;
        const startTime = Date.now();

        while (iterations < this.maxIterations) {
            iterations++;

            // 發送到 Provider
            const chatParams = {
                model: this.model,
                messages: this.messages,
                stream: this.stream,
            };

            if (this.useNativeTools && this.toolDefinitions.length > 0) {
                chatParams.tools = this.toolDefinitions;
            }

            if (this.stream) {
                process.stdout.write(colorize('\n🤖 ', 'cyan'));
            }

            let result;
            let heartbeatLine = false;
            try {
                result = await this.provider.chat({
                    ...chatParams,
                    onToken: this.stream ? (token) => {
                        if (heartbeatLine) {
                            process.stdout.write('\r\x1b[K');
                            heartbeatLine = false;
                        }
                        process.stdout.write(token);
                    } : undefined,
                    onHeartbeat: (info) => {
                        this._handleHeartbeat(info, heartbeatLine, (val) => { heartbeatLine = val; });
                    },
                });
            } catch (e) {
                if (heartbeatLine) {
                    process.stdout.write('\r\x1b[K');
                }
                logError(`API 錯誤: ${e.message}`);
                return `錯誤: ${e.message}`;
            }

            if (this.stream) {
                process.stdout.write('\n');
            }

            // 判斷是否有工具呼叫
            let toolCalls = [];

            if (this.useNativeTools) {
                toolCalls = result.toolCalls || [];
            } else {
                toolCalls = parseToolCalls(result.content);
            }

            if (toolCalls.length === 0) {
                const content = this.useNativeTools
                    ? result.content
                    : stripToolCalls(result.content);

                this.messages.push({ role: 'assistant', content: content });

                if (this.verbose) {
                    const elapsed = Date.now() - startTime;
                    logInfo(`完成: ${iterations} 輪, ${formatDuration(elapsed)}`);
                }

                if (!this.stream) {
                    console.log(colorize('\n🤖 ', 'cyan') + content);
                }

                return content;
            }

            // 有工具呼叫 → 執行工具
            if (this.useNativeTools) {
                this.messages.push({
                    role: 'assistant',
                    content: result.content || '',
                    tool_calls: toolCalls,
                });
            } else {
                this.messages.push({ role: 'assistant', content: result.content });
            }

            for (const call of toolCalls) {
                const fn = call.function;
                const toolName = fn.name;
                const toolArgs = fn.arguments || {};

                if (this.verbose) {
                    logTool(toolName, JSON.stringify(toolArgs));
                }

                const modeIcon = this.governedExecutor ? '🔒' : '🔧';
                console.log(colorize(`  ${modeIcon} ${toolName}(${this._formatArgs(toolArgs)})`, 'gray'));

                // 受控模式：路由到 Broker 中介核心；否則直接執行
                const toolResult = this.governedExecutor
                    ? await this.governedExecutor.executeTool(toolName, toolArgs, {
                        projectRoot: this.projectRoot,
                        noConfirm: this.noConfirm,
                        verbose: this.verbose,
                    })
                    : await executeTool(toolName, toolArgs, {
                        projectRoot: this.projectRoot,
                        noConfirm: this.noConfirm,
                        verbose: this.verbose,
                    });

                if (this.useNativeTools) {
                    const toolMsg = { role: 'tool', content: toolResult };
                    if (call.id) toolMsg.tool_call_id = call.id;
                    this.messages.push(toolMsg);
                } else {
                    this.messages.push({
                        role: 'user',
                        content: formatToolResult(toolName, toolResult),
                    });
                }
            }
        }

        logWarn(`已達最大迭代數 (${this.maxIterations})，強制停止`);
        return '（代理迴圈已達上限，請簡化指令或提高 --max-iterations）';
    }

    /**
     * 處理心跳回呼顯示
     * @private
     */
    _handleHeartbeat(info, heartbeatLine, setHeartbeatLine) {
        if (info.status === 'retry') {
            logWarn(`連線異常，第 ${info.attempt} 次重試...`);
            if (this.stream) {
                process.stdout.write(colorize('\n🤖 ', 'cyan'));
            }
            return;
        }
        if (info.status === 'done') {
            if (heartbeatLine) {
                process.stdout.write('\r\x1b[K');
                setHeartbeatLine(false);
            }
            return;
        }
        if (info.status === 'verifying') {
            const indicator = colorize(`  🔍 驗證中... (${info.noDataCount} 次無回應)`, 'yellow');
            process.stdout.write(`\r\x1b[K${indicator}`);
            setHeartbeatLine(true);
            return;
        }
        if (info.status === 'stalled' || info.status === 'server_down') {
            if (heartbeatLine) {
                process.stdout.write('\r\x1b[K');
                setHeartbeatLine(false);
            }
            return;
        }

        // thinking / generating（Layer 1 心跳）
        const secs = Math.round(info.elapsed / 1000);
        const label = info.status === 'thinking' ? '思考中' : '生成中';
        let extra = '';
        if (info.runningModels && info.runningModels.models && info.runningModels.models.length > 0) {
            const m = info.runningModels.models[0];
            if (m && m.size) {
                const sizeMB = Math.round(m.size / 1024 / 1024);
                extra = ` [${m.name || ''} ${sizeMB}MB]`;
            }
        }
        const indicator = colorize(`  ⏳ ${label}... (${secs}s)${extra}`, 'gray');

        if (this.stream && !heartbeatLine && info.sinceLastToken > 8000) {
            // 只在超過 8 秒無 token 時顯示心跳
            process.stdout.write(indicator);
            setHeartbeatLine(true);
        } else if (heartbeatLine) {
            process.stdout.write(`\r\x1b[K${indicator}`);
        }
    }

    /**
     * 優雅關閉（清理 Broker session 等資源）
     */
    async close() {
        if (this.governedExecutor) {
            await this.governedExecutor.close();
            this.governedExecutor = null;
        }
    }

    clearHistory() {
        if (this.messages.length > 0) {
            this.messages = [this.messages[0]];
        }
    }

    getAvailableToolDefinitions() {
        return this.toolDefinitions.slice();
    }

    getAvailableToolDescriptions() {
        return this.toolDescriptions;
    }

    getGovernedPromptContext() {
        return this.governedExecutor ? this.governedExecutor.getPromptContext() : null;
    }

    getStats() {
        const msgCount = this.messages.length;
        const charCount = this.messages.reduce((sum, m) => sum + (m.content || '').length, 0);
        const governed = this.governedExecutor ? this.governedExecutor.isActive : false;
        return { messageCount: msgCount, totalChars: charCount, governed };
    }

    _formatArgs(args) {
        const entries = Object.entries(args);
        if (entries.length === 0) return '';
        return entries.map(([k, v]) => {
            const val = typeof v === 'string' && v.length > 50 ? v.slice(0, 50) + '...' : v;
            return `${k}: ${JSON.stringify(val)}`;
        }).join(', ');
    }
}

module.exports = { AgentLoop };
