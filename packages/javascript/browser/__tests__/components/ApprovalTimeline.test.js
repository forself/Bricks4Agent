import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { ApprovalTimeline } from '../../ui_components/common/ApprovalTimeline/ApprovalTimeline.js';
import ApprovalTimelineDefault, { ApprovalTimeline as ApprovalTimelineNamed } from '../../ui_components/common/ApprovalTimeline/index.js';

const STATUSES = ['pending', 'current', 'approved', 'rejected', 'returned', 'skipped', 'cancelled'];
const TIME_STYLE = { dateStyle: 'medium', timeStyle: 'short' };

const sampleSteps = () => [
    { id: 1, title: '申請人送出', actor: '王小明', time: '2026-09-01T01:00:00.000Z', status: 'approved', comment: '請協助採購會議室投影機' },
    { id: 2, title: '部門主管審核', actor: '李主任', time: new Date('2026-09-02T02:30:00.000Z'), status: 'current', attachments: [{ name: '報價單.pdf', href: '/files/quote.pdf' }] },
    { id: 3, title: '總務核銷', status: 'pending' }
];

const items = (timeline) => [...timeline.element.querySelectorAll('.cl-approval-timeline__step')];
const titles = (timeline) => [...timeline.element.querySelectorAll('.cl-approval-timeline__title')].map((el) => el.textContent);

let host;
let created;

function make(options = {}) {
    const timeline = new ApprovalTimeline({ steps: sampleSteps(), ...options }).mount(host);
    created.push(timeline);
    return timeline;
}

beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
    created = [];
});

afterEach(() => {
    created.forEach((timeline) => timeline.destroy());
    host.remove();
    vi.restoreAllMocks();
    Locale.setLang('zh-TW');
});

describe('ApprovalTimeline — 結構與狀態', () => {
    it('index.js 匯出具名與預設', () => {
        expect(ApprovalTimelineNamed).toBe(ApprovalTimeline);
        expect(ApprovalTimelineDefault).toBe(ApprovalTimeline);
    });

    it('以語意 <ol> 呈現，每個步驟一個 <li>，清單有無障礙名稱', () => {
        const timeline = make();
        const list = timeline.element.querySelector('ol');
        expect(list).not.toBeNull();
        expect(list.getAttribute('aria-label')).toBe('審核歷程');
        expect(items(timeline).every((item) => item.tagName === 'LI' && item.parentElement === list)).toBe(true);
        expect(titles(timeline)).toEqual(['申請人送出', '部門主管審核', '總務核銷']);
    });

    it('每種狀態都有 Locale 文字標籤與 aria-hidden 符號', () => {
        const timeline = make({ steps: STATUSES.map((status, index) => ({ id: index, title: `步驟 ${index}`, status })) });
        const expected = ['待審核', '審核中', '已核准', '已駁回', '已退回', '已略過', '已取消'];
        items(timeline).forEach((item, index) => {
            expect(item.dataset.status).toBe(STATUSES[index]);
            expect(item.querySelector('.cl-approval-timeline__status').textContent).toBe(expected[index]);
            const rail = item.querySelector('.cl-approval-timeline__rail');
            expect(rail.getAttribute('aria-hidden')).toBe('true');
            expect(rail.querySelector('.cl-approval-timeline__marker').textContent.length).toBeGreaterThan(0);
        });
        const glyphs = items(timeline).map((item) => item.querySelector('.cl-approval-timeline__marker').textContent);
        expect(new Set(glyphs).size).toBe(STATUSES.length);
    });

    it('未知狀態退回 pending', () => {
        const timeline = make({ steps: [{ id: 'x', title: 'X', status: 'mystery' }] });
        expect(items(timeline)[0].dataset.status).toBe('pending');
        expect(items(timeline)[0].querySelector('.cl-approval-timeline__status').textContent).toBe('待審核');
    });

    it('只有 current 步驟帶 aria-current="step"', () => {
        const timeline = make();
        const current = items(timeline).filter((item) => item.getAttribute('aria-current') === 'step');
        expect(current).toHaveLength(1);
        expect(current[0].dataset.stepId).toBe('2');
    });

    it('最後一個步驟沒有連接線', () => {
        const timeline = make();
        const lines = items(timeline).map((item) => item.querySelector('.cl-approval-timeline__line') !== null);
        expect(lines).toEqual([true, true, false]);
    });

    it('顯示處理人與意見，內容一律以文字輸出', () => {
        const timeline = make({ steps: [{ id: 1, title: '<b>T</b>', actor: '<i>A</i>', status: 'rejected', comment: '<script>x()</script>\n第二行' }] });
        expect(timeline.element.querySelector('script')).toBeNull();
        expect(timeline.element.querySelector('b')).toBeNull();
        expect(timeline.element.querySelector('.cl-approval-timeline__actor').textContent).toBe('<i>A</i>');
        expect(timeline.element.querySelector('.cl-approval-timeline__comment').textContent).toBe('<script>x()</script>\n第二行');
    });

    it('沒有步驟時顯示 Locale 空白文字，可覆寫 emptyText 與 ariaLabel', () => {
        const timeline = make({ steps: [] });
        expect(timeline.element.querySelector('.cl-approval-timeline__empty').textContent).toBe('尚無審核紀錄');
        expect(timeline.element.querySelector('ol')).toBeNull();
        const custom = make({ steps: [], emptyText: '尚未送出' });
        expect(custom.element.querySelector('.cl-approval-timeline__empty').textContent).toBe('尚未送出');
        const named = make({ ariaLabel: '請假單簽核' });
        expect(named.element.querySelector('ol').getAttribute('aria-label')).toBe('請假單簽核');
    });

    it('compact 模式縮小標記', () => {
        const normal = make();
        const compact = make({ compact: true });
        expect(normal.element.dataset.compact).toBe('false');
        expect(compact.element.dataset.compact).toBe('true');
        expect(normal.element.querySelector('.cl-approval-timeline__marker').style.width).toBe('24px');
        expect(compact.element.querySelector('.cl-approval-timeline__marker').style.width).toBe('18px');
    });
});

describe('ApprovalTimeline — 時間格式', () => {
    it('預設依 Locale 語言以 Intl.DateTimeFormat(medium, short) 格式化並輸出 datetime', () => {
        const timeline = make();
        const time = timeline.element.querySelector('time');
        const date = new Date('2026-09-01T01:00:00.000Z');
        expect(time.getAttribute('datetime')).toBe(date.toISOString());
        expect(time.textContent).toBe(new Intl.DateTimeFormat('zh-TW', TIME_STYLE).format(date));
        const second = timeline.element.querySelectorAll('time')[1];
        expect(second.textContent).toBe(new Intl.DateTimeFormat('zh-TW', TIME_STYLE).format(new Date('2026-09-02T02:30:00.000Z')));
    });

    it('切換語言後以新語言重新格式化', () => {
        const timeline = make();
        Locale.setLang('en');
        const date = new Date('2026-09-01T01:00:00.000Z');
        expect(timeline.element.querySelector('time').textContent).toBe(new Intl.DateTimeFormat('en', TIME_STYLE).format(date));
    });

    it('formatTime 自訂格式，收到 Date 與 step', () => {
        const formatTime = vi.fn((date, step) => `${step.id}@${date.getUTCHours()}`);
        const timeline = make({ formatTime });
        expect(timeline.element.querySelector('time').textContent).toBe('1@1');
        expect(formatTime.mock.calls[0][0]).toBeInstanceOf(Date);
        expect(formatTime.mock.calls[0][1].id).toBe(1);
    });

    it('無法解析的時間原樣顯示且不輸出 datetime', () => {
        const timeline = make({ steps: [{ id: 1, title: 'A', status: 'approved', time: '上週三' }] });
        const time = timeline.element.querySelector('time');
        expect(time.textContent).toBe('上週三');
        expect(time.hasAttribute('datetime')).toBe(false);
    });

    it('showTimestamps: false 不顯示時間', () => {
        const timeline = make({ showTimestamps: false });
        expect(timeline.element.querySelector('time')).toBeNull();
        expect(timeline.element.querySelector('.cl-approval-timeline__actor').textContent).toBe('王小明');
    });
});

describe('ApprovalTimeline — 附件連結', () => {
    const attachmentSteps = () => [{
        id: 1,
        title: '附件測試',
        status: 'approved',
        attachments: [
            { name: '報價單.pdf', href: '/files/quote.pdf' },
            { name: '惡意', href: 'javascript:alert(1)' },
            { name: '大小寫混合', href: ' JaVaScRiPt:alert(1)' },
            { name: '資料網址', href: 'data:text/html,<script>alert(1)</script>' },
            { name: '外部', href: 'https://example.com/a.pdf' }
        ]
    }];

    it('附件經 sanitizeUrl：危險網址不產生連結，只顯示檔名', () => {
        const timeline = make({ steps: attachmentSteps() });
        const anchors = [...timeline.element.querySelectorAll('a')];
        expect(anchors.map((a) => a.getAttribute('href'))).toEqual(['/files/quote.pdf', 'https://example.com/a.pdf']);
        expect(anchors.some((a) => /javascript|data:/i.test(a.getAttribute('href')))).toBe(false);
        const disabled = [...timeline.element.querySelectorAll('.cl-approval-timeline__attachment--disabled')].map((el) => el.textContent);
        expect(disabled).toEqual(['惡意', '大小寫混合', '資料網址']);
        expect(timeline.element.querySelector('.cl-approval-timeline__attachments').getAttribute('aria-label')).toBe('附件');
    });

    it('預設同分頁開啟：沒有 target 與 rel', () => {
        const timeline = make({ steps: attachmentSteps() });
        const link = timeline.element.querySelector('a');
        expect(link.hasAttribute('target')).toBe(false);
        expect(link.hasAttribute('rel')).toBe(false);
        expect(link.textContent).toBe('報價單.pdf');
    });

    it('openLinksInNewTab: true 加 target="_blank"、rel="noopener noreferrer" 與提示文字', () => {
        const timeline = make({ steps: attachmentSteps(), openLinksInNewTab: true });
        const links = [...timeline.element.querySelectorAll('a')];
        expect(links.every((a) => a.getAttribute('target') === '_blank')).toBe(true);
        expect(links.every((a) => a.getAttribute('rel') === 'noopener noreferrer')).toBe(true);
        expect(links[0].querySelector('.cl-approval-timeline__new-tab').textContent).toBe('（在新分頁開啟）');
    });
});

describe('ApprovalTimeline — 反向與互動', () => {
    it('reverse: true 最新在上，ol 帶 reversed；getSteps 維持原順序', () => {
        const steps = sampleSteps();
        const timeline = make({ steps, reverse: true });
        expect(titles(timeline)).toEqual(['總務核銷', '部門主管審核', '申請人送出']);
        expect(timeline.element.querySelector('ol').hasAttribute('reversed')).toBe(true);
        const lines = items(timeline).map((item) => item.querySelector('.cl-approval-timeline__line') !== null);
        expect(lines).toEqual([true, true, false]);
        expect(timeline.getSteps()).toEqual(steps);
        expect(timeline.getSteps()[0]).toBe(steps[0]);
    });

    it('沒有 onStepClick 時標題不是按鈕', () => {
        const timeline = make();
        expect(timeline.element.querySelector('button')).toBeNull();
    });

    it('提供 onStepClick 時標題成為原生按鈕（可 Tab、Enter／Space），點擊傳回原 step', () => {
        const steps = sampleSteps();
        const onStepClick = vi.fn();
        const timeline = make({ steps, onStepClick, reverse: true });
        const buttons = [...timeline.element.querySelectorAll('button.cl-approval-timeline__title-button')];
        expect(buttons).toHaveLength(3);
        expect(buttons.every((button) => button.type === 'button')).toBe(true);
        buttons[0].focus();
        expect(document.activeElement).toBe(buttons[0]);
        buttons[0].click();
        expect(onStepClick).toHaveBeenCalledWith(steps[2]);
        expect(onStepClick.mock.calls[0][0]).toBe(steps[2]);
    });

    it('setSteps 重繪；語言切換重繪時保留標題按鈕焦點', () => {
        const onStepClick = vi.fn();
        const timeline = make({ onStepClick });
        timeline.setSteps([{ id: 9, title: '新流程', status: 'current' }]);
        expect(titles(timeline)).toEqual(['新流程']);
        const button = timeline.element.querySelector('button');
        button.focus();
        Locale.setLang('en');
        expect(timeline.element.querySelector('.cl-approval-timeline__status').textContent).toBe('In review');
        expect(document.activeElement).toBe(timeline.element.querySelector('button'));
        expect(document.activeElement.textContent).toBe('新流程');
    });
});

describe('ApprovalTimeline — 生命週期與 Locale', () => {
    it('destroy 移除 DOM 與 window 監聽，之後呼叫方法不拋錯', () => {
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const docAdd = vi.spyOn(document, 'addEventListener');
        const timeline = new ApprovalTimeline({ steps: sampleSteps() }).mount(host);
        const added = winAdd.mock.calls.filter(([type]) => type === 'locale-changed');
        expect(added).toHaveLength(1);
        expect(docAdd).not.toHaveBeenCalled();
        timeline.destroy();
        expect(host.children).toHaveLength(0);
        const removed = winRemove.mock.calls.filter(([type]) => type === 'locale-changed');
        expect(removed).toHaveLength(1);
        expect(removed[0][1]).toBe(added[0][1]);
        expect(() => {
            timeline.destroy();
            timeline.setSteps(sampleSteps());
            timeline.mount(host);
        }).not.toThrow();
        expect(timeline.getSteps()).toEqual([]);
        expect(host.children).toHaveLength(0);
    });

    it('切換語言即時更新狀態文字、清單名稱與空白文字', () => {
        const timeline = make();
        const empty = make({ steps: [] });
        Locale.setLang('en');
        const labels = [...timeline.element.querySelectorAll('.cl-approval-timeline__status')].map((el) => el.textContent);
        expect(labels).toEqual(['Approved', 'In review', 'Pending']);
        expect(timeline.element.querySelector('ol').getAttribute('aria-label')).toBe('Approval history');
        expect(empty.element.querySelector('.cl-approval-timeline__empty').textContent).toBe('No review steps yet');
    });

    it('mount 找不到目標時警告並回傳 this', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const timeline = new ApprovalTimeline();
        created.push(timeline);
        expect(timeline.mount('#missing')).toBe(timeline);
        expect(warn).toHaveBeenCalled();
    });
});
