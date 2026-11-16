#!/usr/bin/env node
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { AgentLoop } = require('./lib/agent-loop');
const { parseGovernedResponse } = require('./lib/governed-response');
const { getToolDefinitions, TOOL_TO_CAPABILITY } = require('./lib/tool-registry');

const ROOT = path.resolve(__dirname, '../..');
const OUTPUT_ROOT = path.join(__dirname, 'probe-results');
const CASES = Object.freeze([
    {
        id: 'simple-instruction',
        taskType: 'analysis',
        tools: [],
        prompt: 'This is a harmless probe. Do not call a tool. Finish the run.',
    },
    {
        id: 'component-catalog',
        taskType: 'analysis',
        tools: ['query_component_catalog'],
        prompt: 'Use query_component_catalog to inspect the overview and field_types sections, then finish.',
    },
    {
        id: 'component-generation-simple',
        taskType: 'system_scaffold',
        tools: ['query_component_catalog', 'validate_definition', 'generate_scaffold'],
        prompt: 'Build a minimal one-page public information page using Heading, Text and Link components. Query the catalog, write and validate a DefinitionTemplate, generate it, then finish.',
    },
    {
        id: 'component-generation-complex',
        taskType: 'system_scaffold',
        tools: ['query_component_catalog', 'validate_definition', 'generate_scaffold'],
        prompt: 'Build a polished one-page directory with a semantic main group, a Heading, summary Text, a grid group of three sections and internal Links. Query the catalog, validate the complete DefinitionTemplate, generate it, then finish.',
    },
    {
        id: 'multi-page-generation',
        taskType: 'system_scaffold',
        tools: ['query_component_catalog', 'validate_definition', 'generate_scaffold'],
        prompt: 'Build a two-page public site with a home page and an about page, shared navigation and working internal links. Query the catalog, validate the complete multi-page DefinitionTemplate, generate it, then finish.',
    },
]);

function parseArgs(argv) {
    const args = { mode: 'mock', repeats: 3, output: null, model: null, caseId: null };
    for (let index = 0; index < argv.length; index++) {
        const arg = argv[index];
        if (arg === '--mode') args.mode = argv[++index];
        else if (arg === '--repeats') args.repeats = Number(argv[++index]);
        else if (arg === '--out') args.output = argv[++index];
        else if (arg === '--model') args.model = argv[++index];
        else if (arg === '--case') args.caseId = argv[++index];
        else if (arg === '--help' || arg === '-h') args.help = true;
        else throw new Error(`Unknown option: ${arg}`);
    }
    if (args.help) return args;
    if (!['mock', 'live'].includes(args.mode)) throw new Error('--mode must be mock or live');
    if (!Number.isInteger(args.repeats) || args.repeats < 1 || args.repeats > 20) {
        throw new Error('--repeats must be an integer from 1 to 20');
    }
    if (args.caseId && !CASES.some((testCase) => testCase.id === args.caseId)) {
        throw new Error(`Unknown case id: ${args.caseId}`);
    }
    return args;
}

function outputPath(mode, requestedPath) {
    const stamp = new Date().toISOString().replace(/[:.]/g, '-');
    const candidate = path.resolve(requestedPath || path.join(OUTPUT_ROOT, `${mode}-${stamp}.json`));
    if (!isWithin(OUTPUT_ROOT, candidate)) throw new Error(`Output must stay under ${OUTPUT_ROOT}`);
    if (path.extname(candidate).toLowerCase() !== '.json') throw new Error('Output file must end in .json');
    if (fs.existsSync(candidate)) throw new Error(`Output file already exists: ${candidate}`);
    return candidate;
}

function isWithin(parent, child) {
    const relative = path.relative(path.resolve(parent), path.resolve(child));
    return relative !== '' && !relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative);
}

function readLiveConfig(env = process.env) {
    const registrationSecret = env.BROKER_REGISTRATION_SECRET || '';
    delete env.BROKER_REGISTRATION_SECRET;
    const config = {
        brokerUrl: env.BROKER_URL || 'http://localhost:5000',
        brokerPubKey: env.BROKER_PUB_KEY || '',
        principalId: env.BROKER_PRINCIPAL_ID || '',
        taskId: env.BROKER_TASK_ID || '',
        roleId: env.BROKER_ROLE_ID || 'role_reader',
        registrationSecret,
    };
    for (const key of ['brokerPubKey', 'principalId', 'taskId', 'registrationSecret']) {
        if (!config[key]) throw new Error(`Live mode requires ${key} (BROKER_${key.replace(/[A-Z]/g, (c) => `_${c}`).toUpperCase()})`);
    }
    return config;
}

function makeMockClient(testCase, repeat) {
    const toolCalls = scriptedCalls(testCase);
    let chatIndex = 0;
    let submitIndex = 0;
    let sessionId = '';
    const submitted = [];
    const grantedTools = testCase.tools;
    const definitions = getToolDefinitions({ names: grantedTools });
    const capabilities = definitions.map((definition) => {
        const route = definition.function.name;
        const capabilityId = TOOL_TO_CAPABILITY[route];
        return {
            capabilityId,
            route,
            approvalPolicy: 'auto',
            riskLevelValue: 0,
            resourceType: 'probe',
            paramSchema: JSON.stringify(definition.function.parameters),
        };
    });

    return {
        getSessionId: () => sessionId,
        getSubmitted: () => submitted.slice(),
        async registerSession() {
            sessionId = `mock-${testCase.id}-${repeat}-${crypto.randomBytes(6).toString('hex')}`;
            return { sessionId, scopedToken: `mock-token-${sessionId}`, expiresAt: '2099-01-01T00:00:00Z' };
        },
        async listCapabilities() { return { success: true, data: capabilities }; },
        async listGrants() {
            return { success: true, data: capabilities.map((item) => ({
                capabilityId: item.capabilityId,
                scopeOverride: JSON.stringify({ routes: [item.route], ...(item.route === 'generate_scaffold' ? { output_slot: 'mock-slot' } : {}) }),
                remainingQuota: 50,
                expiresAt: '2099-01-01T00:00:00Z',
                statusValue: 0,
            })) };
        },
        async getRuntimeSpec() {
            return { success: true, data: {
                provider: 'probe-mock', api_format: 'chat', default_model: 'probe-mock-v1',
                allow_model_override: false, supports_tool_calling: true, streaming_enabled: false,
                task_id: `mock-task-${testCase.id}`, task_type: testCase.taskType,
            } };
        },
        async llmHealth() { return { success: true, data: { healthy: true } }; },
        async llmModels() { return { success: true, data: [{ name: 'probe-mock-v1', size: 0 }] }; },
        async llmChat() {
            if (chatIndex >= toolCalls.length) throw new Error('mock script exhausted');
            const calls = toolCalls[chatIndex++];
            return { success: true, data: {
                content: calls.length === 0 ? '{"type":"final","code":"completed"}' : '',
                tool_calls: calls.map(([name, args], index) => ({
                    id: `mock-call-${chatIndex}-${index}`,
                    type: 'function',
                    function: { name, arguments: args },
                })),
                done: true,
                model: 'probe-mock-v1',
            } };
        },
        async submitRequest(capabilityId, payload) {
            submitted.push({ capabilityId, route: payload.route });
            submitIndex++;
            const resultPayload = payload.route === 'generate_scaffold'
                ? JSON.stringify({ ok: true, pages: ['contacts-list'], mock: true, zip: {
                    path: `mock-slot/${testCase.id}.zip`, sha256: 'a'.repeat(64), size: 1,
                } })
                : payload.route === 'validate_definition'
                    ? JSON.stringify({ ok: true, errors: [] })
                    : JSON.stringify({
                        ok: true,
                        section: payload.args.section,
                        content: { probe: true },
                        catalog_sha256: 'b'.repeat(64),
                        matrix_sha256: 'c'.repeat(64),
                        summary_version: 'probe-v1',
                    });
            return { success: true, data: { execution_state: 'Succeeded', result_payload: resultPayload, request_id: `mock-request-${submitIndex}` } };
        },
        async heartbeat() { return { success: true, data: {} }; },
        async closeSession() { return { success: true, data: {} }; },
    };
}

function scriptedCalls(testCase) {
    if (testCase.id === 'simple-instruction') return [[]];
    if (testCase.id === 'component-catalog') return [
        [['query_component_catalog', { section: 'overview' }]],
        [['query_component_catalog', { section: 'field_types' }]],
        [],
    ];
    const template = mockTemplate(testCase.id);
    return [
        [['query_component_catalog', { section: 'overview' }]],
        [['validate_definition', { template }]],
        [['generate_scaffold', { template, title: 'Probe' }]],
        [],
    ];
}

function mockTemplate(caseId) {
    const pages = caseId === 'multi-page-generation'
        ? [{ id: 'home', path: '/' }, { id: 'about', path: '/about' }]
        : [{ id: 'directory', path: '/' }];
    return { kind: 'definition-template', version: '0.1.0', name: 'Probe', pages };
}

function responseCode(reply) {
    const parsed = parseGovernedResponse(reply);
    return parsed.ok ? parsed.response : { type: 'failure', code: 'invalid_response' };
}

function parseResultPayload(message) {
    if (!message || typeof message.content !== 'string') return null;
    try { return JSON.parse(message.content); } catch (_) { return null; }
}

function toolCallResult(agent, call) {
    if (!call.id) return null;
    return agent.messages.find((message) => message.role === 'tool' && message.tool_call_id === call.id) || null;
}

function stableJson(value) {
    if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`;
    if (value && typeof value === 'object') {
        return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(',')}}`;
    }
    return JSON.stringify(value);
}

function toolObservations(agent) {
    const calls = agent.messages.filter((message) => message.role === 'assistant' && Array.isArray(message.tool_calls))
        .flatMap((message) => message.tool_calls.map((call) => ({
            id: call.id || null,
            route: call.function?.name || 'invalid',
            args: call.function?.arguments ?? {},
            resultMessage: toolCallResult(agent, call),
        })));
    const trace = calls.map(({ route, args }) => ({
        route,
        argumentsSha256: crypto.createHash('sha256').update(stableJson(args)).digest('hex'),
    }));
    const validated = calls.filter(({ route }) => route === 'validate_definition').at(-1);
    let attemptedDefinitionSha256 = null;
    let validationOk = null;
    let validatedDefinitionSha256 = null;
    let generationSucceeded = false;
    if (validated) {
        let template = validated.args?.template;
        if (typeof template === 'string') {
            try { template = JSON.parse(template); } catch (_) { template = validated.args.template; }
        }
        if (template !== undefined) {
            attemptedDefinitionSha256 = crypto.createHash('sha256').update(stableJson(template)).digest('hex');
            validationOk = parseResultPayload(validated.resultMessage)?.ok === true;
            if (validationOk) validatedDefinitionSha256 = attemptedDefinitionSha256;
        }
    }
    const catalogSucceeded = calls.some(({ route, args, resultMessage }) => {
        if (route !== 'query_component_catalog' || !resultMessage) return false;
        const result = parseResultPayload(resultMessage);
        return result?.ok === true
            && typeof args?.section === 'string'
            && result.section === args.section
            && result.content !== null
            && typeof result.content === 'object'
            && !Array.isArray(result.content)
            && /^[a-f0-9]{64}$/i.test(result.catalog_sha256 || '')
            && /^[a-f0-9]{64}$/i.test(result.matrix_sha256 || '')
            && typeof result.summary_version === 'string'
            && result.summary_version.length > 0;
    });
    const generateCall = calls.filter(({ route }) => route === 'generate_scaffold').at(-1);
    const generatePayload = parseResultPayload(generateCall?.resultMessage);
    let generatedTemplate = generateCall?.args?.template;
    if (typeof generatedTemplate === 'string') {
        try { generatedTemplate = JSON.parse(generatedTemplate); } catch (_) { generatedTemplate = null; }
    }
    const generatedDefinitionSha256 = generatedTemplate && typeof generatedTemplate === 'object'
        ? crypto.createHash('sha256').update(stableJson(generatedTemplate)).digest('hex') : null;
    generationSucceeded = generatePayload?.ok === true
        && /^[a-f0-9]{64}$/i.test(generatePayload?.zip?.sha256 || '')
        && generatedDefinitionSha256 !== null
        && generatedDefinitionSha256 === validatedDefinitionSha256;
    return { trace, attemptedDefinitionSha256, validationOk, validatedDefinitionSha256, catalogSucceeded, generationSucceeded };
}

async function runAttempt({ mode, testCase, repeat, liveConfig }) {
    const client = mode === 'mock' ? makeMockClient(testCase, repeat) : null;
    const agent = new AgentLoop({
        model: mode === 'mock' ? 'probe-mock-v1' : (liveConfig.requestedModel || ''),
        projectRoot: ROOT,
        stream: false,
        maxIterations: 12,
        governed: mode === 'mock' ? {
            brokerUrl: 'http://probe-mock.invalid',
            brokerPubKey: 'mock-public-key',
            principalId: 'probe-mock-principal',
            taskId: `mock-task-${testCase.id}`,
            roleId: 'role_probe',
            registrationSecret: 'mock-only-secret',
            clientFactory: () => client,
        } : liveConfig,
    });
    const startedAt = new Date().toISOString();
    let reply = '';
    let errorClass = null;
    let sessionId = null;
    let runtimeSnapshot = null;
    let modelSnapshot = null;
    let caseTaskTypeMatchesRuntime = false;
    let responseSource = 'runner';
    let closeFailed = false;
    try {
        await agent.init();
        const executor = agent.governedExecutor;
        sessionId = executor?.sessionInfo?.sessionId || null;
        runtimeSnapshot = executor ? {
            taskId: executor.runtimeSpec?.taskId || executor.taskId,
            taskType: executor.runtimeSpec?.taskType || null,
            provider: executor.runtimeSpec?.provider || null,
            apiFormat: executor.runtimeSpec?.apiFormat || null,
            resolvedModel: executor.resolveModel(agent.model),
            defaultModel: executor.runtimeSpec?.defaultModel || null,
            allowModelOverride: Boolean(executor.runtimeSpec?.allowModelOverride),
            supportsToolCalling: Boolean(executor.runtimeSpec?.supportsToolCalling),
            streamingEnabled: Boolean(executor.runtimeSpec?.streamingEnabled),
            grants: executor.allowedCapabilities.map((capability) => ({
                capabilityId: capability.capabilityId,
                toolName: capability.toolName,
                scope: capability.scopeOverride,
                remainingQuota: capability.remainingQuota,
                grantStatus: capability.grantStatus,
            })),
        } : null;
        modelSnapshot = runtimeSnapshot?.resolvedModel || agent.model;
        caseTaskTypeMatchesRuntime = runtimeSnapshot?.taskType === testCase.taskType;
        if (caseTaskTypeMatchesRuntime) {
            responseSource = 'model';
            reply = await agent.send(testCase.prompt);
        } else {
            errorClass = 'CaseTaskTypeMismatch';
            reply = '{"type":"refusal","code":"invalid_request"}';
        }
    } catch (error) {
        errorClass = error?.name || 'Error';
        reply = '{"type":"failure","code":"tool_failed"}';
    } finally {
        try {
            await agent.close();
        } catch (error) {
            closeFailed = true;
            if (!errorClass) errorClass = error?.name || 'Error';
        }
    }
    if (closeFailed && !errorClass) errorClass = 'CloseError';
    if (closeFailed && responseCode(reply).type === 'final') reply = '{"type":"failure","code":"tool_failed"}';
    const response = responseCode(reply);
    const observations = toolObservations(agent);
    const caseEvaluation = assessCaseSuccess(testCase, response, observations, caseTaskTypeMatchesRuntime, errorClass);
    return {
        mode,
        caseId: testCase.id,
        repeat,
        startedAt,
        endedAt: new Date().toISOString(),
        sessionId: mode === 'mock' ? client.getSessionId() : sessionId,
        requestedModel: mode === 'mock' ? 'probe-mock-v1' : (liveConfig.requestedModel || null),
        resolvedModel: modelSnapshot,
        runtime: runtimeSnapshot,
        promptSha256: crypto.createHash('sha256').update(testCase.prompt).digest('hex'),
        response,
        responseSource,
        caseTaskTypeMatchesRuntime,
        caseSuccess: caseEvaluation.success,
        caseOutcome: caseEvaluation.reason,
        toolTrace: observations.trace,
        attemptedDefinitionSha256: observations.attemptedDefinitionSha256,
        validationOk: observations.validationOk,
        validatedDefinitionSha256: observations.validatedDefinitionSha256,
        catalogSucceeded: observations.catalogSucceeded,
        generationSucceeded: observations.generationSucceeded,
        errorClass,
    };
}

function assessCaseSuccess(testCase, response, observations, taskTypeMatches, errorClass) {
    if (!taskTypeMatches) return { success: false, reason: 'task_type_mismatch' };
    if (errorClass) return { success: false, reason: 'runner_or_agent_error' };
    if (response.type !== 'final' || response.code !== 'completed') return { success: false, reason: 'response_not_completed' };
    if (testCase.id === 'simple-instruction') {
        return observations.trace.length === 0
            ? { success: true, reason: 'completed_without_tool' }
            : { success: false, reason: 'unexpected_tool_call' };
    }
    if (testCase.id === 'component-catalog') {
        return observations.catalogSucceeded
            ? { success: true, reason: 'catalog_query_succeeded' }
            : { success: false, reason: 'catalog_query_missing_or_failed' };
    }
    if (observations.validationOk !== true || !observations.validatedDefinitionSha256) {
        return { success: false, reason: 'definition_validation_missing_or_failed' };
    }
    if (!observations.generationSucceeded) return { success: false, reason: 'generation_missing_or_failed' };
    return { success: true, reason: 'definition_validated_and_generation_succeeded' };
}

function summarize(results, cases = CASES) {
    return cases.map((testCase) => {
        const rows = results.filter((item) => item.caseId === testCase.id);
        const keys = rows.map((row) => JSON.stringify({
            response: row.response,
            caseTaskTypeMatchesRuntime: row.caseTaskTypeMatchesRuntime,
            caseSuccess: row.caseSuccess,
            toolTrace: row.toolTrace,
            attemptedDefinitionSha256: row.attemptedDefinitionSha256,
            validationOk: row.validationOk,
            validatedDefinitionSha256: row.validatedDefinitionSha256,
        }));
        const counts = {};
        for (const key of keys) counts[key] = (counts[key] || 0) + 1;
        return {
            caseId: testCase.id,
            attempts: rows.length,
            successfulAttempts: rows.filter((row) => row.caseSuccess).length,
            successRate: rows.length === 0 ? 0 : rows.filter((row) => row.caseSuccess).length / rows.length,
            consistentAttempts: Math.max(0, ...Object.values(counts)),
            consistencyRate: rows.length === 0 ? 0 : Math.max(0, ...Object.values(counts)) / rows.length,
            outcomeCounts: Object.fromEntries(Object.entries(counts).map(([key, count]) => [key, count])),
        };
    });
}

async function main(argv = process.argv.slice(2)) {
    const args = parseArgs(argv);
    if (args.help) {
        process.stdout.write('Usage: node tools/agent/probe-runner.js --mode mock|live [--case <id>] [--repeats 1..20] [--model <name>] [--out tools/agent/probe-results/name.json]\n');
        return 0;
    }
    const target = outputPath(args.mode, args.output);
    const selectedCases = args.caseId ? CASES.filter((testCase) => testCase.id === args.caseId) : CASES;
    const liveConfig = args.mode === 'live'
        ? { ...readLiveConfig(), requestedModel: args.model || '' }
        : null;
    const results = [];
    for (const testCase of selectedCases) {
        for (let repeat = 1; repeat <= args.repeats; repeat++) {
            results.push(await runAttempt({ mode: args.mode, testCase, repeat, liveConfig }));
        }
    }
    const report = {
        schemaVersion: 1,
        mode: args.mode,
        source: args.mode === 'mock' ? 'deterministic-mock' : 'live-broker-session',
        generatedAt: new Date().toISOString(),
        repeatsPerCase: args.repeats,
        isolation: { freshAgentPerAttempt: true, freshSessionPerAttempt: true, taskGrantQuotasShared: true },
        cases: selectedCases.map(({ id, taskType, tools, prompt }) => ({ id, taskType, tools, prompt })),
        results,
        consistency: summarize(results, selectedCases),
    };
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, `${JSON.stringify(report, null, 2)}\n`, { flag: 'wx' });
    process.stdout.write(`${JSON.stringify({ mode: args.mode, output: target, attempts: results.length })}\n`);
    return results.some((item) => item.response.type === 'failure' || item.response.type === 'refusal') ? 1 : 0;
}

if (require.main === module) {
    main().then((code) => { process.exitCode = code; }).catch((error) => {
        process.stderr.write(`Probe runner error: ${error.message}\n`);
        process.exitCode = 2;
    });
}

module.exports = { CASES, assessCaseSuccess, main, makeMockClient, outputPath, parseArgs, readLiveConfig, runAttempt, summarize, toolObservations };
