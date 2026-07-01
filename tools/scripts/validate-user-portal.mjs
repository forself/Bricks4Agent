#!/usr/bin/env node

import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');

const mimeTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.svg', 'image/svg+xml; charset=utf-8']
]);

function createStaticServer(rootDir) {
    const server = createServer((req, res) => {
        try {
            const requestUrl = new URL(req.url ?? '/', 'http://127.0.0.1');
            const relativePath = decodeURIComponent(requestUrl.pathname).replace(/^\/+/, '');
            const resolvedPath = path.resolve(rootDir, relativePath);

            if (!resolvedPath.startsWith(rootDir)) {
                res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
                res.end('Forbidden');
                return;
            }

            if (!existsSync(resolvedPath) || statSync(resolvedPath).isDirectory()) {
                res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
                res.end('Not Found');
                return;
            }

            const contentType = mimeTypes.get(path.extname(resolvedPath).toLowerCase()) || 'application/octet-stream';
            res.writeHead(200, { 'Content-Type': contentType });
            createReadStream(resolvedPath).pipe(res);
        } catch {
            res.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end('Server Error');
        }
    });

    return new Promise((resolve) => {
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            resolve({ server, baseUrl: `http://127.0.0.1:${address.port}` });
        });
    });
}

async function loadChromium() {
    for (const moduleName of ['@playwright/test', 'playwright']) {
        try {
            const module = await import(moduleName);
            if (module.chromium) return module.chromium;
        } catch {
            // Try next candidate.
        }
    }
    throw new Error('Playwright is not installed. Run npm install first.');
}

function findSystemChromiumExecutable() {
    const candidates = [
        path.join(process.env.ProgramFiles ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe'),
        path.join(process.env['ProgramFiles(x86)'] ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe'),
        path.join(process.env.LOCALAPPDATA ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe'),
        path.join(process.env.ProgramFiles ?? '', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
        path.join(process.env['ProgramFiles(x86)'] ?? '', 'Microsoft', 'Edge', 'Application', 'msedge.exe')
    ];

    return candidates.find((candidate) => candidate && existsSync(candidate)) ?? null;
}

async function launchChromium(chromium) {
    try {
        return await chromium.launch({ headless: true });
    } catch (error) {
        const systemExecutable = findSystemChromiumExecutable();
        if (!systemExecutable || !(error?.message ?? '').includes('Executable doesn')) {
            throw error;
        }
        return await chromium.launch({ headless: true, executablePath: systemExecutable });
    }
}

async function main() {
    console.log('Running user portal validation...\n');
    const chromium = await loadChromium();
    const { server, baseUrl } = await createStaticServer(repoRoot);
    const browser = await launchChromium(chromium);
    const context = await browser.newContext({ viewport: { width: 1366, height: 900 } });

    const requests = [];
    let authenticated = false;
    let commandSubmitted = false;

    try {
        const page = await context.newPage();
        const errors = [];
        page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));
        page.on('console', (msg) => {
            if (msg.type() === 'error') errors.push(`console: ${msg.text()}`);
        });

        await page.route('**/api/v1/portal/**', async (route) => {
            const request = route.request();
            const url = new URL(request.url());
            const apiPath = url.pathname.replace('/api/v1/portal', '');
            requests.push(`${request.method()} ${apiPath}`);

            const ok = (data) => route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({ success: true, message: 'ok', data, traceId: 'test' })
            });

            if (apiPath === '/auth/status') {
                return ok({
                    authenticated,
                    userId: authenticated ? 'portal-user' : '',
                    displayName: authenticated ? 'Portal User' : '',
                    accessTier: authenticated ? 'basic' : '',
                    registrationStatus: authenticated ? 'approved' : '',
                    selfRegistrationEnabled: true,
                    line_verification: authenticated
                        ? {
                            user_id: 'portal-user',
                            verified: false,
                            line_user_id: '',
                            expires_at: null,
                            verified_at: null,
                            command_template: '/verify <user_id> <code>'
                        }
                        : null
                });
            }

            if (apiPath === '/auth/login' && request.method() === 'POST') {
                authenticated = true;
                return ok({
                    authenticated: true,
                    user_id: 'portal-user',
                    display_name: 'Portal User',
                    line_verification: {
                        user_id: 'portal-user',
                        verified: false,
                        command_template: '/verify <user_id> <code>'
                    }
                });
            }

            if (apiPath === '/auth/line-verification' && request.method() === 'POST') {
                return ok({
                    user_id: 'portal-user',
                    code: '123456',
                    expires_at: '2026-06-26T10:10:00Z',
                    command: '/verify portal-user 123456'
                });
            }

            if (apiPath === '/me') {
                return ok({
                    profile: {
                        user_id: 'portal-user',
                        display_name: 'Portal User',
                        access_tier: 'basic',
                        registration_status: 'approved',
                        user_code: 'demo'
                    },
                    line_verification: {
                        user_id: 'portal-user',
                        verified: false,
                        line_user_id: '',
                        expires_at: null,
                        verified_at: null,
                        command_template: '/verify <user_id> <code>'
                    },
                    draft: null
                });
            }

            if (apiPath === '/results') {
                return ok({
                    user_id: 'portal-user',
                    total: commandSubmitted ? 2 : 1,
                    items: [
                        {
                            interaction_id: 'hlm-1',
                            occurred_at: '2026-06-26T10:00:00Z',
                            user_message: commandSubmitted ? '請建立摘要' : '?profile',
                            reply: commandSubmitted ? '已收到摘要需求。' : '目前帳戶狀態正常。',
                            route_mode: 'conversation',
                            workflow_action: '',
                            decision_reason: 'test'
                        }
                    ]
                });
            }

            if (apiPath === '/artifacts') {
                return ok({
                    user_id: 'portal-user',
                    total: 1,
                    items: [
                        {
                            artifact_id: 'art-1',
                            document_id: 'doc-1',
                            file_name: 'result.md',
                            format: 'markdown',
                            overall_status: 'ready',
                            created_at: '2026-06-26T10:00:00Z',
                            download_url: '/api/v1/artifacts/download/art-1?exp=1&sig=test'
                        }
                    ]
                });
            }

            if (apiPath === '/commands' && request.method() === 'POST') {
                commandSubmitted = true;
                return ok({ result: { reply: '已收到摘要需求。' }, artifacts: [] });
            }

            return route.fulfill({
                status: 404,
                contentType: 'application/json',
                body: JSON.stringify({ success: false, message: `Unexpected route ${apiPath}` })
            });
        });

        await page.goto(`${baseUrl}/packages/javascript/browser/user-portal/index.html`, {
            waitUntil: 'load',
            timeout: 15000
        });
        await page.getByTestId('auth-screen').waitFor({ timeout: 10000 });
        await page.getByPlaceholder('user@example.com').fill('portal-user');
        await page.getByPlaceholder('至少 8 個字元').fill('correct-horse-battery');
        await page.locator('.portal-auth__actions button').click();
        await page.getByTestId('portal-shell').waitFor({ timeout: 10000 });
        await page.getByText('目前帳戶狀態正常。').waitFor({ timeout: 10000 });
        await page.getByText('result.md').waitFor({ timeout: 10000 });
        await page.getByTestId('line-verification-panel').locator('button').click();
        await page.getByTestId('line-verification-command').waitFor({ timeout: 10000 });
        await page.getByText('/verify portal-user 123456').waitFor({ timeout: 10000 });

        await page.getByPlaceholder('輸入需求或指令').fill('請建立摘要');
        await page.getByRole('button', { name: '送出需求' }).click();
        await page.getByText('已收到摘要需求。').waitFor({ timeout: 10000 });

        if (errors.length > 0) {
            throw new Error(errors.join('\n'));
        }

        for (const expected of ['GET /auth/status', 'POST /auth/login', 'GET /me', 'GET /results', 'GET /artifacts', 'POST /auth/line-verification', 'POST /commands']) {
            if (!requests.includes(expected)) {
                throw new Error(`Expected request was not observed: ${expected}`);
            }
        }

        console.log('Validation summary:');
        console.log('- Auth screen render: passed');
        console.log('- Login and dashboard render: passed');
        console.log('- LINE verification UI flow: passed');
        console.log('- Command submit flow: passed');
        console.log(`- API routes observed: ${requests.join(', ')}`);
    } finally {
        await context.close();
        await browser.close();
        await new Promise((resolve, reject) => {
            server.close((error) => {
                if (error) reject(error);
                else resolve();
            });
        });
    }
}

main().catch((error) => {
    console.error(`\nValidation failed: ${error.message}`);
    process.exitCode = 1;
});
