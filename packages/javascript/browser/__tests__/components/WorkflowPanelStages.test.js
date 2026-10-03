/**
 * WorkflowPanel — stages / replaceStages / fieldMap（phase 2b 補強）
 *
 * 「預設不變」以 fixtures/WorkflowPanel.legacy-dom.json 為準：那份 golden 由修改前的
 * WorkflowPanel.js（commit 4f1caa3）以下方同一組輸入產生，勿以修改後的元件重新產生。
 */
import { describe, it, expect, afterEach, vi } from 'vitest';
import WorkflowPanelDefault, { WorkflowPanel } from '../../ui_components/layout/WorkflowPanel/WorkflowPanel.js';
import Locale from '../../ui_components/i18n/index.js';
import legacyDom from './fixtures/WorkflowPanel.legacy-dom.json';

// ── 產生 golden 時使用的同一組輸入（勿修改） ─────────────────────────────
const flowA = [
    { StageName: 'Edit', DateTime: '2026-03-01T10:30:00', UnitName: 'Team A', UserName: 'Amy' },
    { StageName: 'Create', DateTime: '2026-03-01T09:00:00', UnitName: 'Team A', UserName: 'Amy' },
    { StageName: 'Audit', DateTime: '2026-03-01T15:10:00', UnitName: 'Desk B', UserName: 'Chris' },
    { StageName: 'Submit', DateTime: '2026-03-01T13:20:00', UnitName: 'Team A', UserName: 'Amy' },
];
const flowB = [
    { StageName: 'Create', DateTime: '2026-03-02T08:50:00', UnitName: 'Office', UserName: '' },
    { StageName: 'Custom', DateTime: '2026-03-02T09:40:00', UnitName: '', UserName: 'Lena' },
    { StageName: 'Rej', DateTime: '2026-03-02T11:10:00', UnitName: 'Desk', UserName: 'Mark' },
];
const flowLong = ['Create', 'Edit', 'Submit', 'Audit', 'Rej', 'Replenish', 'Approved']
    .map((s, i) => ({ StageName: s, DateTime: `2026-04-0${i + 1}T08:0${i}:00`, UnitName: `U${i}`, UserName: `P${i}` }));
const nextApproved = { StageName: 'Approved', NextUnit: 'Ops Desk' };

// ── helpers ─────────────────────────────────────────────────────────────
function htmlOf(options, after) {
    const panel = new WorkflowPanel(options);
    if (after) after(panel);
    const html = panel.element.outerHTML;
    panel.destroy();
    return html;
}

const nodesOf = (panel) => [...panel.element.querySelectorAll('.workflow-node')];
const circleOf = (node) => node.children[0];
const labelOf = (node) => node.children[1];
const nodeByLabel = (panel, text) => nodesOf(panel).find((node) => labelOf(node).textContent === text);

/** 以新鍵名改寫舊格式資料，用於驗證 fieldMap 與舊欄位名輸出一致 */
const toMapped = (items) => items.map((item) => ({
    step: item.StageName, at: item.DateTime, team: item.UnitName, owner: item.UserName,
}));
const MAPPED_FIELDS = { stageName: 'step', dateTime: 'at', unitName: 'team', userName: 'owner', nextUnit: 'assignee' };

afterEach(() => {
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
    document.body.innerHTML = '';
});

describe('WorkflowPanel — 預設輸出不變（對照修改前 golden）', () => {
    it('各代表組態的 DOM 與修改前逐字相同', () => {
        expect(htmlOf({ data: flowA })).toBe(legacyDom.basic);
        expect(htmlOf({ data: flowB, nextStage: nextApproved, onNodeClick: () => {} })).toBe(legacyDom.nextClickUnknown);
        expect(htmlOf({ data: flowLong, itemsPerRow: 3, nextStage: { StageName: 'Close' } })).toBe(legacyDom.multirow);
        expect(htmlOf({ data: flowB, nextStage: nextApproved }, (p) => {
            p._effectiveItemsPerRow = 1;
            p._rerender();
        })).toBe(legacyDom.vertical);
        expect(htmlOf({ data: flowA }, (p) => {
            p.setData(flowB);
            p.setNextStage({ StageName: 'Score', NextUnit: 'Desk C' });
        })).toBe(legacyDom.setters);
        expect(htmlOf({})).toBe(legacyDom.empty);
    });

    it('明確傳入 stages: null、replaceStages: false、fieldMap: null 與省略時相同', () => {
        const opts = { data: flowB, nextStage: nextApproved, onNodeClick: () => {} };
        expect(htmlOf({ ...opts, stages: null, replaceStages: false, fieldMap: null })).toBe(legacyDom.nextClickUnknown);
    });

    it('fieldMap 等於預設欄位名、stages 為空物件時輸出仍與舊版相同', () => {
        expect(htmlOf({ data: flowA, fieldMap: { ...WorkflowPanel.DEFAULT_FIELD_MAP } })).toBe(legacyDom.basic);
        expect(htmlOf({ data: flowB, nextStage: nextApproved, onNodeClick: () => {}, stages: {} }))
            .toBe(legacyDom.nextClickUnknown);
    });

    it('itemsPerRow 的 3–7 夾限不變', () => {
        expect(htmlOf({ data: flowA, itemsPerRow: 10 })).toContain(`max-width: ${legacyDom.clampHighMaxWidth}px`);
        expect(htmlOf({ data: flowA, itemsPerRow: 1 })).toContain(`max-width: ${legacyDom.clampLowMaxWidth}px`);
        expect(new WorkflowPanel({ itemsPerRow: 10 }).options.itemsPerRow).toBe(7);
        expect(new WorkflowPanel({ itemsPerRow: 1 }).options.itemsPerRow).toBe(3);
    });

    it('英文語系下唯一差異是原本寫死的「(待處理)」改走 Locale', () => {
        Locale.setLang('en');
        const html = htmlOf({ data: flowB, nextStage: { StageName: 'Approved' }, onNodeClick: () => {} });
        expect(html).toBe(legacyDom.en_nextClickUnknown.replace('(待處理)', '(To do)'));
    });

    it('onNodeClick 收到原始資料物件與 WorkflowPanel.STAGES 內的同一個階段物件', () => {
        const onNodeClick = vi.fn();
        const panel = new WorkflowPanel({ data: flowB, nextStage: nextApproved, onNodeClick });
        const [first, custom, , next] = nodesOf(panel);

        first.click();
        expect(onNodeClick).toHaveBeenLastCalledWith(flowB[0], WorkflowPanel.STAGES.Create);
        expect(onNodeClick.mock.calls[0][0]).toBe(flowB[0]);
        expect(onNodeClick.mock.calls[0][1]).toBe(WorkflowPanel.STAGES.Create);

        custom.click();
        expect(onNodeClick).toHaveBeenLastCalledWith(flowB[1], { name: 'Custom', icon: '📋', color: 'var(--cl-grey)' });

        next.click(); // 下一階段節點不可點
        expect(onNodeClick).toHaveBeenCalledTimes(2);
        panel.destroy();
    });

    it('元件模組可在 ESM 測試環境直接載入（CommonJS 相容段不再拋錯）', () => {
        expect(WorkflowPanelDefault).toBe(WorkflowPanel);
        expect(Object.keys(WorkflowPanel.STAGES)).toHaveLength(13);
    });
});

describe('WorkflowPanel — stages / replaceStages', () => {
    it('逐階段合併：只覆寫給定欄位，其餘沿用內建值；未覆寫的階段不受影響', () => {
        const panel = new WorkflowPanel({
            data: flowA,
            stages: { Create: { name: 'Draft' }, Edit: { icon: '🖊️', color: 'var(--cl-info)' } },
        });

        const draft = nodeByLabel(panel, 'Draft');
        expect(draft).toBeTruthy();
        expect(circleOf(draft).textContent).toBe('📝');                       // 內建 icon
        expect(circleOf(draft).style.border).toContain('var(--cl-success)'); // 內建 color

        const edit = nodeByLabel(panel, '編輯');                              // 內建 name
        expect(circleOf(edit).textContent).toBe('🖊️');
        expect(circleOf(edit).style.border).toContain('var(--cl-info)');

        expect(nodeByLabel(panel, '陳報')).toBeTruthy();                       // 未覆寫的內建階段
        panel.destroy();
    });

    it('新增的自訂階段：缺 icon/color 用回退外觀，缺 name 顯示階段鍵，未列出的未知階段維持回退', () => {
        const panel = new WorkflowPanel({
            data: [
                { StageName: 'Queued', DateTime: '2026-05-01T09:00:00' },
                { StageName: 'Review', DateTime: '2026-05-01T10:00:00' },
                { StageName: 'Mystery', DateTime: '2026-05-01T11:00:00' },
            ],
            stages: { Queued: { icon: '⏳' }, Review: { name: 'In review', icon: '🔍', color: 'var(--cl-warning)' } },
        });
        const [queued, review, mystery] = nodesOf(panel);

        expect(labelOf(queued).textContent).toBe('Queued');
        expect(circleOf(queued).textContent).toBe('⏳');
        expect(circleOf(queued).style.border).toContain('var(--cl-grey)');

        expect(labelOf(review).textContent).toBe('In review');
        expect(circleOf(review).style.border).toContain('var(--cl-warning)');

        expect(labelOf(mystery).textContent).toBe('Mystery');
        expect(circleOf(mystery).textContent).toBe('📋');
        expect(circleOf(mystery).style.border).toContain('var(--cl-grey)');
        panel.destroy();
    });

    it('replaceStages: true 只使用 stages，內建階段改走回退外觀', () => {
        const panel = new WorkflowPanel({
            data: flowA,
            stages: { Submit: { name: 'Sent', icon: '📨', color: 'var(--cl-primary)' } },
            replaceStages: true,
        });
        const labels = nodesOf(panel).map((node) => labelOf(node).textContent);
        expect(labels).toEqual(['Create', 'Edit', 'Sent', 'Audit']);

        const create = nodesOf(panel)[0];
        expect(circleOf(create).textContent).toBe('📋');
        expect(circleOf(create).style.border).toContain('var(--cl-grey)');
        expect(circleOf(nodeByLabel(panel, 'Sent')).textContent).toBe('📨');
        panel.destroy();
    });

    it('replaceStages: true 且未給 stages 時全部走回退外觀；非 true 的值不啟用取代', () => {
        const panel = new WorkflowPanel({ data: flowA, replaceStages: true });
        for (const node of nodesOf(panel)) expect(circleOf(node).textContent).toBe('📋');
        panel.destroy();

        expect(htmlOf({ data: flowA, replaceStages: 'yes' })).toBe(legacyDom.basic);
    });

    it('從不修改 WorkflowPanel.STAGES，且實例之間互不影響', () => {
        const before = JSON.stringify(WorkflowPanel.STAGES);
        const createRef = WorkflowPanel.STAGES.Create;

        const custom = new WorkflowPanel({
            data: flowA,
            stages: { Create: { name: 'Draft', icon: '✏️', color: 'var(--cl-info)' }, Extra: { name: 'Extra' } },
        });
        const replaced = new WorkflowPanel({ data: flowA, stages: { Create: { name: 'Only' } }, replaceStages: true });

        expect(JSON.stringify(WorkflowPanel.STAGES)).toBe(before);
        expect(WorkflowPanel.STAGES.Create).toBe(createRef);
        expect(WorkflowPanel.STAGES.Extra).toBeUndefined();

        // 其他實例仍輸出舊版 DOM
        expect(htmlOf({ data: flowA })).toBe(legacyDom.basic);
        expect(nodeByLabel(custom, 'Draft')).toBeTruthy();
        expect(nodeByLabel(replaced, 'Only')).toBeTruthy();
        custom.destroy();
        replaced.destroy();
    });

    it('階段名稱與圖示以 textContent 呈現，不解析 HTML', () => {
        const name = '<img src=x onerror="alert(1)">';
        const panel = new WorkflowPanel({
            data: [{ StageName: 'Review', DateTime: '2026-05-01T10:00:00' }],
            stages: { Review: { name, icon: '<b>!</b>' } },
        });
        const [node] = nodesOf(panel);
        expect(labelOf(node).textContent).toBe(name);
        expect(circleOf(node).textContent).toBe('<b>!</b>');
        expect(panel.element.querySelector('img, b')).toBeNull();
        panel.destroy();
    });

    it('可能跳出宣告的色彩字串被忽略並警告；一般 CSS 色彩照用', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const unsafe = [
            'var(--cl-info); background-image: url(https://example.invalid/x.png)',
            'url(x.png)',
            'var(--cl-info',
            'expression(alert(1))',
            'var(--cl-info) !important',
            'var(--cl-info) /* x */',
            '"var(--cl-info)"',
        ];
        for (const color of unsafe) {
            const panel = new WorkflowPanel({
                data: [{ StageName: 'Create', DateTime: '2026-05-01T09:00:00' }],
                stages: { Create: { color } },
            });
            const border = circleOf(nodesOf(panel)[0]).style.border;
            expect(border).toContain('var(--cl-success)'); // 回到內建色
            expect(panel.element.outerHTML).not.toContain('example.invalid');
            panel.destroy();
        }
        expect(warn).toHaveBeenCalledTimes(unsafe.length);
        expect(warn.mock.calls[0][0]).toContain('stages.Create.color');

        const safe = ['var(--cl-info)', '  var(--cl-warning)  ', 'color-mix(in srgb, var(--cl-primary) 40%, transparent)'];
        for (const color of safe) {
            const panel = new WorkflowPanel({
                data: [{ StageName: 'Create', DateTime: '2026-05-01T09:00:00' }],
                stages: { Create: { color } },
            });
            const node = nodesOf(panel)[0];
            expect(circleOf(node).style.border).toContain(color.trim());
            expect(labelOf(node).style.color).toBe(color.trim()); // 目前節點的名稱用階段色
            panel.destroy();
        }
        expect(warn).toHaveBeenCalledTimes(unsafe.length);
    });

    it('onNodeClick 收到原始資料與合併後的階段物件（同一實例內重繪時為同一物件）', () => {
        const onNodeClick = vi.fn();
        const data = [{ StageName: 'Create', DateTime: '2026-05-01T09:00:00' }];
        const panel = new WorkflowPanel({ data, onNodeClick, stages: { Create: { name: 'Draft' } } });

        nodesOf(panel)[0].click();
        const [item, stage] = onNodeClick.mock.calls[0];
        expect(item).toBe(data[0]);
        expect(stage).toEqual({ name: 'Draft', icon: '📝', color: 'var(--cl-success)' });

        panel.setData(data);
        nodesOf(panel)[0].click();
        expect(onNodeClick.mock.calls[1][1]).toBe(stage);
        panel.destroy();
    });

    it('非物件的階段定義被略過；stages 為陣列時視為未提供；原型鍵不會被當成階段', () => {
        expect(htmlOf({ data: flowA, stages: { Create: 'Draft', Edit: null } })).toBe(legacyDom.basic);
        expect(htmlOf({ data: flowA, stages: [{ name: 'x' }] })).toBe(legacyDom.basic);

        const panel = new WorkflowPanel({ data: [{ StageName: 'toString', DateTime: '2026-05-01T09:00:00' }], stages: {} });
        const [node] = nodesOf(panel);
        expect(labelOf(node).textContent).toBe('toString');
        expect(circleOf(node).textContent).toBe('📋');
        panel.destroy();
    });
});

describe('WorkflowPanel — fieldMap', () => {
    it('依 fieldMap 讀取欄位，DOM 與使用舊欄位名的相同資料一致', () => {
        const html = htmlOf({
            data: toMapped(flowB),
            nextStage: { step: 'Approved', assignee: 'Ops Desk' },
            onNodeClick: () => {},
            fieldMap: MAPPED_FIELDS,
        });
        expect(html).toBe(legacyDom.nextClickUnknown);
        expect(htmlOf({ data: toMapped(flowLong), itemsPerRow: 3, nextStage: { step: 'Close' }, fieldMap: MAPPED_FIELDS }))
            .toBe(legacyDom.multirow);
    });

    it('依對應後的時間欄位排序，並顯示單位／人員', () => {
        const panel = new WorkflowPanel({
            data: [
                { step: 'Edit', at: '2026-06-02T10:00:00', team: 'Room B', owner: 'Kim' },
                { step: 'Create', at: '2026-06-01T09:00:00', team: 'Room A', owner: '' },
            ],
            fieldMap: MAPPED_FIELDS,
        });
        const [first, second] = nodesOf(panel);
        expect(labelOf(first).textContent).toBe('立案');
        expect(first.children[2].textContent).toBe('2026/06/01 09:00');
        expect(first.children[3].textContent).toBe('Room A');
        expect(second.children[3].textContent).toBe('Room B / Kim');
        panel.destroy();
    });

    it('只給部分鍵時其餘沿用預設；空字串或非字串值被忽略；stageName 同時套用於 nextStage', () => {
        const panel = new WorkflowPanel({
            data: [{ step: 'Create', DateTime: '2026-06-01T09:00:00', UnitName: 'Room A', UserName: 'Kim' }],
            nextStage: { step: 'Close', NextUnit: 'Front desk' },
            fieldMap: { stageName: 'step', dateTime: '', unitName: 42, unknownKey: 'x' },
        });
        const [node, next] = nodesOf(panel);
        expect(labelOf(node).textContent).toBe('立案');
        expect(node.children[2].textContent).toBe('2026/06/01 09:00');
        expect(node.children[3].textContent).toBe('Room A / Kim');
        expect(labelOf(next).textContent).toBe('歸檔');
        expect(next.children[3].textContent).toBe('Front desk');
        panel.destroy();
    });

    it('nextStage 缺下一單位時仍顯示 Locale 的 pending 文字', () => {
        const panel = new WorkflowPanel({ data: [], nextStage: { step: 'Create' }, fieldMap: MAPPED_FIELDS });
        const [next] = nodesOf(panel);
        expect(next.children[3].textContent).toBe('待定');
        panel.destroy();
    });

    it('onNodeClick 回傳呼叫端原始物件', () => {
        const onNodeClick = vi.fn();
        const data = [{ step: 'Create', at: '2026-06-01T09:00:00' }];
        const panel = new WorkflowPanel({ data, onNodeClick, fieldMap: MAPPED_FIELDS });
        nodesOf(panel)[0].click();
        expect(onNodeClick.mock.calls[0][0]).toBe(data[0]);
        expect(onNodeClick.mock.calls[0][1]).toBe(WorkflowPanel.STAGES.Create);
        panel.destroy();
    });

    it('DEFAULT_FIELD_MAP 凍結且等於舊版欄位名', () => {
        expect(Object.isFrozen(WorkflowPanel.DEFAULT_FIELD_MAP)).toBe(true);
        expect(WorkflowPanel.DEFAULT_FIELD_MAP).toEqual({
            stageName: 'StageName', dateTime: 'DateTime', unitName: 'UnitName', userName: 'UserName', nextUnit: 'NextUnit',
        });
    });
});

describe('WorkflowPanel — Locale', () => {
    it('切換語系後重繪即套用 currentBadge / pending / nextStageHint', () => {
        const panel = new WorkflowPanel({ data: flowB, nextStage: { StageName: 'Approved' } });
        let text = panel.element.textContent;
        expect(text).toContain('目前');
        expect(text).toContain('(待處理)');
        expect(text).toContain('待定');

        Locale.setLang('en');
        panel.setData(flowB);
        text = panel.element.textContent;
        expect(text).toContain('Current');
        expect(text).toContain('(To do)');
        expect(text).toContain('Pending');
        expect(text).not.toContain('待處理');

        Locale.setLang('zh-TW');
        panel.setNextStage({ StageName: 'Approved' });
        expect(panel.element.textContent).toContain('(待處理)');
        panel.destroy();
    });

    it('自訂階段名稱為呼叫端文字，不受語系影響', () => {
        Locale.setLang('en');
        const panel = new WorkflowPanel({
            data: [{ StageName: 'Review', DateTime: '2026-05-01T10:00:00' }],
            stages: { Review: { name: '審閱中' } },
        });
        expect(labelOf(nodesOf(panel)[0]).textContent).toBe('審閱中');
        panel.destroy();
    });
});

describe('WorkflowPanel — RWD、可及性與 destroy', () => {
    function installResizeObserverStub() {
        const observers = [];
        class ResizeObserverStub {
            constructor(callback) {
                this.callback = callback;
                this.observe = vi.fn();
                this.disconnect = vi.fn();
                observers.push(this);
            }
        }
        vi.stubGlobal('ResizeObserver', ResizeObserverStub);
        return observers;
    }
    const nextFrame = () => new Promise((resolve) => requestAnimationFrame(() => resolve()));

    it('容器變窄時改為垂直單欄，自訂階段與欄位對應仍生效', async () => {
        const observers = installResizeObserverStub();
        const panel = new WorkflowPanel({
            data: toMapped(flowB),
            fieldMap: MAPPED_FIELDS,
            stages: { Custom: { name: 'Custom step', icon: '🧩' } },
        }).mount(document.body);

        expect(panel.element.querySelector('.workflow-row')).toBeTruthy();
        observers[0].callback([{ contentRect: { width: 100 } }]);
        await nextFrame();

        expect(panel.element.querySelector('.workflow-column')).toBeTruthy();
        expect(panel.element.querySelector('.workflow-row')).toBeNull();
        expect(nodeByLabel(panel, 'Custom step')).toBeTruthy();
        panel.destroy();
    });

    it('新選項不增加任何可聚焦元素（節點維持舊版的滑鼠點擊行為）', () => {
        const panel = new WorkflowPanel({
            data: flowA, onNodeClick: () => {}, stages: { Create: { name: 'Draft' } }, fieldMap: {},
        });
        expect(panel.element.querySelectorAll('[tabindex], button, a, input').length).toBe(0);
        panel.destroy();
    });

    it('destroy 移除 DOM、斷開 ResizeObserver、取消待執行的重排，且可重複呼叫', async () => {
        const observers = installResizeObserverStub();
        const panel = new WorkflowPanel({ data: flowA, stages: { Create: { name: 'Draft' } } }).mount(document.body);
        const rerender = vi.spyOn(panel, '_rerender');

        observers[0].callback([{ contentRect: { width: 100 } }]); // 排入 rAF 重排
        panel.destroy();
        await nextFrame();

        expect(rerender).not.toHaveBeenCalled();
        expect(observers[0].disconnect).toHaveBeenCalledTimes(1);
        expect(document.body.querySelector('.workflow-panel')).toBeNull();
        expect(() => panel.destroy()).not.toThrow();
        expect(observers[0].disconnect).toHaveBeenCalledTimes(1);
    });

    it('不註冊任何 document / window 監聽器；destroy 後呼叫方法不拋錯', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const panel = new WorkflowPanel({
            data: flowA, onNodeClick: () => {}, stages: { Create: { name: 'Draft' } }, fieldMap: MAPPED_FIELDS,
        }).mount(document.body);
        panel.destroy();

        expect(docAdd).not.toHaveBeenCalled();
        expect(winAdd).not.toHaveBeenCalled();
        expect(() => {
            panel.setData(flowB);
            panel.setNextStage(nextApproved);
            panel.destroy();
        }).not.toThrow();
        expect(document.body.querySelector('.workflow-panel')).toBeNull();
    });
});
