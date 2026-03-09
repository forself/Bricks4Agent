#!/usr/bin/env node
/**
 * 元件 Demo 頁面截圖工具
 * 使用 Playwright 截圖各元件的 demo.html（一對一精準比對）
 *
 * 使用方式：
 *   node tools/screenshot-demos.js --output docs/manuals/screenshots/before
 *   node tools/screenshot-demos.js --output docs/manuals/screenshots/after
 */
import { chromium } from 'playwright';
import path from 'path';
import fs from 'fs';

const BASE_URL = 'http://localhost:9876';
// 偵測伺服器根目錄（可能是專案根或 ui_components）
let UI_BASE = '/packages/javascript/browser/ui_components';

// 從命令列取得輸出目錄
const outputArgIdx = process.argv.indexOf('--output');
const OUTPUT_DIR = outputArgIdx >= 0
    ? path.resolve(process.argv[outputArgIdx + 1])
    : path.resolve('docs/manuals/screenshots');

// 元件 demo.html 清單（名稱 → 路徑）
const DEMOS = [
    // 表單元件 (form/)
    { name: 'form-TextInput', url: `${UI_BASE}/form/TextInput/demo.html` },
    { name: 'form-NumberInput', url: `${UI_BASE}/form/NumberInput/demo.html` },
    { name: 'form-DatePicker', url: `${UI_BASE}/form/DatePicker/demo.html` },
    { name: 'form-TimePicker', url: `${UI_BASE}/form/TimePicker/demo.html` },
    { name: 'form-Dropdown', url: `${UI_BASE}/form/Dropdown/demo.html` },
    { name: 'form-MultiSelectDropdown', url: `${UI_BASE}/form/MultiSelectDropdown/demo.html` },
    { name: 'form-Checkbox', url: `${UI_BASE}/form/Checkbox/demo.html` },
    { name: 'form-Radio', url: `${UI_BASE}/form/Radio/demo.html` },
    { name: 'form-FormField', url: `${UI_BASE}/form/FormField/demo.html` },
    { name: 'form-SearchForm', url: `${UI_BASE}/form/SearchForm/demo.html` },
    { name: 'form-BatchUploader', url: `${UI_BASE}/form/BatchUploader/demo.html` },

    // 通用元件 (common/)
    { name: 'common-BasicButton', url: `${UI_BASE}/common/BasicButton/demo.html` },
    { name: 'common-ActionButton', url: `${UI_BASE}/common/ActionButton/demo.html` },
    { name: 'common-AuthButton', url: `${UI_BASE}/common/AuthButton/demo.html` },
    { name: 'common-DownloadButton', url: `${UI_BASE}/common/DownloadButton/demo.html` },
    { name: 'common-UploadButton', url: `${UI_BASE}/common/UploadButton/demo.html` },
    { name: 'common-ButtonGroup', url: `${UI_BASE}/common/ButtonGroup/demo.html` },
    { name: 'common-ColorPicker', url: `${UI_BASE}/common/ColorPicker/demo.html` },
    { name: 'common-Dialog', url: `${UI_BASE}/common/Dialog/demo.html` },
    { name: 'common-Notification', url: `${UI_BASE}/common/Notification/demo.html` },
    { name: 'common-LoadingSpinner', url: `${UI_BASE}/common/LoadingSpinner/demo.html` },
    { name: 'common-Pagination', url: `${UI_BASE}/common/Pagination/demo.html` },
    { name: 'common-Breadcrumb', url: `${UI_BASE}/common/Breadcrumb/demo.html` },
    { name: 'common-TreeList', url: `${UI_BASE}/common/TreeList/demo.html` },
    { name: 'common-PhotoCard', url: `${UI_BASE}/common/PhotoCard/demo.html` },
    { name: 'common-FeatureCard', url: `${UI_BASE}/common/FeatureCard/demo.html` },
    { name: 'common-ImageViewer', url: `${UI_BASE}/common/ImageViewer/demo.html` },

    // 佈局元件 (layout/)
    { name: 'layout-Panel', url: `${UI_BASE}/layout/Panel/demo.html` },
    { name: 'layout-DataTable', url: `${UI_BASE}/layout/DataTable/demo.html` },
    { name: 'layout-SideMenu', url: `${UI_BASE}/layout/SideMenu/demo.html` },
    { name: 'layout-TabContainer', url: `${UI_BASE}/layout/TabContainer/demo.html` },
    { name: 'layout-FormRow', url: `${UI_BASE}/layout/FormRow/demo.html` },
    { name: 'layout-InfoPanel', url: `${UI_BASE}/layout/InfoPanel/demo.html` },
    { name: 'layout-FunctionMenu', url: `${UI_BASE}/layout/FunctionMenu/demo.html` },
    { name: 'layout-WorkflowPanel', url: `${UI_BASE}/layout/WorkflowPanel/demo.html` },

    // 進階輸入 (input/)
    { name: 'input-CompositeInputs', url: `${UI_BASE}/input/demo.html` },

    // 社群元件 (social/)
    { name: 'social-Avatar', url: `${UI_BASE}/social/Avatar/demo.html` },
    { name: 'social-FeedCard', url: `${UI_BASE}/social/FeedCard/demo.html` },
    { name: 'social-ConnectionCard', url: `${UI_BASE}/social/ConnectionCard/demo.html` },
    { name: 'social-StatCard', url: `${UI_BASE}/social/StatCard/demo.html` },
    { name: 'social-Timeline', url: `${UI_BASE}/social/Timeline/demo.html` },

    // 視覺化 (viz/)
    { name: 'viz-Charts', url: `${UI_BASE}/viz/demo.html` },

    // 資料 (data/)
    { name: 'data-RegionMap', url: `${UI_BASE}/data/RegionMap/demo.html` },
];

async function detectBasePath() {
    // 嘗試不同的 base path
    const testPaths = [
        { base: '', test: '/form/TextInput/demo.html' },
        { base: '/packages/javascript/browser/ui_components', test: '/packages/javascript/browser/ui_components/form/TextInput/demo.html' },
    ];
    for (const { base, test } of testPaths) {
        try {
            const res = await fetch(`${BASE_URL}${test}`);
            if (res.ok) return base;
        } catch {}
    }
    return UI_BASE;
}

async function main() {
    fs.mkdirSync(OUTPUT_DIR, { recursive: true });

    // 自動偵測 base path
    UI_BASE = await detectBasePath();
    // 重建 DEMOS 的 url
    for (const demo of DEMOS) {
        if (demo.url.startsWith('/packages')) {
            demo.url = demo.url.replace('/packages/javascript/browser/ui_components', UI_BASE);
        }
    }

    console.log(`\n📸 元件 Demo 截圖工具`);
    console.log(`Base path: ${UI_BASE || '(root)'}`);
    console.log(`輸出目錄：${OUTPUT_DIR}`);
    console.log(`總共 ${DEMOS.length} 個元件 Demo\n`);

    const browser = await chromium.launch();
    const context = await browser.newContext({
        viewport: { width: 1280, height: 900 }
    });

    let success = 0;
    let failed = 0;

    for (const demo of DEMOS) {
        const page = await context.newPage();
        try {
            await page.goto(`${BASE_URL}${demo.url}`, {
                waitUntil: 'networkidle',
                timeout: 15000
            });
            await page.waitForTimeout(600);
            const outputPath = path.join(OUTPUT_DIR, `${demo.name}.png`);
            await page.screenshot({ path: outputPath, fullPage: true });
            console.log(`  ✅ ${demo.name}`);
            success++;
        } catch (e) {
            console.log(`  ❌ ${demo.name} — ${e.message.split('\n')[0]}`);
            failed++;
        }
        await page.close();
    }

    await browser.close();

    console.log(`\n${'='.repeat(40)}`);
    console.log(`📊 完成：${success} 成功、${failed} 失敗`);
    console.log(`截圖位置：${OUTPUT_DIR}`);
    console.log(`${'='.repeat(40)}\n`);
}

main().catch(console.error);
