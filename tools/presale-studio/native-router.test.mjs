import test from 'node:test';
import assert from 'node:assert/strict';
import { Readable } from 'node:stream';
import { checkSameOrigin, guardRoute, inspectRequest, NativeRouter, readJsonBody, RequestLimiter,
    validateQuery } from './native-router.mjs';

const response = () => ({ headers: new Map(), setHeader(name, value) { this.headers.set(name, value); } });
const request = (url = '/', overrides = {}) => ({
    url, method: 'GET', headers: { host: '127.0.0.1:3092' },
    rawHeaders: ['Host', '127.0.0.1:3092'], socket: { remoteAddress: '127.0.0.1' },
    ...overrides
});
const rejected = (url, status = 400) => assert.throws(() => inspectRequest(request(url), response()),
    error => error.status === status);

test('request gate rejects ambiguous paths, malformed encoding and duplicate query keys', () => {
    for (const url of ['/a/../b', '/a/%2e%2e/b', '/a/%2F/b', '/a/%5c/b', '/a//b',
        '/a/%00/b', '/a/%ZZ', '/a?name=%ZZ', '/a?x=1&x=2']) rejected(url);
    rejected('//outside.invalid/a', 414);
    const res = response();
    const value = inspectRequest(request('/api/presale/e123456789abc?F_123456789ABC=%E8%AA%B2%E7%A8%8B'), res);
    assert.equal(value.pathname, '/api/presale/e123456789abc');
    assert.equal(value.query.get('F_123456789ABC'), '課程');
    assert.equal(res.headers.get('X-Content-Type-Options'), 'nosniff');
    assert.equal(res.headers.get('Referrer-Policy'), 'no-referrer');
    assert.equal(res.headers.get('X-Frame-Options'), 'SAMEORIGIN');
    assert.throws(() => validateQuery(value.query, ['page']), error => error.status === 400);
});

test('host, duplicate headers, origin and fetch metadata are checked centrally', () => {
    assert.throws(() => inspectRequest(request('/', { headers: { host: 'outside.invalid:3092' } }), response()),
        error => error.status === 403);
    assert.throws(() => inspectRequest(request('/', { rawHeaders: [
        'Host', '127.0.0.1:3092', 'hOsT', '127.0.0.1:3092'
    ] }), response()), error => error.status === 400);
    assert.throws(() => inspectRequest(request('/', { headers: { host: '127.0.0.1:3092',
        'content-length': '2', 'transfer-encoding': 'chunked' } }), response()),
    error => error.status === 400);
    const post = request('/', { method: 'POST', headers: {
        host: '127.0.0.1:3092', origin: 'http://outside.invalid' } });
    assert.throws(() => checkSameOrigin(post, post.headers.host), error => error.status === 403);
    post.headers.origin = 'http://127.0.0.1:3092';
    post.headers['sec-fetch-site'] = 'cross-site';
    assert.throws(() => checkSameOrigin(post, post.headers.host), error => error.status === 403);
});

test('native JSON reader enforces media type, size and object shape', async () => {
    const body = (text, headers = {}) => Object.assign(Readable.from([Buffer.from(text)]), {
        headers: { 'content-type': 'application/json', ...headers }
    });
    assert.deepEqual(await readJsonBody(body('{"ok":true}')), { ok: true });
    await assert.rejects(readJsonBody(body('[]')), error => error.status === 400);
    await assert.rejects(readJsonBody(body('{')), error => error.status === 400);
    await assert.rejects(readJsonBody(Object.assign(Readable.from([Buffer.from([0x7b, 0x22, 0x61,
        0x22, 0x3a, 0x22, 0xc3, 0x28, 0x22, 0x7d])]), {
        headers: { 'content-type': 'application/json' }
    })), error => error.status === 400);
    await assert.rejects(readJsonBody(body('{"__proto__":{}}')), error => error.status === 400);
    await assert.rejects(readJsonBody(body('{"a":1,"a":2}')), error => error.status === 400);
    await assert.rejects(readJsonBody(body('{"a":1,"\\u0061":2}')), error => error.status === 400);
    assert.deepEqual(await readJsonBody(body('{"a,b":{"text":"quoted \\\"value\\\""}}')),
        { 'a,b': { text: 'quoted "value"' } });
    await assert.rejects(readJsonBody(body('{"large":1e999}')), error => error.status === 400);
    await assert.rejects(readJsonBody(body('{}', { 'content-type': 'text/plain' })), error => error.status === 415);
    await assert.rejects(readJsonBody(body('{"too":"long"}'), { maxBytes: 4 }), error => error.status === 413);
    await assert.rejects(readJsonBody(body('{}', { 'content-length': '3' })), error => error.status === 400);
});

test('native request limiter bounds repeated requests without external packages', () => {
    const limiter = new RequestLimiter({ maxRequests: 2, windowMs: 60_000 });
    const req = request();
    limiter.check(req);
    limiter.check(req);
    assert.throws(() => limiter.check(req), error => error.status === 429);
    limiter.clear();
    limiter.check(req);
});

test('route table applies exact methods, typed parameters and per-route policies', () => {
    const router = new NativeRouter()
        .register({ path: '/health', methods: ['GET'], kind: 'health' })
        .register({ path: /^\/records\/(?<id>[1-9]\d*)$/, methods: ['GET', 'PUT'], kind: 'record',
            session: true, body: ({ method }) => method === 'PUT' ? 'json' : 'none' });
    assert.equal(router.resolve('GET', '/health').kind, 'health');
    assert.equal(router.resolve('PUT', '/records/12').params.id, '12');
    assert.equal(router.resolve('PUT', '/records/12').body, 'json');
    assert.equal(router.resolve('POST', '/records/12').allow, 'GET, PUT');
    assert.equal(router.resolve('GET', '/records/0').kind, 'not-found');
    assert.throws(() => new NativeRouter().register({ path: /records/, methods: ['GET'], kind: 'bad' }),
        TypeError);
});

test('route guard composes origin, query, authentication and body checks', async () => {
    const req = request('/items?page=1', { method: 'POST',
        headers: { host: '127.0.0.1:3092', 'content-type': 'application/json' },
        [Symbol.asyncIterator]: async function* () { yield Buffer.from('{"ok":true}'); } });
    const context = inspectRequest(req, response());
    const result = await guardRoute(req, context, { query: ['page'], body: 'json',
        authorize: () => ({ code: 'DEMO' }) });
    assert.equal(result.actor.code, 'DEMO');
    assert.deepEqual(result.body, { ok: true });
    await assert.rejects(guardRoute(request('/items', { method: 'DELETE', headers: {
        host: '127.0.0.1:3092', 'content-length': '2' } }),
    inspectRequest(request('/items'), response())), error => error.status === 400);
});
