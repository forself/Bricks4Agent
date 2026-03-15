#!/usr/bin/env node
'use strict';

const http = require('http');

const port = Number(process.env.PORT || '8080');
const model = process.env.MOCK_MODEL || 'openai-proxy-dev';
const responseText = process.env.MOCK_RESPONSE_TEXT || 'STACK_OK';
const expectedApiKey = process.env.EXPECTED_API_KEY || '';

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

function isAuthorized(req) {
    if (!expectedApiKey) {
        return true;
    }

    return req.headers.authorization === `Bearer ${expectedApiKey}`;
}

function writeUnauthorized(res) {
    res.writeHead(401, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: { message: 'unauthorized' } }));
}

const server = http.createServer(async (req, res) => {
    try {
        if (!isAuthorized(req)) {
            writeUnauthorized(res);
            return;
        }

        if (req.method === 'GET' && req.url === '/v1/models') {
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                object: 'list',
                data: [{ id: model, object: 'model' }],
            }));
            return;
        }

        if (req.method === 'POST' && req.url === '/v1/chat/completions') {
            const body = await readJson(req);
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                id: 'chatcmpl_mock',
                object: 'chat.completion',
                model: body.model || model,
                choices: [
                    {
                        index: 0,
                        finish_reason: 'stop',
                        message: {
                            role: 'assistant',
                            content: responseText,
                            tool_calls: [],
                        },
                    },
                ],
                usage: {
                    completion_tokens: 1,
                },
            }));
            return;
        }

        if (req.method === 'POST' && req.url === '/v1/responses') {
            const body = await readJson(req);
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                id: 'resp_mock',
                object: 'response',
                model: body.model || model,
                output_text: responseText,
                output: [],
                usage: {
                    output_tokens: 1,
                },
            }));
            return;
        }

        res.writeHead(404, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({ error: { message: 'not found' } }));
    } catch (error) {
        res.writeHead(500, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
            error: {
                message: error instanceof Error ? error.message : String(error),
            },
        }));
    }
});

server.listen(port, '0.0.0.0', () => {
    console.log(`Mock OpenAI-compatible upstream listening on ${port}`);
});
