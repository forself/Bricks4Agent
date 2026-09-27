import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';
import { Icon } from '../../ui_components/common/Icon/Icon.js';
import { NotificationCenter } from '../../ui_components/common/NotificationCenter/NotificationCenter.js';
import NotificationCenterDefault, { NotificationCenter as NotificationCenterNamed } from '../../ui_components/common/NotificationCenter/index.js';

const TIME_STYLE = { dateStyle: 'medium', timeStyle: 'short' };

const sampleItems = () => [
    { id: 'n3', title: '新的任務指派', message: '請於週五前完成盤點', time: '2026-09-26T08:00:00.000Z', variant: 'warning' },
    { id: 'n2', title: '會議室預約已確認', message: 'A 室 週三 10:00', href: '/rooms/a' },
    { id: 'n1', title: '訂單已出貨', read: true, variant: 'success' }
];

const trigger = (nc) => nc.element.querySelector('.cl-notification-center__trigger');
const panel = (nc) => nc.element.querySelector('.cl-notification-center__panel');
const badge = (nc) => nc.element.querySelector('.cl-notification-center__badge');
const controls = (nc) => [...nc.element.querySelectorAll('.cl-notification-center__item-control')];
const itemTitles = (nc) => [...nc.element.querySelectorAll('.cl-notification-center__item-title')].map((el) => el.textContent);
const live = (nc) => nc.element.querySelector('.cl-notification-center__live');
const loadMoreButton = (nc) => nc.element.querySelector('.cl-notification-center__load-more');
const footer = (nc) => nc.element.querySelector('.cl-notification-center__footer');

function rect({ top = 0, left = 0, width = 0, height = 0 }) {
    return { top, left, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON() { return this; } };
}

function keydown(target, key, init = {}) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
}

async function flush() {
    for (let i = 0; i < 5; i += 1) await Promise.resolve();
}

let host;
let created;

function make(options = {}) {
    const nc = new NotificationCenter({ items: sampleItems(), ...options }).mount(host);
    created.push(nc);
    return nc;
}

beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
    created = [];
});

afterEach(() => {
    created.forEach((nc) => nc.destroy());
    host.remove();
    vi.useRealTimers();
    vi.restoreAllMocks();
    Locale.setLang('zh-TW');
});

describe('NotificationCenter — 觸發鈕與徽章', () => {
    it('index.js 匯出具名與預設', () => {
        expect(NotificationCenterNamed).toBe(NotificationCenter);
        expect(NotificationCenterDefault).toBe(NotificationCenter);
    });

    it('觸發鈕帶 aria-haspopup／aria-expanded／aria-controls，名稱含未讀數', () => {
        const nc = make();
        const button = trigger(nc);
        expect(button.tagName).toBe('BUTTON');
        expect(button.getAttribute('aria-haspopup')).toBe('dialog');
        expect(button.getAttribute('aria-expanded')).toBe('false');
        expect(button.getAttribute('aria-controls')).toBe(panel(nc).id);
        expect(button.getAttribute('aria-label')).toBe('通知（2 則未讀）');
        expect(badge(nc).textContent).toBe('2');
        expect(badge(nc).getAttribute('aria-hidden')).toBe('true');
        expect(badge(nc).style.display).toBe('inline-flex');
        expect(nc.getUnreadCount()).toBe(2);
    });

    it('徽章超過 99 顯示「99+」，無障礙名稱仍是實際數字', () => {
        const items = Array.from({ length: 150 }, (_, i) => ({ id: i, title: `t${i}` }));
        const nc = make({ items, maxItems: 200 });
        expect(badge(nc).textContent).toBe('99+');
        expect(trigger(nc).getAttribute('aria-label')).toBe('通知（150 則未讀）');
        const exact = make({ items: items.slice(0, 99) });
        expect(badge(exact).textContent).toBe('99');
    });

    it('沒有未讀時隱藏徽章，名稱只有基本名稱', () => {
        const nc = make({ items: [{ id: 1, title: 'a', read: true }] });
        expect(badge(nc).style.display).toBe('none');
        expect(trigger(nc).getAttribute('aria-label')).toBe('通知');
    });

    it('ariaLabel 同時用於觸發鈕與面板標題', () => {
        const nc = make({ ariaLabel: '系統訊息' });
        expect(trigger(nc).getAttribute('aria-label')).toBe('系統訊息（2 則未讀）');
        expect(nc.element.querySelector('.cl-notification-center__heading').textContent).toBe('系統訊息');
    });

    it('鈴鐺是 Canvas Icon（無 SVG）', () => {
        const nc = make();
        expect(trigger(nc).querySelector('canvas')).not.toBeNull();
        expect(nc.element.querySelector('svg')).toBeNull();
    });
});

describe('NotificationCenter — 開關與焦點', () => {
    it('open：面板顯示為 role=dialog、有標題、焦點移入面板', () => {
        const nc = make();
        nc.open();
        const dialog = panel(nc);
        expect(nc.isOpen()).toBe(true);
        expect(nc.snapshot().open).toBe(true);
        expect(trigger(nc).getAttribute('aria-expanded')).toBe('true');
        expect(dialog.hidden).toBe(false);
        expect(dialog.style.display).toBe('flex');
        expect(dialog.getAttribute('role')).toBe('dialog');
        const heading = nc.element.querySelector(`#${dialog.getAttribute('aria-labelledby')}`);
        expect(heading.tagName).toBe('H2');
        expect(heading.textContent).toBe('通知');
        expect(document.activeElement).toBe(dialog);
    });

    it('點觸發鈕切換開關', () => {
        const nc = make();
        trigger(nc).click();
        expect(nc.isOpen()).toBe(true);
        trigger(nc).click();
        expect(nc.isOpen()).toBe(false);
        expect(panel(nc).hidden).toBe(true);
        expect(document.activeElement).toBe(trigger(nc));
    });

    it('Escape 關閉並把焦點還給觸發鈕，且不再往外層傳遞', () => {
        const outer = vi.fn();
        host.addEventListener('keydown', outer);
        const nc = make();
        nc.open();
        const first = controls(nc)[0];
        first.focus();
        const event = keydown(first, 'Escape');
        expect(event.defaultPrevented).toBe(true);
        expect(nc.isOpen()).toBe(false);
        expect(document.activeElement).toBe(trigger(nc));
        expect(outer).not.toHaveBeenCalled();
        host.removeEventListener('keydown', outer);
    });

    it('點面板外（焦點已遺失）關閉並還焦點給觸發鈕', () => {
        const nc = make();
        nc.open();
        document.activeElement.blur();
        document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(nc.isOpen()).toBe(false);
        expect(document.activeElement).toBe(trigger(nc));
    });

    it('點到外部可聚焦元素時關閉但不搶焦點', () => {
        const input = document.createElement('input');
        document.body.appendChild(input);
        const nc = make();
        nc.open();
        input.focus();
        input.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(nc.isOpen()).toBe(false);
        expect(document.activeElement).toBe(input);
        input.remove();
    });

    it('外部元素 stopPropagation 也關得掉（capture 監聽）', () => {
        const blocker = document.createElement('div');
        blocker.addEventListener('click', (event) => event.stopPropagation());
        document.body.appendChild(blocker);
        const nc = make();
        nc.open();
        blocker.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(nc.isOpen()).toBe(false);
        blocker.remove();
    });

    it('點面板內部不關閉', () => {
        const nc = make();
        nc.open();
        nc.element.querySelector('.cl-notification-center__heading').dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(nc.isOpen()).toBe(true);
    });

    it('Tab 離開元件（focusout 到外部）關閉且不搶焦點', () => {
        const outside = document.createElement('button');
        document.body.appendChild(outside);
        const nc = make();
        nc.open();
        const first = controls(nc)[0];
        first.focus();
        first.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: outside }));
        expect(nc.isOpen()).toBe(false);
        outside.remove();
    });

    it('焦點在元件內移動不會關閉', () => {
        const nc = make();
        nc.open();
        const [first, second] = controls(nc);
        first.dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: second }));
        expect(nc.isOpen()).toBe(true);
    });

    it('上下鍵、Home、End 在項目間移動', () => {
        const nc = make();
        nc.open();
        const items = controls(nc);
        keydown(panel(nc), 'ArrowDown');
        expect(document.activeElement).toBe(items[0]);
        keydown(items[0], 'ArrowDown');
        expect(document.activeElement).toBe(items[1]);
        keydown(items[1], 'End');
        expect(document.activeElement).toBe(items[2]);
        keydown(items[2], 'ArrowDown');
        expect(document.activeElement).toBe(items[2]);
        keydown(items[2], 'Home');
        expect(document.activeElement).toBe(items[0]);
        keydown(items[0], 'ArrowUp');
        expect(document.activeElement).toBe(items[0]);
    });
});

describe('NotificationCenter — 浮出定位', () => {
    it('開啟時以 fixed 浮出且留在元件內，關閉後還原', () => {
        const nc = make();
        nc.open();
        const dialog = panel(nc);
        expect(dialog.style.position).toBe('fixed');
        expect(dialog.dataset.floating).toBe('fixed');
        expect(nc.element.contains(dialog)).toBe(true);
        expect(dialog.dataset.placement).toBe('bottom');
        nc.close();
        expect(dialog.style.position).toBe('absolute');
        expect(dialog.dataset.floating).toBeUndefined();
        expect(dialog.dataset.placement).toBeUndefined();
    });

    it('下方空間不足時翻到上方，右緣對齊觸發鈕並夾在視窗內', () => {
        const nc = make();
        vi.spyOn(trigger(nc), 'getBoundingClientRect').mockReturnValue(rect({ top: 700, left: 900, width: 40, height: 40 }));
        vi.spyOn(panel(nc), 'getBoundingClientRect').mockReturnValue(rect({ width: 360, height: 300 }));
        nc.open();
        const dialog = panel(nc);
        expect(dialog.dataset.placement).toBe('top');
        expect(dialog.style.top).toBe(`${700 - 6 - 300}px`);
        expect(dialog.style.left).toBe(`${940 - 360}px`);
    });

    it('上層有 transform 時補償 fixed 參考框座標', () => {
        const frame = document.createElement('div');
        frame.style.transform = 'translateX(10px)';
        host.appendChild(frame);
        const nc = new NotificationCenter({ items: sampleItems() }).mount(frame);
        created.push(nc);
        vi.spyOn(frame, 'getBoundingClientRect').mockReturnValue(rect({ top: 100, left: 50, width: 800, height: 600 }));
        vi.spyOn(trigger(nc), 'getBoundingClientRect').mockReturnValue(rect({ top: 120, left: 500, width: 40, height: 40 }));
        vi.spyOn(panel(nc), 'getBoundingClientRect').mockReturnValue(rect({ width: 360, height: 200 }));
        nc.open();
        expect(panel(nc).style.top).toBe(`${160 + 6 - 100}px`);
        expect(panel(nc).style.left).toBe(`${540 - 360 - 50}px`);
    });
});

describe('NotificationCenter — 全域監聽只在開啟期間', () => {
    it('document click（capture）與 window resize／scroll 開啟時掛上、關閉與銷毀時移除', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const nc = make();
        const count = (spy, target, type) => spy.mock.calls.filter(([t]) => t === type).length;

        expect(count(docAdd, document, 'click')).toBe(0);
        expect(count(winAdd, window, 'resize')).toBe(0);
        expect(count(winAdd, window, 'scroll')).toBe(0);

        nc.open();
        const clickCall = docAdd.mock.calls.find(([type]) => type === 'click');
        expect(clickCall[2]).toBe(true);
        expect(count(winAdd, window, 'resize')).toBe(1);
        expect(winAdd.mock.calls.find(([type]) => type === 'scroll')[2]).toBe(true);

        nc.close();
        const removedClick = docRemove.mock.calls.find(([type]) => type === 'click');
        expect(removedClick[1]).toBe(clickCall[1]);
        expect(removedClick[2]).toBe(true);
        expect(count(winRemove, window, 'resize')).toBe(1);
        expect(count(winRemove, window, 'scroll')).toBe(1);

        nc.open();
        nc.destroy();
        expect(count(docAdd, document, 'click')).toBe(count(docRemove, document, 'click'));
        expect(count(winAdd, window, 'resize')).toBe(count(winRemove, window, 'resize'));
        expect(count(winAdd, window, 'scroll')).toBe(count(winRemove, window, 'scroll'));
        expect(count(winAdd, window, 'locale-changed')).toBe(count(winRemove, window, 'locale-changed'));
    });
});

describe('NotificationCenter — 新增、去重與上限', () => {
    it('add 單筆放最上方；陣列視為新到舊', () => {
        const nc = make();
        nc.add({ id: 'n4', title: '第四則' });
        nc.add([{ id: 'n6', title: '第六則' }, { id: 'n5', title: '第五則' }]);
        nc.open();
        expect(itemTitles(nc)).toEqual(['第六則', '第五則', '第四則', '新的任務指派', '會議室預約已確認', '訂單已出貨']);
        expect(nc.getUnreadCount()).toBe(5);
    });

    it('同 id 取代舊的並移到最上方；未指定 read 時沿用舊的已讀狀態', () => {
        const nc = make();
        nc.add({ id: 'n1', title: '訂單已出貨（更新）' });
        let items = nc.getItems();
        expect(items).toHaveLength(3);
        expect(items[0]).toMatchObject({ id: 'n1', title: '訂單已出貨（更新）', read: true });
        nc.add({ id: 'n1', title: '訂單已退回', read: false });
        items = nc.getItems();
        expect(items[0]).toMatchObject({ id: 'n1', read: false });
        expect(nc.getUnreadCount()).toBe(3);
    });

    it('同一批內重複 id 只保留第一筆', () => {
        const nc = make({ items: [] });
        nc.add([{ id: 'a', title: 'first' }, { id: 'a', title: 'second' }]);
        expect(nc.getItems().map((item) => item.title)).toEqual(['first']);
    });

    it('超過 maxItems 時丟棄最舊的', () => {
        const nc = make({ maxItems: 3 });
        nc.add({ id: 'n4', title: '第四則' });
        expect(nc.getItems().map((item) => item.id)).toEqual(['n4', 'n3', 'n2']);
        nc.setItems(Array.from({ length: 10 }, (_, i) => ({ id: i, title: `t${i}` })));
        expect(nc.getItems()).toHaveLength(3);
        expect(nc.getItems()[0].id).toBe(0);
    });

    it('setItems 取代全部、remove 以 id 移除', () => {
        const nc = make();
        nc.setItems([{ id: 7, title: 'x' }, { id: 8, title: 'y', read: true }]);
        expect(nc.getItems().map((item) => item.id)).toEqual([7, 8]);
        expect(nc.getUnreadCount()).toBe(1);
        nc.remove('7');
        expect(nc.getItems().map((item) => item.id)).toEqual([8]);
        expect(nc.getUnreadCount()).toBe(0);
        expect(badge(nc).style.display).toBe('none');
        nc.remove('missing');
        expect(nc.getItems()).toHaveLength(1);
    });

    it('面板開著時新增會即時重繪並保留焦點項目', () => {
        const nc = make();
        nc.open();
        const second = controls(nc)[1];
        second.focus();
        nc.add({ id: 'n9', title: '最新' });
        expect(itemTitles(nc)[0]).toBe('最新');
        expect(document.activeElement.closest('.cl-notification-center__item').dataset.id).toBe('n2');
    });

    it('項目內容以文字輸出，時間依 Locale 格式化，未讀有文字標示', () => {
        const nc = make({ items: [{ id: 1, title: '<img src=x onerror=alert(1)>', time: '2026-09-26T08:00:00.000Z', variant: 'danger' }] });
        nc.open();
        expect(nc.element.querySelector('img')).toBeNull();
        expect(itemTitles(nc)).toEqual(['<img src=x onerror=alert(1)>']);
        const time = nc.element.querySelector('time');
        const date = new Date('2026-09-26T08:00:00.000Z');
        expect(time.getAttribute('datetime')).toBe(date.toISOString());
        expect(time.textContent).toBe(new Intl.DateTimeFormat('zh-TW', TIME_STYLE).format(date));
        expect(nc.element.querySelector('.cl-notification-center__item-unread').textContent).toBe('未讀');
        expect(nc.element.querySelector('.cl-notification-center__item-variant').textContent).toBe('錯誤');
    });

    it('沒有通知時顯示 Locale 空白文字，可由 emptyText 覆寫', () => {
        const nc = make({ items: [] });
        nc.open();
        expect(nc.element.querySelector('.cl-notification-center__empty').textContent).toBe('目前沒有通知');
        const custom = make({ items: [], emptyText: '都看完了' });
        custom.open();
        expect(custom.element.querySelector('.cl-notification-center__empty').textContent).toBe('都看完了');
    });
});

describe('NotificationCenter — 連結安全', () => {
    it('href 經 sanitizeUrl：安全網址為連結，危險網址退回按鈕', () => {
        const nc = make({
            items: [
                { id: 'ok', title: 'ok', href: '/tasks/1' },
                { id: 'bad', title: 'bad', href: 'javascript:alert(1)' },
                { id: 'none', title: 'none' }
            ]
        });
        nc.open();
        const [ok, bad, none] = controls(nc);
        expect(ok.tagName).toBe('A');
        expect(ok.getAttribute('href')).toBe('/tasks/1');
        expect(bad.tagName).toBe('BUTTON');
        expect(bad.hasAttribute('href')).toBe(false);
        expect(none.tagName).toBe('BUTTON');
        expect(nc.element.querySelector('a[href^="javascript"]')).toBeNull();
    });
});

describe('NotificationCenter — 已讀流程', () => {
    it('點按鈕項目：標為已讀（onMarkRead）、關閉面板、焦點回觸發鈕、再呼叫 onItemClick', () => {
        const items = sampleItems();
        const onMarkRead = vi.fn();
        const onItemClick = vi.fn();
        const nc = make({ items, onMarkRead, onItemClick });
        nc.open();
        controls(nc)[0].focus();
        controls(nc)[0].click();
        expect(onMarkRead).toHaveBeenCalledWith(['n3']);
        expect(onItemClick).toHaveBeenCalledTimes(1);
        expect(onItemClick.mock.calls[0][0]).toBe(items[0]);
        expect(onItemClick.mock.calls[0][1]).toBeInstanceOf(MouseEvent);
        expect(nc.isOpen()).toBe(false);
        expect(document.activeElement).toBe(trigger(nc));
        expect(nc.getUnreadCount()).toBe(1);
        expect(badge(nc).textContent).toBe('1');
    });

    it('點連結項目時連結仍連在文件上（瀏覽器才會導覽），onItemClick 可攔截導覽', () => {
        const onItemClick = vi.fn((item, event) => event.preventDefault());
        const nc = make({ onItemClick });
        nc.open();
        const link = controls(nc)[1];
        expect(link.tagName).toBe('A');
        const event = new MouseEvent('click', { bubbles: true, cancelable: true });
        link.dispatchEvent(event);
        expect(link.isConnected).toBe(true);
        expect(event.defaultPrevented).toBe(true);
        expect(onItemClick).toHaveBeenCalledWith(expect.objectContaining({ id: 'n2' }), event);
        expect(link.closest('.cl-notification-center__item').dataset.read).toBe('true');
    });

    it('以修飾鍵點擊時保持面板開啟', () => {
        const nc = make({ onItemClick: (item, event) => event.preventDefault() });
        nc.open();
        controls(nc)[1].dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ctrlKey: true }));
        expect(nc.isOpen()).toBe(true);
        const entry = controls(nc)[1].closest('.cl-notification-center__item');
        expect(entry.dataset.read).toBe('true');
        expect(entry.querySelector('.cl-notification-center__item-unread')).toBeNull();
    });

    it('markReadOnClick: false 點擊不改已讀', () => {
        const onMarkRead = vi.fn();
        const nc = make({ markReadOnClick: false, onMarkRead });
        nc.open();
        controls(nc)[0].click();
        expect(onMarkRead).not.toHaveBeenCalled();
        expect(nc.getUnreadCount()).toBe(2);
    });

    it('「全部標為已讀」只回報有變動的 id，之後變成 aria-disabled', () => {
        const onMarkRead = vi.fn();
        const nc = make({ onMarkRead });
        nc.open();
        const markAll = nc.element.querySelector('.cl-notification-center__mark-all');
        expect(markAll.textContent).toBe('全部標為已讀');
        expect(markAll.getAttribute('aria-disabled')).toBe('false');
        markAll.click();
        expect(onMarkRead).toHaveBeenCalledWith(['n3', 'n2']);
        expect(nc.getUnreadCount()).toBe(0);
        expect(markAll.getAttribute('aria-disabled')).toBe('true');
        expect(nc.isOpen()).toBe(true);
        markAll.click();
        expect(onMarkRead).toHaveBeenCalledTimes(1);
        expect(nc.element.querySelectorAll('.cl-notification-center__item-dot')).toHaveLength(0);
    });

    it('markReadOnOpen：開啟面板即把未讀標為已讀', () => {
        const onMarkRead = vi.fn();
        const nc = make({ markReadOnOpen: true, onMarkRead });
        nc.open();
        expect(onMarkRead).toHaveBeenCalledWith(['n3', 'n2']);
        expect(nc.getUnreadCount()).toBe(0);
    });

    it('程式呼叫 markRead／markAllRead 預設不觸發 onMarkRead，{ emit: true } 才觸發', () => {
        const onMarkRead = vi.fn();
        const nc = make({ onMarkRead });
        nc.markRead('n3');
        expect(onMarkRead).not.toHaveBeenCalled();
        expect(nc.getUnreadCount()).toBe(1);
        nc.markRead(['n2', 'n1'], { emit: true });
        expect(onMarkRead).toHaveBeenCalledWith(['n2']);
        nc.add({ id: 'n4', title: 'new' });
        nc.markAllRead();
        expect(onMarkRead).toHaveBeenCalledTimes(1);
        nc.add({ id: 'n5', title: 'newer' });
        nc.markAllRead({ emit: true });
        expect(onMarkRead).toHaveBeenLastCalledWith(['n5']);
    });
});

describe('NotificationCenter — 載入更多', () => {
    it('沒有 onLoadMore 時即使 hasMore 也不顯示', () => {
        const nc = make({ hasMore: true });
        nc.open();
        expect(footer(nc).style.display).toBe('none');
    });

    it('點「載入更多」顯示載入中，完成後把較舊的通知接在尾端', async () => {
        let resolve;
        const onLoadMore = vi.fn(() => new Promise((r) => { resolve = r; }));
        const nc = make({ onLoadMore, hasMore: true });
        nc.open();
        const button = loadMoreButton(nc);
        expect(footer(nc).style.display).toBe('flex');
        expect(button.textContent).toBe('載入更多');
        button.focus();
        button.click();
        expect(onLoadMore).toHaveBeenCalledTimes(1);
        expect(button.textContent).toBe('載入中…');
        expect(button.getAttribute('aria-disabled')).toBe('true');
        expect(nc.element.querySelector('.cl-notification-center__list-host').getAttribute('aria-busy')).toBe('true');
        button.click();
        expect(onLoadMore).toHaveBeenCalledTimes(1);

        resolve({ items: [{ id: 'n0', title: '最舊的一則' }, { id: 'n1', title: '重複不加' }], hasMore: false });
        await flush();
        expect(itemTitles(nc)).toEqual(['新的任務指派', '會議室預約已確認', '訂單已出貨', '最舊的一則']);
        expect(footer(nc).style.display).toBe('none');
        expect(nc.snapshot()).toMatchObject({ loading: false, hasMore: false });
        expect(document.activeElement).toBe(controls(nc)[3]);
    });

    it('回傳陣列時保留 hasMore；setHasMore(false) 隱藏', async () => {
        const onLoadMore = vi.fn(async () => [{ id: 'old', title: 'old' }]);
        const nc = make({ onLoadMore, hasMore: true });
        nc.open();
        loadMoreButton(nc).click();
        await flush();
        expect(nc.getItems().at(-1).id).toBe('old');
        expect(footer(nc).style.display).toBe('flex');
        expect(loadMoreButton(nc).textContent).toBe('載入更多');
        nc.setHasMore(false);
        expect(footer(nc).style.display).toBe('none');
    });

    it('載入失敗時恢復按鈕並播報錯誤', async () => {
        const error = vi.spyOn(console, 'error').mockImplementation(() => {});
        const nc = make({ onLoadMore: () => Promise.reject(new Error('offline')), hasMore: true });
        nc.open();
        loadMoreButton(nc).click();
        await flush();
        expect(error).toHaveBeenCalled();
        expect(nc.snapshot().loading).toBe(false);
        expect(loadMoreButton(nc).getAttribute('aria-disabled')).toBe('false');
        expect(live(nc).textContent).toBe('載入失敗，請再試一次');
    });

    it('已達 maxItems 時不顯示「載入更多」', () => {
        const nc = make({ onLoadMore: vi.fn(), hasMore: true, maxItems: 3 });
        nc.open();
        expect(footer(nc).style.display).toBe('none');
    });
});

describe('NotificationCenter — live 播報', () => {
    it('初始不播報；連續變動只在節流時間到時播報最新未讀數', () => {
        vi.useFakeTimers();
        const nc = make({ announceDelay: 1000 });
        const region = live(nc);
        expect(region.getAttribute('role')).toBe('status');
        expect(region.getAttribute('aria-live')).toBe('polite');
        vi.advanceTimersByTime(3000);
        expect(region.textContent).toBe('');

        nc.add({ id: 'a', title: 'a' });
        vi.advanceTimersByTime(600);
        nc.add({ id: 'b', title: 'b' });
        expect(region.textContent).toBe('');
        vi.advanceTimersByTime(400);
        expect(region.textContent).toBe('4 則未讀通知');

        nc.markAllRead();
        vi.advanceTimersByTime(1000);
        expect(region.textContent).toBe('沒有未讀通知');
    });

    it('未讀數沒變時不更新', () => {
        vi.useFakeTimers();
        const nc = make();
        const region = live(nc);
        const setter = vi.spyOn(region, 'textContent', 'set');
        nc.add({ id: 'x', title: 'x', read: true });
        nc.markRead('n1');
        vi.advanceTimersByTime(2000);
        expect(setter).not.toHaveBeenCalled();
    });
});

describe('NotificationCenter — 生命週期與 Locale', () => {
    it('開著時 destroy：移除 DOM、Icon、計時器與所有監聽；之後呼叫方法不拋錯', () => {
        vi.useFakeTimers();
        const iconDestroy = vi.spyOn(Icon.prototype, 'destroy');
        const nc = new NotificationCenter({ items: sampleItems() }).mount(host);
        nc.open();
        nc.add({ id: 'z', title: 'z' });   // 排入一個未讀數播報
        const region = live(nc);
        const setter = vi.spyOn(region, 'textContent', 'set');
        nc.destroy();
        expect(host.children).toHaveLength(0);
        expect(iconDestroy).toHaveBeenCalled();
        // jsdom 的 focus() 自己會排計時器，所以不用 getTimerCount；改驗證播報計時器已取消
        vi.advanceTimersByTime(5000);
        expect(setter).not.toHaveBeenCalled();
        expect(nc.isOpen()).toBe(false);
        expect(() => {
            nc.destroy();
            nc.open();
            nc.close();
            nc.add({ id: 'q', title: 'q' });
            nc.setItems([]);
            nc.markRead('n3');
            nc.markAllRead();
            nc.remove('n3');
            nc.setHasMore(true);
            nc.mount(host);
        }).not.toThrow();
        expect(nc.getItems()).toEqual([]);
        expect(host.children).toHaveLength(0);
    });

    it('切換語言即時更新觸發鈕名稱、標題、動作與項目文字', () => {
        const nc = make({ onLoadMore: vi.fn(), hasMore: true });
        nc.open();
        Locale.setLang('en');
        expect(trigger(nc).getAttribute('aria-label')).toBe('Notifications (2 unread)');
        expect(nc.element.querySelector('.cl-notification-center__heading').textContent).toBe('Notifications');
        expect(nc.element.querySelector('.cl-notification-center__mark-all').textContent).toBe('Mark all as read');
        expect(loadMoreButton(nc).textContent).toBe('Load more');
        expect(nc.element.querySelector('.cl-notification-center__item-unread').textContent).toBe('Unread');
        const empty = make({ items: [] });
        empty.open();
        expect(empty.element.querySelector('.cl-notification-center__empty').textContent).toBe('No notifications');
    });

    it('mount 找不到目標時警告並回傳 this', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const nc = new NotificationCenter();
        created.push(nc);
        expect(nc.mount('#missing')).toBe(nc);
        expect(warn).toHaveBeenCalled();
    });
});
