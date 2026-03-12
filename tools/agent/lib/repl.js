'use strict';

const readline = require('readline');
const { AgentLoop } = require('./agent-loop');
const { colorize, bold, logInfo, logSuccess, logWarn, logError } = require('./utils');

/**
 * 互動式 REPL
 */
class AgentRepl {
    /**
     * @param {AgentLoop} agentLoop
     * @param {Object} options
     */
    constructor(agentLoop, options = {}) {
        this.agent = agentLoop;
        this.options = options;
        this.rl = null;
        this.running = false;
    }

    /**
     * 啟動 REPL
     */
    async start() {
        await this.agent.init();

        this.rl = readline.createInterface({
            input: process.stdin,
            output: process.stdout,
            prompt: colorize('\n👤 ', 'green'),
        });

        this.running = true;

        const providerLabel = this.agent.provider ? this.agent.provider.name : 'agent';
        console.log(bold(`\n═══ ${providerLabel.toUpperCase()} Agent ═══`));
        console.log(`輸入訊息與 AI 對話，輸入 ${colorize('/help', 'cyan')} 查看指令`);
        console.log(`按 ${colorize('Ctrl+D', 'cyan')} 或輸入 ${colorize('/exit', 'cyan')} 退出\n`);

        this.rl.prompt();

        this.rl.on('line', async (line) => {
            const input = line.trim();
            if (!input) {
                this.rl.prompt();
                return;
            }

            // 處理特殊指令
            if (input.startsWith('/')) {
                await this._handleCommand(input);
                this.rl.prompt();
                return;
            }

            // 發送給 agent
            try {
                await this.agent.send(input);
            } catch (e) {
                logError(`錯誤: ${e.message}`);
            }

            this.rl.prompt();
        });

        this.rl.on('close', () => {
            console.log(colorize('\n👋 再見！', 'cyan'));
            process.exit(0);
        });

        // Ctrl+C 不退出
        this.rl.on('SIGINT', () => {
            console.log(colorize('\n(按 Ctrl+D 或輸入 /exit 退出)', 'gray'));
            this.rl.prompt();
        });
    }

    /**
     * 處理 REPL 特殊指令
     */
    async _handleCommand(input) {
        const parts = input.slice(1).split(/\s+/);
        const cmd = parts[0].toLowerCase();
        const arg = parts.slice(1).join(' ');

        switch (cmd) {
            case 'help':
            case 'h':
                this._showHelp();
                break;

            case 'model':
                if (!arg) {
                    logInfo(`當前模型: ${this.agent.model}`);
                } else {
                    this.agent.model = arg;
                    this.agent.clearHistory();
                    await this.agent.init();
                    logSuccess(`已切換至模型: ${arg}`);
                }
                break;

            case 'models':
                try {
                    const models = await this.agent.provider.listModels();
                    if (models.length === 0) {
                        logWarn('沒有可用模型');
                    } else {
                        console.log(bold('\n可用模型:'));
                        for (const m of models) {
                            const size = m.size ? ` (${(m.size / 1024 / 1024 / 1024).toFixed(1)} GB)` : '';
                            const current = m.name === this.agent.model ? colorize(' ← 當前', 'green') : '';
                            console.log(`  ${m.name}${size}${current}`);
                        }
                    }
                } catch (e) {
                    logError(`無法列出模型: ${e.message}`);
                }
                break;

            case 'clear':
                this.agent.clearHistory();
                logSuccess('對話歷史已清除');
                break;

            case 'history':
                const stats = this.agent.getStats();
                logInfo(`訊息數: ${stats.messageCount}, 總字元: ${stats.totalChars}`);
                break;

            case 'tools':
                const { getToolDescriptions } = require('./tool-registry');
                console.log(bold('\n可用工具:'));
                console.log(getToolDescriptions());
                break;

            case 'exit':
            case 'quit':
            case 'q':
                this.rl.close();
                break;

            default:
                logWarn(`未知指令: /${cmd}。輸入 /help 查看所有指令`);
        }
    }

    _showHelp() {
        console.log(bold('\n指令清單:'));
        console.log(`  ${colorize('/help', 'cyan')}           顯示此說明`);
        console.log(`  ${colorize('/model <name>', 'cyan')}   切換模型`);
        console.log(`  ${colorize('/models', 'cyan')}         列出可用模型`);
        console.log(`  ${colorize('/clear', 'cyan')}          清除對話歷史`);
        console.log(`  ${colorize('/history', 'cyan')}        顯示對話統計`);
        console.log(`  ${colorize('/tools', 'cyan')}          列出可用工具`);
        console.log(`  ${colorize('/exit', 'cyan')}           退出`);
    }
}

module.exports = { AgentRepl };
