// Explicit route table for the isolated prototype API. Node's http module
// supplies the server, but it does not supply routing.
const BUILD_PATH = /^\/api\/prototype\/([a-f0-9]{32})(?:\/|$)/;
const RESOURCE_PATH = /^\/api\/presale\/e[a-f0-9]{12}(?:\/(?<id>[1-9]\d*))?$/;
const READ_MODULE_PATH = /^\/api\/presale\/e[a-f0-9]{12}\/(?<module>query|lookup|summary|export)$/;
const BATCH_PATH = /^\/api\/presale\/e[a-f0-9]{12}\/batch$/;

import { NativeRouter } from './native-router.mjs';

const ROUTES = new NativeRouter()
    .register({ path: '/auth/exchange', methods: ['POST'], kind: 'auth-exchange', body: 'json' })
    .register({ path: '/auth/logout', methods: ['POST'], kind: 'auth-logout', body: 'json', session: true })
    .register({ path: '/demo/session', methods: ['POST'], kind: 'demo-session', body: 'json' })
    .register({ path: '/session', methods: ['GET'], kind: 'session', session: true })
    .register({ path: '/reset', methods: ['POST'], kind: 'reset', body: 'json', session: true })
    .register({ path: READ_MODULE_PATH, methods: ['POST'], kind: 'read-module', body: 'json', session: true })
    .register({ path: BATCH_PATH, methods: ['POST'], kind: 'batch', body: 'json', session: true })
    .register({ path: RESOURCE_PATH, methods: ['GET', 'POST', 'PUT', 'DELETE'], kind: 'resource',
        session: true, body: ({ method }) => ['POST', 'PUT'].includes(method) ? 'json' : 'none',
        query: ({ method, match }) => method === 'GET' && !match.groups?.id
            ? [{ name: 'page', type: 'integer', min: 1, max: 1_000_000 },
                { name: 'pageSize', type: 'integer', min: 1, max: 100 },
                { name: /^F_[A-F0-9]{12}$/, type: 'string', maxLength: 100 }] : [] });

export function prototypeBuildId(pathname) {
    return BUILD_PATH.exec(pathname)?.[1] ?? null;
}

export function resolvePrototypeRoute(method, pathname, base) {
    if (pathname !== base && !pathname.startsWith(`${base}/`)) return null;
    const suffix = pathname.slice(base.length);
    return ROUTES.resolve(method, suffix);
}
