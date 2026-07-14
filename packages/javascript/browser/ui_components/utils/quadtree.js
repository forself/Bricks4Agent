/**
 * quadtree.js — 四叉樹(零依賴純函式;node 可直測)。
 * 雙用途:Barnes-Hut n-body 斥力近似(O(n log n))與 Canvas 命中/範圍查詢。
 * 節點:{ x0,y0,x1,y1, body|null, kids|null, mass, mx,my }(mx,my=質心)。
 */

export function buildQuadtree(bodies, getX = (b) => b.x, getY = (b) => b.y) {
    if (!bodies.length) return null;
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const b of bodies) {
        const x = getX(b), y = getY(b);
        if (x < x0) x0 = x; if (x > x1) x1 = x;
        if (y < y0) y0 = y; if (y > y1) y1 = y;
    }
    // 正方形邊界(BH 精度)+ 防零尺寸
    const size = Math.max(x1 - x0, y1 - y0, 1e-6);
    const root = { x0, y0, x1: x0 + size, y1: y0 + size, body: null, kids: null, mass: 0, mx: 0, my: 0 };

    const insert = (node, b, x, y, depth) => {
        if (node.kids === null && node.body === null) { node.body = b; node._bx = x; node._by = y; return; }
        if (node.kids === null) {
            // 分裂;同點防無限遞迴(depth 上限後串成鏈:直接掛 body 清單)
            if (depth > 32) { (node.chain || (node.chain = [])).push({ b, x, y }); return; }
            const old = node.body, ox = node._bx, oy = node._by;
            node.body = null; node.kids = [null, null, null, null];
            place(node, old, ox, oy, depth);
        }
        place(node, b, x, y, depth);
    };
    const place = (node, b, x, y, depth) => {
        const midX = (node.x0 + node.x1) / 2, midY = (node.y0 + node.y1) / 2;
        const qi = (x >= midX ? 1 : 0) + (y >= midY ? 2 : 0);
        let kid = node.kids[qi];
        if (!kid) {
            kid = node.kids[qi] = {
                x0: qi & 1 ? midX : node.x0, x1: qi & 1 ? node.x1 : midX,
                y0: qi & 2 ? midY : node.y0, y1: qi & 2 ? node.y1 : midY,
                body: null, kids: null, mass: 0, mx: 0, my: 0
            };
        }
        insert(kid, b, x, y, depth + 1);
    };
    for (const b of bodies) insert(root, b, getX(b), getY(b), 0);

    // 後序累計質量與質心
    const accumulate = (node) => {
        if (!node) return { m: 0, mx: 0, my: 0 };
        let m = 0, sx = 0, sy = 0;
        if (node.body !== null) { m += 1; sx += node._bx; sy += node._by; }
        if (node.chain) for (const c of node.chain) { m += 1; sx += c.x; sy += c.y; }
        if (node.kids) for (const k of node.kids) {
            const r = accumulate(k);
            m += r.m; sx += r.mx * r.m; sy += r.my * r.m;
        }
        node.mass = m;
        node.mx = m ? sx / m : 0;
        node.my = m ? sy / m : 0;
        return { m, mx: node.mx, my: node.my };
    };
    accumulate(root);
    return root;
}

/**
 * Barnes-Hut 斥力:對位於 (x,y) 的 self 累加 fx/fy(k>0=被其他質量推開)。
 * self 會被排除(否則自身在樹中的巨力會炸掉模擬)。
 * @param {*} self - 查詢主體(葉節點比對排除;可為 null 表不排除)
 * @param {number} theta - 精度/速度權衡(0.9 常用;越小越準越慢)
 * @param {number} k - 斥力強度(>0)
 */
export function bhAccumulate(root, self, x, y, theta, k, out) {
    if (!root || root.mass === 0) return;
    const stack = [root];
    while (stack.length) {
        const node = stack.pop();
        if (!node || node.mass === 0) continue;
        const isLeaf = node.kids === null;
        let dx = node.mx - x, dy = node.my - y;
        let d2 = dx * dx + dy * dy;
        const w = node.x1 - node.x0;
        if (isLeaf || (w * w) / d2 < theta * theta) {
            let mass = node.mass;
            if (isLeaf) {
                // 葉:逐 body 排除 self(chain 同理),用實際座標
                const bodies = [];
                if (node.body !== null && node.body !== self) bodies.push([node._bx, node._by]);
                if (node.chain) for (const c of node.chain) if (c.b !== self) bodies.push([c.x, c.y]);
                for (const [bx, by] of bodies) {
                    let ddx = bx - x, ddy = by - y, dd2 = ddx * ddx + ddy * ddy;
                    if (dd2 < 1e-4) { ddx = 0.001; ddy = 0.001; dd2 = 2e-6; }   // 同點防除零(固定偏移,保確定性)
                    const f = k / dd2;
                    out.fx -= ddx * f;
                    out.fy -= ddy * f;
                }
                continue;
            }
            // 內部節點以質心近似(self 佔比可忽略;theta 已控誤差)
            if (d2 < 1e-4) { dx = 0.001; dy = 0.001; d2 = 2e-6; }
            const f = (k * mass) / d2;
            out.fx -= dx * f;
            out.fy -= dy * f;
        } else {
            for (const kid of node.kids) if (kid) stack.push(kid);
        }
    }
}

/** 範圍查詢:回傳 (x,y) 半徑 r 內最近的 body(命中測試用)。 */
export function nearestBody(root, x, y, r) {
    if (!root) return null;
    let best = null, bestD2 = r * r;
    const stack = [root];
    while (stack.length) {
        const node = stack.pop();
        if (!node) continue;
        // 剪枝:節點方框到點的最短距離 > 目前最佳 → 跳過
        const dx = x < node.x0 ? node.x0 - x : x > node.x1 ? x - node.x1 : 0;
        const dy = y < node.y0 ? node.y0 - y : y > node.y1 ? y - node.y1 : 0;
        if (dx * dx + dy * dy > bestD2) continue;
        const tryBody = (b, bx, by) => {
            const ddx = bx - x, ddy = by - y, d2 = ddx * ddx + ddy * ddy;
            if (d2 <= bestD2) { best = b; bestD2 = d2; }
        };
        if (node.body !== null) tryBody(node.body, node._bx, node._by);
        if (node.chain) for (const c of node.chain) tryBody(c.b, c.x, c.y);
        if (node.kids) for (const kid of node.kids) if (kid) stack.push(kid);
    }
    return best;
}
