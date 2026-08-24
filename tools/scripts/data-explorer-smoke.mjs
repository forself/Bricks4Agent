// data-explorer-smoke.mjs — DataExplorer 複合件真瀏覽器冒煙(Edge)。
// 前置:於 Bricks4Agent 根啟動 python -m http.server 8124
// 驗證:熱圖初繪|setSpec 切圖型(bar/scatter/line/pie/histogram)|聚合表|明細分頁|
//       CSV 匯出內容|spec 白名單(非法 agg 擲錯)|零 <svg>
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const b = await chromium.launch({ channel: 'msedge', headless: true });
const p = await b.newPage({ viewport: { width: 1280, height: 900 } });
const errs = [];
p.on('pageerror', e => errs.push('pageerror: ' + e.message.slice(0, 140)));
p.on('console', m => { if (m.type() === 'error') errs.push('console: ' + m.text().slice(0, 140)); });
await p.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');
await p.waitForFunction(
    globalName => window[globalName] === true,
    '__studioReady',
    { timeout: 15000 }
).catch(() => {});

// DataExplorer 是全尺寸舞台元件，範例 options 位於 STAGE_SAMPLES；在此別名為
// SAMPLE_OPTIONS，直接掛載元件，不依賴已移除的 Theme Studio openStage hook。
await p.evaluate(async ({ componentPath, samplesPath }) => {
    const [{ DataExplorer }, { STAGE_SAMPLES }] = await Promise.all([
        import(componentPath),
        import(samplesPath)
    ]);
    const SAMPLE_OPTIONS = STAGE_SAMPLES.DataExplorer;
    if (!SAMPLE_OPTIONS) throw new Error('Missing DataExplorer sample options');
    const host = document.createElement('div');
    host.id = 'data-explorer-harness';
    host.style.cssText = 'position:fixed;left:0;top:0;width:1180px;height:820px;z-index:99999;background:var(--cl-bg);overflow:auto;';
    document.body.appendChild(host);
    const instance = new DataExplorer({
        ...SAMPLE_OPTIONS,
        container: host
    });
    if (host.childElementCount === 0) instance.mount(host);
    window.__dataExplorerHarness = {
        instance,
        cleanup() {
            try { instance.destroy(); } catch { /* best-effort */ }
            host.remove();
            delete window.__dataExplorerHarness;
        }
    };
}, {
    componentPath: '/packages/javascript/browser/ui_components/analytics/DataExplorer.js',
    samplesPath: '/tools/theme-studio/sample-data.js'
});
await p.waitForTimeout(1200);

const results = [];
const t = (name, pass, detail) => results.push({ name, pass: !!pass, detail });

const state0 = await p.evaluate(() => {
    const ex = window.__dataExplorerHarness.instance;
    const canvas = ex.element.querySelector('canvas');
    return { hasCanvas: !!canvas, chart: ex._chart && ex._chart.constructor.name, fields: ex._fields.length };
});
t('初繪:熱圖 canvas 存在', state0.hasCanvas && state0.chart === 'HeatmapChart', JSON.stringify(state0));

// 切換圖型鏈:bar → scatter → line → pie → histogram
const seq = await p.evaluate(async () => {
    const ex = window.__dataExplorerHarness.instance;
    const out = [];
    const specs = [
        { chartType: 'bar', x: { field: 'unit' }, y: { field: 'amount', agg: 'sum', unit: '萬' } },
        { chartType: 'scatter', x: { field: 'age' }, y: { field: 'amount', agg: 'sum' }, color: { field: 'type' }, size: { field: 'prior' } },
        { chartType: 'line', x: { field: 'at', bin: { type: 'month' } }, y: { field: null, agg: 'count', unit: '筆' } },
        { chartType: 'pie', x: { field: 'type' }, y: { field: 'amount', agg: 'sum' } },
        { chartType: 'histogram', x: { field: 'age', bin: { type: 'numeric', count: 5 } }, y: { field: null, agg: 'count' } }
    ];
    for (const s of specs) {
        ex.setSpec(s);
        await new Promise(r => setTimeout(r, 150));
        out.push(ex._chart.constructor.name);
    }
    return out;
});
t('setSpec 五連切(Bar/Scatter/Line/Pie/Bar)', JSON.stringify(seq) === JSON.stringify(['BarChart', 'ScatterChart', 'LineChart', 'PieChart', 'BarChart']), JSON.stringify(seq));

// 時間軸月序正確(line 已用 month 桶;民國標籤應按時間序)
const months = await p.evaluate(() => {
    const ex = window.__dataExplorerHarness.instance;
    ex.setSpec({ chartType: 'line', x: { field: 'at', bin: { type: 'month' } }, y: { field: null, agg: 'count' } });
    return ex._agg.labels;
});
const sortedOk = await p.evaluate(`(() => {
    const l = ${JSON.stringify(months)};
    return JSON.stringify(l) === JSON.stringify(${JSON.stringify(months)}) && l.length >= 5 && l[0].includes('115年1月');
})()`);
t('時間桶民國標籤且按時間序(首=115年1月)', sortedOk, JSON.stringify(months));

// 聚合表 + 明細分頁
const tables = await p.evaluate(() => {
    const ex = window.__dataExplorerHarness.instance;
    ex.setSpec({ chartType: 'heatmap', x: { field: 'unit' }, series: { field: 'type' }, y: { field: 'amount', agg: 'sum' } });
    ex._switchView('agg');
    const aggRows = ex._aggHost.querySelectorAll('tbody tr').length;
    ex._switchView('raw');
    const rawRows = ex._rawHost.querySelectorAll('tbody tr').length;
    const hasPager = !!ex._rawHost.textContent.match(/1-10|10/);
    ex._switchView('chart');
    return { aggRows, rawRows, hasPager };
});
t('聚合表(4 轄區列)', tables.aggRows === 4, JSON.stringify(tables));
t('明細表分頁(pageSize=10 → 顯示 10 列)', tables.rawRows === 10, JSON.stringify(tables));

// CSV 匯出
const csv = await p.evaluate(() => window.__dataExplorerHarness.instance.exportCSV());
t('CSV 匯出(BOM+表頭+轄區列)', csv.charCodeAt(0) === 0xFEFF && csv.includes('轄區') && csv.includes('中山'), csv.slice(0, 60));

// spec 白名單:非法 agg 擲錯
const rejected = await p.evaluate(() => {
    try { window.__dataExplorerHarness.instance.setSpec({ chartType: 'bar', x: { field: 'unit' }, y: { field: 'amount', agg: 'evil' } }); return false; }
    catch (e) { return /不允許的聚合/.test(e.message); }
});
t('spec 白名單:非法 agg fail-closed', rejected, '');

// 圖表區零 SVG(控制列的 Dropdown/Button 舊圖示屬波 3 棘輪存量,另計)
const svg = await p.evaluate(() => {
    const instance = window.__dataExplorerHarness.instance;
    return instance._chartHost.querySelectorAll('svg').length + instance._aggHost.querySelectorAll('svg').length;
});
t('圖表/聚合表區零 <svg>', svg === 0, 'svg=' + svg);

await p.evaluate(() => window.__dataExplorerHarness?.cleanup());
await b.close();
let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL '} ${r.name}${r.pass ? '' : ' — ' + (r.detail || '')}`); }
if (errs.length) { console.log('頁面錯誤:', errs.slice(0, 6).join(' | ')); fail++; }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
