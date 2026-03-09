#!/usr/bin/env node
/**
 * 元件 Demo 頁面截圖工具
 * 使用 Playwright 自動截圖各元件的 demo 頁面
 */
import { chromium } from 'playwright';
import path from 'path';

const BASE_URL = 'http://localhost:9876';
const OUTPUT_DIR = path.resolve('d:/proj');

const pages = [
    { name: 'Checkbox',    url: '/form/Checkbox/demo.html' },
    { name: 'ToggleSwitch', url: '/form/ToggleSwitch/demo.html' },
    { name: 'TextInput',   url: '/form/TextInput/demo.html' },
    { name: 'NumberInput',  url: '/form/NumberInput/demo.html' },
    { name: 'Radio',       url: '/form/Radio/demo.html' },
    { name: 'Dropdown',    url: '/form/Dropdown/demo.html' },
    { name: 'CompositeInputs', url: '/input/demo.html' },
];

async function main() {
    const browser = await chromium.launch();
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });

    for (const pg of pages) {
        const page = await context.newPage();
        try {
            await page.goto(`${BASE_URL}${pg.url}`, { waitUntil: 'networkidle', timeout: 15000 });
            await page.waitForTimeout(500);
            const outputPath = path.join(OUTPUT_DIR, `component-${pg.name}.png`);
            await page.screenshot({ path: outputPath, fullPage: true });
            console.log(`✅ ${pg.name} → ${outputPath}`);
        } catch (e) {
            console.log(`❌ ${pg.name}: ${e.message}`);
        }
        await page.close();
    }

    await browser.close();
    console.log('\n截圖完成');
}

main();
