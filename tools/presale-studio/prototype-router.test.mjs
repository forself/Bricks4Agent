import test from 'node:test';
import assert from 'node:assert/strict';
import { prototypeBuildId, resolvePrototypeRoute } from './prototype-router.mjs';

test('prototype router accepts only explicit methods and resource paths', () => {
    const base = '/api/prototype';
    const resource = '/api/presale/e123456789abc';
    assert.equal(resolvePrototypeRoute('POST', `${base}/demo/session`, base).kind, 'demo-session');
    assert.equal(resolvePrototypeRoute('GET', `${base}/session`, base).kind, 'session');
    assert.equal(resolvePrototypeRoute('POST', `${base}/reset`, base).kind, 'reset');
    assert.equal(resolvePrototypeRoute('PUT', `${base}/reset`, base).kind, 'method-not-allowed');
    const detail = resolvePrototypeRoute('GET', `${base}${resource}/12`, base);
    assert.equal(detail.kind, 'resource');
    assert.equal(detail.path, `${resource}/12`);
    assert.equal(detail.body, 'none');
    assert.deepEqual(detail.query, []);
    assert.equal(resolvePrototypeRoute('POST', `${base}/session`, base).allow, 'GET');
    assert.equal(resolvePrototypeRoute('GET', `${base}${resource}/0`, base).kind, 'not-found');
    assert.equal(resolvePrototypeRoute('GET', `${base}${resource}/1/extra`, base).kind, 'not-found');
    for (const module of ['query', 'lookup', 'summary', 'export']) {
        const route = resolvePrototypeRoute('POST', `${base}${resource}/${module}`, base);
        assert.equal(route.kind, 'read-module');
        assert.equal(route.params.module, module);
        assert.equal(route.session, true);
        assert.equal(resolvePrototypeRoute('GET', `${base}${resource}/${module}`, base).kind, 'method-not-allowed');
    }
    assert.equal(resolvePrototypeRoute('POST', `${base}${resource}/batch`, base).kind, 'batch');
    assert.equal(resolvePrototypeRoute('GET', `${base}${resource}/batch`, base).kind, 'method-not-allowed');
    assert.equal(resolvePrototypeRoute('GET', `${base}/api/presale/other`, base).kind, 'not-found');
    assert.equal(resolvePrototypeRoute('GET', `${base}x/session`, base), null);
});

test('workbench build route captures only the exact bounded id', () => {
    const id = 'a'.repeat(32);
    assert.equal(prototypeBuildId(`/api/prototype/${id}/session`), id);
    assert.equal(prototypeBuildId(`/api/prototype/${id}x/session`), null);
    assert.equal(prototypeBuildId('/api/prototype/short/session'), null);
});
