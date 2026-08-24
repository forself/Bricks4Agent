/**
 * aggregation-engine.js — 統計聚合引擎(零依賴純函式;node 可直測)。
 *
 * DataExplorer/圖表家族的資料層:rows(明細)+ ChartSpec → 聚合結果。
 * 設計原則:
 *   - 確定性:群鍵依「首次出現順序」穩定輸出(測試可逐字斷言)
 *   - null/undefined 一律歸 '(空)' 群;數值聚合忽略非數值
 *   - spec 是不可信輸入:agg 僅接受白名單枚舉,未知即擲錯(fail-closed)
 */

export const AGGS = ['count', 'sum', 'avg', 'min', 'max', 'distinct', 'median'];
export const NULL_KEY = '(空)';

const keyOf = (v) => (v == null || v === '' ? NULL_KEY : String(v));
const nums = (rows, field) => rows.map(r => Number(r[field])).filter(n => !Number.isNaN(n));

/** 單群聚合(白名單;未知 agg 擲錯)。 */
export function aggregate(rows, field, agg) {
    switch (agg) {
        case 'count': return rows.length;
        case 'sum': return nums(rows, field).reduce((a, b) => a + b, 0);
        case 'avg': { const v = nums(rows, field); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : null; }
        case 'min': { const v = nums(rows, field); return v.length ? Math.min(...v) : null; }
        case 'max': { const v = nums(rows, field); return v.length ? Math.max(...v) : null; }
        case 'distinct': return new Set(rows.map(r => keyOf(r[field]))).size;
        case 'median': {
            const v = nums(rows, field).sort((a, b) => a - b);
            if (!v.length) return null;
            const m = v.length >> 1;
            return v.length % 2 ? v[m] : (v[m - 1] + v[m]) / 2;
        }
        default: throw new Error(`[aggregation] 不允許的聚合:${agg}(白名單:${AGGS.join(',')})`);
    }
}

/**
 * 分組(單鍵或多鍵;keyFns 可為欄位名字串或 row=>key 函式)。
 * @returns {Array<{ key:string, keys:string[], rows:Object[] }>} 依首次出現順序
 */
export function groupBy(rows, keyFns) {
    const fns = (Array.isArray(keyFns) ? keyFns : [keyFns])
        .map(k => typeof k === 'function' ? k : (r) => r[k]);
    const map = new Map();
    for (const row of rows) {
        const keys = fns.map(f => keyOf(f(row)));
        const key = keys.join('');
        let g = map.get(key);
        if (!g) { g = { key: keys.join(' / '), keys, rows: [] }; map.set(key, g); }
        g.rows.push(row);
    }
    return [...map.values()];
}

/**
 * 一維聚合:x 分組 → y 聚合。
 * @returns {{ labels:string[], values:number[], groups }} 供 Bar/Pie/Line 直用
 */
export function summarize(rows, { x, y }) {
    const groups = groupBy(rows, x.key || x.field);
    const labels = groups.map(g => g.key);
    const values = groups.map(g => aggregate(g.rows, y.field, y.agg || 'count'));
    return { labels, values, groups };
}

/**
 * 二維樞紐:x × series 分組 → y 聚合(堆疊/分組長條、熱圖用)。
 * @returns {{ xLabels, seriesLabels, matrix:number[][] }} matrix[si][xi];無資料格為 null
 */
export function pivot(rows, { x, series, y }) {
    const xGroups = groupBy(rows, x.key || x.field);
    const xLabels = xGroups.map(g => g.key);
    const xIndex = new Map(xLabels.map((l, i) => [l, i]));
    const sGroups = groupBy(rows, series.key || series.field);
    const seriesLabels = sGroups.map(g => g.key);

    const cell = new Map();   // 'si|xi' → rows
    for (const row of rows) {
        const xk = keyOf(typeof (x.key || x.field) === 'function' ? (x.key || x.field)(row) : row[x.field]);
        const sk = keyOf(typeof (series.key || series.field) === 'function' ? (series.key || series.field)(row) : row[series.field]);
        const id = seriesLabels.indexOf(sk) + '|' + xIndex.get(xk);
        if (!cell.has(id)) cell.set(id, []);
        cell.get(id).push(row);
    }
    const matrix = seriesLabels.map((_, si) => xLabels.map((_, xi) => {
        const rs = cell.get(si + '|' + xi);
        return rs ? aggregate(rs, y.field, y.agg || 'count') : null;
    }));
    return { xLabels, seriesLabels, matrix };
}

/**
 * 數值等寬分箱。
 * @returns {{ edges:number[], labels:string[], indexOf:(v:number)=>number }} indexOf 域外回 -1
 */
export function binNumeric(values, binCount = 10) {
    const v = values.map(Number).filter(n => !Number.isNaN(n));
    if (!v.length) return { edges: [], labels: [], indexOf: () => -1 };
    let min = Math.min(...v), max = Math.max(...v);
    if (min === max) max = min + 1;
    const step = (max - min) / binCount;
    const edges = Array.from({ length: binCount + 1 }, (_, i) => min + step * i);
    const fmtN = (n) => Number(n.toFixed(step >= 1 ? 0 : 2));
    const labels = Array.from({ length: binCount }, (_, i) => `${fmtN(edges[i])}–${fmtN(edges[i + 1])}`);
    const indexOf = (x) => {
        const n = Number(x);
        if (Number.isNaN(n) || n < min || n > max) return -1;
        return Math.min(binCount - 1, Math.floor((n - min) / step));
    };
    return { edges, labels, indexOf };
}

/**
 * 時間分桶鍵(排序安全的 ISO 式 key)+ 顯示標籤(可民國)。
 * @param {string} unit - 'year'|'quarter'|'month'|'day'
 * @param {{roc?:boolean}} [opt] - roc:true 標籤用民國年
 * @returns {{ key:string, label:string }|null} 無法解析回 null
 */
export function bucketTime(value, unit = 'month', opt = {}) {
    const d = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(d.getTime())) return null;
    const y = d.getFullYear();
    const yy = opt.roc ? `${y - 1911}` : `${y}`;
    const m = d.getMonth() + 1, mm = String(m).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    switch (unit) {
        case 'year': return { key: `${y}`, label: opt.roc ? `${yy}年` : `${y}` };
        case 'quarter': { const q = Math.floor((m - 1) / 3) + 1; return { key: `${y}-Q${q}`, label: `${yy}年Q${q}` }; }
        case 'month': return { key: `${y}-${mm}`, label: `${yy}年${m}月` };
        case 'day': return { key: `${y}-${mm}-${dd}`, label: `${yy}/${mm}/${dd}` };
        default: throw new Error(`[aggregation] 不允許的時間單位:${unit}`);
    }
}

/** Top-N + 其餘歸「(其他)」(圖表防爆版)。 */
export function topN({ labels, values }, n, otherLabel = '(其他)') {
    if (labels.length <= n) return { labels: [...labels], values: [...values] };
    const idx = labels.map((_, i) => i).sort((a, b) => (values[b] ?? -Infinity) - (values[a] ?? -Infinity));
    const keep = idx.slice(0, n).sort((a, b) => a - b);   // 保留原出現順序
    const rest = idx.slice(n);
    const outL = keep.map(i => labels[i]);
    const outV = keep.map(i => values[i]);
    outL.push(otherLabel);
    outV.push(rest.reduce((s, i) => s + (values[i] || 0), 0));
    return { labels: outL, values: outV };
}
