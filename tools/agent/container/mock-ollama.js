#!/usr/bin/env node
'use strict';

const http = require('http');

const port = Number(process.env.PORT || '11434');
const model = process.env.MOCK_MODEL || 'stack-test-model';
const responseText = process.env.MOCK_RESPONSE_TEXT || 'STACK_OK';
const toolCall = process.env.MOCK_TOOL_CALL || '';
const toolPath = process.env.MOCK_TOOL_PATH || 'README.html';
// Generic tool args (JSON). Falls back to {path: toolPath} for read_file back-compat.
let toolArgs = {};
try { toolArgs = process.env.MOCK_TOOL_ARGS_JSON ? JSON.parse(process.env.MOCK_TOOL_ARGS_JSON) : {}; }
catch (_) { toolArgs = {}; }

function parseJsonArray(name) {
    try {
        const value = process.env[name] ? JSON.parse(process.env[name]) : [];
        return Array.isArray(value) ? value : [];
    } catch (_) {
        console.error(`${name} is not valid JSON; ignoring it.`);
        return [];
    }
}

// Optional multi-step script: [{ "name": "apply_patch", "args": {...} }, ...]. The n-th tool
// call is returned once n tool results are in the conversation. Without it, MOCK_TOOL_CALL
// (one call) is used as before.
const toolSequence = parseJsonArray('MOCK_TOOL_SEQUENCE_JSON');
// Optional proof that tools really ran: entry i is a substring (or an array of substrings)
// that the i-th tool result must contain. When set, the final answer is
// "<MOCK_RESPONSE_TEXT> TOOL_RESULT_VERIFIED" or "TOOL_RESULT_MISMATCH ..." with a snippet.
const toolResultExpectations = parseJsonArray('MOCK_EXPECT_TOOL_RESULTS_JSON');

function plannedSteps() {
    if (toolSequence.length > 0) {
        return toolSequence.map((step) => ({ name: step.name, args: step.args || {} }));
    }
    if (!toolCall) {
        return [];
    }
    // read_file keeps its {path} shorthand; other tools use MOCK_TOOL_ARGS_JSON.
    return [{ name: toolCall, args: toolCall === 'read_file' ? { path: toolPath } : toolArgs }];
}

function toolResults(body) {
    return Array.isArray(body.messages)
        ? body.messages.filter((message) => message && message.role === 'tool')
            .map((message) => (typeof message.content === 'string' ? message.content : JSON.stringify(message.content)))
        : [];
}

function finalText(results) {
    if (toolResultExpectations.length === 0) {
        return responseText;
    }
    for (let index = 0; index < toolResultExpectations.length; index += 1) {
        const expected = [].concat(toolResultExpectations[index]).map(String);
        const actual = results[index];
        const missing = actual === undefined ? expected : expected.filter((needle) => !actual.includes(needle));
        if (missing.length > 0) {
            const snippet = (actual === undefined ? '(no tool result)' : actual).replace(/\s+/g, ' ').slice(0, 400);
            return `TOOL_RESULT_MISMATCH step=${index} missing=${JSON.stringify(missing)} result=${snippet}`;
        }
    }
    return `${responseText} TOOL_RESULT_VERIFIED`;
}

function readJson(req) {
    return new Promise((resolve, reject) => {
        let data = '';
        req.on('data', (chunk) => {
            data += chunk;
        });
        req.on('end', () => {
            try {
                resolve(data ? JSON.parse(data) : {});
            } catch (error) {
                reject(error);
            }
        });
        req.on('error', reject);
    });
}

function requestIncludesTool(body, name) {
    return Array.isArray(body.tools) &&
        body.tools.some((tool) => {
            const fn = tool.function || tool;
            return fn && fn.name === name;
        });
}

const server = http.createServer(async (req, res) => {
    try {
        if (req.method === 'GET' && req.url === '/') {
            res.writeHead(200, { 'Content-Type': 'text/plain' });
            res.end('ok');
            return;
        }

        if (req.method === 'GET' && req.url === '/api/tags') {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                models: [
                    {
                        name: model,
                        size: 1024,
                    },
                ],
            }));
            return;
        }

        if (req.method === 'POST' && req.url === '/api/chat') {
            const body = await readJson(req);
            const steps = plannedSteps();
            const results = toolResults(body);
            const next = steps[results.length];
            if (next && requestIncludesTool(body, next.name)) {
                res.writeHead(200, { 'Content-Type': 'application/json' });
                res.end(JSON.stringify({
                    model: body.model || model,
                    message: {
                        content: '',
                        tool_calls: [
                            {
                                id: `call_mock_${next.name}_${results.length}`,
                                function: {
                                    name: next.name,
                                    arguments: next.args,
                                },
                            },
                        ],
                        thinking: '',
                    },
                    total_duration: 1,
                    eval_count: 1,
                }));
                return;
            }

            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                model: body.model || model,
                message: {
                    content: finalText(results),
                    tool_calls: [],
                    thinking: '',
                },
                total_duration: 1,
                eval_count: 1,
            }));
            return;
        }

        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: 'not found' }));
    } catch (error) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
            error: error instanceof Error ? error.message : String(error),
        }));
    }
});

server.listen(port, '0.0.0.0', () => {
    console.log(`Mock Ollama listening on ${port}`);
});
