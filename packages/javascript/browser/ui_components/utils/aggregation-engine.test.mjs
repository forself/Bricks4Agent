// aggregation-engine.test.mjs — 零依賴 node 直測:node utils/aggregation-engine.test.mjs
import { aggregate, groupBy, summarize, pivot, binNumeric, bucketTime, topN, NULL_KEY } from './aggregation-engine.js';

const results = [];
const t = (name, fn) => { try { fn(); results.push({ name, pass: true }); } catch (e) { results.push({ name, pass: false, detail: e.message }); } };
const eq = (a, b, msg) => { const A = JSON.stringify(a), B = JSON.stringify(b); if (A !== B) throw new Error(`${msg || ''} 期望 ${B} 得到 ${A}`); };
const ok = (c, msg) => { if (!c) throw new Error(msg || 'assert failed'); };

const rows = [
    { unit: '中山', type: '竊盜', n: 3, at: '2026-01-15' },
    { unit: '大安', type: '詐欺', n: 5, at: '2026-02-03' },
    { unit: '中山', type: '詐欺', n: 2, at: '2026-02-20' },
    { unit: '中山', type: '竊盜', n: 4, at: '2026-04-09' },
    { unit: '信義', type: null,   n: 'x', at: 'bad-date' },
];

t('aggregate 全白名單', () => {
    eq(aggregate(rows, 'n', 'count'), 5);
    eq(aggregate(rows, 'n', 'sum'), 14);            // 'x' 忽略
    eq(aggregate(rows, 'n', 'avg'), 3.5);
    eq(aggregate(rows, 'n', 'min'), 2);
    eq(aggregate(rows, 'n', 'max'), 5);
    eq(aggregate(rows, 'type', 'distinct'), 3);     // 竊盜/詐欺/(空)
    eq(aggregate(rows, 'n', 'median'), 3.5);
});
t('median 奇數筆', () => eq(aggregate(rows.slice(0, 3), 'n', 'median'), 3));
t('未知 agg fail-closed 擲錯', () => { let e = false; try { aggregate(rows, 'n', 'evil'); } catch { e = true; } ok(e); });
t('groupBy 首次出現順序穩定 + null 歸(空)', () => {
    const g = groupBy(rows, 'unit');
    eq(g.map(x => x.key), ['中山', '大安', '信義']);
    eq(g[0].rows.length, 3);
    const g2 = groupBy(rows, 'type');
    eq(g2.map(x => x.key), ['竊盜', '詐欺', NULL_KEY]);
});
t('groupBy 多鍵', () => {
    const g = groupBy(rows, ['unit', 'type']);
    eq(g[0].key, '中山 / 竊盜'); eq(g[0].rows.length, 2);
});
t('summarize 一維聚合', () => {
    const s = summarize(rows, { x: { field: 'unit' }, y: { field: 'n', agg: 'sum' } });
    eq(s.labels, ['中山', '大安', '信義']);
    eq(s.values, [9, 5, 0]);
});
t('pivot 二維樞紐(空格=null)', () => {
    const pv = pivot(rows, { x: { field: 'unit' }, series: { field: 'type' }, y: { field: 'n', agg: 'sum' } });
    eq(pv.xLabels, ['中山', '大安', '信義']);
    eq(pv.seriesLabels, ['竊盜', '詐欺', NULL_KEY]);
    eq(pv.matrix, [[7, null, null], [2, 5, null], [null, null, 0]]);
});
t('binNumeric 等寬 + 域外 -1 + 邊界含右端', () => {
    const b = binNumeric([0, 10, 5, 7], 5);
    eq(b.edges.length, 6); eq(b.labels.length, 5);
    eq(b.indexOf(0), 0); eq(b.indexOf(10), 4); eq(b.indexOf(5), 2);
    eq(b.indexOf(-1), -1); eq(b.indexOf('x'), -1);
});
t('binNumeric 單一值不除零', () => {
    const b = binNumeric([5, 5, 5], 4);
    ok(b.indexOf(5) >= 0);
});
t('bucketTime 各單位 + 民國 + 壞日期 null', () => {
    eq(bucketTime('2026-02-20', 'month'), { key: '2026-02', label: '2026年2月' });
    eq(bucketTime('2026-02-20', 'month', { roc: true }).label, '115年2月');
    eq(bucketTime('2026-04-09', 'quarter').key, '2026-Q2');
    eq(bucketTime('2026-04-09', 'year', { roc: true }).label, '115年');
    eq(bucketTime('2026-04-09', 'day').key, '2026-04-09');
    eq(bucketTime('bad-date', 'month'), null);
});
t('bucketTime key 可排序(字串序=時間序)', () => {
    const ks = ['2026-11-05', '2026-02-01', '2026-10-31'].map(d => bucketTime(d, 'month').key).sort();
    eq(ks, ['2026-02', '2026-10', '2026-11']);
});
t('topN 保留原順序 + 其他加總', () => {
    const r = topN({ labels: ['a', 'b', 'c', 'd'], values: [1, 9, 5, 3] }, 2);
    eq(r.labels, ['b', 'c', '(其他)']);
    eq(r.values, [9, 5, 4]);
});
t('topN 不足 N 原樣', () => {
    const r = topN({ labels: ['a'], values: [1] }, 5);
    eq(r.labels, ['a']); eq(r.values, [1]);
});

let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL'} ${r.name}${r.pass ? '' : ' — ' + r.detail}`); }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
