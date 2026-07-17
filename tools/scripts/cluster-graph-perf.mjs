// cluster-graph-perf.mjs — ClusterGraph 5000 節點效能與互動驗證(Edge 無頭)。
// 前置:於 Bricks4Agent 根啟動 python -m http.server 8124
// 斷言:overview 模式 avg step<12ms、draw<12ms(60 幀取樣);drill 預設可見<100;
//       展開/收合互動正確;零 <svg>。
import pkg from '../../../tim-web/poc/node_modules/playwright-core/index.js';
const { chromium } = pkg;

const b = await chromium.launch({ channel: 'msedge', headless: true });
const p = await b.newPage({ viewport: { width: 1280, height: 900 } });
const errs = [];
p.on('pageerror', e => errs.push('pageerror: ' + e.message.slice(0, 120)));
p.on('console', m => { if (m.type() === 'error') errs.push('console: ' + m.text().slice(0, 120)); });
await p.goto('http://127.0.0.1:8124/tools/theme-studio/index.html');
await p.waitForFunction(
    globalName => window[globalName] === true,
    '__studioReady',
    { timeout: 15000 }
).catch(() => {});

await p.evaluate(async ({ clusterPath, forcePath }) => {
    const [{ ClusterGraph }, { createRng }] = await Promise.all([
        import(clusterPath),
        import(forcePath),
    ]);

    // 合成:24 頂層幫派 → 各 5 子群 → 共 5000 人;6000 條邊(70% 群內、30% 跨群)
    const rng = createRng(2026);
    const groups = [], nodes = [], edges = [];
    for (let t = 0; t < 24; t++) {
        groups.push({ id: `T${t}`, parent: null, label: `幫派${t}` });
        for (let s = 0; s < 5; s++) groups.push({ id: `T${t}S${s}`, parent: `T${t}`, label: `堂${t}-${s}` });
    }
    const leafIds = groups.filter((group) => group.parent).map((group) => group.id);
    for (let i = 0; i < 5000; i++) {
        nodes.push({ id: `p${i}`, label: `成員${i}`, group: leafIds[Math.floor(rng() * leafIds.length)] });
    }
    const byGroup = {};
    nodes.forEach((node) => { (byGroup[node.group] || (byGroup[node.group] = [])).push(node.id); });
    for (let i = 0; i < 6000; i++) {
        if (rng() < 0.7) {
            const groupId = leafIds[Math.floor(rng() * leafIds.length)];
            const members = byGroup[groupId];
            if (members.length < 2) continue;
            edges.push({ source: members[Math.floor(rng() * members.length)], target: members[Math.floor(rng() * members.length)] });
        } else {
            edges.push({ source: `p${Math.floor(rng() * 5000)}`, target: `p${Math.floor(rng() * 5000)}` });
        }
    }
    const host = document.createElement('div');
    host.style.cssText = 'position:fixed; left:0; top:0; width:1000px; height:700px; z-index:99999; background:var(--cl-bg);';
    document.body.appendChild(host);
    window.__data = { nodes, groups, edges };
    window.__g = new ClusterGraph({ container: host, width: '100%', height: '100%', mode: 'drill', nodes, groups, edges, seed: 2026 });
    window.__ready = true;
}, {
    clusterPath: '/packages/javascript/browser/ui_components/viz/ClusterGraph.js',
    forcePath: '/packages/javascript/browser/ui_components/utils/force-engine.js',
});
await p.waitForFunction(
    globalName => window[globalName] === true,
    '__ready',
    { timeout: 20000 }
);
await p.waitForTimeout(800);

const results = [];
const t = (name, pass, detail) => results.push({ name, pass: !!pass, detail });

// 1) drill 預設:可見=24 個頂層聚合節點
const drill = await p.evaluate('({ n: window.__g._simNodes.length, links: window.__g._simLinks.length })');
t('drill 預設可見=頂層聚合(24)', drill.n === 24, JSON.stringify(drill));

// 2) 展開互動:模擬點擊第一個聚合節點
const expandOk = await p.evaluate(`(() => {
    const g = window.__g;
    const meta = g._simNodes[0];
    const before = g._simNodes.length;
    g._clickNode(meta);
    return { before, after: g._simNodes.length, expanded: g._expanded.size };
})()`);
t('點聚合節點展開(可見數增加)', expandOk.after > expandOk.before && expandOk.expanded === 1, JSON.stringify(expandOk));

// 3) 全景 5000 節點效能:停自動迴圈,手動量測 60 幀
const perf = await p.evaluate(`(async () => {
    const g = window.__g;
    g.setMode('overview');
    await new Promise(r => setTimeout(r, 300));      // 讓迴圈起跑後接管
    g._sim.reheat(1);
    let tStep = 0, tDraw = 0;
    const N = 60;
    for (let i = 0; i < N; i++) {
        let a = performance.now();
        g._sim.step();
        tStep += performance.now() - a;
        g._simTree = null;
        a = performance.now();
        g._renderNow();
        tDraw += performance.now() - a;
    }
    return { nodes: g._simNodes.length, links: g._simLinks.length, stepMs: +(tStep / N).toFixed(2), drawMs: +(tDraw / N).toFixed(2) };
})()`);
t('overview 全景=5000 節點', perf.nodes === 5000, JSON.stringify(perf));
t(`BH 模擬 avg step < 12ms(實測 ${perf.stepMs}ms)`, perf.stepMs < 12, '');
t(`Canvas 繪製 avg draw < 12ms(實測 ${perf.drawMs}ms)`, perf.drawMs < 12, '');

// 4) 收合回鑽取
const back = await p.evaluate(`(() => { const g = window.__g; g.setMode('drill'); g.collapseAll(); return g._simNodes.length; })()`);
t('collapseAll 回頂層(24)', back === 24, 'n=' + back);

// 5) 零 SVG + 命中測試
const misc = await p.evaluate(`(() => {
    const g = window.__g;
    const svg = g.element.querySelectorAll('svg').length;
    const n0 = g._simNodes[0];
    const v = g._view;
    const sx = n0.x * v.scale + v.tx, sy = n0.y * v.scale + v.ty;
    const hit = g._hitNode(sx, sy);
    return { svg, hitOk: hit === n0 };
})()`);
t('零 <svg>', misc.svg === 0, '');
t('世界座標命中測試(縮放/平移下)', misc.hitOk, '');

await b.close();
let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL '} ${r.name}${r.pass ? '' : ' — ' + (r.detail || '')}`); }
if (errs.length) { console.log('頁面錯誤:', errs.slice(0, 5).join(' | ')); fail++; }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
