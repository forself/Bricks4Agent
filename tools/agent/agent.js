#!/usr/bin/env node
'use strict';

const { AgentLoop } = require('./lib/agent-loop');
const { AgentRepl } = require('./lib/repl');
const { StateMachine } = require('./lib/state-machine');
const { buildCrudPipeline } = require('./lib/pipelines/crud-pipeline');
const { runPipelines } = require('./lib/pipelines/pipeline-runner');
const { createProvider, listProviders } = require('./lib/providers/provider-factory');
const { resolveProjectRoot, bold, logInfo, logError, logWarn } = require('./lib/utils');

function parseArgs(argv) {
    const args = {
        run: null,
        generate: false,
        pipeline: null,
        pipelineEntity: null,
        pipelineFields: null,
        pipelinePath: null,
        dryRun: false,
        validateOnly: false,
        force: false,
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
        governed: false,
        brokerUrl: null,
        brokerPubKey: null,
        principalId: null,
        taskId: null,
        roleId: null,
    };

    for (let i = 0; i < argv.length; i++) {
        switch (argv[i]) {
            case '--run':
            case '-r':
                args.run = argv[++i] || '';
                break;
            case '--model':
            case '-m':
                args.model = argv[++i] || args.model;
                break;
            case '--host':
            case '-H':
                args.host = argv[++i] || args.host;
                break;
            case '--provider':
            case '-P':
                args.provider = argv[++i] || args.provider;
                break;
            case '--api-key':
            case '-k':
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
            case '--verbose':
            case '-v':
                args.verbose = true;
                break;
            case '--list-models':
                args.listModels = true;
                break;
            case '--help':
            case '-h':
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
            case '--generate':
            case '-g':
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
            case '--governed':
                args.governed = true;
                break;
            case '--broker-url':
                args.brokerUrl = argv[++i] || '';
                break;
            case '--broker-pub-key':
                args.brokerPubKey = argv[++i] || '';
                break;
            case '--principal-id':
                args.principalId = argv[++i] || '';
                break;
            case '--task-id':
                args.taskId = argv[++i] || '';
                break;
            case '--role-id':
                args.roleId = argv[++i] || '';
                break;
        }
    }

    return args;
}

function showHelp() {
    const providers = listProviders().join(', ');
    console.log(`
${bold('AI Agent CLI')}

Usage:
  node agent.js [options]

General:
  --run, -r "<prompt>"     Run a single prompt
  --model, -m <name>       Model name (default: llama3.1)
  --provider, -P <type>    Provider: ${providers}
  --api-key, -k <key>      Provider API key
  --host, -H <url>         Custom provider base URL
  --no-stream              Disable streaming output
  --force-react            Force ReAct XML tool mode
  --force-native           Force native tool calling mode
  --max-iterations <n>     Max agent iterations (default: 20)
  --no-confirm             Skip confirmation prompts
  --verbose, -v            Verbose logging
  --list-models            List available models
  --help, -h               Show help
  --version                Show version

Pipelines:
  --generate, -g           Run project.json generation pipeline
  --pipeline crud          Run CRUD pipeline
  --entity <Name>          Entity name for CRUD pipeline
  --fields '<json>'        Field definition JSON
  --project-path <path>    Target project path
  --dry-run                Validate generated plan without writing
  --validate               Validate generation pipeline only
  --force                  Overwrite existing generated artifacts

Governed mode:
  --governed
  --broker-url <url>
  --broker-pub-key <base64>
  --principal-id <id>
  --task-id <id>
  --role-id <id>

Examples:
  node agent.js
  node agent.js --run "Read AGENT.md and summarize the constraints"
  node agent.js -P openai -k sk-xxx -m gpt-5
  node agent.js --governed --broker-url http://localhost:5000 --broker-pub-key <base64> --principal-id prn_x --task-id task_x --role-id role_reader --run "Inspect the repo"
`);
}

function getGovernedConfig(args) {
    if (!args.governed) {
        return null;
    }

    const brokerUrl = args.brokerUrl || process.env.BROKER_URL || 'http://localhost:5000';
    const brokerPubKey = args.brokerPubKey || process.env.BROKER_PUB_KEY || '';
    const principalId = args.principalId || process.env.BROKER_PRINCIPAL_ID || '';
    const taskId = args.taskId || process.env.BROKER_TASK_ID || '';
    const roleId = args.roleId || process.env.BROKER_ROLE_ID || 'role_reader';

    if (!brokerPubKey) {
        throw new Error('Governed mode requires --broker-pub-key or BROKER_PUB_KEY');
    }
    if (!principalId) {
        throw new Error('Governed mode requires --principal-id or BROKER_PRINCIPAL_ID');
    }
    if (!taskId) {
        throw new Error('Governed mode requires --task-id or BROKER_TASK_ID');
    }

    return { brokerUrl, brokerPubKey, principalId, taskId, roleId };
}

function createAgent(args, projectRoot, governedConfig, provider) {
    return new AgentLoop({
        model: args.model,
        provider,
        projectRoot,
        stream: args.stream,
        noConfirm: args.noConfirm,
        verbose: args.verbose,
        maxIterations: args.maxIterations,
        forceStrategy: args.forceStrategy,
        governed: governedConfig,
    });
}

async function printModels(agentOrProvider, governed) {
    const models = await agentOrProvider.listModels();
    if (models.length === 0) {
        console.log('No models returned.');
        return;
    }

    const label = governed ? agentOrProvider.name : agentOrProvider.name;
    console.log(bold(`Available models (${label}):`));
    for (const model of models) {
        const size = model.size ? ` (${(model.size / 1024 / 1024 / 1024).toFixed(1)} GB)` : '';
        console.log(`  ${model.name}${size}`);
    }
}

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

    let governedConfig;
    try {
        governedConfig = getGovernedConfig(args);
    } catch (e) {
        logError(e.message);
        process.exit(1);
    }

    if (governedConfig) {
        logInfo(`Governed mode enabled: broker=${governedConfig.brokerUrl}, principal=${governedConfig.principalId}, task=${governedConfig.taskId}`);
        if (args.provider || args.host || args.apiKey) {
            logWarn('Direct provider/api-key/host options are ignored in governed mode.');
        }
    }

    let provider = null;
    if (!governedConfig) {
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
    }

    const projectRoot = resolveProjectRoot();
    if (args.verbose) {
        logInfo(`Project root: ${projectRoot}`);
    }

    if (args.generate && (args.validateOnly || args.dryRun)) {
        try {
            if (!args.pipelinePath) {
                throw new Error('Generation validation requires --project-path');
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

    if (args.listModels) {
        try {
            if (governedConfig) {
                const agent = createAgent(args, projectRoot, governedConfig, null);
                await agent.init();
                await printModels(agent.provider, true);
                await agent.close();
            } else {
                await printModels(provider, false);
            }
        } catch (e) {
            logError(e.message);
            process.exit(1);
        }
        return;
    }

    const agent = createAgent(args, projectRoot, governedConfig, provider);

    if (args.generate) {
        try {
            if (!args.pipelinePath) {
                throw new Error('Generation requires --project-path');
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
        } finally {
            await agent.close();
        }
        return;
    }

    if (args.pipeline) {
        try {
            if (args.pipeline !== 'crud') {
                throw new Error(`Unsupported pipeline: ${args.pipeline}`);
            }
            if (!args.pipelineEntity) {
                throw new Error('CRUD pipeline requires --entity');
            }

            let fields;
            try {
                fields = JSON.parse(args.pipelineFields || '[]');
            } catch {
                throw new Error('--fields must be valid JSON');
            }

            if (fields.length === 0) {
                fields = [{ name: 'Name', type: 'string' }];
                logInfo('No fields provided, using default Name:string');
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
        } finally {
            await agent.close();
        }
        return;
    }

    if (args.run) {
        try {
            await agent.send(args.run);
        } catch (e) {
            logError(e.message);
            process.exit(1);
        } finally {
            await agent.close();
        }
        return;
    }

    const repl = new AgentRepl(agent);
    if (governedConfig) {
        const cleanup = async () => {
            await agent.close();
        };
        process.on('SIGINT', async () => {
            await cleanup();
            process.exit(0);
        });
        process.on('SIGTERM', async () => {
            await cleanup();
            process.exit(0);
        });
    }

    try {
        await repl.start();
    } catch (e) {
        logError(e.message);
        process.exit(1);
    } finally {
        await agent.close();
    }
}

main().catch((e) => {
    logError(`Fatal error: ${e.message}`);
    process.exit(1);
});
