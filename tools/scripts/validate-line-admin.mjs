import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const html = await readFile('packages/csharp/broker/wwwroot/line-admin.html', 'utf8');

assert.match(html, /data-tab="monitoring"/, 'line-admin must expose a monitoring tab');
assert.match(html, /data-tab="permissions"/, 'line-admin must expose a permissions tab');
assert.match(html, /id="login-username"/, 'line-admin login must accept an operator username');
assert.match(html, /hasPermission\(/, 'line-admin must contain permission-aware UI gating');
assert.match(html, /system\.monitor\.read/, 'line-admin must reference monitoring permission');
assert.match(html, /permission\.operator\.manage/, 'line-admin must reference operator management permission');
assert.match(html, /\/api\/v1\/local-admin\/operators/, 'line-admin must call operator management API');
assert.match(html, /renderOperatorList\(/, 'line-admin must render local admin operators');

console.log('line-admin validation passed');
