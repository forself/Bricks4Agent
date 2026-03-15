#!/usr/bin/env node
'use strict';

const http = require('http');

const port = Number(process.env.PORT || '11434');
const model = process.env.MOCK_MODEL || 'stack-test-model';
const responseText = process.env.MOCK_RESPONSE_TEXT || 'STACK_OK';

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
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({
                model: body.model || model,
                message: {
                    content: responseText,
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
