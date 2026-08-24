/**
 * force-engine.js — 力導向模擬核心(零依賴純函式;node 可直測)。
 * ClusterGraph 的物理層:Barnes-Hut 斥力 + 彈簧邊 + 群心吸力 + 向心力,
 * 速度阻尼積分 + alpha 退火。RNG 可注入(預設 LCG 種子)→ 版面確定性、測試可重現。
 *
 * 用法:
 *   const sim = createSimulation(nodes, links, { seed: 42, clusterOf: n => n.group, clusterCenters });
 *   while (sim.alpha() > 0.01) sim.step();
 * nodes 需可變(引擎寫入 x,y,vx,vy);links: { source, target }(id 或節點參照皆可)。
 */
import { buildQuadtree, bhAccumulate } from './quadtree.js';

/** 可種子 LCG(確定性;夠均勻供初始散佈)。 */
export function createRng(seed = 1) {
    let s = (seed >>> 0) || 1;
    return () => (s = (s * 1664525 + 1013904223) >>> 0) / 4294967296;
}

export function createSimulation(nodes, links = [], opts = {}) {
    const o = {
        seed: 1,
        charge: -120,          // 斥力強度(負=相斥;BH 內取絕對值用)
        theta: 0.9,            // BH 精度
        linkDistance: 40,
        linkStrength: 0.08,
        clusterStrength: 0.06, // 群心吸力
        centerStrength: 0.015, // 全域向心(防漂移)
        friction: 0.85,        // 速度阻尼
        alpha: 1,
        alphaDecay: 0.025,
        alphaMin: 0.01,
        maxVelocity: 30,
        clusterOf: null,       // n => clusterKey(選配)
        clusterCenters: null,  // Map(clusterKey → {x,y})(選配;不給則用群質心)
        ...opts
    };
    const rng = o.rng || createRng(o.seed);
    let alpha = o.alpha;

    // 初始化位置(未給者以 rng 散佈;確定性)
    for (const n of nodes) {
        if (n.x == null) n.x = (rng() - 0.5) * 400;
        if (n.y == null) n.y = (rng() - 0.5) * 400;
        n.vx = n.vx || 0; n.vy = n.vy || 0;
    }
    // link 端點解參照(id → 節點)
    const byId = new Map(nodes.map(n => [n.id, n]));
    const L = links.map(l => ({
        source: typeof l.source === 'object' ? l.source : byId.get(l.source),
        target: typeof l.target === 'object' ? l.target : byId.get(l.target),
        weight: l.weight || 1
    })).filter(l => l.source && l.target && l.source !== l.target);

    function step() {
        if (alpha < o.alphaMin) return false;

        // 1) BH 斥力(k>0=相斥;self 由 bh 內部排除)
        const root = buildQuadtree(nodes);
        const k = Math.abs(o.charge) * alpha;
        for (const n of nodes) {
            if (n.fixed) continue;
            const out = { fx: 0, fy: 0 };
            bhAccumulate(root, n, n.x, n.y, o.theta, k, out);
            n.vx += out.fx; n.vy += out.fy;
        }
        // 2) 彈簧邊
        const kl = o.linkStrength * alpha;
        for (const l of L) {
            const dx = l.target.x - l.source.x, dy = l.target.y - l.source.y;
            const d = Math.sqrt(dx * dx + dy * dy) || 1e-6;
            const f = ((d - o.linkDistance) / d) * kl * l.weight;
            const fx = dx * f, fy = dy * f;
            if (!l.source.fixed) { l.source.vx += fx; l.source.vy += fy; }
            if (!l.target.fixed) { l.target.vx -= fx; l.target.vy -= fy; }
        }
        // 3) 群心吸力
        if (o.clusterOf) {
            let centers = o.clusterCenters;
            if (!centers) {
                centers = new Map();
                const acc = new Map();
                for (const n of nodes) {
                    const c = o.clusterOf(n);
                    if (c == null) continue;
                    let a = acc.get(c);
                    if (!a) { a = { x: 0, y: 0, n: 0 }; acc.set(c, a); }
                    a.x += n.x; a.y += n.y; a.n++;
                }
                for (const [c, a] of acc) centers.set(c, { x: a.x / a.n, y: a.y / a.n });
            }
            const kc = o.clusterStrength * alpha;
            for (const n of nodes) {
                if (n.fixed) continue;
                const c = o.clusterOf(n);
                const ctr = c != null ? centers.get(c) : null;
                if (!ctr) continue;
                n.vx += (ctr.x - n.x) * kc;
                n.vy += (ctr.y - n.y) * kc;
            }
        }
        // 4) 全域向心 + 積分
        const kg = o.centerStrength * alpha;
        for (const n of nodes) {
            if (n.fixed) { n.vx = 0; n.vy = 0; continue; }
            n.vx = (n.vx - n.x * kg) * o.friction;
            n.vy = (n.vy - n.y * kg) * o.friction;
            const v = Math.sqrt(n.vx * n.vx + n.vy * n.vy);
            if (v > o.maxVelocity) { n.vx = n.vx / v * o.maxVelocity; n.vy = n.vy / v * o.maxVelocity; }
            n.x += n.vx; n.y += n.vy;
        }
        alpha *= (1 - o.alphaDecay);
        return true;
    }

    return {
        step,
        alpha: () => alpha,
        reheat: (a = 0.5) => { alpha = Math.max(alpha, a); },
        stop: () => { alpha = 0; },
        links: () => L,
        options: o
    };
}
