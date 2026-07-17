// wave2-stage-sweep.mjs — 波 2(SVG→Canvas 遷移)真瀏覽器驗收(Edge)。
// 前置:於 Bricks4Agent 根啟動 python -m http.server 8124
// 驗證:7 支重型圖表舞台渲染(canvas 有、svg 零、無 console 錯誤)|
//       Timeline/Flame 點擊詳情彈窗內容非空(ModalPanel.alert 修復回歸)|
//       Rose/Sparkline/Progress(circle)/Rating/RegionMap 直掛(svg 零、canvas 有)
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const b = await chromium.launch({ channel: 'msedge', headless: true });
const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
const errs = [];
p.on('pageerror', e => errs.push('pageerror: ' + e.message.slice(0, 160)));
p.on('console', m => { if (m.type() === 'error') errs.push('console: ' + m.text().slice(0, 160)); });
await p.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');
await p.waitForFunction(
    globalName => window[globalName] === true,
    '__studioReady',
    { timeout: 15000 }
).catch(() => {});

// 新 JSON Studio 不暴露舊舞台相容 hook。驗收在頁面沙箱內建立自己的舞台，
// 只依賴正式元件工廠與範例資料。
await p.evaluate(async ({ factoryPath, samplesPath }) => {
    const [{ ComponentFactory }, { STAGE_SAMPLES }] = await Promise.all([
        import(factoryPath),
        import(samplesPath)
    ]);
    const host = document.createElement('div');
    host.id = 'wave2-harness-stage';
    host.style.cssText = 'position:fixed;left:0;top:0;width:1200px;height:780px;z-index:99999;background:var(--cl-bg);overflow:auto;';
    document.body.appendChild(host);

    const harness = {
        host,
        instance: null,
        lastError: '',
        open(name) {
            try { this.instance?.destroy?.(); } catch { /* best-effort */ }
            this.instance = null;
            this.lastError = '';
            host.replaceChildren();
            try {
                const options = {
                    container: host,
                    containerId: host.id,
                    ...(STAGE_SAMPLES[name] || {})
                };
                const instance = ComponentFactory.create(name, options);
                if (!instance) throw new Error(`ComponentFactory could not create ${name}`);
                if (host.childElementCount === 0) {
                    if (typeof instance.mount === 'function') instance.mount(host);
                    else if (instance.element) host.appendChild(instance.element);
                    else if (typeof instance.render === 'function') instance.render(host);
                    else throw new Error(`${name} has no mount contract`);
                }
                this.instance = instance;
                return true;
            } catch (error) {
                this.lastError = String(error?.message || error);
                return false;
            }
        },
        cleanup() {
            try { this.instance?.destroy?.(); } catch { /* best-effort */ }
            this.instance = null;
            host.remove();
            delete window.__wave2Harness;
        }
    };
    window.__wave2Harness = harness;
}, {
    factoryPath: '/packages/javascript/browser/ui_components/binding/ComponentFactory.js',
    samplesPath: '/tools/theme-studio/sample-data.js'
});

const results = [];
const t = (name, pass, detail) => results.push({ name, pass: !!pass, detail });

/* ── 一、七支重型圖表舞台掃描 ─────────────────────────────────────────── */
const HEAVY = ['OrgChart', 'HierarchyChart', 'RelationChart', 'SankeyChart', 'SunburstChart', 'TimelineChart', 'FlameChart'];
for (const name of HEAVY) {
    const before = errs.length;
    await p.evaluate(componentName => window.__wave2Harness.open(componentName), name);
    await p.waitForTimeout(700);
    const st = await p.evaluate(() => {
        const inst = window.__wave2Harness.instance;
        const el = inst && inst.element;
        if (!el) return { ok: false, why: window.__wave2Harness.lastError || 'no element' };
        return {
            ok: true,
            canvas: el.querySelectorAll('canvas').length,
            svg: el.querySelectorAll('svg').length,
            regions: Array.isArray(inst._regions) ? inst._regions.length : -1
        };
    });
    const newErrs = errs.slice(before);
    t(`${name} 舞台:canvas≥1`, st.ok && st.canvas >= 1, JSON.stringify(st));
    t(`${name} 舞台:svg=0`, st.ok && st.svg === 0, 'svg=' + st.svg);
    t(`${name} 舞台:無 console 錯誤`, newErrs.length === 0, newErrs.join(' | '));
}

/* ── 二、Timeline/Flame 點擊詳情彈窗內容非空(alert content bug 回歸)──── */
for (const name of ['TimelineChart', 'FlameChart']) {
    await p.evaluate(componentName => window.__wave2Harness.open(componentName), name);
    await p.waitForTimeout(700);
    const r = await p.evaluate(async () => {
        const inst = window.__wave2Harness.instance;
        const regions = inst._regions || [];
        if (!regions.length) return { why: 'no regions' };
        // 取一個有 bounds 的命中區中心,對 canvas 發真實 click(走 _hitTest 全路徑)
        const reg = regions.find(x => x.bounds) || regions[0];
        const bb = reg.bounds || { x: reg.x, y: reg.y, w: reg.w || 8, h: reg.h || 8 };
        const canvas = inst.element.querySelector('canvas');
        const rect = canvas.getBoundingClientRect();
        const ev = new MouseEvent('click', {
            bubbles: true,
            clientX: rect.left + bb.x + bb.w / 2,
            clientY: rect.top + bb.y + bb.h / 2
        });
        canvas.dispatchEvent(ev);
        await new Promise(r2 => setTimeout(r2, 350));
        // 找最新 modal(z 最高的 .panel/.modal 容器),量其文字內容
        const panels = [...document.querySelectorAll('.modal-panel, .panel--modal, [class*="modal"]')]
            .filter(x => x.offsetParent !== null || x.getClientRects().length);
        const top = panels[panels.length - 1];
        if (!top) return { why: 'no modal opened' };
        const text = (top.textContent || '').replace(/\\s+/g, ' ').trim();
        // 關閉:點 OK 鈕(找含「確定/OK」的按鈕),失敗則移除節點
        const btn = [...top.querySelectorAll('button')].find(x => /確定|OK|ok/.test(x.textContent));
        if (btn) btn.click(); else top.remove();
        await new Promise(r2 => setTimeout(r2, 200));
        return { textLen: text.length, sample: text.slice(0, 80) };
    });
    // 內容非空:必須超過純 OK 鈕的字數(>10),證明 replaceWith(root) 生效
    t(`${name} 點擊詳情:彈窗內容非空`, r.textLen > 10, JSON.stringify(r));
}

/* ── 三、Rose/Sparkline/Progress(circle)/Rating/RegionMap 直掛 ─────────── */
const mounted = await p.evaluate(`(async () => {
    const base = '/packages/javascript/browser/ui_components';
    const { RoseChart } = await import(base + '/viz/RoseChart.js');
    const { Sparkline } = await import(base + '/viz/Sparkline.js');
    const { Progress } = await import(base + '/common/Progress/Progress.js');
    const { Rating } = await import(base + '/form/Rating/Rating.js');
    const { RegionMap } = await import(base + '/data/RegionMap/RegionMap.js');
    const out = {};
    const host = document.createElement('div');
    host.style.cssText = 'position:fixed; left:0; top:0; width:900px; height:600px; background:#fff; z-index:99999; display:flex; flex-wrap:wrap; gap:8px; overflow:auto;';
    document.body.appendChild(host);
    const put = (name, inst) => {
        const cell = document.createElement('div');
        cell.style.cssText = 'width:280px; height:180px;';
        host.appendChild(cell);
        if (typeof inst.mount === 'function') inst.mount(cell);
        else if (typeof inst.render === 'function') inst.render(cell);
        else if (inst.element) cell.appendChild(inst.element);
        return cell;
    };
    const probe = (name, cell) => {
        out[name] = {
            canvas: cell.querySelectorAll('canvas').length,
            svg: cell.querySelectorAll('svg').length,
            empty: cell.children.length === 0
        };
    };
    const rose = new RoseChart({ width: 260, height: 160, data: { labels: ['東', '南', '西'], series: [{ name: '風', data: [20, 35, 15] }] } });
    const c1 = put('rose', rose);
    const spark = new Sparkline({ data: [5, 8, 6, 12, 9, 14, 11], type: 'line' });
    const c2 = put('spark', spark);
    const prog = new Progress({ type: 'circle', value: 75 });
    const c3 = put('progress', prog);
    const rate = new Rating({ value: 4, max: 5, readonly: false });
    const c4 = put('rating', rate);
    const rm = new RegionMap({
        width: '280px', height: '180px', showLabels: true, showValues: true,
        data: { TPE: { value: 87 }, KHH: { value: 62 }, TXG: { value: 45 } },
        colorScale: RegionMap.createColorScale(0, 100)
    });
    const c5 = put('regionmap', rm);
    await new Promise(r => setTimeout(r, 500));
    probe('rose', c1); probe('spark', c2); probe('progress', c3); probe('rating', c4); probe('regionmap', c5);
    // RegionMap 互動:模擬點 TPE 中心(viewBox→canvas 座標換算走 hitTest)
    out.regionClick = await (async () => {
        let clicked = null;
        rm.options.onClick = (code) => { clicked = code; };
        const canvas = c5.querySelector('canvas');
        if (!canvas) return 'no canvas';
        // 掃描 canvas 命中一個有值區域:粗掃 12×12 網格 dispatch click
        const rect = canvas.getBoundingClientRect();
        for (let gy = 1; gy < 12 && !clicked; gy++) for (let gx = 1; gx < 12 && !clicked; gx++) {
            canvas.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: rect.left + rect.width * gx / 12, clientY: rect.top + rect.height * gy / 12 }));
            await new Promise(r => setTimeout(r, 5));
        }
        return clicked;
    })();
    host.remove();
    [rose, spark, prog, rate, rm].forEach(x => { try { x.destroy && x.destroy(); } catch (e) {} });
    return out;
})()`);
t('RoseChart 直掛:canvas 有 svg 零', mounted.rose && mounted.rose.canvas >= 1 && mounted.rose.svg === 0, JSON.stringify(mounted.rose));
t('Sparkline 直掛:canvas 有 svg 零', mounted.spark && mounted.spark.canvas >= 1 && mounted.spark.svg === 0, JSON.stringify(mounted.spark));
t('Progress(circle) 直掛:canvas 有 svg 零', mounted.progress && mounted.progress.canvas >= 1 && mounted.progress.svg === 0, JSON.stringify(mounted.progress));
t('Rating 直掛:canvas 有 svg 零', mounted.rating && mounted.rating.canvas >= 1 && mounted.rating.svg === 0, JSON.stringify(mounted.rating));
t('RegionMap 直掛:canvas 有 svg 零', mounted.regionmap && mounted.regionmap.canvas >= 1 && mounted.regionmap.svg === 0, JSON.stringify(mounted.regionmap));
t('RegionMap 點擊命中回調(onClick)', typeof mounted.regionClick === 'string' && mounted.regionClick.length >= 3, 'clicked=' + mounted.regionClick);

await p.evaluate(() => window.__wave2Harness?.cleanup());
await b.close();
let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL '} ${r.name}${r.pass ? '' : ' — ' + (r.detail || '')}`); }
const pageErrs = errs.filter(e => !/favicon/.test(e));
if (pageErrs.length) { console.log('頁面錯誤:', pageErrs.slice(0, 8).join(' | ')); fail++; }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
