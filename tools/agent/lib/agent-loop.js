'use strict';

const { TOOL_DEFINITIONS, executeTool, getToolDescriptions } = require('./tool-registry');
const { buildSystemPrompt } = require('./system-prompt');
const { parseToolCalls, stripToolCalls, formatToolResult } = require('./react-parser');
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

        const systemPrompt = buildSystemPrompt({
            projectRoot: this.projectRoot,
            useReact: !this.useNativeTools,
            verbose: this.verbose,
            toolDescriptions: this.toolDescriptions,
            governed: this.governedExecutor ? this.governedExecutor.getPromptContext() : null,
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

            if (toolCalls.length === 0) {
                const content = this.useNativeTools
                    ? result.content
                    : stripToolCalls(result.content);

                this.messages.push({ role: 'assistant', content });

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

            for (const call of toolCalls) {
                const fn = call.function;
                const toolName = fn.name;
                const toolArgs = fn.arguments || {};

                if (this.verbose) {
                    logTool(toolName, JSON.stringify(toolArgs));
                }

                const modeIcon = this.governedExecutor ? '[governed]' : '[local]';
                console.log(colorize(`  ${modeIcon} ${toolName}(${this._formatArgs(toolArgs)})`, 'gray'));

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
        const entries = Object.entries(args);
        if (entries.length === 0) return '';
        return entries.map(([key, value]) => {
            const display = typeof value === 'string' && value.length > 50 ? `${value.slice(0, 50)}...` : value;
            return `${key}: ${JSON.stringify(display)}`;
        }).join(', ');
    }
}

module.exports = { AgentLoop };
