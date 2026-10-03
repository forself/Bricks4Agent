// Real browser regression; deliberately no CSP so it cannot mask sanitizer bugs.
// Test-only dependency: playwright/playwright-core (normal Node/NODE_PATH resolution).
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import path from 'node:path';

const require = createRequire(import.meta.url);
let chromium;
for (const name of ['playwright', 'playwright-core']) {
    try { ({ chromium } = require(name)); break; } catch { /* next test dependency */ }
}
if (!chromium) throw new Error('Install test-only playwright-core or expose it through NODE_PATH.');
const root = path.resolve(import.meta.dirname, '../..');
const server = createServer(async (req, res) => {
    try {
        const pathname = decodeURIComponent(new URL(req.url, 'http://127.0.0.1').pathname);
        if (pathname === '/') {
            res.writeHead(200, { 'Content-Type': 'text/html' });
            res.end('<!doctype html><title>Sanitizer regression</title><body></body>');
            return;
        }
        if (pathname === '/favicon.ico') { res.writeHead(204).end(); return; }
        const file = path.resolve(root, '.' + pathname);
        const allowed = file.startsWith(path.join(root, 'packages/javascript/browser/ui_components') + path.sep)
            || file === path.join(root, 'templates/spa/frontend/components/utils/security.js');
        if (!allowed || !['.js', '.css'].includes(path.extname(file))) { res.writeHead(404).end(); return; }
        const body = await readFile(file);
        res.writeHead(200, { 'Content-Type': path.extname(file) === '.js' ? 'text/javascript' : 'text/css' });
        res.end(body);
    } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
let browser;
try {
    const origin = `http://127.0.0.1:${server.address().port}`;
    browser = await chromium.launch({ channel: 'msedge', headless: true });
    const page = await browser.newPage();
    const failures = [];
    page.on('pageerror', error => failures.push(error.message));
    page.on('response', response => { if (response.status() >= 400) failures.push(`HTTP ${response.status()}: ${response.url()}`); });
    await page.route('**/*', route => {
        if (new URL(route.request().url()).origin !== origin) {
            failures.push('Unexpected external request');
            return route.abort();
        }
        return route.continue();
    });
    await page.goto(origin);
    const checks = await page.evaluate(async () => {
        const { sanitizeHTML } = await import('/packages/javascript/browser/ui_components/utils/security.js');
        const { sanitizeHTML: templateSanitize } = await import('/templates/spa/frontend/components/utils/security.js');
        const { WebTextEditor } = await import('/packages/javascript/browser/ui_components/editor/WebTextEditor/WebTextEditor.js');
        const checks = [];
        const host = document.createElement('div');
        document.body.append(host);
        window.__sanitizerProbe = 0;
        host.innerHTML = '<span onclick="window.__sanitizerProbe=1">control</span>';
        host.firstChild.click();
        checks.push({ name: 'positive control: inline event executes without CSP', pass: window.__sanitizerProbe === 1 });
        host.replaceChildren();
        const payloads = [
            '<section><img onerror="window.__sanitizerProbe=1"></section>',
            '<main><section><p onclick="window.__sanitizerProbe=1">nested</p></section></main>',
            '<section><a href="java&#9;script:window.__sanitizerProbe=1">link</a></section>',
            '<section><a href="java&#10;script:window.__sanitizerProbe=1">link</a></section>',
            '<section><a href="data:text/html,active">link</a><img src="data:image/svg+xml;base64,PHN2Zz4="></section>',
            '<figure><svg onload="window.__sanitizerProbe=1"></svg><math><mtext><img onerror="window.__sanitizerProbe=1"></mtext></math></figure>',
            '<article><iframe srcdoc="active"></iframe><template><img onerror="window.__sanitizerProbe=1"></template></article>',
            '<section><p style="color:red" contenteditable="true">text</p><a ping="/ping">link</a></section>',
        ];
        const safe = node => [...node.querySelectorAll('*')].every(el =>
            !['SCRIPT', 'IFRAME', 'SVG', 'MATH', 'OBJECT', 'EMBED', 'TEMPLATE', 'STYLE'].includes(el.tagName)
            && [...el.attributes].every(a => !/^on/i.test(a.name)
                && !['style', 'srcdoc', 'srcset', 'ping', 'contenteditable'].includes(a.name)
                && (!['href', 'src'].includes(a.name) || !/^(javascript:|vbscript:|data:text|data:image\/svg)/i.test(a.value.replace(/[\x00-\x20]/g, '')))));
        for (const [name, sanitize] of [['library', sanitizeHTML], ['template', templateSanitize]]) {
            payloads.forEach((html, index) => {
                window.__sanitizerProbe = 0;
                const output = sanitize(html);
                host.innerHTML = output;
                host.querySelector('img')?.dispatchEvent(new Event('error'));
                host.querySelector('p')?.click();
                checks.push({ name: `${name}: payload ${index + 1} / serialize-reparse`,
                    pass: safe(host) && window.__sanitizerProbe === 0 && sanitize(output) === output });
                host.replaceChildren();
            });
            const benign = '<p><b>文字</b> <a href="/web/search?id=1">link</a></p><table><tbody><tr><td colspan="2">cell</td></tr></tbody></table>';
            checks.push({ name: `${name}: preserves formatted text, local link and table`, pass: sanitize(benign) === benign });
        }
        payloads.forEach((html, index) => {
            const container = document.createElement('div');
            const restoredContainer = document.createElement('div');
            host.append(container, restoredContainer);
            const editor = new WebTextEditor({ container });
            editor.setContent(html + '<p>保留文字</p>');
            const restored = new WebTextEditor({ container: restoredContainer });
            const loaded = restored.load(editor.save());
            checks.push({ name: `WebTextEditor: payload ${index + 1} / save-load`,
                pass: loaded && safe(editor.editor) && safe(restored.editor) && restored.editor.textContent.includes('保留文字') });
            editor.destroy();
            restored.destroy();
        });
        host.remove();
        return checks;
    });
    console.log(JSON.stringify({ browser: browser.version(), checks, failures,
        pass: checks.every(check => check.pass) && failures.length === 0 }, null, 2));
    if (checks.some(check => !check.pass) || failures.length) process.exitCode = 1;
} finally {
    await browser?.close();
    await new Promise(resolve => server.close(resolve));
}
