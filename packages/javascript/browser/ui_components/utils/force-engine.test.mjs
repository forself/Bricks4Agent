// force-engine.test.mjs — 零依賴 node 直測:node utils/force-engine.test.mjs
import { buildQuadtree, bhAccumulate, nearestBody } from './quadtree.js';
import { createSimulation, createRng } from './force-engine.js';

const results = [];
const t = (name, fn) => { try { fn(); results.push({ name, pass: true }); } catch (e) { results.push({ name, pass: false, detail: e.message }); } };
const ok = (c, msg) => { if (!c) throw new Error(msg || 'assert failed'); };
const dist = (a, b) => Math.hypot(a.x - b.x, a.y - b.y);

t('quadtree:質量與質心正確', () => {
    const root = buildQuadtree([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 0, y: 10 }, { x: 10, y: 10 }]);
    ok(root.mass === 4, 'mass=' + root.mass);
    ok(Math.abs(root.mx - 5) < 1e-9 && Math.abs(root.my - 5) < 1e-9, `com=(${root.mx},${root.my})`);
});
t('quadtree:重複同點不炸(chain 防遞迴)', () => {
    const pts = Array.from({ length: 50 }, () => ({ x: 3, y: 3 }));
    const root = buildQuadtree(pts);
    ok(root.mass === 50);
});
t('nearestBody:找到最近者且尊重半徑', () => {
    const a = { x: 0, y: 0, id: 'a' }, b = { x: 8, y: 0, id: 'b' };
    const root = buildQuadtree([a, b]);
    ok(nearestBody(root, 1, 0, 3) === a);
    ok(nearestBody(root, 7, 0, 3) === b);
    ok(nearestBody(root, 100, 100, 5) === null);
});
t('bhAccumulate:兩點互斥方向正確 + self 排除', () => {
    const A = { x: 0, y: 0 }, B = { x: 10, y: 0 };
    const root = buildQuadtree([A, B]);
    const out = { fx: 0, fy: 0 };
    bhAccumulate(root, A, A.x, A.y, 0.9, 100, out);   // k>0=相斥 → A 被 B 推向 -x
    ok(out.fx < 0, 'fx=' + out.fx);
    ok(Math.abs(out.fx + 10) < 1e-9, '應恰為 -10(僅 B:f=k/d²=1,fx=-dx·f);自身未排除會是巨值。fx=' + out.fx);
});
t('rng:同種子同序列、異種子異序列', () => {
    const a = createRng(42), b = createRng(42), c = createRng(7);
    const sa = [a(), a(), a()], sb = [b(), b(), b()], sc = [c(), c(), c()];
    ok(JSON.stringify(sa) === JSON.stringify(sb));
    ok(JSON.stringify(sa) !== JSON.stringify(sc));
    ok(sa.every(v => v >= 0 && v < 1));
});
t('模擬:兩節點斥力拉開距離', () => {
    const n = [{ id: 1, x: -1, y: 0 }, { id: 2, x: 1, y: 0 }];
    const sim = createSimulation(n, [], { seed: 1, centerStrength: 0 });
    const d0 = dist(n[0], n[1]);
    for (let i = 0; i < 30; i++) sim.step();
    ok(dist(n[0], n[1]) > d0, `d ${d0}→${dist(n[0], n[1])}`);
});
t('模擬:彈簧邊收斂到 linkDistance 量級', () => {
    const n = [{ id: 1, x: -200, y: 0 }, { id: 2, x: 200, y: 0 }];
    const sim = createSimulation(n, [{ source: 1, target: 2 }], { seed: 1, linkDistance: 50, charge: -30, centerStrength: 0, alphaDecay: 0.01 });
    for (let i = 0; i < 300 && sim.alpha() > 0.01; i++) sim.step();
    const d = dist(n[0], n[1]);
    ok(d > 20 && d < 120, 'd=' + d);
});
t('模擬:群心吸力使同群聚攏、異群分離', () => {
    const rng = createRng(9);
    const nodes = Array.from({ length: 60 }, (_, i) => ({ id: i, group: i < 30 ? 'A' : 'B', x: (rng() - 0.5) * 100, y: (rng() - 0.5) * 100 }));
    const centers = new Map([['A', { x: -150, y: 0 }], ['B', { x: 150, y: 0 }]]);
    const sim = createSimulation(nodes, [], { seed: 9, clusterOf: (x) => x.group, clusterCenters: centers, clusterStrength: 0.2, charge: -40, centerStrength: 0, alphaDecay: 0.01 });
    for (let i = 0; i < 200 && sim.alpha() > 0.01; i++) sim.step();
    const mean = (g, k) => nodes.filter(x => x.group === g).reduce((s, x) => s + x[k], 0) / 30;
    ok(mean('A', 'x') < -50, 'A x̄=' + mean('A', 'x'));
    ok(mean('B', 'x') > 50, 'B x̄=' + mean('B', 'x'));
});
t('模擬:alpha 退火至停止;step 回傳 false', () => {
    const n = [{ id: 1 }, { id: 2 }];
    const sim = createSimulation(n, [], { seed: 1 });
    let steps = 0;
    while (sim.step()) { steps++; ok(steps < 1000, '未收斂'); }
    ok(sim.alpha() < 0.011);
    ok(sim.step() === false);
});
t('模擬:確定性(同種子兩次跑位置逐字相同)', () => {
    const mk = () => Array.from({ length: 20 }, (_, i) => ({ id: i }));
    const a = mk(), b = mk();
    const la = [{ source: 0, target: 1 }, { source: 2, target: 3 }];
    const s1 = createSimulation(a, la, { seed: 123 });
    const s2 = createSimulation(b, la, { seed: 123 });
    for (let i = 0; i < 50; i++) { s1.step(); s2.step(); }
    ok(JSON.stringify(a.map(n => [n.x.toFixed(6), n.y.toFixed(6)])) === JSON.stringify(b.map(n => [n.x.toFixed(6), n.y.toFixed(6)])));
});
t('模擬:fixed 節點不動(拖曳釘住用)', () => {
    const n = [{ id: 1, x: 0, y: 0, fixed: true }, { id: 2, x: 5, y: 0 }];
    const sim = createSimulation(n, [], { seed: 1 });
    for (let i = 0; i < 30; i++) sim.step();
    ok(n[0].x === 0 && n[0].y === 0);
});
t('reheat 提升 alpha(增量更新用)', () => {
    const sim = createSimulation([{ id: 1 }], [], { seed: 1 });
    while (sim.step());
    sim.reheat(0.6);
    ok(sim.alpha() >= 0.6);
    ok(sim.step() === true);
});

let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL'} ${r.name}${r.pass ? '' : ' — ' + r.detail}`); }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
