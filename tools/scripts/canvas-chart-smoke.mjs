// canvas-chart-smoke.mjs — CanvasChart 基底真瀏覽器冒煙(Edge/playwright-core,借 tim-web/poc 依賴)。
// 前置:於 Bricks4Agent 根啟動 python -m http.server 8124
// 驗證:DPR 背景儲存|ThemeBus 換膚重繪|hit-region hover→tooltip|點擊回拋|exportPNG|零 SVG
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const b = await chromium.launch({ channel: 'msedge', headless: true });
const p = await b.newPage({ viewport: { width: 900, height: 700 }, deviceScaleFactor: 1.5 });
const errs = [];
p.on('pageerror', e => errs.push('pageerror: ' + e.message.slice(0, 120)));
p.on('console', m => { if (m.type() === 'error') errs.push('console: ' + m.text().slice(0, 120)); });
await p.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');   // 借主題環境(theme.css 已載)
await p.waitForFunction(
    globalName => window[globalName] === true,
    '__studioReady',
    { timeout: 15000 }
).catch(() => {});

await p.evaluate(async modulePath => {
    const { CanvasChart } = await import(modulePath);
    window.__drawCount = 0;
    class DemoChart extends CanvasChart {
        draw(ctx, w, h) {
            window.__drawCount++;
            const t = this.tokens(['--cl-primary', '--cl-success']);
            ctx.fillStyle = t['--cl-primary'];
            ctx.fillRect(20, 20, 100, 60);
            this.addRegion({ shape: 'rect', x: 20, y: 20, w: 100, h: 60, data: { name: '甲', v: 42 } });
            ctx.fillStyle = t['--cl-success'];
            ctx.beginPath(); ctx.arc(220, 60, 30, 0, Math.PI * 2); ctx.fill();
            this.addRegion({ shape: 'circle', cx: 220, cy: 60, r: 30, data: { name: '乙', v: 7 } });
            ctx.font = this.font(12);
            ctx.fillStyle = t['--cl-primary'];
            ctx.fillText(this.ellipsis(ctx, '這是一段會被截斷的超長標籤文字測試', 80), 20, 110);
        }
        getTooltip(d) { return [{ label: '名稱', value: d.name }, { label: '值', value: d.v }]; }
    }
    const host = document.createElement('div');
    host.id = 'cc-smoke';   // 選擇器需鎖定本冒煙實例(波 2 後展示廊卡片也是 canvas 圖表,不能全頁首中)
    host.style.cssText = 'position:fixed; left:0; top:0; width:400px; height:200px; z-index:99999; background:var(--cl-bg);';
    document.body.appendChild(host);
    window.__clicked = null;
    window.__chart = new DemoChart({ container: host, width: '100%', height: '100%', title: '冒煙圖', onPointClick: (d) => { window.__clicked = d; } });
}, '/packages/javascript/browser/ui_components/viz/CanvasChart.js');
await p.waitForFunction(
    minimum => window.__drawCount >= minimum,
    1,
    { timeout: 8000 }
);

const results = [];
const t = (name, pass, detail) => results.push({ name, pass: !!pass, detail });

// 1) DPR 背景儲存
const dpr = await p.evaluate(`(() => { const c = window.__chart.canvas; return { bw: c.width, cssW: c.clientWidth, dpr: window.devicePixelRatio }; })()`);
t('DPR 背景儲存縮放(width=cssW×dpr)', Math.abs(dpr.bw - dpr.cssW * dpr.dpr) <= 2, JSON.stringify(dpr));

// 2) ThemeBus:切深色 → 自動重繪
const before = await p.evaluate('window.__drawCount');
await p.evaluate(`document.documentElement.setAttribute('data-theme', 'dark')`);
await p.waitForFunction(
    previous => window.__drawCount > previous,
    before,
    { timeout: 3000 }
).catch(() => {});
const after = await p.evaluate('window.__drawCount');
t('ThemeBus:data-theme 變更自動重繪', after > before, `draw ${before}→${after}`);
await p.evaluate(`document.documentElement.setAttribute('data-theme', '')`);

// 3) token 即時變更(Theme Studio 的 setProperty 路徑)→ 自動重繪
const b2 = await p.evaluate('window.__drawCount');
await p.evaluate(`document.documentElement.style.setProperty('--cl-primary', 'rgb(200, 30, 30)')`);
await p.waitForFunction(
    previous => window.__drawCount > previous,
    b2,
    { timeout: 3000 }
).catch(() => {});
t('ThemeBus:root token setProperty 自動重繪', (await p.evaluate('window.__drawCount')) > b2, '');
// 驗色真的吃到新 token:抽 canvas 像素
const px = await p.evaluate(`(() => { const c = window.__chart.canvas, x = Math.round(40 * (c.width / c.clientWidth)), y = Math.round(40 * (c.height / c.clientHeight)); const d = c.getContext('2d').getImageData(x, y, 1, 1).data; return [...d]; })()`);
t('重繪吃到新 token(矩形變紅)', px[0] > 150 && px[1] < 90, JSON.stringify(px));
await p.evaluate(`document.documentElement.style.removeProperty('--cl-primary')`);

// 4) hover → tooltip(DOM、textContent)
await p.hover('#cc-smoke .cl-canvas-chart__body canvas', { position: { x: 60, y: 50 } });
await p.waitForTimeout(120);
const tip = await p.evaluate(`(() => { const t = document.querySelector('#cc-smoke .cl-canvas-chart__tooltip'); return { show: t.style.display !== 'none', text: t.textContent }; })()`);
t('hover 命中 → tooltip 顯示且含資料', tip.show && tip.text.includes('甲') && tip.text.includes('42'), JSON.stringify(tip));

// 5) 點擊回拋
await p.click('#cc-smoke .cl-canvas-chart__body canvas', { position: { x: 220, y: 60 } });
const clicked = await p.evaluate('window.__clicked');
t('點擊命中(circle)回拋 data', clicked && clicked.name === '乙', JSON.stringify(clicked));

// 6) exportPNG 高倍
const png = await p.evaluate('window.__chart.exportPNG(2)');
t('exportPNG(2) 回傳 dataURL', typeof png === 'string' && png.startsWith('data:image/png') && png.length > 2000, 'len=' + (png || '').length);

// 7) 零 SVG
const svg = await p.evaluate(`window.__chart.element.querySelectorAll('svg').length`);
t('元件內零 <svg>', svg === 0, 'svg=' + svg);

await p.evaluate(() => {
    window.__chart?.destroy();
    document.querySelector('#cc-smoke')?.remove();
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('--cl-primary');
    delete window.__chart;
    delete window.__clicked;
    delete window.__drawCount;
});
await b.close();
let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL '} ${r.name}${r.pass ? '' : ' — ' + (r.detail || '')}`); }
if (errs.length) { console.log('頁面錯誤:', errs.slice(0, 5).join(' | ')); fail++; }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
