/**
 * DataExplorer — 統計探索面板(大型複合元件)。
 * 管線:查詢資料(setData/dataSource/bind 表單)→ ChartSpec(使用者操作)
 *       → AggregationEngine(純函式)→ Canvas 圖表 + 聚合表 + 明細分頁表。
 *
 * 維度→視覺通道:x、y、series(第三維)、color(色階維)、size(氣泡維)——
 * 2D/3D/4D 全靠通道組合(政策:無真 3D)。spec 為可序列化 JSON、可存
 * PageDefinition/URL;**視為不可信輸入**:chartType/agg/欄位全白名單驗證。
 *
 * @example
 * const ex = new DataExplorer({
 *     container,
 *     fields: [                                     // schema(可省略→自動推斷)
 *         { name: 'unit', label: '轄區', type: 'category' },
 *         { name: 'type', label: '案類', type: 'category' },
 *         { name: 'amount', label: '金額', type: 'number', unit: '萬' },
 *         { name: 'at', label: '發生日', type: 'time' }
 *     ],
 *     data: rows,                                   // 或 dataSource: async (q) => rows
 *     spec: { chartType: 'heatmap', x: { field: 'unit' }, series: { field: 'type' }, y: { field: 'amount', agg: 'sum' } }
 * });
 * ex.mount('#host');
 * // ex.bind(searchForm);  ex.getSpec();  ex.setSpec(spec);  ex.refresh(query);
 */
import { Dropdown } from '../form/Dropdown/index.js';
import { TextInput } from '../form/TextInput/index.js';
import { BasicButton } from '../common/BasicButton/index.js';
import { DataTable } from '../layout/DataTable/index.js';
import { LoadingSpinner } from '../common/LoadingSpinner/index.js';
import { summarize, pivot, binNumeric, bucketTime, topN, AGGS } from '../utils/aggregation-engine.js';
import { BarChart } from '../viz/BarChart.js';
import { LineChart } from '../viz/LineChart.js';
import { PieChart } from '../viz/PieChart.js';
import { ScatterChart } from '../viz/ScatterChart.js';
import { HeatmapChart } from '../viz/HeatmapChart.js';

export const CHART_TYPES = [
    { value: 'bar', label: '長條圖(2D)' },
    { value: 'groupedBar', label: '分組長條(3D:系列)' },
    { value: 'stackedBar', label: '堆疊長條(3D:系列)' },
    { value: 'line', label: '折線圖(2D/3D)' },
    { value: 'pie', label: '圓餅圖(占比)' },
    { value: 'histogram', label: '直方圖(分佈)' },
    { value: 'scatter', label: '散布圖(2D~4D)' },
    { value: 'heatmap', label: '熱圖(色階)' }
];
const TIME_UNITS = [
    { value: 'year', label: '年' }, { value: 'quarter', label: '季' },
    { value: 'month', label: '月' }, { value: 'day', label: '日' }
];
const AGG_LABELS = { count: '筆數', sum: '總和', avg: '平均', min: '最小', max: '最大', distinct: '相異數', median: '中位數' };
const NONE = '__none__';

let seq = 0;

export class DataExplorer {
    constructor(options = {}) {
        this.options = {
            container: null,
            fields: null,            // schema;null=由資料推斷
            data: [],
            dataSource: null,        // async (query) => rows
            spec: null,
            pageSize: 20,
            roc: true,               // 時間桶標籤用民國
            title: '',
            onSpecChange: null,
            onError: null,
            ...options
        };
        this._rows = Array.isArray(this.options.data) ? this.options.data : [];
        this._fields = this.options.fields || null;
        this.__spec = this.options.spec || null;   // 初始 spec(_afterData 會白名單驗證)
        this._chart = null;
        this._controlInputs = [];    // 控制列的 Dropdown/TextInput(Dropdown 掛 document 監聽,重建前必 destroy)
        this._aggTable = null;
        this._rawTable = null;
        this._unitTimer = null;
        this._uid = 'dx-' + (++seq);
        this._view = 'chart';        // chart | agg | raw
        this._destroyed = false;

        this._buildDom();
        const c = this.options.container;
        const target = typeof c === 'string' ? document.querySelector(c) : c;
        if (target) target.appendChild(this.element);
        if (this._rows.length) this._afterData();
        else if (this.options.dataSource) this.refresh({});
        else this._renderAll();
    }

    /* ══ 資料介面 ══ */

    setData(rows) {
        this._rows = Array.isArray(rows) ? rows : [];
        this._afterData();
    }

    async refresh(query = {}) {
        if (typeof this.options.dataSource !== 'function') return;
        this._setLoading(true);
        try {
            const rows = await this.options.dataSource(query);
            this._rows = Array.isArray(rows) ? rows : [];
            this._afterData();
        } catch (e) {
            console.error('[DataExplorer] dataSource 失敗:', e);
            if (this.options.onError) this.options.onError(e);
            this._rows = [];
            this._afterData();
        } finally {
            this._setLoading(false);
        }
    }

    /** 繫結查詢表單:攔截其 onSearch,查詢值轉給 dataSource。 */
    bind(form) {
        if (!form || !form.options) return this;
        const prev = form.options.onSearch;
        form.options.onSearch = (query) => {
            if (typeof prev === 'function') prev(query);
            this.refresh(query);
        };
        return this;
    }

    _afterData() {
        if (!this.options.fields) this._fields = this._inferFields(this._rows);
        if (!this._spec) this._spec = this._defaultSpec();
        this._spec = this._validateSpec(this._spec);
        this._buildControls();
        this._renderAll();
    }

    /* ══ Schema 推斷 ══ */

    _inferFields(rows) {
        if (!rows.length) return [];
        const names = Object.keys(rows[0]);
        const sample = rows.slice(0, 50);
        return names.map(name => {
            let num = 0, time = 0, n = 0;
            for (const r of sample) {
                const v = r[name];
                if (v == null || v === '') continue;
                n++;
                if (typeof v === 'number' || (!Number.isNaN(Number(v)) && String(v).trim() !== '')) num++;
                else if (!Number.isNaN(new Date(v).getTime()) && /[-/T]/.test(String(v))) time++;
            }
            const type = n && time / n > 0.8 ? 'time' : n && num / n > 0.8 ? 'number' : 'category';
            return { name, label: name, type };
        });
    }

    _f(name) { return (this._fields || []).find(f => f.name === name) || null; }
    _byType(...types) { return (this._fields || []).filter(f => types.includes(f.type)); }

    /* ══ Spec ══ */

    get _spec() { return this.__spec; }
    set _spec(v) { this.__spec = v; }

    _defaultSpec() {
        const cat = this._byType('category')[0];
        const num = this._byType('number')[0];
        return {
            chartType: 'bar',
            x: { field: cat ? cat.name : (this._fields[0] && this._fields[0].name), bin: null },
            y: { field: num ? num.name : null, agg: num ? 'sum' : 'count', unit: num ? (num.unit || '') : '筆' },
            series: null, color: null, size: null, topN: 30
        };
    }

    /** 白名單驗證(spec 可能來自 URL/儲存,一律不可信)。非法即修正或擲錯。 */
    _validateSpec(spec) {
        const s = JSON.parse(JSON.stringify(spec || {}));
        if (!CHART_TYPES.some(c => c.value === s.chartType)) s.chartType = 'bar';
        const okField = (ax) => ax && ax.field && this._f(ax.field);
        if (!okField(s.x)) s.x = this._defaultSpec().x;
        if (s.y && s.y.agg && !AGGS.includes(s.y.agg)) throw new Error(`[DataExplorer] 不允許的聚合:${s.y.agg}`);
        if (!s.y || (!okField(s.y) && s.y.agg !== 'count')) s.y = { field: null, agg: 'count', unit: '筆' };
        for (const k of ['series', 'color', 'size']) if (s[k] && !okField(s[k])) s[k] = null;
        if (s.x.bin && s.x.bin.type && !TIME_UNITS.some(u => u.value === s.x.bin.type) && s.x.bin.type !== 'numeric') s.x.bin = null;
        return s;
    }

    getSpec() { return JSON.parse(JSON.stringify(this._spec)); }
    setSpec(spec) {
        this._spec = this._validateSpec(spec);
        this._buildControls();
        this._renderAll();
        if (this.options.onSpecChange) this.options.onSpecChange(this.getSpec());
    }

    /* ══ DOM ══ */

    _buildDom() {
        const el = document.createElement('div');
        el.className = 'cl-data-explorer';
        el.style.cssText = 'display: flex; flex-direction: column; gap: 10px; min-width: 0; width: 100%; height: 100%; box-sizing: border-box;';
        if (this.options.title) {
            const t = document.createElement('div');
            t.textContent = this.options.title;
            t.style.cssText = 'font-size: var(--cl-font-size-lg); font-weight: 600; color: var(--cl-text);';
            el.appendChild(t);
        }
        // 控制列(spec builder)
        this._controls = document.createElement('div');
        this._controls.className = 'cl-data-explorer__controls';
        this._controls.style.cssText = 'display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: flex-end;' +
            ' padding: 10px 12px; background: var(--cl-bg-secondary); border: 1px solid var(--cl-border-light); border-radius: var(--cl-radius-md);';
        el.appendChild(this._controls);
        // 視圖切換 + 匯出
        const bar = document.createElement('div');
        bar.style.cssText = 'display: flex; gap: 8px; align-items: center; flex-wrap: wrap;';
        this._viewBtns = {};
        [['chart', '圖表'], ['agg', '聚合結果'], ['raw', '明細資料']].forEach(([k, label]) => {
            const b = new BasicButton({
                type: 'custom', customLabel: label,
                variant: k === this._view ? 'primary' : 'secondary',
                onClick: () => this._switchView(k)
            });
            b.mount(bar);
            this._viewBtns[k] = b;
        });
        const spacer = document.createElement('span');
        spacer.style.cssText = 'margin-left: auto;';
        bar.appendChild(spacer);
        new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '匯出圖 PNG', onClick: () => this._exportPNG() }).mount(bar);
        new BasicButton({ type: 'custom', variant: 'secondary', customLabel: '匯出 CSV', onClick: () => this._exportCSV() }).mount(bar);
        el.appendChild(bar);
        // 內容區(三視圖)
        this._body = document.createElement('div');
        this._body.style.cssText = 'position: relative; flex: 1 1 auto; min-height: 260px;';
        this._chartHost = document.createElement('div');
        this._chartHost.style.cssText = 'width: 100%; height: 100%; min-height: 260px;';
        this._aggHost = document.createElement('div');
        this._aggHost.style.cssText = 'display: none;';
        this._rawHost = document.createElement('div');
        this._rawHost.style.cssText = 'display: none;';
        this._body.appendChild(this._chartHost);
        this._body.appendChild(this._aggHost);
        this._body.appendChild(this._rawHost);
        el.appendChild(this._body);
        // 載入遮罩
        this._loading = document.createElement('div');
        this._loading.style.cssText = 'position: absolute; inset: 0; display: none; align-items: center; justify-content: center; background: var(--cl-bg-overlay-strong); z-index: 5;';
        this._body.appendChild(this._loading);
        this._spinner = new LoadingSpinner({ variant: 'dots' });
        this._spinner.mount(this._loading);
        this.element = el;
    }

    _setLoading(on) { this._loading.style.display = on ? 'flex' : 'none'; }

    _switchView(k) {
        this._view = k;
        this._chartHost.style.display = k === 'chart' ? 'block' : 'none';
        this._aggHost.style.display = k === 'agg' ? 'block' : 'none';
        this._rawHost.style.display = k === 'raw' ? 'block' : 'none';
        for (const [key, b] of Object.entries(this._viewBtns)) {
            // BasicButton 無 setVariant 公開 API 時以重掛代替:直接改樣式最小干預
            b.element && (b.element.style.opacity = key === k ? '1' : '0.65');
        }
        if (k === 'chart' && this._chart) this._chart.render();
    }

    /* ══ 控制列(spec builder)══ */

    _buildControls() {
        const host = this._controls;
        for (const c of this._controlInputs) c.destroy();
        this._controlInputs = [];
        host.textContent = '';
        const s = this._spec;
        if (!this._fields || !this._fields.length) {
            const hint = document.createElement('span');
            hint.textContent = '尚無資料——setData(rows) 或提供 dataSource';
            hint.style.cssText = 'color: var(--cl-text-dim); font-size: var(--cl-font-size-sm);';
            host.appendChild(hint);
            return;
        }
        const mk = (label, node) => {
            const wrap = document.createElement('div');
            wrap.style.cssText = 'display: flex; flex-direction: column; gap: 2px; min-width: 110px;';
            const l = document.createElement('span');
            l.textContent = label;
            l.style.cssText = 'font-size: var(--cl-font-size-xs); color: var(--cl-text-secondary);';
            wrap.appendChild(l);
            wrap.appendChild(node);
            host.appendChild(wrap);
            return wrap;
        };
        const dd = (items, value, onChange, width = '130px') => {
            const box = document.createElement('div');
            const d = new Dropdown({ items, value, width, onChange });
            d.mount(box);
            this._controlInputs.push(d);
            return box;
        };
        const fieldItems = (types, withNone) => {
            const items = this._byType(...types).map(f => ({ label: f.label || f.name, value: f.name }));
            if (withNone) items.unshift({ label: '(無)', value: NONE });
            return items;
        };
        const patch = (fn) => (v) => { fn(v); this.setSpec(this._spec); };

        // 圖型
        mk('圖表類型', dd(CHART_TYPES, s.chartType, patch(v => { this._spec.chartType = v; })));
        const ct = s.chartType;

        if (ct === 'scatter') {
            mk('X 軸(數值)', dd(fieldItems(['number']), s.x.field, patch(v => { this._spec.x = { field: v }; })));
            mk('Y 軸(數值)', dd(fieldItems(['number']), s.y.field, patch(v => { this._spec.y = { field: v, agg: 'sum' }; })));
            mk('顏色維', dd(fieldItems(['number', 'category'], true), s.color ? s.color.field : NONE,
                patch(v => { this._spec.color = v === NONE ? null : { field: v }; })));
            mk('尺寸維(氣泡)', dd(fieldItems(['number'], true), s.size ? s.size.field : NONE,
                patch(v => { this._spec.size = v === NONE ? null : { field: v }; })));
        } else if (ct === 'histogram') {
            mk('欄位(數值)', dd(fieldItems(['number']), s.x.field, patch(v => { this._spec.x = { field: v, bin: { type: 'numeric', count: (s.x.bin && s.x.bin.count) || 10 } }; })));
            mk('分箱數', dd([5, 10, 15, 20].map(n => ({ label: String(n), value: n })), (s.x.bin && s.x.bin.count) || 10,
                patch(v => { this._spec.x.bin = { type: 'numeric', count: v }; }), '80px'));
        } else {
            const xf = this._f(s.x.field);
            mk('X 軸(維度)', dd(fieldItems(['category', 'time', 'number']), s.x.field,
                patch(v => { this._spec.x = { field: v, bin: this._f(v) && this._f(v).type === 'time' ? { type: 'month' } : null }; })));
            if (xf && xf.type === 'time') {
                mk('時間桶', dd(TIME_UNITS, (s.x.bin && s.x.bin.type) || 'month',
                    patch(v => { this._spec.x.bin = { type: v }; }), '80px'));
            }
            mk('Y 聚合', dd(AGGS.map(a => ({ label: AGG_LABELS[a] || a, value: a })), s.y.agg,
                patch(v => { this._spec.y.agg = v; if (v === 'count') this._spec.y.unit = '筆'; }), '96px'));
            if (s.y.agg !== 'count') {
                mk('Y 欄位(數值)', dd(fieldItems(['number']), s.y.field || (this._byType('number')[0] || {}).name,
                    patch(v => { this._spec.y.field = v; this._spec.y.unit = (this._f(v) && this._f(v).unit) || ''; })));
            }
            if (ct !== 'pie') {
                mk(ct === 'heatmap' ? 'Y 軸維度(必選)' : '系列(第三維)',
                    dd(fieldItems(['category'], ct !== 'heatmap'), s.series ? s.series.field : NONE,
                        patch(v => { this._spec.series = v === NONE ? null : { field: v }; })));
            }
        }
        // 單位(逐鍵觸發全管線太重:spec 即時更新,重繪 debounce 200ms)
        const unitBox = document.createElement('div');
        const ti = new TextInput({
            value: s.y.unit || '', placeholder: '如:件/萬', width: '84px',
            onChange: (v) => {
                this._spec.y.unit = v;
                if (this._unitTimer) clearTimeout(this._unitTimer);
                this._unitTimer = setTimeout(() => { this._unitTimer = null; this._renderAll(); }, 200);
            }
        });
        ti.mount(unitBox);
        this._controlInputs.push(ti);
        mk('單位', unitBox);
    }

    /* ══ 聚合 + 圖表轉接 ══ */

    _xKeyFn() {
        const s = this._spec;
        const f = this._f(s.x.field);
        if (f && f.type === 'time') {
            const unit = (s.x.bin && s.x.bin.type) || 'month';
            const cache = new Map();
            return (r) => {
                const v = r[s.x.field];
                if (!cache.has(v)) { const b = bucketTime(v, unit, { roc: this.options.roc }); cache.set(v, b ? b.label : null); }
                return cache.get(v);
            };
        }
        return s.x.field;
    }

    _compute() {
        const s = this._spec;
        // 時間 x:先按時間排序(groupBy 首次出現序=時間序;民國標籤字典序不可靠)
        const xf = this._f(s.x.field);
        const rows = (xf && xf.type === 'time')
            ? this._rows
                .map(r => [new Date(r[s.x.field]).getTime(), r])   // 每列只轉一次時間戳(sort 穩定,順序不變)
                .sort((a, b) => a[0] - b[0])
                .map(p => p[1])
            : this._rows;
        const yDef = { field: s.y.field, agg: s.y.agg || 'count' };
        switch (s.chartType) {
            case 'histogram': {
                const vals = rows.map(r => r[s.x.field]);
                const bins = binNumeric(vals, (s.x.bin && s.x.bin.count) || 10);
                const counts = bins.labels.map(() => 0);
                for (const v of vals) { const i = bins.indexOf(v); if (i >= 0) counts[i]++; }
                return { kind: '1d', labels: bins.labels, values: counts, unitOverride: '筆' };
            }
            case 'scatter': {
                const pts = rows.map(r => ({
                    x: Number(r[s.x.field]), y: Number(r[s.y.field]),
                    c: s.color ? r[s.color.field] : null,
                    s: s.size ? Number(r[s.size.field]) : null,
                    label: r[(this._fields.find(f => f.type === 'category') || {}).name]
                }));
                return { kind: 'points', points: pts };
            }
            case 'heatmap':
            case 'groupedBar':
            case 'stackedBar': {
                if (!s.series) {   // 無系列→退化 1d
                    const r1 = summarize(rows, { x: { key: this._xKeyFn(), field: s.x.field }, y: yDef });
                    return { kind: '1d', ...topN(r1, s.topN || 30) };
                }
                const pv = pivot(rows, { x: { key: this._xKeyFn(), field: s.x.field }, series: { field: s.series.field }, y: yDef });
                return { kind: '2d', ...pv };
            }
            case 'line': {
                if (s.series) {
                    const pv = pivot(rows, { x: { key: this._xKeyFn(), field: s.x.field }, series: { field: s.series.field }, y: yDef });
                    return { kind: '2d', ...pv };
                }
                const r1 = summarize(rows, { x: { key: this._xKeyFn(), field: s.x.field }, y: yDef });
                return { kind: '1d', ...r1 };
            }
            default: {   // bar / pie
                const r1 = summarize(rows, { x: { key: this._xKeyFn(), field: s.x.field }, y: yDef });
                return { kind: '1d', ...topN(r1, s.topN || 30) };
            }
        }
    }

    _renderAll() {
        if (this._destroyed) return;
        // 本次即為完整重繪，取消尚未觸發的 unit 輸入 debounce（避免 200ms 後多一次冗餘重繪）
        if (this._unitTimer) { clearTimeout(this._unitTimer); this._unitTimer = null; }
        let agg;
        try { agg = this._compute(); }
        catch (e) {
            console.error('[DataExplorer] 聚合失敗:', e);
            agg = { kind: '1d', labels: [], values: [] };
        }
        this._agg = agg;
        this._renderChart(agg);
        this._renderAggTable(agg);
        this._renderRawTable();
    }

    _renderChart(agg) {
        const s = this._spec;
        if (this._chart) { this._chart.destroy(); this._chart = null; }
        this._chartHost.textContent = '';
        const unit = agg.unitOverride || s.y.unit || '';
        const common = { container: this._chartHost, width: '100%', height: '100%', unit };
        const xL = (this._f(s.x.field) || {}).label || s.x.field;
        const yL = s.y.agg === 'count' ? '筆數' : `${(this._f(s.y.field) || {}).label || s.y.field}(${AGG_LABELS[s.y.agg]})`;
        switch (s.chartType) {
            case 'line':
                this._chart = new LineChart({
                    ...common,
                    data: agg.kind === '2d'
                        ? { labels: agg.xLabels, series: agg.seriesLabels.map((n, i) => ({ name: n, data: agg.matrix[i] })) }
                        : { labels: agg.labels, series: [{ name: yL, data: agg.values }] }
                });
                break;
            case 'pie':
                this._chart = new PieChart({ ...common, data: (agg.labels || []).map((n, i) => ({ name: n, value: agg.values[i] || 0 })) });
                break;
            case 'scatter':
                this._chart = new ScatterChart({
                    ...common,
                    points: agg.points || [],
                    xLabel: xL, yLabel: (this._f(s.y.field) || {}).label || s.y.field,
                    xUnit: (this._f(s.x.field) || {}).unit || '', yUnit: unit,
                    colorLabel: s.color ? ((this._f(s.color.field) || {}).label || s.color.field) : '',
                    sizeLabel: s.size ? ((this._f(s.size.field) || {}).label || s.size.field) : ''
                });
                break;
            case 'heatmap': {
                const is2d = agg.kind === '2d';
                this._chart = new HeatmapChart({
                    ...common,
                    xLabels: is2d ? agg.xLabels : (agg.labels || []),
                    yLabels: is2d ? agg.seriesLabels : [yL],
                    matrix: is2d ? agg.matrix : [agg.values || []]
                });
                break;
            }
            default: {   // bar / groupedBar / stackedBar / histogram
                const stacked = s.chartType === 'stackedBar';
                this._chart = new BarChart({
                    ...common, stacked,
                    data: agg.kind === '2d'
                        ? { labels: agg.xLabels, series: agg.seriesLabels.map((n, i) => ({ name: n, data: agg.matrix[i] })) }
                        : { labels: agg.labels, series: [{ name: yL, data: agg.values }] }
                });
            }
        }
    }

    _aggAsRows(agg) {
        const s = this._spec;
        const xL = (this._f(s.x.field) || {}).label || s.x.field || 'x';
        if (agg.kind === '2d') {
            const cols = [xL, ...agg.seriesLabels.map(String)];
            const rows = agg.xLabels.map((l, xi) => [l, ...agg.seriesLabels.map((_, si) => agg.matrix[si][xi])]);
            return { cols, rows };
        }
        if (agg.kind === 'points') {
            const cols = ['x', 'y', 'color', 'size'];
            return { cols, rows: (agg.points || []).map(p => [p.x, p.y, p.c ?? '', p.s ?? '']) };
        }
        return { cols: [xL, '值'], rows: (agg.labels || []).map((l, i) => [l, agg.values[i]]) };
    }

    _renderAggTable(agg) {
        if (this._aggTable) { this._aggTable.destroy(); this._aggTable = null; }
        this._aggHost.textContent = '';
        const { cols, rows } = this._aggAsRows(agg);
        this._aggTable = new DataTable({
            container: this._aggHost,
            columns: cols.map(c => ({ name: c })),
            data: rows,
            pageSize: this.options.pageSize
        });
    }

    _renderRawTable() {
        if (this._rawTable) { this._rawTable.destroy(); this._rawTable = null; }
        this._rawHost.textContent = '';
        if (!this._rows.length || !this._fields) return;
        this._rawTable = new DataTable({
            container: this._rawHost,
            columns: this._fields.map(f => ({ name: f.label || f.name })),
            data: this._rows.map(r => this._fields.map(f => r[f.name] ?? '')),
            pageSize: this.options.pageSize
        });
    }

    /* ══ 匯出 ══ */

    _exportPNG() {
        if (!this._chart || !this._chart.exportPNG) return;
        const a = document.createElement('a');
        a.href = this._chart.exportPNG(2);
        a.download = 'chart.png';
        a.click();
    }

    exportCSV() {
        const { cols, rows } = this._aggAsRows(this._agg || { kind: '1d', labels: [], values: [] });
        const esc = (v) => {
            const s = v == null ? '' : String(v);
            return /[",\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
        };
        return '﻿' + [cols.map(esc).join(','), ...rows.map(r => r.map(esc).join(','))].join('\r\n');
    }

    _exportCSV() {
        const blob = new Blob([this.exportCSV()], { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = 'aggregated.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    /* ══ 生命週期 ══ */

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element.parentNode !== target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._destroyed = true;
        if (this._unitTimer) { clearTimeout(this._unitTimer); this._unitTimer = null; }
        for (const c of this._controlInputs) c.destroy();
        this._controlInputs = [];
        if (this._aggTable) { this._aggTable.destroy(); this._aggTable = null; }
        if (this._rawTable) { this._rawTable.destroy(); this._rawTable = null; }
        if (this._chart) this._chart.destroy();
        if (this.element && this.element.parentNode) this.element.parentNode.removeChild(this.element);
        this.element = null;
    }
}

export default DataExplorer;
