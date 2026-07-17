/**
 * TimelineChart — 泳道時間軸(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:Array of { id, group, start, end, label, details }  (start/end 為毫秒 epoch)
 * 泳道由 group 欄位自動分列;事件塊繪製矩形+ellipsis 文字。
 *
 * @example
 * new TimelineChart({
 *     container: '#host',
 *     data: [
 *         { id: 1, group: 'Server A', start: Date.now(), end: Date.now() + 5000,
 *           label: 'HTTP Request', details: 'GET /api/users' }
 *     ],
 *     title: 'System Event Timeline'
 * });
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';
import { ModalPanel } from '../layout/Panel/index.js';
import { FALLBACK_PAINT } from '../utils/theme-bus.js';

/** hexToRgb: 與 HeatmapChart 同款亮度自適應文字色 */
function hexToRgbArr(hex) {
    const h = (hex || FALLBACK_PAINT).replace('#', '');
    const v = h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6);
    const n = parseInt(v, 16) || 0;
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

export class TimelineChart extends CanvasChart {
    constructor(options = {}) {
        super({
            padding: { top: 40, right: 20, bottom: 30, left: 100 },
            ...options,
            width: typeof options.width === 'number' ? options.width + 'px' : (options.width || '100%'),
            height: typeof options.height === 'number' ? options.height + 'px' : (options.height || '300px'),
        });
        this.data = options.data || [];
        // 舊版語意:點擊事件列內建開詳情面板。使用者自傳 onPointClick 則以其為準。
        if (typeof this.options.onPointClick !== 'function') {
            this.options.onPointClick = (d) => this._handleClick(d);
        }
    }

    /** 更新資料並重繪(舊 API 相容)。 */
    setData(data) { this.data = data; this.render(); }

    draw(ctx, w, h) {
        const data = this.data;
        const tok = this.tokens([
            '--cl-text', '--cl-text-secondary', '--cl-text-dim',
            '--cl-border-light', '--cl-bg-secondary'
        ]);

        if (!data || !data.length) {
            ctx.font = this.font(13);
            ctx.fillStyle = tok['--cl-text-dim'];
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }

        // ── 1. 版面計算 ──
        const p = this.options.padding;
        const marginTop = p.top;
        const marginBottom = p.bottom;
        const marginLeft = p.left;
        const marginRight = p.right;

        const groups = [...new Set(data.map(d => d.group))];
        const drawH = Math.max(10, h - marginTop - marginBottom);
        const laneH = drawH / groups.length;
        const BAR_H = Math.min(20, laneH * 0.55);

        // 時間域
        const times = data.flatMap(d => [d.start, d.end]);
        const minTime = Math.min(...times);
        const maxTime = Math.max(...times);
        const timeRange = Math.max(1, maxTime - minTime);
        const drawW = Math.max(10, w - marginLeft - marginRight);
        const xOf = (t) => marginLeft + ((t - minTime) / timeRange) * drawW;

        // ── 2. 泳道線 + 群組標籤 ──
        ctx.lineWidth = 1;
        ctx.strokeStyle = tok['--cl-border-light'];
        ctx.fillStyle = tok['--cl-text'];
        ctx.font = this.font(12, 600);
        ctx.textAlign = 'right';
        ctx.textBaseline = 'middle';

        for (let i = 0; i < groups.length; i++) {
            const laneY = marginTop + i * laneH;
            // 分隔線
            ctx.beginPath();
            ctx.moveTo(0, laneY);
            ctx.lineTo(w, laneY);
            ctx.stroke();
            // 群組標籤
            ctx.fillText(
                this.ellipsis(ctx, groups[i], marginLeft - 12),
                marginLeft - 8,
                laneY + laneH / 2
            );
        }
        // 底部分隔線
        const bottomY = marginTop + groups.length * laneH;
        ctx.beginPath();
        ctx.moveTo(0, bottomY);
        ctx.lineTo(w, bottomY);
        ctx.stroke();

        // ── 3. 事件塊 ──
        ctx.font = this.font(10);
        ctx.textBaseline = 'middle';

        for (let i = 0; i < data.length; i++) {
            const d = data[i];
            const gi = groups.indexOf(d.group);
            const bx = xOf(d.start);
            const bw = Math.max(2, xOf(d.end) - bx);
            const laneY = marginTop + gi * laneH;
            const by = laneY + (laneH - BAR_H) / 2;

            // 顏色 token(分類色;亮度自適應文字)
            const fillHex = categoricalColor(gi);
            ctx.fillStyle = fillHex;

            // 圓角矩形
            const radius = Math.min(4, bw / 2, BAR_H / 2);
            ctx.beginPath();
            ctx.roundRect
                ? ctx.roundRect(bx, by, bw, BAR_H, radius)
                : (() => {
                    ctx.moveTo(bx + radius, by);
                    ctx.lineTo(bx + bw - radius, by);
                    ctx.arcTo(bx + bw, by, bx + bw, by + BAR_H, radius);
                    ctx.lineTo(bx + bw, by + BAR_H - radius);
                    ctx.arcTo(bx + bw, by + BAR_H, bx, by + BAR_H, radius);
                    ctx.lineTo(bx + radius, by + BAR_H);
                    ctx.arcTo(bx, by + BAR_H, bx, by, radius);
                    ctx.lineTo(bx, by + radius);
                    ctx.arcTo(bx, by, bx + bw, by, radius);
                    ctx.closePath();
                })();
            ctx.globalAlpha = 0.85;
            ctx.fill();
            ctx.globalAlpha = 1;

            // 文字(寬度夠才標;亮度自適應)
            if (bw > 18) {
                const [rr, gg, bb] = hexToRgbArr(fillHex);
                ctx.fillStyle = (0.299 * rr + 0.587 * gg + 0.114 * bb) > 150 ? '#000000aa' : '#ffffffdd';
                ctx.textAlign = 'left';
                ctx.fillText(this.ellipsis(ctx, d.label || '', bw - 6), bx + 4, by + BAR_H / 2);
            }

            // hit region
            this.addRegion({
                shape: 'rect', x: bx, y: by, w: bw, h: BAR_H,
                data: d,
                clickable: true
            });
        }

        // ── 4. 時間軸(底部 5 刻度)──
        const axisY = h - marginBottom / 2;
        const timeSteps = 5;
        ctx.fillStyle = tok['--cl-text-secondary'];
        ctx.font = this.font(10);
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';

        for (let i = 0; i <= timeSteps; i++) {
            const t = minTime + (timeRange / timeSteps) * i;
            const x = xOf(t);
            const label = new Date(t).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
            ctx.fillText(label, x, axisY);
        }
    }

    getTooltip(d) {
        const duration = ((d.end - d.start) / 1000).toFixed(1) + 's';
        return [
            { label: '事件', value: d.label },
            { label: 'Group', value: d.group },
            { label: 'Start', value: new Date(d.start).toLocaleTimeString() },
            { label: 'End', value: new Date(d.end).toLocaleTimeString() },
            { label: 'Duration', value: duration },
        ];
    }

    /** 點擊回呼:由 CanvasChart 基底觸發 onPointClick;同時支援 ModalPanel 詳情面板。 */
    _handleClick(d) {
        const duration = ((d.end - d.start) / 1000).toFixed(1) + 's';

        // 詳情卡 DOM(CSP 相容:cssText + textContent)
        const root = document.createElement('div');
        root.style.cssText = 'min-width:220px;';

        const title = document.createElement('h3');
        title.textContent = d.label || '';
        title.style.cssText = 'margin:0 0 8px 0; border-bottom:1px solid var(--cl-border-light);' +
            ' padding-bottom:8px; font-size:var(--cl-font-size-lg); color:var(--cl-text-dark);';
        root.appendChild(title);

        const body = document.createElement('div');
        body.style.cssText = 'font-size:var(--cl-font-size-sm); color:var(--cl-text-heading); line-height:1.6;';
        const makeRow = (label, val) => {
            const row = document.createElement('div');
            const strong = document.createElement('strong');
            strong.textContent = label + ': ';
            row.appendChild(strong);
            row.appendChild(document.createTextNode(val));
            return row;
        };
        body.appendChild(makeRow('Start', new Date(d.start).toLocaleTimeString()));
        body.appendChild(makeRow('End', new Date(d.end).toLocaleTimeString()));
        body.appendChild(makeRow('Duration', duration));
        if (d.details) {
            const det = document.createElement('div');
            det.style.cssText = 'margin-top:8px; padding:6px; background:var(--cl-bg-secondary);' +
                ' border-radius:var(--cl-radius-sm);';
            det.textContent = d.details;
            body.appendChild(det);
        }
        root.appendChild(body);

        const actions = document.createElement('div');
        actions.style.cssText = 'margin-top:8px; text-align:right;';
        const btn = document.createElement('button');
        btn.textContent = 'Analyze';
        btn.style.cssText = 'padding:2px 8px; font-size:var(--cl-font-size-xs); cursor:pointer;';
        btn.addEventListener('click', () => {
            ModalPanel.alert({ message: 'Drilldown: ' + (d.id ?? d.label) });
        });
        actions.appendChild(btn);
        root.appendChild(actions);

        // ModalPanel.alert 只認 message 字串(content 會被 setContent 覆蓋)——
        // 取回 modal 後把 message <p> 換成 DOM 詳情(保留 OK 鈕列)。
        const modal = ModalPanel.alert({ message: '' });
        const msgEl = modal.content && modal.content.querySelector('p');
        if (msgEl) msgEl.replaceWith(root);
        else modal.setContent(root);
    }
}

export default TimelineChart;
