// Exercises save, local catalog reuse and explicit rebinding in a real browser.
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createPresaleServer } from './server.mjs';

const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
if (!existsSync(edge)) throw new Error('Microsoft Edge is required for this browser check');
const playwright = await import(new URL('../../../tim-web/poc/node_modules/playwright-core/index.js', import.meta.url));
const chromium = playwright.chromium ?? playwright.default?.chromium;
if (!chromium) throw new Error('An existing playwright-core runtime is required');

const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-library-browser-'));
const server = createPresaleServer({ libraryRoot: path.join(root, 'library') });
let browser;
try {
    server.listen(0, '127.0.0.1'); await once(server, 'listening');
    browser = await chromium.launch({ executablePath: edge, headless: true, args: ['--no-sandbox'] });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const base = `http://127.0.0.1:${server.address().port}`;
    await page.goto(base);
    await page.locator('#presets').getByRole('button', { name: /空白開始/ }).click();
    await page.locator('#confirmed').check();
    await page.locator('#component-palette .palette-item').first()
        .getByRole('button', { name: '加入' }).click();
    assert.equal(await page.locator('#page-canvas .canvas-item').count(), 1);
    await page.locator('#library-title').fill('我的查詢元件');
    await page.locator('#library-save-component').click();
    await page.waitForFunction(() => [...document.querySelectorAll('#library-components .library-entry')]
        .some(node => node.textContent.includes('我的查詢元件')));
    await page.locator('#library-title').fill('我的查詢頁面');
    await page.locator('#library-save-page').click();
    await page.waitForFunction(() => [...document.querySelectorAll('#library-pages .library-entry')]
        .some(node => node.textContent.includes('我的查詢頁面')));
    const catalog = await (await fetch(`${base}/api/library`)).json();
    assert.equal(catalog.items.length, 2);

    await page.locator('#presets').getByRole('button', { name: /空白開始/ }).click();
    await page.locator('#confirmed').check();
    assert.equal(await page.locator('#page-canvas .canvas-item').count(), 0);
    await page.locator('#component-palette .palette-item')
        .filter({ hasText: '我的查詢元件' }).getByRole('button', { name: '加入' }).click();
    await page.locator('#library-mapping').waitFor({ state: 'visible' });
    const targetEntity = await page.locator('#library-mapping select').first().inputValue();
    const targetField = await page.locator('#library-mapping select').nth(1).inputValue();
    await page.locator('#library-mapping').getByRole('button', { name: '加入頁面畫布' }).click();
    assert.equal(await page.locator('#page-canvas .canvas-item').count(), 1);
    await page.locator('#generate').click();
    await page.locator('#result').waitFor({ state: 'visible' });
    let href = await page.locator('#open-planned').getAttribute('href');
    const generated = await page.evaluate(async target =>
        (await fetch(target.replace('planned-page.html', 'page-plan.json'))).json(), href);
    assert.equal(generated.items[0].ref, `Presale${targetEntity.slice(2)}Search`);
    assert.equal(generated.items[0].wiring.fields[0].field_code, targetField);

    await page.locator('#library-pages .library-entry').filter({ hasText: '我的查詢頁面' })
        .getByRole('button', { name: '套用' }).click();
    await page.locator('#library-mapping').waitFor({ state: 'visible' });
    page.once('dialog', dialog => { void dialog.accept(); });
    await page.locator('#library-mapping').getByRole('button', { name: '套用此頁面' }).click();
    await page.locator('#generate').click();
    await page.waitForFunction(previous => document.querySelector('#open-planned')?.getAttribute('href') !== previous,
        href);
    href = await page.locator('#open-planned').getAttribute('href');
    const pagePlan = await page.evaluate(async target =>
        (await fetch(target.replace('planned-page.html', 'page-plan.json'))).json(), href);
    assert.equal(pagePlan.items[0].ref, `Presale${targetEntity.slice(2)}Search`);
    page.once('dialog', dialog => { void dialog.accept(); });
    await page.locator('#library-components .library-entry').filter({ hasText: '我的查詢元件' })
        .getByRole('button', { name: '移除' }).click();
    await page.waitForFunction(() => ![...document.querySelectorAll('#component-palette .palette-item')]
        .some(node => node.textContent.includes('我的查詢元件')));
    assert.equal((await (await fetch(`${base}/api/library`)).json()).items.length, 1);
    assert.deepEqual(errors, []);
    process.stdout.write('Presale local library browser smoke passed: save, palette reuse, rebind, page generation.\n');
} finally {
    if (browser) await browser.close();
    server.close(); await once(server, 'close');
    const tempRoot = path.resolve(os.tmpdir()), target = path.resolve(root);
    if (!target.startsWith(`${tempRoot}${path.sep}`)) throw new Error('Unexpected library test path');
    rmSync(target, { recursive: true, force: true });
}
