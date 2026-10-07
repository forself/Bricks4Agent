'use strict';

const { TOOL_DEFINITIONS, executeTool, getToolDescriptions } = require('./tool-registry');
const {
    buildSystemPrompt,
    isGenerationTask,
    generationUnsupportedToolMessage,
    GENERATION_CONTINUE_REMINDER,
} = require('./system-prompt');
const { parseToolCalls, stripToolCalls, formatToolResult } = require('./react-parser');
const { parseTextToolCalls } = require('./text-tool-calls');
const { colorize, bold, logInfo, logWarn, logError, logTool, formatDuration } = require('./utils');
const { GovernedExecutor } = require('./governed-executor');

class AgentLoop {
    constructor(options) {
        this.model = options.model || 'llama3.1';
        this.provider = options.provider || null;
        this.directProvider = options.provider || null;
        this.projectRoot = options.projectRoot;
        this.stream = options.stream !== false;
        this.noConfirm = options.noConfirm || false;
        this.verbose = options.verbose || false;
        this.maxIterations = options.maxIterations || 20;
        this.forceStrategy = options.forceStrategy || null;

        this.governedConfig = options.governed || null;
        this.governedExecutor = null;

        this.messages = [];
        this.generationTask = false;
        this.useNativeTools = true;
        this.toolDefinitions = TOOL_DEFINITIONS.slice();
        this.toolDescriptions = getToolDescriptions();
        this.initialized = false;
    }

    async init() {
        if (this.governedExecutor) {
            await this.governedExecutor.close();
            this.governedExecutor = null;
        }

        if (this.governedConfig) {
            this.governedExecutor = new GovernedExecutor({
                ...this.governedConfig,
                verbose: this.verbose,
            });
            await this.governedExecutor.init();
            this.provider = this.governedExecutor;
            this.toolDefinitions = this.governedExecutor.getAllowedToolDefinitions();
            this.toolDescriptions = this.governedExecutor.getAllowedToolDescriptions();
            if (typeof this.provider.resolveModel === 'function') {
                this.model = this.provider.resolveModel(this.model);
            }
        } else {
            this.provider = this.directProvider;
            this.toolDefinitions = TOOL_DEFINITIONS.slice();
            this.toolDescriptions = getToolDescriptions();
        }

        if (!this.provider) {
            throw new Error('No provider available for this run');
        }

        const ok = await this.provider.healthCheck();
        if (!ok) {
            throw new Error(
                `Unable to reach ${this.provider.name} provider (${this.provider.baseUrl})\n` +
                'Check connectivity and configured runtime settings.'
            );
        }

        if (this.forceStrategy === 'react') {
            this.useNativeTools = false;
            if (this.verbose) logInfo('Using forced ReAct strategy');
        } else if (this.forceStrategy === 'native') {
            this.useNativeTools = true;
            if (this.verbose) logInfo('Using forced native tool calling strategy');
        } else {
            this.useNativeTools = this.provider.supportsToolCalling(this.model);
            if (this.verbose) {
                logInfo(`Tool strategy for ${this.model}: ${this.useNativeTools ? 'native tool calling' : 'ReAct XML'}`);
            }
        }

        const governedContext = this.governedExecutor ? this.governedExecutor.getPromptContext() : null;
        this.generationTask = Boolean(governedContext) && isGenerationTask(governedContext);
        const systemPrompt = buildSystemPrompt({
            projectRoot: this.projectRoot,
            useReact: !this.useNativeTools,
            verbose: this.verbose,
            toolDescriptions: this.toolDescriptions,
            governed: governedContext,
            maxIterations: this.maxIterations,
        });

        this.messages = [{ role: 'system', content: systemPrompt }];
        this.initialized = true;

        const strategy = this.useNativeTools ? 'Native Tool Calling' : 'ReAct XML';
        const mode = this.governedExecutor ? ' | Mode: governed' : '';
        logInfo(`Model: ${bold(this.model)} | Provider: ${this.provider.name} | Strategy: ${strategy}${mode}`);
    }

    async send(userMessage) {
        if (!this.initialized) await this.init();

        this.messages.push({ role: 'user', content: userMessage });

        let iterations = 0;
        const startTime = Date.now();
        // 生成任務：還沒有成功的 generate_scaffold 時遇到沒有工具呼叫的回合，追加一次提醒再繼續（仍受迭代上限約束）。
        // 呼叫全部是沒有授予的名稱的回合也算沒有進展：同樣送出那一次提醒；同一個名稱在這種回合連續出現第二次時結束。
        let generated = false;
        let reminded = false;
        let previousUnsupported = new Set();

        while (iterations < this.maxIterations) {
            iterations++;
            if (typeof this.provider.resolveModel === 'function') {
                this.model = this.provider.resolveModel(this.model);
            }

            const chatParams = {
                model: this.model,
                messages: this.messages,
                stream: this.stream,
            };

            if (this.useNativeTools && this.toolDefinitions.length > 0) {
                chatParams.tools = this.toolDefinitions;
            }

            if (this.stream) {
                process.stdout.write(colorize('\n> ', 'cyan'));
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
                        this._handleHeartbeat(info, heartbeatLine, (value) => { heartbeatLine = value; });
                    },
                });
            } catch (e) {
                if (heartbeatLine) {
                    process.stdout.write('\r\x1b[K');
                }
                logError(`API error: ${e.message}`);
                return `API error: ${e.message}`;
            }

            if (this.stream) {
                process.stdout.write('\n');
            }

            let toolCalls = [];
            if (this.useNativeTools) {
                toolCalls = result.toolCalls || [];
            } else {
                toolCalls = parseToolCalls(result.content);
            }

            // 生成任務還沒生成時，模型把呼叫寫成內文的 JSON（native 與 ReAct 都可能）：改當成工具呼叫執行。
            // 只限受治理的生成任務；名稱沒有授予的呼叫不執行，回一則 unsupported 結果讓模型改用可用的工具。
            let textCalls = false;
            if (toolCalls.length === 0 && this.generationTask && !generated) {
                toolCalls = parseTextToolCalls(result.content).map((call, index) => ({
                    id: `text_call_${iterations}_${index}`,
                    function: { name: call.name, arguments: call.arguments },
                }));
                textCalls = toolCalls.length > 0;
                if (textCalls) {
                    logWarn(`Model wrote ${toolCalls.length} tool call(s) as text; running them as tool calls`);
                }
            }

            if (toolCalls.length === 0) {
                previousUnsupported = new Set();
                const content = this.useNativeTools
                    ? result.content
                    : stripToolCalls(result.content);

                this.messages.push({ role: 'assistant', content });

                if (this.generationTask && !generated && !reminded && iterations < this.maxIterations) {
                    reminded = true;
                    logWarn('Generation task replied without a tool call before generating; sending one reminder');
                    this.messages.push({ role: 'user', content: GENERATION_CONTINUE_REMINDER });
                    continue;
                }

                if (this.verbose) {
                    const elapsed = Date.now() - startTime;
                    logInfo(`Completed in ${iterations} iterations, ${formatDuration(elapsed)}`);
                }

                if (!this.stream) {
                    console.log(colorize('\n> ', 'cyan') + content);
                }

                return content;
            }

            if (this.useNativeTools) {
                this.messages.push({
                    role: 'assistant',
                    content: result.content || '',
                    tool_calls: toolCalls,
                });
            } else {
                this.messages.push({ role: 'assistant', content: result.content });
            }

            const grantedNames = this.toolDefinitions.map((definition) => definition.function?.name).filter(Boolean);
            const turnUnsupported = new Set();
            let turnSupported = 0;
            for (const call of toolCalls) {
                const fn = call.function;
                const toolName = fn.name;
                const toolArgs = fn.arguments || {};

                // 先決定授權，再印參數：參數印不出來（例如巢狀過深）也不影響授權判斷與這一回合的進行。
                // 生成任務中名稱沒有授予的呼叫（文字或 native）不執行，回可照做的說明。
                // 文字呼叫只出現在生成任務（見上方），所以一般代理的行為不變。
                const unsupported = this.generationTask && !grantedNames.includes(toolName);

                if (this.verbose) {
                    logTool(toolName, safeStringify(toolArgs));
                }

                const modeIcon = this.governedExecutor ? '[governed]' : '[local]';
                console.log(colorize(`  ${modeIcon} ${toolName}(${this._formatArgs(toolArgs)})`, 'gray'));

                let toolResult;
                if (unsupported) {
                    turnUnsupported.add(toolName);
                    toolResult = generationUnsupportedToolMessage(toolName, grantedNames);
                } else if (this.governedExecutor) {
                    turnSupported += 1;
                    toolResult = await this.governedExecutor.executeTool(toolName, toolArgs, {
                        projectRoot: this.projectRoot,
                        noConfirm: this.noConfirm,
                        verbose: this.verbose,
                    });
                } else {
                    turnSupported += 1;
                    toolResult = await executeTool(toolName, toolArgs, {
                        projectRoot: this.projectRoot,
                        noConfirm: this.noConfirm,
                        verbose: this.verbose,
                    });
                }

                if (toolName === 'generate_scaffold' && isSuccessfulGenerateResult(toolResult)) {
                    generated = true;
                }

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

            // 生成任務中呼叫全部是沒有授予的名稱：這一回合沒有進展。同一個名稱在這種回合連續出現第二次就結束
            // （模型不會因為再試一次而得到這個工具），不等到回合上限；第一次則與沒有工具呼叫的回合共用那一次提醒。
            if (this.generationTask && !generated && turnSupported === 0 && turnUnsupported.size > 0) {
                const repeated = [...turnUnsupported].find((name) => previousUnsupported.has(name));
                if (repeated !== undefined) {
                    const summary = `Stopped: the model called "${String(repeated).slice(0, 64)}", which is not an available tool, `
                        + `in two turns in a row, so the run ended before the iteration limit. Nothing was generated. `
                        + `Available tools: ${grantedNames.join(', ') || '(none)'}.`;
                    logWarn(summary);
                    return summary;
                }
                previousUnsupported = turnUnsupported;
                if (!reminded && iterations < this.maxIterations) {
                    reminded = true;
                    logWarn('Generation task called only tools that are not available; sending one reminder');
                    this.messages.push({ role: 'user', content: GENERATION_CONTINUE_REMINDER });
                }
            } else {
                previousUnsupported = new Set();
            }
        }

        logWarn(`Reached max iterations (${this.maxIterations})`);
        return 'Stopped because max iterations were reached. Increase --max-iterations if needed.';
    }

    _handleHeartbeat(info, heartbeatLine, setHeartbeatLine) {
        if (info.status === 'retry') {
            logWarn(`Retrying request, attempt ${info.attempt}...`);
            if (this.stream) {
                process.stdout.write(colorize('\n> ', 'cyan'));
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
            const indicator = colorize(`  ... verifying upstream (${info.noDataCount} checks)`, 'yellow');
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

        const secs = Math.round(info.elapsed / 1000);
        const label = info.status === 'thinking' ? ' thinking' : ' generating';
        let extra = '';
        if (info.runningModels && info.runningModels.models && info.runningModels.models.length > 0) {
            const model = info.runningModels.models[0];
            if (model && model.size) {
                const sizeMB = Math.round(model.size / 1024 / 1024);
                extra = ` [${model.name || ''} ${sizeMB}MB]`;
            }
        }
        const indicator = colorize(`  ...${label} (${secs}s)${extra}`, 'gray');

        if (this.stream && !heartbeatLine && info.sinceLastToken > 8000) {
            process.stdout.write(indicator);
            setHeartbeatLine(true);
        } else if (heartbeatLine) {
            process.stdout.write(`\r\x1b[K${indicator}`);
        }
    }

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
        const charCount = this.messages.reduce((sum, message) => sum + (message.content || '').length, 0);
        const governed = this.governedExecutor ? this.governedExecutor.isActive : false;
        return { messageCount: msgCount, totalChars: charCount, governed };
    }

    _formatArgs(args) {
        try {
            const entries = Object.entries(args);
            if (entries.length === 0) return '';
            return entries.map(([key, value]) => {
                const display = typeof value === 'string' && value.length > 50 ? `${value.slice(0, 50)}...` : value;
                return `${key}: ${JSON.stringify(display)}`;
            }).join(', ');
        } catch (_) {
            return UNPRINTABLE_ARGUMENTS;
        }
    }
}

const UNPRINTABLE_ARGUMENTS = '(arguments not printable)';

/** 日誌用的序列化：序列化失敗（例如巢狀過深耗盡呼叫堆疊）時不丟例外。 */
function safeStringify(value) {
    try {
        return JSON.stringify(value);
    } catch (_) {
        return UNPRINTABLE_ARGUMENTS;
    }
}

/** generate_scaffold 成功時，broker 回傳 ok: true 與 zip 的 sha256（失敗時是錯誤訊息或 ok: false）。 */
function isSuccessfulGenerateResult(result) {
    if (typeof result !== 'string') {
        return false;
    }
    try {
        const parsed = JSON.parse(result);
        return parsed?.ok === true && typeof parsed?.zip?.sha256 === 'string' && parsed.zip.sha256.length > 0;
    } catch (_) {
        return false;
    }
}

module.exports = { AgentLoop };
