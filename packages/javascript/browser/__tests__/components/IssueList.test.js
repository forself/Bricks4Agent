import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { IssueList } from '../../ui_components/common/IssueList/IssueList.js';
import IssueListDefault, { IssueList as IssueListNamed } from '../../ui_components/common/IssueList/index.js';

const sample = () => [
    { id: 'w1', severity: 'warning', title: '參與人數超過建議上限', meta: 'B 室' },
    { id: 'e1', severity: 'error', title: '會議室時段重疊', message: 'A 室 10:00 已被預約', source: { field: 'room' } },
    { id: 'i1', severity: 'info', title: '尚未填寫備註' },
    { id: 'e2', severity: 'error', title: '缺少主持人', source: { field: 'host' } },
    { id: 'w2', severity: 'warning', title: '時段接近下班時間' }
];

const titles = (list) => [...list.element.querySelectorAll('.cl-issue-list__title')].map((el) => el.textContent);
const rows = (list) => [...list.element.querySelectorAll('.cl-issue-list__row')];
const countButton = (list, key) => list.element.querySelector(`.cl-issue-list__count--${key}`);
const live = (list) => list.element.querySelector('.cl-issue-list__live');

function keydown(target, key, init = {}) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
}

function keyup(target, key) {
    const event = new KeyboardEvent('keyup', { key, bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
}

let host;
let created;

function make(options = {}) {
    const list = new IssueList({ issues: sample(), ...options }).mount(host);
    created.push(list);
    return list;
}

beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
    created = [];
});

afterEach(() => {
    created.forEach((list) => list.destroy());
    host.remove();
    vi.useRealTimers();
    vi.restoreAllMocks();
    Locale.setLang('zh-TW');
});

describe('IssueList — 預設行為', () => {
    it('index.js 匯出具名與預設', () => {
        expect(IssueListNamed).toBe(IssueList);
        expect(IssueListDefault).toBe(IssueList);
    });

    it('預設依嚴重度穩定排序（error → warning → info，同級維持輸入順序）', () => {
        const list = make();
        expect(titles(list)).toEqual(['會議室時段重疊', '缺少主持人', '參與人數超過建議上限', '時段接近下班時間', '尚未填寫備註']);
        expect(list.element.querySelectorAll('.cl-issue-list__item')[0].dataset.severity).toBe('error');
    });

    it("sort: 'none' 維持輸入順序", () => {
        const list = make({ sort: 'none' });
        expect(titles(list)).toEqual(sample().map((issue) => issue.title));
    });

    it('嚴重度以文字標籤 + 圖示 + 顏色呈現（不單靠顏色）', () => {
        const list = make();
        const first = list.element.querySelector('.cl-issue-list__item');
        expect(first.querySelector('.cl-issue-list__severity').textContent).toBe('錯誤');
        const icon = first.querySelector('.cl-issue-list__icon');
        expect(icon.getAttribute('aria-hidden')).toBe('true');
        expect(icon.textContent.length).toBeGreaterThan(0);
        expect(first.style.borderLeftColor).toBe('var(--cl-danger)');
        const labels = [...list.element.querySelectorAll('.cl-issue-list__severity')].map((el) => el.textContent);
        expect(labels).toEqual(['錯誤', '錯誤', '警告', '警告', '提示']);
    });

    it('顯示 message 與 meta，未知嚴重度退回 info', () => {
        const list = make({ issues: [{ id: 'x', severity: 'fatal', title: 'T', message: 'M', meta: 'm1' }] });
        const item = list.element.querySelector('.cl-issue-list__item');
        expect(item.dataset.severity).toBe('info');
        expect(item.querySelector('.cl-issue-list__message').textContent).toBe('M');
        expect(item.querySelector('.cl-issue-list__meta').textContent).toBe('m1');
    });

    it('摘要計數顯示全部總數，預設「全部」為按下狀態', () => {
        const list = make();
        expect(countButton(list, 'all').textContent).toBe('全部5');
        expect(countButton(list, 'error').querySelector('.cl-issue-list__count-value').textContent).toBe('2');
        expect(countButton(list, 'warning').querySelector('.cl-issue-list__count-value').textContent).toBe('2');
        expect(countButton(list, 'info').querySelector('.cl-issue-list__count-value').textContent).toBe('1');
        expect(countButton(list, 'all').getAttribute('aria-pressed')).toBe('true');
        expect(countButton(list, 'error').getAttribute('aria-pressed')).toBe('false');
        // 無障礙名稱含可見文字並以空白分隔（避免相鄰 span 被接成「錯誤2」）
        expect(countButton(list, 'all').getAttribute('aria-label')).toBe('全部 5');
        expect(countButton(list, 'error').getAttribute('aria-label')).toBe('錯誤 2');
        expect(list.element.querySelector('.cl-issue-list__summary').getAttribute('role')).toBe('group');
    });

    it('空清單顯示 Locale 預設文字，可由 emptyText 覆寫', () => {
        const list = make({ issues: [] });
        expect(list.element.querySelector('.cl-issue-list__empty').textContent).toBe('沒有問題');
        const custom = make({ issues: [], emptyText: '全部通過' });
        expect(custom.element.querySelector('.cl-issue-list__empty').textContent).toBe('全部通過');
    });

    it('資料一律以文字輸出（不解析 HTML）', () => {
        const list = make({ issues: [{ id: 1, severity: 'error', title: '<img src=x onerror=alert(1)>', message: '<b>m</b>' }] });
        expect(list.element.querySelector('img')).toBeNull();
        expect(list.element.querySelector('b')).toBeNull();
        expect(titles(list)).toEqual(['<img src=x onerror=alert(1)>']);
    });

    it('清單有無障礙名稱，可由 ariaLabel 覆寫', () => {
        const list = make();
        expect(list.element.querySelector('.cl-issue-list__body').getAttribute('aria-label')).toBe('問題清單');
        const named = make({ ariaLabel: '表單檢查結果' });
        expect(named.element.querySelector('.cl-issue-list__body').getAttribute('aria-label')).toBe('表單檢查結果');
    });
});

describe('IssueList — 分組', () => {
    it("groupBy: 'severity' 依嚴重度分組並顯示組標題與筆數", () => {
        const list = make({ groupBy: 'severity' });
        const groups = [...list.element.querySelectorAll('.cl-issue-list__group')];
        expect(groups.map((group) => group.dataset.severity)).toEqual(['error', 'warning', 'info']);
        const headings = groups.map((group) => group.querySelector('.cl-issue-list__group-heading'));
        expect(headings.map((heading) => heading.textContent)).toEqual(['錯誤（2）', '警告（2）', '提示（1）']);
        const firstList = groups[0].querySelector('ul');
        expect(firstList.getAttribute('aria-labelledby')).toBe(headings[0].id);
        expect(titles(list)).toEqual(['會議室時段重疊', '缺少主持人', '參與人數超過建議上限', '時段接近下班時間', '尚未填寫備註']);
    });

    it('分組 + 篩選時略過沒有項目的組', () => {
        const list = make({ groupBy: 'severity', filter: ['warning'] });
        const groups = [...list.element.querySelectorAll('.cl-issue-list__group')];
        expect(groups.map((group) => group.dataset.severity)).toEqual(['warning']);
    });
});

describe('IssueList — 篩選與摘要', () => {
    it('filter 選項只顯示指定嚴重度，摘要計數仍為全部', () => {
        const list = make({ filter: ['error'] });
        expect(titles(list)).toEqual(['會議室時段重疊', '缺少主持人']);
        expect(countButton(list, 'all').getAttribute('aria-pressed')).toBe('false');
        expect(countButton(list, 'error').getAttribute('aria-pressed')).toBe('true');
        expect(countButton(list, 'warning').querySelector('.cl-issue-list__count-value').textContent).toBe('2');
    });

    it('點計數即篩選；再點同一個或「全部」取消篩選', () => {
        const list = make();
        countButton(list, 'warning').click();
        expect(list.getFilter()).toEqual(['warning']);
        expect(titles(list)).toEqual(['參與人數超過建議上限', '時段接近下班時間']);
        countButton(list, 'warning').click();
        expect(list.getFilter()).toBeNull();
        expect(rows(list)).toHaveLength(5);
        countButton(list, 'info').click();
        expect(titles(list)).toEqual(['尚未填寫備註']);
        countButton(list, 'all').click();
        expect(list.getFilter()).toBeNull();
        expect(countButton(list, 'all').getAttribute('aria-pressed')).toBe('true');
    });

    it('setFilter 正規化：去除無效值、依固定順序、空陣列視為全部', () => {
        const list = make();
        list.setFilter(['info', 'bogus', 'error']);
        expect(list.getFilter()).toEqual(['error', 'info']);
        expect(list.snapshot().filter).toEqual(['error', 'info']);
        expect(titles(list)).toEqual(['會議室時段重疊', '缺少主持人', '尚未填寫備註']);
        list.setFilter([]);
        expect(list.getFilter()).toBeNull();
        list.setFilter('warning');
        expect(list.getFilter()).toEqual(['warning']);
        // 三種全選等同不篩選：「全部」維持按下狀態
        list.setFilter(['info', 'warning', 'error']);
        expect(list.getFilter()).toBeNull();
        expect(countButton(list, 'all').getAttribute('aria-pressed')).toBe('true');
    });

    it('篩選後沒有符合項目時顯示專用文字（不是「沒有問題」）', () => {
        const list = make({ issues: [{ id: 1, severity: 'info', title: 'x' }], filter: ['error'] });
        expect(list.element.querySelector('.cl-issue-list__empty').textContent).toBe('目前的篩選條件下沒有項目');
    });

    it('showSummary: false 不顯示摘要列', () => {
        const list = make({ showSummary: false });
        expect(list.element.querySelector('.cl-issue-list__summary')).toBeNull();
        expect(rows(list)).toHaveLength(5);
    });
});

describe('IssueList — 鍵盤操作', () => {
    it('roving tabindex：整份清單只有一個 Tab 停駐點', () => {
        const list = make();
        const tabbable = rows(list).filter((row) => row.tabIndex === 0);
        expect(tabbable).toHaveLength(1);
        expect(tabbable[0]).toBe(rows(list)[0]);
        expect(rows(list).every((row) => row.getAttribute('role') === 'button')).toBe(true);
    });

    it('上下鍵、Home、End 在列之間移動焦點並更新 tabindex', () => {
        const list = make();
        list.focusFirst();
        const all = rows(list);
        expect(document.activeElement).toBe(all[0]);
        keydown(all[0], 'ArrowDown');
        expect(document.activeElement).toBe(all[1]);
        expect(all[1].tabIndex).toBe(0);
        expect(all[0].tabIndex).toBe(-1);
        keydown(all[1], 'End');
        expect(document.activeElement).toBe(all[4]);
        keydown(all[4], 'ArrowDown');
        expect(document.activeElement).toBe(all[4]);
        keydown(all[4], 'ArrowUp');
        expect(document.activeElement).toBe(all[3]);
        keydown(all[3], 'Home');
        expect(document.activeElement).toBe(all[0]);
    });

    it('上下鍵跨組移動', () => {
        const list = make({ groupBy: 'severity' });
        const all = rows(list);
        all[1].focus();
        keydown(all[1], 'ArrowDown');
        expect(document.activeElement).toBe(all[2]);
        expect(document.activeElement.closest('.cl-issue-list__group').dataset.severity).toBe('warning');
    });

    it('Enter 觸發 onSelect，傳回呼叫端原物件（含 source）', () => {
        const issues = sample();
        const onSelect = vi.fn();
        const list = make({ issues, onSelect });
        const first = rows(list)[0];
        first.focus();
        const event = keydown(first, 'Enter');
        expect(event.defaultPrevented).toBe(true);
        expect(onSelect).toHaveBeenCalledTimes(1);
        expect(onSelect.mock.calls[0][0]).toBe(issues[1]);
        expect(onSelect.mock.calls[0][0].source).toEqual({ field: 'room' });
    });

    it('Space 在 keyup 觸發 onSelect（keydown 只擋捲動）', () => {
        const onSelect = vi.fn();
        const list = make({ onSelect });
        const second = rows(list)[1];
        second.focus();
        const down = keydown(second, ' ');
        expect(down.defaultPrevented).toBe(true);
        expect(onSelect).not.toHaveBeenCalled();
        keyup(second, ' ');
        expect(onSelect).toHaveBeenCalledTimes(1);
        expect(onSelect.mock.calls[0][0].id).toBe('e2');
    });

    it('滑鼠點擊列觸發 onSelect', () => {
        const onSelect = vi.fn();
        const list = make({ onSelect });
        rows(list)[2].querySelector('.cl-issue-list__title').click();
        expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'w1' }));
        expect(rows(list)[2].tabIndex).toBe(0);
    });

    it('修飾鍵組合不攔截', () => {
        const list = make();
        const all = rows(list);
        all[0].focus();
        const event = keydown(all[0], 'ArrowDown', { ctrlKey: true });
        expect(event.defaultPrevented).toBe(false);
        expect(document.activeElement).toBe(all[0]);
    });
});

describe('IssueList — 忽略（dismiss）', () => {
    it('預設不顯示忽略按鈕', () => {
        const list = make();
        expect(list.element.querySelector('.cl-issue-list__dismiss')).toBeNull();
    });

    it('點忽略按鈕移除該列並以原物件呼叫 onDismiss', () => {
        const issues = sample();
        const onDismiss = vi.fn();
        const list = make({ issues, dismissible: true, onDismiss });
        const dismiss = list.element.querySelector('.cl-issue-list__dismiss');
        expect(dismiss.getAttribute('aria-label')).toBe('忽略：會議室時段重疊');
        dismiss.click();
        expect(onDismiss).toHaveBeenCalledWith(issues[1]);
        expect(titles(list)).not.toContain('會議室時段重疊');
        expect(list.getIssues()).toHaveLength(4);
    });

    it('只有作用列的忽略按鈕在 Tab 順序內', () => {
        const list = make({ dismissible: true });
        const buttons = [...list.element.querySelectorAll('.cl-issue-list__dismiss')];
        expect(buttons.filter((button) => button.tabIndex === 0)).toHaveLength(1);
        const all = rows(list);
        all[0].focus();
        keydown(all[0], 'ArrowDown');
        expect(buttons[1].tabIndex).toBe(0);
        expect(buttons[0].tabIndex).toBe(-1);
    });

    it('Delete 鍵忽略目前列，焦點移到同位置的下一列', () => {
        const onDismiss = vi.fn();
        const list = make({ dismissible: true, onDismiss });
        const all = rows(list);
        all[1].focus();
        keydown(all[1], 'Delete');
        expect(onDismiss).toHaveBeenCalledWith(expect.objectContaining({ id: 'e2' }));
        const after = rows(list);
        expect(after).toHaveLength(4);
        expect(document.activeElement).toBe(after[1]);
        expect(document.activeElement.querySelector('.cl-issue-list__title').textContent).toBe('參與人數超過建議上限');
    });

    it('未開啟 dismissible 時 Delete 鍵不動作', () => {
        const onDismiss = vi.fn();
        const list = make({ onDismiss });
        const first = rows(list)[0];
        first.focus();
        keydown(first, 'Delete');
        expect(onDismiss).not.toHaveBeenCalled();
        expect(rows(list)).toHaveLength(5);
    });

    it('忽略最後一筆後焦點移到「全部」計數按鈕', () => {
        const list = make({ issues: [{ id: 1, severity: 'error', title: 'only' }], dismissible: true });
        const row = rows(list)[0];
        row.focus();
        keydown(row, 'Delete');
        expect(rows(list)).toHaveLength(0);
        expect(document.activeElement).toBe(countButton(list, 'all'));
    });
});

describe('IssueList — 資料方法', () => {
    it('addIssue 新增；同 id 就地取代', () => {
        const list = make();
        list.addIssue({ id: 'e3', severity: 'error', title: '新錯誤' });
        expect(titles(list)).toEqual(['會議室時段重疊', '缺少主持人', '新錯誤', '參與人數超過建議上限', '時段接近下班時間', '尚未填寫備註']);
        list.addIssue({ id: 'w1', severity: 'info', title: '改為提示' });
        expect(list.getIssues()).toHaveLength(6);
        expect(list.getIssues()[0].title).toBe('改為提示');
        // 改為 info 後進入 info 桶，穩定排序：輸入順序較前的 w1 排在 i1 之前
        expect(titles(list).slice(-2)).toEqual(['改為提示', '尚未填寫備註']);
    });

    it('removeIssue 以 id 移除（數字與字串形式相容），未知 id 不動作', () => {
        const list = make({ issues: [{ id: 7, severity: 'error', title: 'a' }, { id: 8, severity: 'info', title: 'b' }] });
        list.removeIssue('7');
        expect(titles(list)).toEqual(['b']);
        list.removeIssue('nope');
        expect(titles(list)).toEqual(['b']);
    });

    it('clear 清空、getIssues 以輸入順序回傳呼叫端物件', () => {
        const issues = sample();
        const list = make({ issues });
        const got = list.getIssues();
        expect(got).toEqual(issues);
        expect(got[0]).toBe(issues[0]);
        expect(got).not.toBe(issues);
        list.clear();
        expect(list.getIssues()).toEqual([]);
        expect(list.element.querySelector('.cl-issue-list__empty').textContent).toBe('沒有問題');
    });

    it('setIssues 重新驗證後，焦點仍停在同一個 id 的問題', () => {
        const list = make();
        const all = rows(list);
        all[3].focus();
        const focusedTitle = document.activeElement.querySelector('.cl-issue-list__title').textContent;
        const next = sample().filter((issue) => issue.id !== 'e1');
        list.setIssues(next);
        expect(document.activeElement.querySelector('.cl-issue-list__title').textContent).toBe(focusedTitle);
        expect(document.activeElement.tabIndex).toBe(0);
    });

    it('maxHeight：數字轉 px、字串照用，並開啟捲動', () => {
        const list = make({ maxHeight: 200 });
        const body = list.element.querySelector('.cl-issue-list__body');
        expect(body.style.maxHeight).toBe('200px');
        expect(body.style.overflowY).toBe('auto');
        const other = make({ maxHeight: '50vh' });
        expect(other.element.querySelector('.cl-issue-list__body').style.maxHeight).toBe('50vh');
        const none = make();
        expect(none.element.querySelector('.cl-issue-list__body').style.maxHeight).toBe('');
    });
});

describe('IssueList — live region 播報', () => {
    it('live region 為 polite status，初始資料不播報', () => {
        vi.useFakeTimers();
        const list = make();
        const region = live(list);
        expect(region.getAttribute('role')).toBe('status');
        expect(region.getAttribute('aria-live')).toBe('polite');
        expect(region.getAttribute('aria-atomic')).toBe('true');
        vi.advanceTimersByTime(5000);
        expect(region.textContent).toBe('');
    });

    it('setIssues 後節流播報最新摘要', () => {
        vi.useFakeTimers();
        const list = make({ announceDelay: 1000 });
        const region = live(list);
        list.setIssues([{ id: 1, severity: 'error', title: 'a' }]);
        vi.advanceTimersByTime(500);
        list.setIssues([{ id: 1, severity: 'error', title: 'a' }, { id: 2, severity: 'warning', title: 'b' }]);
        vi.advanceTimersByTime(400);
        list.setIssues([{ id: 1, severity: 'error', title: 'a' }, { id: 2, severity: 'warning', title: 'b' }, { id: 3, severity: 'info', title: 'c' }]);
        expect(region.textContent).toBe('');
        vi.advanceTimersByTime(100);
        expect(region.textContent).toBe('共 3 項：錯誤 1、警告 1、提示 1');

        // 下一個時間窗重新計時
        list.clear();
        vi.advanceTimersByTime(999);
        expect(region.textContent).toBe('共 3 項：錯誤 1、警告 1、提示 1');
        vi.advanceTimersByTime(1);
        expect(region.textContent).toBe('沒有問題');
    });

    it('摘要沒變時不更新 live region', () => {
        vi.useFakeTimers();
        const list = make();
        const region = live(list);
        const setter = vi.spyOn(region, 'textContent', 'set');
        list.setIssues(sample().map((issue) => ({ ...issue, title: `${issue.title}（更新）` })));
        vi.advanceTimersByTime(2000);
        expect(setter).not.toHaveBeenCalled();
        expect(region.textContent).toBe('');
    });

    it('addIssue／removeIssue／忽略同樣會播報', () => {
        vi.useFakeTimers();
        const list = make({ issues: [], dismissible: true });
        const region = live(list);
        list.addIssue({ id: 1, severity: 'warning', title: 'a' });
        vi.advanceTimersByTime(1000);
        expect(region.textContent).toBe('共 1 項：錯誤 0、警告 1、提示 0');
        list.element.querySelector('.cl-issue-list__dismiss').click();
        vi.advanceTimersByTime(1000);
        expect(region.textContent).toBe('沒有問題');
    });
});

describe('IssueList — 生命週期', () => {
    it('destroy 移除 DOM、window 監聽與計時器，之後呼叫方法不拋錯', () => {
        vi.useFakeTimers();
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const docAdd = vi.spyOn(document, 'addEventListener');
        const list = new IssueList({ issues: sample() }).mount(host);
        expect(list.snapshot().lifecycle).toBe('mounted');
        const added = winAdd.mock.calls.filter(([type]) => type === 'locale-changed');
        expect(added).toHaveLength(1);
        expect(docAdd).not.toHaveBeenCalled();

        list.setIssues([]);   // 排入一個播報
        const region = live(list);
        list.destroy();
        expect(host.children).toHaveLength(0);
        const removed = winRemove.mock.calls.filter(([type]) => type === 'locale-changed');
        expect(removed).toHaveLength(1);
        expect(removed[0][1]).toBe(added[0][1]);
        vi.advanceTimersByTime(5000);
        expect(region.textContent).toBe('');
        expect(vi.getTimerCount()).toBe(0);

        expect(() => {
            list.destroy();
            list.setIssues(sample());
            list.addIssue({ id: 'z', severity: 'error', title: 'z' });
            list.removeIssue('z');
            list.clear();
            list.setFilter(['error']);
            list.focusFirst();
            list.mount(host);
        }).not.toThrow();
        expect(list.getIssues()).toEqual([]);
        expect(host.children).toHaveLength(0);
    });

    it('mount 找不到目標時警告並回傳 this', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const list = new IssueList();
        created.push(list);
        expect(list.mount('#does-not-exist')).toBe(list);
        expect(warn).toHaveBeenCalled();
    });
});

describe('IssueList — Locale 切換', () => {
    it('切換語言即時更新摘要、嚴重度標籤、組標題與空白文字', () => {
        const list = make({ groupBy: 'severity' });
        const empty = make({ issues: [] });
        Locale.setLang('en');
        expect(countButton(list, 'all').querySelector('.cl-issue-list__count-label').textContent).toBe('All');
        expect(countButton(list, 'error').getAttribute('aria-label')).toBe('Error 2');
        expect(list.element.querySelector('.cl-issue-list__severity').textContent).toBe('Error');
        expect(list.element.querySelector('.cl-issue-list__group-heading').textContent).toBe('Error (2)');
        expect(list.element.querySelector('.cl-issue-list__summary').getAttribute('aria-label')).toBe('Filter by severity');
        expect(empty.element.querySelector('.cl-issue-list__empty').textContent).toBe('No issues');
    });

    it('語言切換本身不觸發播報；之後的變動以新語言播報', () => {
        vi.useFakeTimers();
        const list = make();
        const region = live(list);
        Locale.setLang('en');
        vi.advanceTimersByTime(2000);
        expect(region.textContent).toBe('');
        list.setIssues([{ id: 1, severity: 'error', title: 'a' }]);
        vi.advanceTimersByTime(1000);
        expect(region.textContent).toBe('1 issues: 1 errors, 0 warnings, 0 info');
    });
});
