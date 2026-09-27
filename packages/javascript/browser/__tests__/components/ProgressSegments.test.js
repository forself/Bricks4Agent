/**
 * Progress — segments / showLegend / setSegments（phase 2b 補強）
 *
 * 「單一數值模式不變」以 fixtures/Progress.legacy-dom.json 為準：那份 golden 由修改前的
 * Progress.js（commit 4f1caa3）以下方同一組輸入產生，勿以修改後的元件重新產生。
 */
import { describe, it, expect, afterEach, vi } from 'vitest';
import { Progress } from '../../ui_components/common/Progress/Progress.js';
import { notifyThemeChange, FALLBACK_PAINT } from '../../ui_components/utils/theme-bus.js';
import Locale from '../../ui_components/i18n/index.js';
import legacyDom from './fixtures/Progress.legacy-dom.json';

const root = document.documentElement;
const created = [];

/** 建立並掛到 body；afterEach 統一 destroy */
function create(options) {
    const progress = new Progress(options);
    created.push(progress);
    return progress.mount(document.body);
}

/** 與產生 golden 時相同：建立 → 可選操作 → 取 outerHTML → destroy */
function htmlOf(options, after) {
    const progress = new Progress(options);
    if (after) after(progress);
    const html = progress.element.outerHTML;
    progress.destroy();
    return html;
}

const segmentsOf = (progress) => [...progress.element.querySelectorAll('.cl-progress-bar-segment')];
const trackOf = (progress) => progress.element.querySelector('.cl-progress-bar-track');
const legendItemsOf = (progress) => [...progress.element.querySelectorAll('.cl-progress-legend-item')];
const nextFrame = () => new Promise((resolve) => requestAnimationFrame(() => resolve()));

/** 記錄 canvas 的 arc / stroke（strokeStyle、lineWidth、lineCap） */
function recordCanvas(progress) {
    const log = [];
    const ctx = progress._ctx;
    const arc = ctx.arc;
    const stroke = ctx.stroke;
    ctx.arc = function (...args) {
        log.push(['arc', ...args.map((v) => +v.toFixed(4))]);
        return arc.apply(this, args);
    };
    ctx.stroke = function () {
        log.push(['stroke', this.strokeStyle, this.lineWidth, this.lineCap]);
        return stroke.call(this);
    };
    return log;
}

function setTokens(tokens) {
    for (const [name, value] of Object.entries(tokens)) root.style.setProperty(name, value);
}

afterEach(async () => {
    created.splice(0).forEach((progress) => progress.destroy());
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
    root.removeAttribute('style');
    document.body.innerHTML = '';
    // 重設 <html style> 會觸發 theme-bus 的 rAF 廣播；先讓它跑完，避免打到下一個測試的實例
    await nextFrame();
    await nextFrame();
});

const RICH = { value: 45, max: 200, variant: 'warning', size: 'large', showText: true };
const TWO = [
    { value: 50, variant: 'success', label: 'Done' },
    { value: 30, variant: 'warning', label: 'Doing' },
];

describe('Progress — 單一數值模式不變（對照修改前 golden）', () => {
    it('bar / circle 各組態的 DOM 與修改前逐字相同', () => {
        expect(htmlOf({ value: 60 })).toBe(legacyDom.barDefault);
        expect(htmlOf(RICH)).toBe(legacyDom.barRich);
        expect(htmlOf({ indeterminate: true, showText: true })).toBe(legacyDom.barInd);
        expect(htmlOf({ type: 'circle', value: 75, showText: true, size: 'large' })).toBe(legacyDom.circle);
        expect(htmlOf({ type: 'circle', value: 20, showText: true, size: 'small', variant: 'success' })).toBe(legacyDom.circleSmall);
        expect(htmlOf({ type: 'circle', indeterminate: true, showText: true })).toBe(legacyDom.circleInd);
    });

    it('setValue / setVariant 後的 DOM 與修改前相同', () => {
        expect(htmlOf(RICH, (p) => {
            p.setValue(30);
            p.setVariant('danger');
            p.setValue(-5);
            p.setValue('abc');
            p.setValue(999);
        })).toBe(legacyDom.barSetters);
        expect(htmlOf({ value: 10, variant: 'nope', size: 'huge' }, (p) => p.setVariant('info'))).toBe(legacyDom.barBadVariant);
        expect(htmlOf({ type: 'circle', value: 10, showText: true }, (p) => {
            p.setValue(55);
            p.setVariant('warning');
        })).toBe(legacyDom.circleSetters);
    });

    it('circle 的繪圖指令序列與修改前相同', () => {
        setTokens({ '--cl-primary': 'rgb(10, 20, 30)', '--cl-bg-subtle': 'rgb(40, 50, 60)' });
        const progress = new Progress({ type: 'circle', value: 75 });
        created.push(progress);
        document.body.appendChild(progress.element);

        const log = [];
        const ctx = progress._ctx;
        for (const method of ['setTransform', 'clearRect', 'beginPath', 'arc', 'stroke']) {
            const original = ctx[method];
            ctx[method] = function (...args) {
                log.push([
                    method,
                    ...args.map((v) => (typeof v === 'number' ? +v.toFixed(4) : v)),
                    method === 'stroke' ? `${this.strokeStyle}|${this.lineWidth}|${this.lineCap}` : '',
                ]);
                return original.apply(this, args);
            };
        }
        progress.setValue(40);
        expect(log).toEqual(legacyDom.circleDrawLog);
    });

    it('明確傳入 segments: null，或只開 showLegend 而沒有分段時，與舊版相同', () => {
        expect(htmlOf({ ...RICH, segments: null, showLegend: false })).toBe(legacyDom.barRich);
        expect(htmlOf({ ...RICH, showLegend: true })).toBe(legacyDom.barRich);
        expect(htmlOf({ type: 'circle', value: 75, showText: true, size: 'large', showLegend: true })).toBe(legacyDom.circle);
    });

    it('非陣列的 segments 視為未提供', () => {
        expect(htmlOf({ value: 60, segments: { value: 10 } })).toBe(legacyDom.barDefault);
        expect(htmlOf({ value: 60 }, (p) => p.setSegments('nope'))).toBe(legacyDom.barDefault);
    });
});

describe('Progress — bar 分段', () => {
    it('依序堆疊各段（寬度為 max 的比例），軌道改為 role="img" 並以 aria-label 摘要', () => {
        const progress = create({ max: 200, segments: TWO });
        const track = trackOf(progress);
        const [done, doing] = segmentsOf(progress);

        expect(done.style.left).toBe('0%');
        expect(done.style.width).toBe('25%');
        expect(done.style.background).toBe('var(--cl-success)');
        expect(doing.style.left).toBe('25%');
        expect(doing.style.width).toBe('15%');
        expect(doing.style.background).toBe('var(--cl-warning)');
        expect(done.dataset.segmentIndex).toBe('0');

        expect(track.getAttribute('role')).toBe('img');
        expect(track.getAttribute('aria-label')).toBe('Done：25%，Doing：15%');
        for (const name of ['aria-valuemin', 'aria-valuemax', 'aria-valuenow']) {
            expect(track.hasAttribute(name)).toBe(false);
        }
        expect(track.classList.contains('cl-progress-bar-track--segmented')).toBe(true);
        expect(progress.element.querySelector('.cl-progress-bar-fill')).toBeNull();
    });

    it('累計超過 max 的部分被截掉；負值、非數字與非物件項目視為 0', () => {
        const progress = create({ segments: [{ value: 70 }, { value: 50 }, { value: 10 }, { value: -5 }, { value: 'x' }, null] });
        const widths = segmentsOf(progress).map((el) => el.style.width);
        const lefts = segmentsOf(progress).map((el) => el.style.left);
        expect(widths).toEqual(['70%', '30%', '0%', '0%', '0%', '0%']);
        expect(lefts).toEqual(['0%', '70%', '100%', '100%', '100%', '100%']);
        expect(progress._segments.reduce((sum, s) => sum + s.value, 0)).toBe(100);

        progress.setSegments([{ value: 33.3 }, { value: 33.3 }, { value: 33.4 }, { value: 5 }]);
        const total = progress._segments.reduce((sum, s) => sum + s.value, 0);
        expect(total).toBeLessThanOrEqual(100);
        expect(segmentsOf(progress)[3].style.width).toBe('0%');

        const zeroMax = create({ max: 0, segments: [{ value: 10 }] });
        expect(segmentsOf(zeroMax)[0].style.width).toBe('0%');
    });

    it('未指定色彩時依序輪替；color 只接受單一 var(--*) token，其餘值忽略', () => {
        const progress = create({ segments: Array.from({ length: 7 }, () => ({ value: 10 })) });
        expect(segmentsOf(progress).map((el) => el.style.background)).toEqual([
            'var(--cl-primary)', 'var(--cl-success)', 'var(--cl-warning)', 'var(--cl-danger)',
            'var(--cl-info)', 'var(--cl-grey)', 'var(--cl-primary)',
        ]);

        progress.setSegments([
            { value: 10, color: 'var(--cl-purple)' },
            { value: 10, color: '  var( --cl-cyan )  ' },
            { value: 10, variant: 'neutral', color: 'red' },
            { value: 10, variant: 'info', color: 'var(--cl-x); background: url(https://example.invalid/y)' },
            { value: 10, variant: 'magenta' },
        ]);
        expect(segmentsOf(progress).map((el) => el.style.background)).toEqual([
            'var(--cl-purple)', 'var(--cl-cyan)', 'var(--cl-grey)', 'var(--cl-info)', 'var(--cl-info)',
        ]);
        expect(progress.element.outerHTML).not.toContain('example.invalid');
    });

    it('showText 顯示總百分比；單一模式本來沒有文字時由分段模式建立、回到單一模式時移除', () => {
        const progress = create({ indeterminate: true, showText: true });
        expect(progress.element.querySelector('.cl-progress-text')).toBeNull();

        progress.setSegments(TWO);
        const text = progress.element.querySelector('.cl-progress-text');
        expect(text.textContent).toBe('80%');
        expect(text.previousElementSibling).toBe(trackOf(progress));

        progress.setSegments(null);
        expect(progress.element.querySelector('.cl-progress-text')).toBeNull();
        expect(progress.element.outerHTML).toBe(legacyDom.barInd);
    });

    it('showLegend：每段一列（裝飾色塊＋標籤＋「數值（百分比）」），缺標籤時用 Locale 預設名', () => {
        const progress = create({
            showText: true,
            showLegend: true,
            segments: [{ value: 30, variant: 'success', label: 'Done' }, { value: 12.5, color: 'var(--cl-info)' }],
        });
        const legend = progress.element.querySelector('ul.cl-progress-legend');
        expect(legend).toBeTruthy();
        expect(legend.previousElementSibling.className).toBe('cl-progress-text');
        expect(progress.element.style.flexWrap).toBe('wrap');

        const [first, second] = legendItemsOf(progress);
        const swatch = first.querySelector('.cl-progress-legend-swatch');
        expect(swatch.getAttribute('aria-hidden')).toBe('true');
        expect(swatch.style.background).toBe('var(--cl-success)');
        expect(first.querySelector('.cl-progress-legend-label').textContent).toBe('Done');
        expect(first.querySelector('.cl-progress-legend-value').textContent).toBe('30（30%）');
        expect(second.querySelector('.cl-progress-legend-label').textContent).toBe('區段 2');
        expect(second.querySelector('.cl-progress-legend-value').textContent).toBe('12.5（13%）');
        expect(second.dataset.segmentIndex).toBe('1');
    });

    it('標籤以 textContent 呈現，不解析 HTML', () => {
        const label = '<img src=x onerror="alert(1)">';
        const progress = create({ showLegend: true, segments: [{ value: 40, label }] });
        expect(progress.element.querySelector('img')).toBeNull();
        expect(progress.element.querySelector('.cl-progress-legend-label').textContent).toBe(label);
        expect(trackOf(progress).getAttribute('aria-label')).toBe(`${label}：40%`);
    });

    it('空陣列：仍為分段模式，軌道沒有區段、aria-label 為「無資料」', () => {
        const progress = create({ value: 50, segments: [] });
        expect(segmentsOf(progress)).toHaveLength(0);
        expect(trackOf(progress).getAttribute('role')).toBe('img');
        expect(trackOf(progress).getAttribute('aria-label')).toBe('無資料');
    });
});

describe('Progress — setSegments', () => {
    it('單一模式 → 分段 → 更新 → 回到單一模式：區段元素重用，最後 DOM 與修改前逐字相同', () => {
        const progress = create({ ...RICH, showLegend: true });
        expect(progress.setSegments([{ value: 20 }, { value: 30 }, { value: 40 }])).toBe(progress);
        const [first] = segmentsOf(progress);
        expect(segmentsOf(progress)).toHaveLength(3);
        expect(legendItemsOf(progress)).toHaveLength(3);
        expect(progress.element.querySelector('.cl-progress-text').textContent).toBe('45%');

        progress.setSegments([{ value: 100, variant: 'danger', label: 'Late' }, { value: 60 }]);
        expect(segmentsOf(progress)).toHaveLength(2);
        expect(segmentsOf(progress)[0]).toBe(first);            // 重用 → 寬度變化可套用 transition
        expect(first.style.width).toBe('50%');
        expect(first.style.background).toBe('var(--cl-danger)');
        expect(legendItemsOf(progress)).toHaveLength(2);

        progress.setSegments(null);
        expect(progress.element.outerHTML).toBe(legacyDom.barRich);
    });

    it('分段模式中 setValue 只記住數值，回到單一模式才反映在寬度、ARIA 與文字', () => {
        const progress = create({ value: 10, showText: true });
        progress.setSegments([{ value: 50 }]);
        progress.setValue(80);

        const track = trackOf(progress);
        expect(track.hasAttribute('aria-valuenow')).toBe(false);
        expect(progress.element.querySelector('.cl-progress-text').textContent).toBe('50%');

        progress.setSegments(null);
        expect(track.getAttribute('aria-valuenow')).toBe('80');
        expect(progress.element.querySelector('.cl-progress-bar-fill').style.width).toBe('80%');
        expect(progress.element.querySelector('.cl-progress-text').textContent).toBe('80%');
    });

    it('bar indeterminate：進入分段時取消 WAAPI 動畫，離開後重新啟動', () => {
        const animations = [];
        const animate = vi.fn(() => {
            const handle = { cancel: vi.fn() };
            animations.push(handle);
            return handle;
        });
        Object.defineProperty(Element.prototype, 'animate', { configurable: true, writable: true, value: animate });
        try {
            const progress = create({ indeterminate: true, showText: true });
            expect(animate).toHaveBeenCalledTimes(1);

            progress.setSegments(TWO);
            expect(animations[0].cancel).toHaveBeenCalledTimes(1);

            progress.setSegments(null);
            expect(animate).toHaveBeenCalledTimes(2);
            expect(progress.element.outerHTML).toBe(legacyDom.barInd);
        } finally {
            delete Element.prototype.animate;
        }
    });

    it('建構時給 segments 等同建構後呼叫 setSegments', () => {
        const a = create({ max: 200, showText: true, showLegend: true, segments: TWO });
        const b = create({ max: 200, showText: true, showLegend: true });
        b.setSegments(TWO);
        expect(a.element.outerHTML).toBe(b.element.outerHTML);
        expect(a.options.segments).toBe(TWO);
    });
});

describe('Progress — circle 分段', () => {
    const CIRCLE = { type: 'circle', value: 75, showText: true, size: 'large' };

    it('canvas 帶 role="img" 與摘要 aria-label；wrapper 移除 progressbar 語意', () => {
        const progress = create({ ...CIRCLE, segments: TWO });
        const wrapper = progress.element;
        const canvas = wrapper.querySelector('canvas');

        expect(canvas.getAttribute('role')).toBe('img');
        expect(canvas.getAttribute('aria-label')).toBe('Done：50%，Doing：30%');
        for (const name of ['role', 'aria-valuemin', 'aria-valuemax', 'aria-valuenow']) {
            expect(wrapper.hasAttribute(name)).toBe(false);
        }
        expect(wrapper.querySelector('.cl-progress-circle-text').textContent).toBe('80%');
    });

    it('從 12 點鐘方向依序畫多段弧：顏色由 theme token 解析、端點為 butt、0% 區段略過', () => {
        setTokens({
            '--cl-bg-subtle': 'rgb(3, 3, 3)',
            '--cl-success': 'rgb(1, 1, 1)',
            '--cl-purple': 'rgb(4, 4, 4)',
        });
        const progress = create({
            type: 'circle',
            size: 'medium',
            segments: [
                { value: 25, variant: 'success' },
                { value: 0, variant: 'danger' },
                { value: 50, color: 'var(--cl-purple)' },
                { value: 10, color: 'var(--cl-undefined-token)' },
            ],
        });
        const log = recordCanvas(progress);
        progress.setSegments(progress.options.segments);

        const q = Math.PI / 2;
        expect(log).toEqual([
            ['arc', 40, 40, 37, 0, +(Math.PI * 2).toFixed(4)],
            ['stroke', 'rgb(3, 3, 3)', 6, 'butt'],
            ['arc', 40, 40, 37, +(-q).toFixed(4), 0],
            ['stroke', 'rgb(1, 1, 1)', 6, 'butt'],
            ['arc', 40, 40, 37, 0, +(Math.PI).toFixed(4)],
            ['stroke', 'rgb(4, 4, 4)', 6, 'butt'],
            ['arc', 40, 40, 37, +(Math.PI).toFixed(4), +(Math.PI + Math.PI * 0.2).toFixed(4)],
            ['stroke', FALLBACK_PAINT, 6, 'butt'],
        ]);
    });

    it('掛載後重繪一次，使 canvas 顏色取自實際文件的 token', () => {
        setTokens({ '--cl-success': 'rgb(1, 1, 1)', '--cl-bg-subtle': 'rgb(3, 3, 3)' });
        const progress = new Progress({ type: 'circle', segments: [{ value: 50, variant: 'success' }] });
        created.push(progress);
        const log = recordCanvas(progress);
        progress.mount(document.body);
        expect(log.filter((entry) => entry[0] === 'stroke').map((entry) => entry[1])).toEqual(['rgb(3, 3, 3)', 'rgb(1, 1, 1)']);
    });

    it('主題變更時以新的 token 值重繪分段', async () => {
        setTokens({ '--cl-success': 'rgb(1, 1, 1)' });
        const progress = create({ type: 'circle', segments: [{ value: 50, variant: 'success' }] });
        const log = recordCanvas(progress);

        setTokens({ '--cl-success': 'rgb(9, 9, 9)' });
        notifyThemeChange();
        await nextFrame();
        await nextFrame();

        const strokes = log.filter((entry) => entry[0] === 'stroke').map((entry) => entry[1]);
        expect(strokes).toContain('rgb(9, 9, 9)');
    });

    it('showLegend：canvas 與中央文字包進 ring、圖例在下方；回到單一模式後 DOM 與修改前逐字相同', () => {
        const progress = create({ ...CIRCLE, showLegend: true });
        progress.setSegments(TWO);

        const wrapper = progress.element;
        const ring = wrapper.querySelector('.cl-progress-circle-ring');
        expect(ring.parentElement).toBe(wrapper);
        expect([...ring.children].map((el) => el.tagName)).toEqual(['CANVAS', 'SPAN']);
        expect(ring.nextElementSibling.className).toBe('cl-progress-legend');
        expect(wrapper.style.flexDirection).toBe('column');
        expect(wrapper.style.width).toBe('auto');

        progress.setSegments(null);
        expect(wrapper.querySelector('.cl-progress-circle-ring')).toBeNull();
        expect(wrapper.outerHTML).toBe(legacyDom.circle);
    });

    it('indeterminate circle：進入分段時停止 rAF 迴圈，離開後恢復且 DOM 還原', async () => {
        const frames = async (n) => { for (let i = 0; i < n; i += 1) await nextFrame(); };
        const progress = create({ type: 'circle', indeterminate: true, showText: true });
        await frames(3);
        expect(progress._indAngle).toBeGreaterThan(0);          // 迴圈在轉

        progress.setSegments(TWO);
        expect(progress._indRaf).toBe(0);
        const frozen = progress._indAngle;
        await frames(3);
        expect(progress._indAngle).toBe(frozen);                // 分段模式：迴圈已停

        progress.setSegments(null);
        expect(progress._indRaf).not.toBe(0);
        await frames(3);
        expect(progress._indAngle).not.toBe(frozen);            // 回到單一模式：迴圈恢復
        expect(progress.element.outerHTML).toBe(legacyDom.circleInd);
    });
});

describe('Progress — Locale 與可及性', () => {
    it('aria-label、預設標籤與圖例數值隨語系切換（下一次 setSegments 生效）', () => {
        const progress = create({ max: 200, showLegend: true, segments: [...TWO, { value: 20 }] });
        expect(trackOf(progress).getAttribute('aria-label')).toBe('Done：25%，Doing：15%，區段 3：10%');

        Locale.setLang('en');
        progress.setSegments([...TWO, { value: 20 }]);
        expect(trackOf(progress).getAttribute('aria-label')).toBe('Done: 25%, Doing: 15%, Segment 3: 10%');
        expect(legendItemsOf(progress)[0].querySelector('.cl-progress-legend-value').textContent).toBe('50 (25%)');

        progress.setSegments([]);
        expect(trackOf(progress).getAttribute('aria-label')).toBe('No data');
    });

    it('圖例數值依語系格式化（最多兩位小數）', () => {
        const progress = create({ max: 10000, showLegend: true, segments: [{ value: 1234.5678, label: 'Orders' }] });
        expect(progress.element.querySelector('.cl-progress-legend-value').textContent).toBe('1,234.57（12%）');
    });

    it('分段模式沒有可聚焦元素（純顯示元件，不在 Tab 順序中）', () => {
        const bar = create({ showLegend: true, showText: true, segments: TWO });
        const circle = create({ type: 'circle', showLegend: true, showText: true, segments: TWO });
        for (const progress of [bar, circle]) {
            expect(progress.element.querySelectorAll('[tabindex], button, a, input').length).toBe(0);
        }
    });
});

describe('Progress — mount / destroy', () => {
    it('mount 為 render 的別名並回傳 this', () => {
        const progress = new Progress({ value: 30 });
        created.push(progress);
        expect(progress.mount(document.body)).toBe(progress);
        expect(document.body.contains(progress.element)).toBe(true);
    });

    it('destroy 移除 DOM（含圖例）、解除主題訂閱、停止 rAF，可重複呼叫；之後呼叫方法不拋錯', async () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');

        const bar = new Progress({ showLegend: true, showText: true, segments: TWO }).mount(document.body);
        const circle = new Progress({ type: 'circle', indeterminate: true, showLegend: true }).mount(document.body);
        circle.setSegments(TWO);
        circle.setSegments(null); // 重新啟動 rAF 迴圈
        const draw = vi.spyOn(circle, '_drawCircle');

        bar.destroy();
        circle.destroy();
        expect(document.body.children).toHaveLength(0);

        notifyThemeChange();
        await nextFrame();
        await nextFrame();
        expect(draw).not.toHaveBeenCalled();

        expect(docAdd).not.toHaveBeenCalled();
        expect(winAdd).not.toHaveBeenCalled();
        expect(() => {
            for (const progress of [bar, circle]) {
                progress.destroy();
                progress.setValue(20);
                progress.setVariant('success');
                progress.setSegments(TWO);
                progress.setSegments(null);
                progress.render(document.body);
                progress.mount(document.body);
            }
        }).not.toThrow();
        expect(document.body.children).toHaveLength(0);
    });
});
