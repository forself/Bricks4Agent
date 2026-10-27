import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import http from 'node:http';
import { distance, polygonContains } from '../frontend/geometry.js';
import { generate } from '../tools/generate.mjs';

const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright-core');
const moduleRoot = fileURLToPath(new URL('../', import.meta.url));
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'b4a-gis-browser-'));
let web, mock, browser;
try {
    const site = path.join(temp, 'site');
    let clientOrigin;
    const markers = [];
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=', 'base64');
    const listen = server => new Promise(resolve => server.listen(0, '127.0.0.1', () => resolve(`http://127.0.0.1:${server.address().port}`)));
    // Test-only contract double. No production service, database, .NET or data tools.
    mock = http.createServer(async (req, res) => {
        res.setHeader('Access-Control-Allow-Origin', clientOrigin);
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        if (req.method === 'OPTIONS') { res.writeHead(204); res.end(); return; }
        const url = new URL(req.url, 'http://mock');
        if (url.pathname.startsWith('/service/api/tiles/')) { res.setHeader('Content-Type', 'image/png'); res.end(png); return; }
        res.setHeader('Content-Type', 'application/json');
        if (url.pathname === '/service/api/addresses') { res.end(JSON.stringify({ candidates: [{ address: '臺北市測試路1號', lon: 121.56, lat: 25.03, level: 'address-point', match: 'exact-normalized' }] })); return; }
        let raw = ''; for await (const chunk of req) raw += chunk;
        const body = JSON.parse(raw || '{}');
        if (url.pathname === '/service/api/markers') { markers.push({ ...body, id: markers.length + 1 }); res.end(JSON.stringify({ id: markers.length })); return; }
        if (url.pathname === '/service/api/query') {
            const items = markers.filter(p => p.time >= (body.from ?? 0) && p.time <= (body.to ?? Infinity)
                && (!body.polygon || polygonContains([p.lon, p.lat], body.polygon))
                && (!body.circle || distance([p.lon, p.lat], body.circle) <= body.circle[2]));
            res.end(JSON.stringify({ items, exact: true, candidates: markers.length })); return;
        }
        res.writeHead(404); res.end('{}');
    });
    const serviceOrigin = await listen(mock), serviceBase = `${serviceOrigin}/service/api/`;
    generate(JSON.parse(fs.readFileSync(path.join(moduleRoot, 'fixtures/gis.page.json'))), site, { serviceBase });
    web = http.createServer((req, res) => {
        const pathname = decodeURIComponent(new URL(req.url, 'http://web').pathname);
        const file = path.resolve(site, pathname.replace(/^\/client\//, '') || 'index.html');
        if (!pathname.startsWith('/client/') || !file.startsWith(site + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
        res.setHeader('Content-Type', ({ '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.json': 'application/json' })[path.extname(file)] || 'application/octet-stream');
        res.end(fs.readFileSync(file));
    });
    clientOrigin = await listen(web);
    const origin = clientOrigin, base = `${origin}/client/`;
    browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || '/usr/bin/chromium', headless: true });
    const context = await browser.newContext({ viewport: { width: 1100, height: 1000 } });
    const external = [], pageErrors = [], consoleErrors = [], tileResponses = [];
    await context.route('**/*', async route => {
        if (![origin, serviceOrigin].includes(new URL(route.request().url()).origin)) { external.push(route.request().url()); return route.abort(); }
        await route.continue();
    });
    const page = await context.newPage();
    page.on('pageerror', error => pageErrors.push(error.message));
    page.on('console', message => { if (message.type() === 'error' && !message.text().includes('404')) consoleErrors.push(message.text()); });
    page.on('response', response => { if (response.url().includes('/tiles/') && response.status() === 200) tileResponses.push(response.url()); });
    await page.goto(base); await page.locator('canvas[data-point-count="0"]').waitFor();
    assert.equal(await page.locator('[data-tool-node="map"] canvas').count(), 1);
    assert.ok((await page.locator('body').textContent()).includes('FIXTURE'));
    await page.getByRole('textbox', { name: '查詢本地地址' }).fill('台北市測試路１號');
    await page.getByRole('button', { name: '查詢本地地址', exact: true }).click();
    await page.getByRole('button', { name: /臺北市測試路1號/ }).click();
    await page.getByRole('button', { name: '新增標記', exact: true }).click();
    const canvas = page.locator('canvas'); const box = await canvas.boundingBox();
    await canvas.click({ position: { x: box.width / 2, y: box.height / 2 } });
    await page.locator('canvas[data-point-count="1"]').waitFor();
    await page.getByRole('button', { name: /密度格網/ }).click();
    await page.getByRole('button', { name: /圓形範圍/ }).click();
    await canvas.click({ position: { x: box.width / 2, y: box.height / 2 } });
    await Promise.all([page.waitForResponse(r => r.url().endsWith('/query') && r.ok()),
        canvas.click({ position: { x: box.width / 2 + 30, y: box.height / 2 } })]);
    assert.equal(await canvas.getAttribute('data-point-count'), '1');
    await page.getByRole('button', { name: /多邊形範圍/ }).click();
    await canvas.click({ position: { x: 10, y: 10 } });
    await canvas.click({ position: { x: 30, y: 10 } });
    await canvas.dblclick({ position: { x: 20, y: 30 } });
    await page.locator('canvas[data-point-count="0"]').waitFor();
    await page.getByRole('button', { name: '清除範圍', exact: true }).click();
    await page.locator('canvas[data-point-count="1"]').waitFor();
    await page.getByRole('slider', { name: '時間上限', exact: true }).fill('0');
    await page.getByRole('slider', { name: '時間上限', exact: true }).dispatchEvent('input');
    await page.locator('canvas[data-point-count="0"]').waitFor();
    await page.getByRole('button', { name: '播放／暫停', exact: true }).click();
    await page.waitForFunction(() => Number(document.querySelector('input[aria-label="時間上限"]').value) > 0);
    await page.getByRole('button', { name: '播放／暫停', exact: true }).click();
    await page.reload(); await page.locator('canvas[data-point-count="1"]').waitFor();
    const lifecycle = await page.evaluate(async () => {
        const { default: Page } = await import('./GisPage.js');
        const { mountGis } = await import('./modules/offline-gis/frontend/runtime.js');
        const host = document.createElement('div'); document.body.append(host);
        const { GisApi } = await import('./modules/offline-gis/frontend/api.js');
        const { serviceBase } = await import('./service-config.js');
        const app = await mountGis(Page, host, { api: new GisApi(serviceBase) }), map = app.page.getComponent('map'), timeline = app.page.getComponent('timeline');
        timeline.element.querySelector('button').click();
        const playing = Boolean(timeline.timer); app.destroy();
        const result = { playing, stopped: timeline.timer === null, aborted: map.abort.signal.aborted, detached: host.childElementCount === 0 };
        host.remove(); return result;
    });
    assert.deepEqual(lifecycle, { playing: true, stopped: true, aborted: true, detached: true });
    assert.ok(tileResponses.length > 0, 'No mock service PNG was displayed');
    assert.deepEqual(external, []); assert.deepEqual(pageErrors, []); assert.deepEqual(consoleErrors, []);
    if (process.env.GIS_SCREENSHOT) await page.screenshot({ path: process.env.GIS_SCREENSHOT, fullPage: true });
    console.log(JSON.stringify({ passed: true, generator: 'PageGenerator → DynamicToolRenderer → local GIS factory',
        service: 'independent cross-origin contract mock; no database/backend', checks: ['subpath', 'only-configured-origins', 'mock-tiles', 'address-normalization-and-locate', 'persistent-marker', 'density', 'circle', 'polygon', 'time-filter', 'playback', 'reload'],
        tileResponses: tileResponses.length, externalRequests: external.length, pageErrors: pageErrors.length }));
} finally {
    await browser?.close();
    for (const server of [web, mock]) if (server) { server.closeAllConnections(); await new Promise(resolve => server.close(resolve)); }
    fs.rmSync(temp, { recursive: true, force: true });
}
