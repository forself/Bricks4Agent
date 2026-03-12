#!/usr/bin/env node
'use strict';

/**
 * AI Agent CLI
 *
 * 讓本機 Ollama 或雲端 API 模型成為自主代理，支援檔案讀寫、指令執行、專案操作。
 * 零外部依賴，只用 Node.js 內建模組。
 *
 * 用法:
 *   node agent.js                                          # 互動式 REPL（Ollama）
 *   node agent.js --run "讀取 AGENT.md"                    # 單次執行
 *   node agent.js --model qwen2.5:14b                      # 指定模型
 *   node agent.js --provider openai --api-key sk-xxx       # 使用 OpenAI
 *   node agent.js --provider gemini --model gemini-2.0-flash # 使用 Gemini
 *   node agent.js --list-models                            # 列出可用模型
 *
 * @module agent
 * @version 2.0.0
 */

const { AgentLoop } = require('./lib/agent-loop');
const { AgentRepl } = require('./lib/repl');
const { StateMachine } = require('./lib/state-machine');
const { buildCrudPipeline } = require('./lib/pipelines/crud-pipeline');
const { runPipelines, loadProjectConfig, detectEntityStatus } = require('./lib/pipelines/pipeline-runner');
const { createProvider, listProviders } = require('./lib/providers/provider-factory');
const { resolveProjectRoot, bold, colorize, logInfo, logError, logSuccess, logWarn } = require('./lib/utils');

// ─── 參數解析 ───

function parseArgs(argv) {
    const args = {
        run: null,
        generate: false,       // 自動化模式: 讀 project.json 批次生成
        pipeline: null,        // 管線模式: 'crud'
        pipelineEntity: null,  // 實體名稱
        pipelineFields: null,  // 欄位定義 (JSON)
        pipelinePath: null,    // 專案子路徑
        dryRun: false,         // 乾跑: 只顯示計畫
        validateOnly: false,   // 只驗證: 跳過 Agent
        force: false,          // 強制: 重新生成已存在的
        model: 'llama3.1',
        host: null,
        provider: null,
        apiKey: null,
        stream: true,
        forceStrategy: null,
        maxIterations: 20,
        noConfirm: false,
        verbose: false,
        listModels: false,
        help: false,
        version: false,
    };

    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case '--run': case '-r':
                args.run = argv[++i] || '';
                break;
            case '--model': case '-m':
                args.model = argv[++i] || args.model;
                break;
            case '--host': case '-H':
                args.host = argv[++i] || args.host;
                break;
            case '--provider': case '-P':
                args.provider = argv[++i] || args.provider;
                break;
            case '--api-key': case '-k':
                args.apiKey = argv[++i] || args.apiKey;
                break;
            case '--no-stream':
                args.stream = false;
                break;
            case '--force-react':
                args.forceStrategy = 'react';
                break;
            case '--force-native':
                args.forceStrategy = 'native';
                break;
            case '--max-iterations':
                args.maxIterations = parseInt(argv[++i], 10) || 20;
                break;
            case '--no-confirm':
                args.noConfirm = true;
                break;
            case '--verbose': case '-v':
                args.verbose = true;
                break;
            case '--list-models':
                args.listModels = true;
                break;
            case '--help': case '-h':
                args.help = true;
                break;
            case '--version':
                args.version = true;
                break;
            case '--pipeline':
                args.pipeline = argv[++i] || 'crud';
                break;
            case '--entity':
                args.pipelineEntity = argv[++i] || '';
                break;
            case '--fields':
                args.pipelineFields = argv[++i] || '';
                break;
            case '--project-path':
                args.pipelinePath = argv[++i] || '';
                break;
            case '--generate': case '-g':
                args.generate = true;
                break;
            case '--dry-run':
                args.dryRun = true;
                break;
            case '--validate':
                args.validateOnly = true;
                break;
            case '--force':
                args.force = true;
                break;
        }
    }

    return args;
}

// ─── 說明文字 ───

function showHelp() {
    const providers = listProviders().join(', ');
    console.log(`
${bold('AI Agent CLI')} — 讓本機或雲端模型成為自主代理

${bold('用法:')}
  node agent.js [選項]

${bold('選項:')}
  --run, -r "<prompt>"     單次執行指令後退出
  --model, -m <name>       模型名稱 (預設: llama3.1)
  --provider, -P <type>    Provider: ${providers} (自動偵測)
  --api-key, -k <key>      雲端 API 金鑰（或用環境變數）
  --host, -H <url>         覆蓋 Provider 預設端點
  --no-stream              關閉串流輸出
  --force-react            強制使用 ReAct XML 回退模式
  --force-native           強制使用原生 tool calling
  --max-iterations <n>     最大迭代輪數 (預設: 20)
  --no-confirm             跳過確認提示 (CI/自動化用)
  --verbose, -v            顯示除錯資訊
  --list-models            列出可用模型
  --help, -h               顯示此說明
  --version                顯示版本

${bold('範例:')}
  node agent.js                                        # 本地 Ollama 互動式對話
  node agent.js -m qwen2.5:14b                         # 使用特定 Ollama 模型
  node agent.js -r "列出專案結構"                       # 單次執行
  node agent.js -r "生成部落格功能" --no-confirm        # 自動化
  node agent.js --force-react -m phi3                   # 強制 ReAct 模式

  ${bold('雲端 API:')}
  node agent.js -P openai -k sk-xxx -m gpt-4o          # OpenAI
  node agent.js -P gemini -k AIza... -m gemini-2.0-flash # Gemini
  node agent.js -P deepseek -k sk-xxx -m deepseek-chat  # DeepSeek
  node agent.js -P groq -k gsk_xxx -m llama-3.1-70b-versatile # Groq
  node agent.js -P openai -H http://localhost:8000 -m my-model # 自架 vLLM

  ${bold('環境變數（自動偵測 provider）:')}
  OPENAI_API_KEY=sk-xxx node agent.js -m gpt-4o
  GEMINI_API_KEY=AIza... node agent.js -P gemini -m gemini-2.0-flash

${bold('自動化生成 (讀取 project.json):')}
  node agent.js --generate --project-path projects/PhotoDiary              # 生成所有未完成實體
  node agent.js --generate --project-path projects/PhotoDiary --dry-run    # 乾跑：只顯示計畫
  node agent.js --generate --project-path projects/PhotoDiary --validate   # 只驗證已有檔案
  node agent.js --generate --project-path projects/PhotoDiary --force      # 強制重新生成
  node agent.js --generate --project-path projects/PhotoDiary --entity DiaryEntry  # 只處理特定實體

  --generate, -g           從 project.json 讀取 entities，批次執行管線
  --dry-run                乾跑模式：只顯示執行計畫
  --validate               只驗證模式：不執行 Agent，只跑 postcondition
  --force                  強制重新生成已存在的實體

${bold('管線模式 (手動指定):')}
  node agent.js --pipeline crud --entity DiaryEntry --fields '[{"name":"Title","type":"string"},{"name":"Content","type":"text"}]'
  node agent.js --pipeline crud --entity Product --fields '[{"name":"Name","type":"string"},{"name":"Price","type":"decimal"}]' --project-path projects/Shop

  --pipeline <type>        管線類型: crud
  --entity <Name>          實體名稱 (PascalCase)
  --fields '<json>'        欄位定義 JSON 陣列
  --project-path <path>    專案子路徑（相對於根目錄）

${bold('REPL 指令:')}
  /help     顯示指令說明    /model <name>  切換模型
  /models   列出可用模型    /clear         清除歷史
  /history  對話統計        /tools         列出工具
  /exit     退出
`);
}

// ─── 主程式 ───

async function main() {
    const args = parseArgs(process.argv.slice(2));

    if (args.version) {
        console.log('agent-cli v2.0.0');
        return;
    }

    if (args.help) {
        showHelp();
        return;
    }

    // 建立 Provider
    let provider;
    try {
        provider = createProvider({
            provider: args.provider,
            host: args.host,
            apiKey: args.apiKey,
        });
    } catch (e) {
        logError(e.message);
        process.exit(1);
    }

    // 列出模型
    if (args.listModels) {
        try {
            const models = await provider.listModels();
            if (models.length === 0) {
                console.log('沒有可用模型');
            } else {
                console.log(bold(`可用模型 (${provider.name}):`));
                for (const m of models) {
                    const size = m.size ? ` (${(m.size / 1024 / 1024 / 1024).toFixed(1)} GB)` : '';
                    console.log(`  ${m.name}${size}`);
                }
            }
        } catch (e) {
            logError(`無法連線到 ${provider.name}: ${e.message}`);
            process.exit(1);
        }
        return;
    }

    // 解析專案根目錄
    const projectRoot = resolveProjectRoot();
    if (args.verbose) {
        logInfo(`專案根目錄: ${projectRoot}`);
    }

    // 快速路徑: --generate --validate 或 --generate --dry-run 不需要 Agent
    if (args.generate && (args.validateOnly || args.dryRun)) {
        try {
            if (!args.pipelinePath) {
                logError('自動化模式需要 --project-path 參數 (e.g. --project-path projects/PhotoDiary)');
                process.exit(1);
            }

            const result = await runPipelines({
                agent: null,
                projectRoot,
                projectPath: args.pipelinePath,
                entityFilter: args.pipelineEntity,
                force: args.force,
                dryRun: args.dryRun,
                validateOnly: args.validateOnly,
            });

            if (!result.completed) {
                process.exit(1);
            }
        } catch (e) {
            logError(e.message);
            if (args.verbose) console.error(e.stack);
            process.exit(1);
        }
        return;
    }

    // 建立 Agent（只在需要 LLM 時）
    const agent = new AgentLoop({
        model: args.model,
        provider,
        projectRoot,
        stream: args.stream,
        noConfirm: args.noConfirm,
        verbose: args.verbose,
        maxIterations: args.maxIterations,
        forceStrategy: args.forceStrategy,
    });

    if (args.generate) {
        // 自動化模式: 讀 project.json 批次生成（需要 Agent）
        try {
            if (!args.pipelinePath) {
                logError('自動化模式需要 --project-path 參數 (e.g. --project-path projects/PhotoDiary)');
                process.exit(1);
            }

            const result = await runPipelines({
                agent,
                projectRoot,
                projectPath: args.pipelinePath,
                entityFilter: args.pipelineEntity,
                force: args.force,
                dryRun: false,
                validateOnly: false,
            });

            if (!result.completed) {
                process.exit(1);
            }
        } catch (e) {
            logError(e.message);
            if (args.verbose) console.error(e.stack);
            process.exit(1);
        }
    } else if (args.pipeline) {
        // 管線模式（狀態機 + 檢核點）
        try {
            if (args.pipeline !== 'crud') {
                logError(`未知管線類型: ${args.pipeline} (目前只支援: crud)`);
                process.exit(1);
            }
            if (!args.pipelineEntity) {
                logError('管線模式需要 --entity 參數');
                process.exit(1);
            }

            let fields;
            try {
                fields = JSON.parse(args.pipelineFields || '[]');
            } catch {
                logError('--fields 必須是合法 JSON 陣列');
                process.exit(1);
            }

            if (fields.length === 0) {
                fields = [{ name: 'Name', type: 'string' }];
                logInfo('未指定欄位，使用預設: Name:string');
            }

            const states = buildCrudPipeline({
                entityName: args.pipelineEntity,
                fields,
                projectPath: args.pipelinePath || '',
            });

            const sm = new StateMachine({
                states,
                agent,
                projectRoot,
            });

            const result = await sm.run({});
            console.log(result.report);

            if (!result.completed) {
                process.exit(1);
            }
        } catch (e) {
            logError(e.message);
            if (args.verbose) console.error(e.stack);
            process.exit(1);
        }
    } else if (args.run) {
        // 單次執行模式
        try {
            await agent.send(args.run);
        } catch (e) {
            logError(e.message);
            process.exit(1);
        }
    } else {
        // 互動式 REPL
        const repl = new AgentRepl(agent);
        try {
            await repl.start();
        } catch (e) {
            logError(e.message);
            process.exit(1);
        }
    }
}

main().catch((e) => {
    logError(`致命錯誤: ${e.message}`);
    process.exit(1);
});
