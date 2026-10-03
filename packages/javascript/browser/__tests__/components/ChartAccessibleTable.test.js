import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { CanvasChart } from '../../ui_components/viz/CanvasChart.js';
import { BarChart } from '../../ui_components/viz/BarChart.js';
import { LineChart } from '../../ui_components/viz/LineChart.js';
import { PieChart } from '../../ui_components/viz/PieChart.js';
import { RoseChart } from '../../ui_components/viz/RoseChart.js';
import { HeatmapChart } from '../../ui_components/viz/HeatmapChart.js';
import { ScatterChart } from '../../ui_components/viz/ScatterChart.js';
import { SankeyChart } from '../../ui_components/viz/SankeyChart.js';
import { RelationChart } from '../../ui_components/viz/RelationChart.js';
import { ClusterGraph } from '../../ui_components/viz/ClusterGraph.js';
import { OrgChart } from '../../ui_components/viz/OrgChart.js';
import { HierarchyChart } from '../../ui_components/viz/HierarchyChart.js';
import { SunburstChart } from '../../ui_components/viz/SunburstChart.js';
import { FlameChart } from '../../ui_components/viz/FlameChart.js';
import { TimelineChart } from '../../ui_components/viz/TimelineChart.js';
import { Sparkline } from '../../ui_components/viz/Sparkline.js';
import Locale from '../../ui_components/i18n/index.js';
import { resetUid, nextUid } from '../../ui_components/utils/uid.js';

/** 等待資料表的微任務重建完成(macrotask 之前所有微任務都已執行)。 */
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
const wait = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

let container;
let charts;

function make(Cls, options = {}) {
    const chart = new Cls({ container, ...options });
    charts.push(chart);
    return chart;
}

const wrapOf = (chart) => chart.element.querySelector('.cl-canvas-chart__a11y');
const tableOf = (chart) => chart.element.querySelector('table');
const captionOf = (chart) => tableOf(chart).querySelector('caption').textContent;
const headersOf = (chart) => [...tableOf(chart).querySelectorAll('thead th')].map((th) => th.textContent);
const rowsOf = (chart) => [...tableOf(chart).querySelectorAll('tbody tr')]
    .map((tr) => [...tr.children].map((cell) => cell.textContent));

const T0 = Date.UTC(2026, 0, 5, 1, 2, 3);
const T1 = Date.UTC(2026, 0, 5, 4, 5, 6);

const barData = {
    labels: ['一月', '二月'],
    series: [
        { name: '營收', data: [1234.5, 2000] },
        { name: '', data: [1, null] }
    ]
};

/** 每種圖表的最小可用資料(預設關閉測試用)。 */
const FIXTURES = [
    ['BarChart', BarChart, { data: barData }],
    ['LineChart', LineChart, { data: barData }],
    ['PieChart', PieChart, { data: [{ name: '甲', value: 3 }] }],
    ['RoseChart', RoseChart, { data: barData }],
    ['HeatmapChart', HeatmapChart, { xLabels: ['A'], yLabels: ['R'], matrix: [[1]] }],
    ['ScatterChart', ScatterChart, { points: [{ x: 1, y: 2 }] }],
    ['SankeyChart', SankeyChart, { data: { nodes: [{ name: 'a' }, { name: 'b' }], links: [{ source: 0, target: 1, value: 1 }] } }],
    ['RelationChart', RelationChart, { nodes: [{ id: 'a' }, { id: 'b' }], links: [{ source: 'a', target: 'b' }] }],
    ['ClusterGraph', ClusterGraph, { nodes: [{ id: 'p1', group: 'g' }], groups: [{ id: 'g', parent: null }] }],
    ['OrgChart', OrgChart, { root: { id: 'r', title: '主管', label: '部門' } }],
    ['SunburstChart', SunburstChart, { data: { name: 'root', children: [{ name: 'a', value: 1 }] } }],
    ['FlameChart', FlameChart, { data: { name: 'main', value: 10 } }],
    ['TimelineChart', TimelineChart, { data: [{ id: 1, group: 'A', start: T0, end: T1, label: 'task' }] }],
    ['Sparkline', Sparkline, { data: [1, 2, 3] }]
];

beforeEach(() => {
    Locale.setLang('zh-TW');
    resetUid();
    charts = [];
    container = document.createElement('div');
    document.body.appendChild(container);
});

afterEach(() => {
    for (const chart of charts) chart.destroy();
    container.remove();
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
});

describe('accessibleTable 預設關閉:預設 DOM 與 ARIA 不變', () => {
    for (const [name, Cls, options] of FIXTURES) {
        it(`${name} 預設不建立資料表、不加 aria-describedby`, async () => {
            const chart = make(Cls, options);
            await flush();
            expect(chart.options.accessibleTable).toBe(false);
            expect(chart.options.accessibleTableMaxRows).toBe(500);
            expect(chart.element.querySelector('table')).toBeNull();
            expect(chart.element.querySelector('.cl-canvas-chart__a11y')).toBeNull();
            expect(chart.canvas.getAttribute('role')).toBe('img');
            expect(chart.canvas.getAttribute('aria-label')).toBeTruthy();
            expect(chart.canvas.hasAttribute('aria-describedby')).toBe(false);
            expect([...chart.element.children].map((el) => el.className)).toEqual(['cl-canvas-chart__body']);
            // getDataTable 為純模型,關閉時仍可呼叫
            expect(Array.isArray(chart.getDataTable().columns)).toBe(true);
        });
    }

    it('關閉時不註冊 window 監聽、不消耗 uid 序號', async () => {
        const addSpy = vi.spyOn(window, 'addEventListener');
        const chart = make(BarChart, { data: barData, title: '營收' });
        chart.setData({ labels: ['x'], series: [{ name: 's', data: [1] }] });
        chart.update({ unit: 'kg' });
        await flush();
        expect(addSpy.mock.calls.some(([type]) => type === 'locale-changed')).toBe(false);
        expect(nextUid('probe')).toBe('probe-1');
        expect(chart.canvas.getAttribute('aria-label')).toBe('營收');
    });

    it('accessibleTable 非 true / "visible" 的值一律視為關閉', async () => {
        for (const value of ['true', 'hidden', 1, 'yes']) {
            const chart = make(BarChart, { data: barData, accessibleTable: value });
            await flush();
            expect(tableOf(chart)).toBeNull();
        }
    });
});

describe('accessibleTable: true(視覺隱藏,供輔助科技)', () => {
    it('在根元素內、canvas 之後建立真實 <table>,以 CSSOM 視覺隱藏(非 display:none)', async () => {
        const chart = make(BarChart, { data: barData, title: '月營收', accessibleTable: true });
        await flush();
        const wrap = wrapOf(chart);
        const table = tableOf(chart);
        expect(table).not.toBeNull();
        expect(chart.element.contains(table)).toBe(true);
        expect(chart.element.lastElementChild).toBe(wrap);
        expect(chart.canvas.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
        expect(wrap.dataset.mode).toBe('hidden');
        expect(wrap.style.position).toBe('absolute');
        expect(wrap.style.width).toBe('1px');
        expect(wrap.style.height).toBe('1px');
        expect(wrap.style.overflow).toBe('hidden');
        expect(wrap.style.clip).toContain('rect');
        expect(wrap.style.clipPath).toBe('inset(50%)');
        expect(wrap.style.display).not.toBe('none');
        // 隱藏時不可成為 Tab 停駐點
        expect(wrap.hasAttribute('tabindex')).toBe(false);
        expect(wrap.hasAttribute('role')).toBe(false);
    });

    it('canvas 保留既有 role / aria-label,aria-describedby 指向資料表 id', async () => {
        const chart = make(BarChart, { data: barData, title: '月營收', accessibleTable: true });
        await flush();
        const table = tableOf(chart);
        expect(table.id).toBe('cl-chart-table-1');
        expect(chart.canvas.getAttribute('role')).toBe('img');
        expect(chart.canvas.getAttribute('aria-label')).toBe('月營收');
        expect(chart.canvas.getAttribute('aria-describedby')).toBe('cl-chart-table-1');
    });

    it('caption 取 title → ariaLabel → Locale 預設;表頭 th[scope=col],首欄為列標題 th[scope=row]', async () => {
        const titled = make(BarChart, { data: barData, title: '月營收', ariaLabel: '替代說明', accessibleTable: true });
        const labelled = make(BarChart, { data: barData, ariaLabel: '替代說明', accessibleTable: true });
        const plain = make(BarChart, { data: barData, accessibleTable: true });
        await flush();
        expect(captionOf(titled)).toBe('月營收');
        expect(captionOf(labelled)).toBe('替代說明');
        expect(captionOf(plain)).toBe('圖表資料');
        const table = tableOf(titled);
        expect([...table.querySelectorAll('thead th')].every((th) => th.getAttribute('scope') === 'col')).toBe(true);
        const firstCells = [...table.querySelectorAll('tbody tr')].map((tr) => tr.firstElementChild);
        expect(firstCells.every((cell) => cell.tagName === 'TH' && cell.getAttribute('scope') === 'row')).toBe(true);
        expect(table.querySelector('caption').id).toBe(`${table.id}-caption`);
    });

    it('既有 aria-describedby 保留並附加;canvas 缺 aria-label 時補上 caption,關閉後還原', async () => {
        const chart = make(BarChart, { data: barData, title: '月營收' });
        chart.canvas.setAttribute('aria-describedby', 'external-note');
        chart.canvas.removeAttribute('aria-label');
        chart.update({ accessibleTable: true });
        await flush();
        const id = tableOf(chart).id;
        expect(chart.canvas.getAttribute('aria-describedby')).toBe(`external-note ${id}`);
        expect(chart.canvas.getAttribute('aria-label')).toBe('月營收');
        chart.update({ accessibleTable: false });
        expect(chart.canvas.getAttribute('aria-describedby')).toBe('external-note');
        expect(chart.canvas.hasAttribute('aria-label')).toBe(false);
        expect(chart.canvas.getAttribute('role')).toBe('img');
    });
});

describe("accessibleTable: 'visible'(圖下可見資料表)", () => {
    it('以 token 樣式呈現可見表格(深色模式由 token 切換),外層為可聚焦的捲動區', async () => {
        const chart = make(BarChart, { data: barData, title: '月營收', accessibleTable: 'visible' });
        await flush();
        const wrap = wrapOf(chart);
        const table = tableOf(chart);
        expect(wrap.dataset.mode).toBe('visible');
        expect(wrap.style.position).toBe('static');
        expect(wrap.style.clip).toBe('');
        expect(wrap.style.overflowX).toBe('auto');
        expect(wrap.getAttribute('role')).toBe('region');
        expect(wrap.getAttribute('tabindex')).toBe('0');
        expect(wrap.getAttribute('aria-labelledby')).toBe(table.querySelector('caption').id);
        expect(table.style.color).toBe('var(--cl-text)');
        expect(table.style.background).toContain('var(--cl-bg)');
        expect(table.style.fontSize).toBe('var(--cl-font-size-sm)');
        const th = table.querySelector('thead th');
        expect(th.style.background).toContain('var(--cl-bg-secondary)');
        const numericCell = table.querySelector('tbody td');
        expect(numericCell.style.textAlign).toBe('end');
        expect(numericCell.style.color).toBe('var(--cl-text)');
        // 所有顏色皆為 token(無字面色碼)
        for (const el of table.querySelectorAll('*')) {
            expect(el.style.cssText).not.toMatch(/#[0-9a-f]{3,8}\b|rgb/i);
        }
    });

    it('鍵盤:可見表格外層可 Tab 聚焦(tabindex=0),隱藏模式不可聚焦', async () => {
        const chart = make(BarChart, { data: barData, accessibleTable: 'visible' });
        await flush();
        const wrap = wrapOf(chart);
        wrap.focus();
        expect(document.activeElement).toBe(wrap);
        expect(wrap.tabIndex).toBe(0);
        chart.update({ accessibleTable: true });
        await flush();
        expect(wrapOf(chart).hasAttribute('tabindex')).toBe(false);
        expect(wrapOf(chart).tabIndex).toBe(-1);
    });

    it('已排版時固定繪圖區高度、根元素改 auto(資料表不擠壓圖),切回隱藏/關閉即還原', async () => {
        const chart = make(BarChart, { data: barData, accessibleTable: 'visible' });
        await flush();
        const body = chart._canvasWrap;
        expect(chart.element.style.height).toBe('260px');   // jsdom 未排版:不固定
        Object.defineProperty(body, 'clientHeight', { configurable: true, get: () => 180 });
        chart._renderNow();
        expect(chart.element.style.height).toBe('auto');
        expect(body.style.height).toBe('180px');
        expect(body.style.flex).toBe('0 0 auto');
        chart.update({ accessibleTable: true });
        expect(chart.element.style.height).toBe('260px');
        expect(body.style.height).toBe('');
        expect(body.style.flex).toBe('1 1 auto');
        chart.update({ accessibleTable: 'visible' });
        chart._renderNow();
        expect(chart.element.style.height).toBe('auto');
        chart.update({ accessibleTable: false });
        expect(chart.element.style.height).toBe('260px');
        expect(body.style.height).toBe('');
    });

    it('隱藏 ↔ 可見切換只換樣式,不重建 DOM 節點', async () => {
        const chart = make(BarChart, { data: barData, accessibleTable: true });
        await flush();
        const table = tableOf(chart);
        chart.update({ accessibleTable: 'visible' });
        await flush();
        expect(tableOf(chart)).toBe(table);
        expect(wrapOf(chart).style.position).toBe('static');
        expect(tableOf(chart).querySelector('tbody td').style.textAlign).toBe('end');
        chart.update({ accessibleTable: true });
        await flush();
        expect(tableOf(chart)).toBe(table);
        expect(wrapOf(chart).style.position).toBe('absolute');
        expect(tableOf(chart).querySelector('tbody td').style.cssText).toBe('');
    });
});

describe('各圖表的資料表內容', () => {
    it('BarChart:類別 + 每系列一欄(系列名稱為表頭,缺名用 Locale),數字以 Intl 格式化並附單位', async () => {
        const chart = make(BarChart, { data: barData, unit: 'kg', accessibleTable: true });
        await flush();
        expect(headersOf(chart)).toEqual(['類別', '營收', '系列 2']);
        expect(rowsOf(chart)).toEqual([
            ['一月', '1,234.5 kg', '1 kg'],
            ['二月', '2,000 kg', '']
        ]);
    });

    it('LineChart:null 斷點呈現為空白格', async () => {
        const chart = make(LineChart, {
            data: { labels: ['Q1', 'Q2', 'Q3'], series: [{ name: '訂單', data: [10, null, 30] }] },
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['類別', '訂單']);
        expect(rowsOf(chart)).toEqual([['Q1', '10'], ['Q2', ''], ['Q3', '30']]);
    });

    it('PieChart:名稱 / 數值 / 占比,只列圖上有畫的扇區(value > 0)', async () => {
        const chart = make(PieChart, {
            data: [{ name: '甲', value: 60 }, { name: '乙', value: 40 }, { name: '丙', value: 0 }],
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['名稱', '數值', '占比']);
        expect(rowsOf(chart)).toEqual([['甲', '60', '60%'], ['乙', '40', '40%']]);
    });

    it('RoseChart:與 Bar 同形(類別 + 系列欄)', async () => {
        const chart = make(RoseChart, { data: { labels: ['N', 'S'], series: [{ name: '量', data: [3, 4] }] }, accessibleTable: true });
        await flush();
        expect(headersOf(chart)).toEqual(['類別', '量']);
        expect(rowsOf(chart)).toEqual([['N', '3'], ['S', '4']]);
    });

    it('HeatmapChart:列 / 欄 / 數值(長格式,略過 null 格)', async () => {
        const chart = make(HeatmapChart, {
            xLabels: ['A', 'B'], yLabels: ['R1', 'R2'], matrix: [[3, 5], [2, null]], unit: 'h',
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['列', '欄', '數值']);
        expect(rowsOf(chart)).toEqual([['R1', 'A', '3 h'], ['R1', 'B', '5 h'], ['R2', 'A', '2 h']]);
    });

    it('ScatterChart:表頭沿用軸名稱與單位,選配欄(名稱/大小/分類)只在有值時出現', async () => {
        const chart = make(ScatterChart, {
            points: [
                { x: 8, y: 1200, s: 3, c: '甲組', label: '任務 A' },
                { x: 4.5, y: 300, c: '乙組' },
                { x: 'bad', y: 1 }
            ],
            xLabel: '工時', xUnit: 'h', yLabel: '產出', sizeLabel: '人數',
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['名稱', '工時', '產出', '人數', '類別']);
        expect(rowsOf(chart)).toEqual([
            ['任務 A', '8 h', '1,200', '3', '甲組'],
            ['', '4.5 h', '300', '', '乙組']
        ]);
        const bare = make(ScatterChart, { points: [{ x: 1, y: 2 }], accessibleTable: true });
        await flush();
        expect(headersOf(bare)).toEqual(['X 值', 'Y 值']);
    });

    it('SankeyChart:來源 / 目標 / 數值,每條流一列(節點名稱)', async () => {
        const chart = make(SankeyChart, {
            data: {
                nodes: [{ name: '訪客' }, { name: '註冊' }, { name: '下單' }],
                links: [{ source: 0, target: 1, value: 10 }, { source: 1, target: 2, value: 2500 }]
            },
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['來源', '目標', '數值']);
        expect(rowsOf(chart)).toEqual([['訪客', '註冊', '10'], ['註冊', '下單', '2,500']]);
    });

    it('RelationChart:起點 / 終點 / 權重(節點 label,缺則 id;略過懸空邊與自環)', async () => {
        const chart = make(RelationChart, {
            nodes: [{ id: 'a', label: 'Alice', group: 'g1' }, { id: 'b', label: 'Bob' }, { id: 'c' }],
            links: [
                { source: 'a', target: 'b', value: 3 },
                { source: 'b', target: 'c' },
                { source: 'a', target: 'missing', value: 1 },
                { source: 'a', target: 'a', value: 9 }
            ],
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['起點', '終點', '權重']);
        expect(rowsOf(chart)).toEqual([['Alice', 'Bob', '3'], ['Bob', 'c', '']]);
    });

    it('ClusterGraph:人員層級邊,同對(不分方向)合併為權重', async () => {
        const chart = make(ClusterGraph, {
            nodes: [{ id: 'p1', label: '甲', group: 'g1' }, { id: 'p2', label: '乙', group: 'g1' }, { id: 'p3', label: '丙', group: 'g2' }],
            groups: [{ id: 'g1', parent: null, label: 'G1' }, { id: 'g2', parent: null, label: 'G2' }],
            edges: [
                { source: 'p1', target: 'p2' }, { source: 'p2', target: 'p1' },
                { source: 'p1', target: 'p3' }, { source: 'p1', target: 'p1' }, { source: 'p1', target: 'nobody' }
            ],
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['起點', '終點', '權重']);
        expect(rowsOf(chart)).toEqual([['甲', '乙', '2'], ['甲', '丙', '1']]);
    });

    it('SunburstChart:路徑 / 數值(非葉=子加總、葉缺值=1),前序含根,不寫回原資料', async () => {
        const data = {
            name: '總計',
            children: [
                { name: 'A', children: [{ name: 'A1', value: 3 }, { name: 'A2' }] },
                { name: 'B', value: 5 }
            ]
        };
        const chart = make(SunburstChart, { data, accessibleTable: true });
        const model = chart.getDataTable();          // 同步呼叫:尚未繪製
        expect(Object.prototype.hasOwnProperty.call(data, '_value')).toBe(false);
        expect(model.rows.map((r) => [r.path, r.value])).toEqual([
            ['總計', 9], ['總計 / A', 4], ['總計 / A / A1', 3], ['總計 / A / A2', 1], ['總計 / B', 5]
        ]);
        await flush();
        expect(headersOf(chart)).toEqual(['路徑', '數值']);
        expect(rowsOf(chart)[2]).toEqual(['總計 / A / A1', '3']);
    });

    it('FlameChart:路徑 / 數值(節點原值)', async () => {
        const chart = make(FlameChart, {
            data: { name: 'main', value: 100, children: [{ name: 'init', value: 20, children: [{ name: 'load', value: 5 }] }] },
            accessibleTable: true
        });
        await flush();
        expect(headersOf(chart)).toEqual(['路徑', '數值']);
        expect(rowsOf(chart)).toEqual([['main', '100'], ['main / init', '20'], ['main / init / load', '5']]);
    });

    it('OrgChart / HierarchyChart:路徑(title)/ 說明(label),收合子樹不影響資料表', async () => {
        const root = {
            id: 'r', title: '王大明', label: '總經理',
            children: [{ id: 'c1', title: '李小華', label: '技術長', children: [{ id: 'c2', label: '工程師' }] }]
        };
        const chart = make(OrgChart, { root, accessibleTable: true });
        await flush();
        expect(headersOf(chart)).toEqual(['路徑', '說明']);
        const expected = [['王大明', '總經理'], ['王大明 / 李小華', '技術長'], ['王大明 / 李小華 / 工程師', '工程師']];
        expect(rowsOf(chart)).toEqual(expected);
        const spy = vi.spyOn(chart, 'getDataTable');
        chart._handleToggleClick('c1');               // 收合(僅檢視狀態)
        await flush();
        expect(spy).not.toHaveBeenCalled();
        expect(rowsOf(chart)).toEqual(expected);
        const hier = make(HierarchyChart, { root, accessibleTable: true });
        await flush();
        expect(rowsOf(hier)).toEqual(expected);
    });

    it('TimelineChart:名稱 / 群組 / 開始 / 結束(Intl.DateTimeFormat);無 group 時不出現群組欄', async () => {
        const chart = make(TimelineChart, {
            data: [
                { id: 1, group: '伺服器 A', start: T0, end: T1, label: '部署' },
                { id: 2, group: '伺服器 B', start: T1, end: T1 + 60000, label: '備份' }
            ],
            accessibleTable: true
        });
        await flush();
        const dtf = new Intl.DateTimeFormat('zh-TW', { dateStyle: 'medium', timeStyle: 'medium' });
        expect(headersOf(chart)).toEqual(['名稱', '群組', '開始', '結束']);
        expect(rowsOf(chart)).toEqual([
            ['部署', '伺服器 A', dtf.format(new Date(T0)), dtf.format(new Date(T1))],
            ['備份', '伺服器 B', dtf.format(new Date(T1)), dtf.format(new Date(T1 + 60000))]
        ]);
        const noGroup = make(TimelineChart, { data: [{ id: 1, start: T0, end: T1, label: '部署' }], accessibleTable: true });
        await flush();
        expect(headersOf(noGroup)).toEqual(['名稱', '開始', '結束']);
    });

    it('Sparkline:序號 / 數值;標題取呼叫端 ariaLabel,未給用 Locale 預設(canvas 標籤不變)', async () => {
        const chart = make(Sparkline, { data: [5, 3, 1200], accessibleTable: true });
        const named = make(Sparkline, { data: [1], ariaLabel: '每日流量', accessibleTable: true });
        await flush();
        expect(captionOf(chart)).toBe('圖表資料');
        expect(chart.canvas.getAttribute('aria-label')).toBe('sparkline');
        expect(headersOf(chart)).toEqual(['序號', '數值']);
        expect(rowsOf(chart)).toEqual([['1', '5'], ['2', '3'], ['3', '1,200']]);
        expect(captionOf(named)).toBe('每日流量');
    });

    it('無資料時顯示單一「無資料」列(跨全部欄)', async () => {
        const chart = make(BarChart, { accessibleTable: true });
        await flush();
        const cell = tableOf(chart).querySelector('tbody td');
        expect(cell.textContent).toBe('無資料');
        expect(cell.getAttribute('colspan')).toBe('1');
    });

    it('所有文字走 textContent:資料中的 HTML 不會被解析', async () => {
        const evil = '<img src=x onerror="alert(1)">';
        const chart = make(BarChart, {
            data: { labels: [evil], series: [{ name: evil, data: [1] }] },
            title: evil,
            accessibleTable: true
        });
        await flush();
        const table = tableOf(chart);
        expect(table.querySelector('img')).toBeNull();
        expect(captionOf(chart)).toBe(evil);
        expect(headersOf(chart)[1]).toBe(evil);
        expect(rowsOf(chart)[0][0]).toBe(evil);
    });
});

describe('基底 getDataTable:涵蓋 options.data 常見形狀', () => {
    class Probe extends CanvasChart {}

    it('{ labels, series } / 數字陣列 / [{ name, value }] / { nodes, links } / 階層', () => {
        const series = new Probe({ data: barData });
        expect(series.getDataTable().columns.map((c) => c.label)).toEqual(['類別', '營收', '系列 2']);
        const numbers = new Probe({ data: [4, 5] });
        expect(numbers.getDataTable().rows).toEqual([{ index: 1, value: 4 }, { index: 2, value: 5 }]);
        const items = new Probe({ data: [{ label: '甲', value: 1 }] });
        expect(items.getDataTable().rows).toEqual([{ name: '甲', value: 1 }]);
        const graph = new Probe({ data: { nodes: [{ id: 'x', name: '起' }, { name: '迄' }], links: [{ source: 'x', target: 1, value: 2 }] } });
        expect(graph.getDataTable().rows).toEqual([{ source: '起', target: '迄', value: 2 }]);
        const tree = new Probe({ data: { name: 'r', value: 3, children: [{ name: 'c', value: 1 }] } });
        expect(tree.getDataTable().rows).toEqual([{ path: 'r', value: 3 }, { path: 'r / c', value: 1 }]);
        const none = new Probe({});
        expect(none.getDataTable()).toBeNull();
        for (const chart of [series, numbers, items, graph, tree, none]) chart.destroy();
    });

    it('階層走訪可防環狀參照', () => {
        const root = { name: 'r', children: [] };
        root.children.push({ name: 'c', children: [root] });
        const chart = new Probe({ data: root });
        expect(chart.getDataTable().rows.map((r) => r.path)).toEqual(['r', 'r / c']);
        chart.destroy();
    });

    it('getDataTable 擲錯時記錄 console.error,資料表顯示「無資料」而不中斷', async () => {
        class Broken extends CanvasChart {
            getDataTable() { throw new Error('boom'); }
        }
        const errorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
        const chart = make(Broken, { accessibleTable: true });
        await flush();
        expect(errorSpy).toHaveBeenCalled();
        expect(tableOf(chart).querySelector('tbody td').textContent).toBe('無資料');
    });
});

describe('資料變更同步:每次資料變更重建一次,不隨動畫幀/重繪', () => {
    it('setData / update 觸發重建;同一輪多次變更合併為一次;單純 render() 與動畫幀不重建', async () => {
        const chart = make(BarChart, { data: barData, accessibleTable: true });
        await flush();
        const spy = vi.spyOn(chart, 'getDataTable');
        chart.render();
        chart.render();
        chart._renderNow();
        chart._renderNow();
        await wait(40);                               // 涵蓋數個 rAF 繪製
        expect(spy).not.toHaveBeenCalled();

        chart.setData({ labels: ['甲'], series: [{ name: 'A', data: [1] }] });
        chart.setData({ labels: ['乙'], series: [{ name: 'B', data: [2] }] });
        await flush();
        expect(spy).toHaveBeenCalledTimes(1);
        expect(rowsOf(chart)).toEqual([['乙', '2']]);

        chart.update({ data: { labels: ['丙'], series: [{ name: 'C', data: [3] }] } });
        await flush();
        expect(spy).toHaveBeenCalledTimes(2);
        expect(rowsOf(chart)).toEqual([['丙', '3']]);
    });

    it('直接替換 options 資料後呼叫 render() 也會同步(參照比對)', async () => {
        const chart = make(HeatmapChart, { xLabels: ['A'], yLabels: ['R'], matrix: [[1]], accessibleTable: true });
        await flush();
        chart.options.matrix = [[42]];
        chart.render();
        await flush();
        expect(rowsOf(chart)).toEqual([['R', 'A', '42']]);
    });

    it('原地修改資料後以 update() 刷新;render() 單獨不重建', async () => {
        const data = { labels: ['a'], series: [{ name: 's', data: [1] }] };
        const chart = make(BarChart, { data, accessibleTable: true });
        await flush();
        data.series[0].data[0] = 7;
        chart.render();
        await flush();
        expect(rowsOf(chart)).toEqual([['a', '1']]);
        chart.update();
        await flush();
        expect(rowsOf(chart)).toEqual([['a', '7']]);
    });

    it('資料存在實例欄位的圖表(Flame/Timeline/Relation/Cluster)更新後同步', async () => {
        const flame = make(FlameChart, { data: { name: 'a', value: 1 }, accessibleTable: true });
        const timeline = make(TimelineChart, { data: [], accessibleTable: true });
        const relation = make(RelationChart, { nodes: [], links: [], accessibleTable: true });
        const cluster = make(ClusterGraph, { nodes: [], groups: [], edges: [], accessibleTable: true });
        await flush();
        flame.setData({ name: 'b', value: 2 });
        timeline.setData([{ id: 1, start: T0, end: T1, label: 'x' }]);
        relation.update({ nodes: [{ id: 1, label: 'n1' }, { id: 2, label: 'n2' }], links: [{ source: 1, target: 2, value: 5 }] });
        cluster.setData({ nodes: [{ id: 'a', group: 'g' }, { id: 'b', group: 'g' }], groups: [{ id: 'g', parent: null }], edges: [{ source: 'a', target: 'b' }] });
        await flush();
        expect(rowsOf(flame)).toEqual([['b', '2']]);
        expect(rowsOf(timeline)[0][0]).toBe('x');
        expect(rowsOf(relation)).toEqual([['n1', 'n2', '5']]);
        expect(rowsOf(cluster)).toEqual([['a', 'b', '1']]);
    });

    it('力導向模擬每幀重繪不會重建資料表', async () => {
        const chart = make(RelationChart, {
            nodes: [{ id: 'a' }, { id: 'b' }], links: [{ source: 'a', target: 'b' }], accessibleTable: true
        });
        await flush();
        const spy = vi.spyOn(chart, 'getDataTable');
        await wait(80);                               // RelationChart 以 rAF 迴圈逐幀 _renderNow()
        expect(spy).not.toHaveBeenCalled();
    });

    it('離視口釋放背景儲存時(略過繪製)資料表仍同步', async () => {
        const chart = make(BarChart, { data: barData, accessibleTable: true });
        await flush();
        chart._offscreen = true;
        chart._releaseBackingStore();
        chart.setData({ labels: ['z'], series: [{ name: 's', data: [9] }] });
        await flush();
        expect(rowsOf(chart)).toEqual([['z', '9']]);
    });
});

describe('列數上限 accessibleTableMaxRows', () => {
    const tenLabels = { labels: Array.from({ length: 10 }, (_, i) => `L${i + 1}`), series: [{ name: 'v', data: Array.from({ length: 10 }, (_, i) => i) }] };

    it('超過上限只列前 N 列,末列註明省略筆數(跨全部欄)', async () => {
        const chart = make(BarChart, { data: tenLabels, accessibleTable: true, accessibleTableMaxRows: 3 });
        await flush();
        const rows = [...tableOf(chart).querySelectorAll('tbody tr')];
        expect(rows).toHaveLength(4);
        const note = rows[3];
        expect(note.className).toBe('cl-canvas-chart__table-note');
        expect(note.textContent).toBe('另有 7 筆資料未列出');
        expect(note.firstElementChild.getAttribute('colspan')).toBe('2');
    });

    it('預設上限 500', async () => {
        const chart = make(Sparkline, { data: Array.from({ length: 620 }, (_, i) => i), accessibleTable: true });
        await flush();
        const rows = tableOf(chart).querySelectorAll('tbody tr');
        expect(rows).toHaveLength(501);
        expect(rows[500].textContent).toBe('另有 120 筆資料未列出');
    });

    it('0 只留註記列;Infinity 不截斷;非法值退回預設', async () => {
        const zero = make(BarChart, { data: tenLabels, accessibleTable: true, accessibleTableMaxRows: 0 });
        const all = make(BarChart, { data: tenLabels, accessibleTable: true, accessibleTableMaxRows: Infinity });
        const bad = make(BarChart, { data: tenLabels, accessibleTable: true, accessibleTableMaxRows: -5 });
        await flush();
        expect(rowsOf(zero)).toEqual([['另有 10 筆資料未列出']]);
        expect(rowsOf(all)).toHaveLength(10);
        expect(rowsOf(bad)).toHaveLength(10);
    });
});

describe('destroy 與關閉清理', () => {
    it('destroy 移除 DOM、ARIA 關聯與 window 監聽;可重複呼叫、之後呼叫方法不擲錯', async () => {
        const addSpy = vi.spyOn(window, 'addEventListener');
        const removeSpy = vi.spyOn(window, 'removeEventListener');
        const docAdd = vi.spyOn(document, 'addEventListener');
        const chart = make(BarChart, { data: barData, accessibleTable: 'visible' });
        await flush();
        const added = addSpy.mock.calls.filter(([type]) => type === 'locale-changed');
        expect(added).toHaveLength(1);
        const canvas = chart.canvas;
        chart.destroy();
        expect(container.querySelector('.cl-canvas-chart')).toBeNull();
        expect(container.querySelector('table')).toBeNull();
        expect(canvas.hasAttribute('aria-describedby')).toBe(false);
        expect(removeSpy.mock.calls.some(([type, fn]) => type === 'locale-changed' && fn === added[0][1])).toBe(true);
        expect(docAdd).not.toHaveBeenCalled();
        expect(() => chart.destroy()).not.toThrow();
        expect(() => chart.update({ accessibleTable: true })).not.toThrow();
        expect(() => chart.setData(barData)).not.toThrow();
        expect(() => chart.render()).not.toThrow();
        expect(chart._a11y).toBeNull();
    });

    it('destroy 後已排程的重建不執行', async () => {
        const chart = make(BarChart, { data: barData, accessibleTable: true });
        const spy = vi.spyOn(chart, 'getDataTable');
        chart.destroy();
        await flush();
        expect(spy).not.toHaveBeenCalled();
    });

    it('以 update({ accessibleTable: false }) 或直接改選項後 render() 關閉:移除資料表與監聽', async () => {
        const removeSpy = vi.spyOn(window, 'removeEventListener');
        const chart = make(BarChart, { data: barData, accessibleTable: true });
        await flush();
        chart.update({ accessibleTable: false });
        expect(tableOf(chart)).toBeNull();
        expect(chart.canvas.hasAttribute('aria-describedby')).toBe(false);
        expect(chart.canvas.getAttribute('role')).toBe('img');
        expect(removeSpy.mock.calls.some(([type]) => type === 'locale-changed')).toBe(true);
        await flush();
        expect([...chart.element.children].map((el) => el.className)).toEqual(['cl-canvas-chart__body']);

        chart.update({ accessibleTable: true });
        await flush();
        expect(tableOf(chart)).not.toBeNull();
        chart.options.accessibleTable = false;
        chart.render();
        expect(tableOf(chart)).toBeNull();
    });
});

describe('Locale 切換', () => {
    it('切換語系後重建 caption / 表頭 / 註記列 / 數字格式', async () => {
        const chart = make(BarChart, {
            data: { labels: ['a', 'b', 'c'], series: [{ data: [1500, 2, 3] }] },
            accessibleTable: true,
            accessibleTableMaxRows: 1
        });
        await flush();
        expect(captionOf(chart)).toBe('圖表資料');
        expect(headersOf(chart)).toEqual(['類別', '系列 1']);
        expect(rowsOf(chart)).toEqual([['a', '1,500'], ['另有 2 筆資料未列出']]);

        Locale.setLang('en');
        await flush();
        expect(captionOf(chart)).toBe('Chart data');
        expect(headersOf(chart)).toEqual(['Category', 'Series 1']);
        expect(rowsOf(chart)).toEqual([['a', '1,500'], ['Rows not shown: 2']]);
    });

    it('其他圖表表頭同樣走 Locale(en)', async () => {
        Locale.setLang('en');
        const pie = make(PieChart, { data: [{ name: 'x', value: 1 }], accessibleTable: true });
        const sankey = make(SankeyChart, { data: { nodes: [{ name: 'a' }, { name: 'b' }], links: [{ source: 0, target: 1, value: 1 }] }, accessibleTable: true });
        const empty = make(LineChart, { accessibleTable: true });
        await flush();
        expect(headersOf(pie)).toEqual(['Name', 'Value', 'Percentage']);
        expect(headersOf(sankey)).toEqual(['Source', 'Target', 'Value']);
        expect(tableOf(empty).querySelector('tbody td').textContent).toBe('No data');
    });
});
