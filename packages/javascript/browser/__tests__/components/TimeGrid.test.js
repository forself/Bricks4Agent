import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TimeGrid } from '../../ui_components/layout/TimeGrid/TimeGrid.js';
import TimeGridFromIndex from '../../ui_components/layout/TimeGrid/index.js';
import Locale from '../../ui_components/i18n/index.js';

const COLUMNS = [
    { key: 'roomA', label: 'Room A' },
    { key: 'roomB', label: 'Room B' },
    { key: 'roomC', label: 'Room C' }
];

// 預設 timeRange 08:00–18:00 / 30 分鐘 → 列索引：08:00=0, 08:30=1, 09:00=2, 09:30=3, 10:00=4 ...
const ROW = (time) => {
    const [h, m] = time.split(':').map(Number);
    return (h * 60 + m - 480) / 30;
};

let host;
let grids;

function create(options = {}) {
    const grid = new TimeGrid({ columns: COLUMNS, ...options });
    grids.push(grid);
    return grid;
}

function mounted(options = {}) {
    return create(options).mount(host);
}

function cellOf(grid, column, slot) {
    return grid.element.querySelector(`[role="gridcell"][data-column="${column}"][data-slot="${slot}"]`);
}

function itemOf(grid, id) {
    return grid.element.querySelector(`[data-item-id="${id}"]`);
}

function keydown(key, options = {}, target = document.activeElement) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options });
    target.dispatchEvent(event);
    return event;
}

function pointer(target, type, { x = 0, y = 0, id = 1, button = 0 } = {}) {
    target.dispatchEvent(new PointerEvent(type, { bubbles: true, cancelable: true, clientX: x, clientY: y, pointerId: id, button, isPrimary: true }));
}

// 儲存格幾何：第 c 欄第 r 列位於 (100 + c*100, 50 + r*40)，寬 100、高 40
function mockGeometry() {
    return vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function rect() {
        const row = this.dataset ? this.dataset.row : undefined;
        const col = this.dataset ? this.dataset.col : undefined;
        if (row !== undefined && col !== undefined) {
            const left = 100 + Number(col) * 100;
            const top = 50 + Number(row) * 40;
            return { left, top, width: 100, height: 40, right: left + 100, bottom: top + 40, x: left, y: top, toJSON() {} };
        }
        return { left: 0, top: 0, width: 2000, height: 2000, right: 2000, bottom: 2000, x: 0, y: 0, toJSON() {} };
    });
}

const at = (col, row) => ({ x: 150 + col * 100, y: 70 + row * 40 });
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
const pct = (value) => Number.parseFloat(String(value).replace(/^calc\(/, ''));

beforeEach(() => {
    host = document.createElement('div');
    document.body.appendChild(host);
    grids = [];
});

afterEach(() => {
    for (const grid of grids) grid.destroy();
    host.remove();
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
    vi.useRealTimers();
});

describe('TimeGrid — structure and defaults', () => {
    it('exports the class from index.js and exposes the variant list', () => {
        expect(TimeGridFromIndex).toBe(TimeGrid);
        expect(TimeGrid.VARIANTS).toEqual(['primary', 'success', 'warning', 'danger', 'info', 'neutral']);
    });

    it('renders an ARIA grid with row/column headers, counts and a polite live region', () => {
        const grid = mounted();
        const root = grid.element.querySelector('[role="grid"]');
        expect(root.getAttribute('aria-label')).toBe('時段表');
        expect(root.getAttribute('aria-rowcount')).toBe('21');
        expect(root.getAttribute('aria-colcount')).toBe('4');
        expect(root.querySelectorAll('[role="row"]')).toHaveLength(21);
        expect(root.querySelectorAll('[role="columnheader"]')).toHaveLength(4);
        expect(root.querySelectorAll('[role="rowheader"]')).toHaveLength(20);
        expect(root.querySelectorAll('[role="gridcell"]')).toHaveLength(60);
        expect(root.querySelector('[role="columnheader"][aria-colindex="3"]').textContent).toBe('Room B');
        expect(root.querySelector('[role="row"][aria-rowindex="3"] [role="rowheader"]').textContent).toBe('08:30–09:00');
        const live = grid.element.querySelector('[aria-live="polite"]');
        expect(live).not.toBeNull();
        expect(root.contains(live)).toBe(false);
        // 預設不可選取：不宣告 aria-selected / aria-multiselectable
        expect(root.hasAttribute('aria-multiselectable')).toBe(false);
        expect(root.querySelector('[role="gridcell"][aria-selected]')).toBeNull();
    });

    it('uses theme tokens for colors and applies slotHeight, height and ariaLabel options', () => {
        const grid = mounted({ slotHeight: 30, height: 400, ariaLabel: 'Bookings' });
        const root = grid.element.querySelector('[role="grid"]');
        expect(root.getAttribute('aria-label')).toBe('Bookings');
        expect(grid.element.querySelector('.b4a-timegrid__row').style.height).toBe('30px');
        const scroller = grid.element.querySelector('.b4a-timegrid__scroller');
        expect(scroller.style.height).toBe('400px');
        expect(scroller.style.overflow).toBe('auto');
        expect(grid.element.style.cssText).toContain('var(--cl-bg)');
        expect(grid.element.style.cssText).not.toMatch(/#[0-9a-f]{3,8}\b|rgb/i);
    });
});

describe('TimeGrid — slots', () => {
    it('generates slots from the default time range (08:00–18:00, 30 minutes)', () => {
        const grid = create();
        const labels = [...grid.element.querySelectorAll('[role="rowheader"]')].map((el) => el.textContent);
        expect(labels).toHaveLength(20);
        expect(labels[0]).toBe('08:00–08:30');
        expect(labels[19]).toBe('17:30–18:00');
        expect(cellOf(grid, 'roomA', '08:00')).not.toBeNull();
    });

    it('ends with a shorter slot when the range is not a multiple of the step', () => {
        const grid = create({ timeRange: { start: '09:00', end: '10:15', step: 30 } });
        const labels = [...grid.element.querySelectorAll('[role="rowheader"]')].map((el) => el.textContent);
        expect(labels).toEqual(['09:00–09:30', '09:30–10:00', '10:00–10:15']);
    });

    it('accepts explicit slots with irregular lengths, gaps and labels, ordered by start time', () => {
        const grid = create({
            slots: [
                { key: 's2', start: '10:10', end: '11:00' },
                { key: 's1', label: 'First block', start: '08:10', end: '09:00' },
                { key: 's3', start: '13:00', end: '14:30' }
            ],
            items: [
                { id: 'k', column: 'roomA', start: 's1', end: 's3', title: 'Key range' },
                { id: 't', column: 'roomB', start: '10:30', end: '13:30', title: 'Time range' },
                { id: 'one', column: 'roomC', start: 's3', end: 's3', title: 'Same key' }
            ]
        });
        const labels = [...grid.element.querySelectorAll('[role="rowheader"]')].map((el) => el.textContent);
        expect(labels).toEqual(['First block', '10:10–11:00', '13:00–14:30']);
        // key 形式的 end 為不含：s1..s3 → 佔 s1、s2
        expect(itemOf(grid, 'k').parentElement).toBe(cellOf(grid, 'roomA', 's1'));
        expect(itemOf(grid, 'k').style.height).toBe(`${2 * 40 - 3}px`);
        // 時間形式取所有重疊的時段：10:30–13:30 → s2、s3
        expect(itemOf(grid, 't').parentElement).toBe(cellOf(grid, 'roomB', 's2'));
        expect(itemOf(grid, 't').style.height).toBe(`${2 * 40 - 3}px`);
        expect(itemOf(grid, 't').getAttribute('aria-label')).toContain('10:30–13:30');
        // start 與 end 為同一 key → 單一時段
        expect(itemOf(grid, 'one').style.height).toBe('37px');
    });

    it('reports invalid slot definitions once and skips them', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const slots = [
            { key: 'a', start: '08:00', end: '09:00' },
            { key: 'bad', start: '10:00', end: '09:00' },
            { key: 'a', start: '11:00', end: '12:00' }
        ];
        const grid = create({ slots });
        expect(grid.element.querySelectorAll('[role="rowheader"]')).toHaveLength(1);
        expect(warn).toHaveBeenCalledTimes(1);
        expect(warn.mock.calls[0][0]).toContain('[TimeGrid]');
        grid.setSlots(slots);
        expect(warn).toHaveBeenCalledTimes(1);
    });

    it('falls back to the default range when timeRange is invalid', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const grid = create({ timeRange: { start: '18:00', end: '08:00', step: 30 } });
        expect(grid.element.querySelectorAll('[role="rowheader"]')).toHaveLength(20);
        expect(warn).toHaveBeenCalledTimes(1);
    });
});

describe('TimeGrid — item placement', () => {
    it('places an item as one block in its start cell spanning consecutive slots', () => {
        const grid = create({
            items: [{ id: 1, column: 'roomB', start: '09:00', end: '10:30', title: 'Weekly sync', subtitle: 'Floor 3', variant: 'success' }]
        });
        const el = itemOf(grid, 1);
        expect(el.tagName).toBe('BUTTON');
        expect(el.type).toBe('button');
        expect(el.tabIndex).toBe(-1);
        expect(el.parentElement).toBe(cellOf(grid, 'roomB', '09:00'));
        expect(el.style.height).toBe(`${3 * 40 - 3}px`);
        expect(el.dataset.variant).toBe('success');
        expect(el.style.cssText).toContain('var(--cl-success)');
        expect(el.textContent).toContain('Weekly sync');
        expect(el.textContent).toContain('Floor 3');
        expect(el.getAttribute('aria-label')).toBe('Weekly sync（Floor 3），Room B，09:00–10:30');
        expect(grid.element.querySelectorAll('[data-item-id]')).toHaveLength(1);
    });

    it('treats end as exclusive and defaults to a single slot when end is omitted', () => {
        const grid = create({
            items: [
                { id: 'a', column: 'roomA', start: '09:00', title: 'No end' },
                { id: 'b', column: 'roomB', start: '09:00', end: '09:30', title: 'Half hour' },
                { id: 'c', column: 'roomC', start: '09:15', end: '09:45', title: 'Unaligned' }
            ]
        });
        expect(itemOf(grid, 'a').style.height).toBe('37px');
        expect(itemOf(grid, 'b').style.height).toBe('37px');
        // 09:15–09:45 與 09:00、09:30 兩個時段重疊
        expect(itemOf(grid, 'c').parentElement).toBe(cellOf(grid, 'roomC', '09:00'));
        expect(itemOf(grid, 'c').style.height).toBe('77px');
        expect(itemOf(grid, 'c').getAttribute('aria-label')).toContain('09:15–09:45');
    });

    it('treats a zero-length item as one slot and skips items that fall entirely into a gap', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const grid = create({
            slots: [
                { key: 'am', start: '09:00', end: '12:00' },
                { key: 'pm', start: '13:00', end: '17:00' }
            ],
            items: [
                { id: 'point', column: 'roomA', start: '10:00', end: '10:00', title: 'Point' },
                { id: 'padded', column: 'roomB', start: '9:00', end: '09:00', title: 'Padded' },
                { id: 'gap', column: 'roomC', start: '12:15', end: '12:45', title: 'Lunch gap' },
                { id: 'late', column: 'roomC', start: '18:00', end: '19:00', title: 'After hours' },
                { id: 'backwards', column: 'roomC', start: 'pm', end: 'am', title: 'Backwards' }
            ]
        });
        expect(itemOf(grid, 'point').parentElement).toBe(cellOf(grid, 'roomA', 'am'));
        expect(itemOf(grid, 'padded').parentElement).toBe(cellOf(grid, 'roomB', 'am'));
        expect(itemOf(grid, 'gap')).toBeNull();
        expect(itemOf(grid, 'late')).toBeNull();
        expect(itemOf(grid, 'backwards')).toBeNull();
        expect(warn).toHaveBeenCalledTimes(1);
        expect(warn.mock.calls[0][0]).toContain('空檔');
        expect(warn.mock.calls[0][0]).toContain('超出');
    });

    it('mounts by selector and warns when the target does not exist', () => {
        host.id = 'timegrid-host';
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const grid = create();
        expect(grid.mount('#timegrid-host')).toBe(grid);
        expect(host.firstElementChild).toBe(grid.element);
        expect(grid.snapshot().lifecycle).toBe('mounted');
        const other = create();
        expect(other.mount('#does-not-exist')).toBe(other);
        expect(warn).toHaveBeenCalledTimes(1);
    });

    it('clips items that start before the visible range but keeps their real times in the label', () => {
        const grid = create({ items: [{ id: 'x', column: 'roomA', start: '07:00', end: '08:45', title: 'Early' }] });
        expect(itemOf(grid, 'x').parentElement).toBe(cellOf(grid, 'roomA', '08:00'));
        expect(itemOf(grid, 'x').style.height).toBe('77px');
        expect(itemOf(grid, 'x').getAttribute('aria-label')).toContain('07:00–08:45');
    });

    it('lays out overlapping items side by side in lanes computed per overlap cluster', () => {
        const grid = create({
            items: [
                { id: 'A', column: 'roomA', start: '09:00', end: '11:00', title: 'A' },
                { id: 'B', column: 'roomA', start: '09:30', end: '10:00', title: 'B' },
                { id: 'C', column: 'roomA', start: '10:00', end: '10:30', title: 'C' },
                { id: 'D', column: 'roomA', start: '13:00', end: '14:00', title: 'D' },
                { id: 'E', column: 'roomB', start: '09:00', end: '10:00', title: 'E' },
                { id: 'F', column: 'roomB', start: '09:00', end: '10:00', title: 'F' },
                { id: 'G', column: 'roomB', start: '09:30', end: '10:30', title: 'G' }
            ]
        });
        const lane = (id) => ({ left: pct(itemOf(grid, id).style.left), width: pct(itemOf(grid, id).style.width) });
        expect(lane('A')).toEqual({ left: 0, width: 50 });
        expect(lane('B')).toEqual({ left: 50, width: 50 });
        // B 結束後 C 重用第二道
        expect(lane('C')).toEqual({ left: 50, width: 50 });
        // D 自成一個群組，佔滿整欄
        expect(lane('D')).toEqual({ left: 0, width: 100 });
        // 三個互相重疊 → 三道
        expect(lane('E').width).toBeCloseTo(33.3333, 3);
        expect(lane('F').left).toBeCloseTo(33.3333, 3);
        expect(lane('G').left).toBeCloseTo(66.6667, 3);
        expect(grid.snapshot().lifecycle).toBe('created');
    });

    it('renders ghost items as a translucent dashed preview that does not squeeze solid items', () => {
        const grid = create({
            items: [
                { id: 'solid', column: 'roomA', start: '09:00', end: '10:00', title: 'Existing' },
                { id: 'ghost', column: 'roomA', start: '09:30', end: '10:30', title: 'Proposed', ghost: true }
            ]
        });
        const solid = itemOf(grid, 'solid');
        const ghost = itemOf(grid, 'ghost');
        expect(pct(solid.style.width)).toBe(100);
        expect(pct(ghost.style.width)).toBe(100);
        expect(ghost.classList.contains('b4a-timegrid__item--ghost')).toBe(true);
        expect(ghost.dataset.ghost).toBe('true');
        expect(ghost.style.opacity).toBe('0.7');
        expect(ghost.style.cssText).toContain('dashed');
        expect(Number(ghost.style.zIndex)).toBeGreaterThan(Number(solid.style.zIndex));
        expect(ghost.getAttribute('aria-label')).toBe('Proposed，Room A，09:30–10:30（預覽）');
        expect(solid.dataset.ghost).toBeUndefined();
    });

    it('warns once about unknown columns and slots, skips those items but keeps them in getItems()', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const items = [
            { id: 'ok', column: 'roomA', start: '09:00', end: '10:00', title: 'Fine' },
            { id: 'n1', column: 'nowhere', start: '09:00', title: 'Lost 1' },
            { id: 'n2', column: 'nowhere', start: '10:00', title: 'Lost 2' },
            { id: 's1', column: 'roomB', start: '25:00', title: 'Bad time' },
            { id: 's2', column: 'roomB', start: 'lunch', title: 'Bad key' }
        ];
        const grid = create({ items });
        expect(warn).toHaveBeenCalledTimes(1);
        const message = warn.mock.calls[0][0];
        expect(message).toContain('nowhere');
        expect(message).toContain('25:00');
        expect(message).toContain('lunch');
        expect(message).toContain('"n1"');
        expect(grid.element.querySelectorAll('[data-item-id]')).toHaveLength(1);
        expect(grid.getItems().map((item) => item.id)).toEqual(['ok', 'n1', 'n2', 's1', 's2']);

        grid.setColumns(COLUMNS);
        grid.setItems(items);
        expect(warn).toHaveBeenCalledTimes(1);

        grid.addItem({ id: 'n3', column: 'elsewhere', start: '09:00' });
        expect(warn).toHaveBeenCalledTimes(2);
    });
});

describe('TimeGrid — pointer selection', () => {
    it('selects a slot range by dragging across empty cells in one column', () => {
        mockGeometry();
        const onSelect = vi.fn();
        const grid = mounted({ selectable: true, onSelect });
        const start = cellOf(grid, 'roomB', '09:00');
        pointer(start, 'pointerdown', at(1, ROW('09:00')));
        pointer(document, 'pointermove', at(1, ROW('09:30')));
        pointer(document, 'pointermove', at(1, ROW('10:00')));
        expect(cellOf(grid, 'roomB', '10:00').getAttribute('aria-selected')).toBe('true');
        expect(grid.getSelection().confirmed).toBe(false);
        pointer(document, 'pointerup', at(1, ROW('10:00')));

        expect(onSelect).toHaveBeenCalledTimes(1);
        expect(onSelect).toHaveBeenCalledWith({
            column: 'roomB',
            start: '09:00',
            end: '10:30',
            startTime: '09:00',
            endTime: '10:30',
            slots: ['09:00', '09:30', '10:00']
        });
        const selected = [...grid.element.querySelectorAll('[aria-selected="true"]')].map((el) => el.dataset.slot);
        expect(selected).toEqual(['09:00', '09:30', '10:00']);
        expect(cellOf(grid, 'roomB', '09:00').style.background).toBe('var(--cl-bg-active)');
        expect(grid.getSelection()).toMatchObject({ column: 'roomB', start: '09:00', end: '10:30', confirmed: true });

        grid.clearSelection();
        expect(grid.getSelection()).toBeNull();
        expect(grid.element.querySelector('[aria-selected="true"]')).toBeNull();
        expect(grid.element.querySelector('[role="grid"]').getAttribute('aria-multiselectable')).toBe('true');
    });

    it('keeps a drag selection in its column and stops at occupied cells', () => {
        mockGeometry();
        const onSelect = vi.fn();
        const grid = mounted({
            selectable: true,
            onSelect,
            items: [{ id: 'busy', column: 'roomB', start: '10:00', end: '11:00', title: 'Busy' }]
        });
        pointer(cellOf(grid, 'roomB', '09:00'), 'pointerdown', at(1, ROW('09:00')));
        pointer(document, 'pointermove', at(2, ROW('11:30')));
        pointer(document, 'pointerup', at(2, ROW('11:30')));
        expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ column: 'roomB', start: '09:00', end: '10:00', slots: ['09:00', '09:30'] }));
    });

    it('ignores ghost items when checking occupancy', () => {
        mockGeometry();
        const onSelect = vi.fn();
        const grid = mounted({
            selectable: true,
            onSelect,
            items: [{ id: 'g', column: 'roomA', start: '09:30', end: '10:00', title: 'Preview', ghost: true }]
        });
        pointer(cellOf(grid, 'roomA', '09:00'), 'pointerdown', at(0, ROW('09:00')));
        pointer(document, 'pointermove', at(0, ROW('10:00')));
        pointer(document, 'pointerup', at(0, ROW('10:00')));
        expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ start: '09:00', end: '10:30' }));
    });

    it('a click selects one slot and reports the cell through onCellClick', () => {
        mockGeometry();
        const onSelect = vi.fn();
        const onCellClick = vi.fn();
        const grid = mounted({ selectable: true, onSelect, onCellClick });
        const target = cellOf(grid, 'roomC', '17:30');
        pointer(target, 'pointerdown', at(2, ROW('17:30')));
        pointer(document, 'pointerup', at(2, ROW('17:30')));
        target.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        // 最後一個時段的結束邊界沒有下一個 key，以時間表示
        expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ column: 'roomC', start: '17:30', end: '18:00', slots: ['17:30'] }));
        expect(onCellClick).toHaveBeenCalledTimes(1);
        expect(onCellClick.mock.calls[0][0]).toEqual({ column: 'roomC', slot: '17:30', startTime: '17:30', endTime: '18:00' });
        expect(onCellClick.mock.calls[0][1]).toBeInstanceOf(MouseEvent);
    });

    it('a selection payload can be passed straight to addItem', () => {
        mockGeometry();
        let range = null;
        const grid = mounted({ selectable: true, onSelect: (value) => { range = value; } });
        pointer(cellOf(grid, 'roomA', '16:00'), 'pointerdown', at(0, ROW('16:00')));
        pointer(document, 'pointerup', at(0, ROW('17:30')));
        grid.addItem({ id: 'new', title: 'New booking', column: range.column, start: range.start, end: range.end });
        expect(itemOf(grid, 'new').parentElement).toBe(cellOf(grid, 'roomA', '16:00'));
        expect(itemOf(grid, 'new').style.height).toBe(`${4 * 40 - 3}px`);
    });

    it('does not start a selection on an occupied cell, and Escape cancels a drag selection', () => {
        mockGeometry();
        const onSelect = vi.fn();
        const grid = mounted({
            selectable: true,
            onSelect,
            items: [{ id: 'busy', column: 'roomA', start: '09:00', end: '10:00', title: 'Busy' }]
        });
        pointer(cellOf(grid, 'roomA', '09:30'), 'pointerdown', at(0, ROW('09:30')));
        expect(grid.getSelection()).toBeNull();

        pointer(cellOf(grid, 'roomB', '09:00'), 'pointerdown', at(1, ROW('09:00')));
        expect(grid.getSelection()).not.toBeNull();
        keydown('Escape', {}, document);
        expect(grid.getSelection()).toBeNull();
        pointer(document, 'pointerup', at(1, ROW('09:00')));
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('attaches document listeners only while a pointer interaction is active', () => {
        mockGeometry();
        const add = vi.spyOn(document, 'addEventListener');
        const remove = vi.spyOn(document, 'removeEventListener');
        const grid = mounted({ selectable: true });
        const net = (type) => add.mock.calls.filter(([t]) => t === type).length - remove.mock.calls.filter(([t]) => t === type).length;
        expect(net('pointermove')).toBe(0);
        pointer(cellOf(grid, 'roomA', '09:00'), 'pointerdown', at(0, ROW('09:00')));
        expect(net('pointermove')).toBe(1);
        expect(net('pointerup')).toBe(1);
        expect(net('keydown')).toBe(1);
        pointer(document, 'pointerup', at(0, ROW('09:00')));
        expect(net('pointermove')).toBe(0);
        expect(net('pointerup')).toBe(0);
        expect(net('pointercancel')).toBe(0);
        expect(net('keydown')).toBe(0);
    });
});

describe('TimeGrid — keyboard navigation and selection', () => {
    it('uses a roving tabindex and moves with arrows, Home/End, PageUp/PageDown and Ctrl+Home/End', () => {
        const grid = mounted();
        const tabbable = () => [...grid.element.querySelectorAll('[tabindex="0"]')];
        expect(tabbable()).toEqual([cellOf(grid, 'roomA', '08:00')]);

        grid.focusCell('roomA', '08:00');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '08:00'));
        keydown('ArrowUp');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '08:00'));
        keydown('ArrowRight');
        expect(document.activeElement).toBe(cellOf(grid, 'roomB', '08:00'));
        keydown('ArrowDown');
        expect(document.activeElement).toBe(cellOf(grid, 'roomB', '08:30'));
        expect(tabbable()).toEqual([cellOf(grid, 'roomB', '08:30')]);
        keydown('End');
        expect(document.activeElement).toBe(cellOf(grid, 'roomC', '08:30'));
        keydown('Home');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '08:30'));
        keydown('PageDown');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '11:00'));
        keydown('PageUp');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '08:30'));
        keydown('End', { ctrlKey: true });
        expect(document.activeElement).toBe(cellOf(grid, 'roomC', '17:30'));
        keydown('ArrowRight');
        expect(document.activeElement).toBe(cellOf(grid, 'roomC', '17:30'));
        keydown('Home', { ctrlKey: true });
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '08:00'));
        expect(tabbable()).toHaveLength(1);
    });

    it('draws a visible focus ring inside the focused cell', () => {
        const grid = mounted();
        grid.focusCell('roomB', '09:00');
        const ring = cellOf(grid, 'roomB', '09:00').querySelector('.b4a-timegrid__focus');
        expect(ring).not.toBeNull();
        expect(ring.style.cssText).toContain('var(--cl-border-focus)');
        keydown('ArrowDown');
        expect(cellOf(grid, 'roomB', '09:00').querySelector('.b4a-timegrid__focus')).toBeNull();
        expect(cellOf(grid, 'roomB', '09:30').querySelector('.b4a-timegrid__focus')).not.toBeNull();
    });

    it('Enter on an empty cell fires onCellClick; Shift+arrows extend and Enter confirms the selection', () => {
        const onCellClick = vi.fn();
        const onSelect = vi.fn();
        const grid = mounted({ selectable: true, onCellClick, onSelect });
        grid.focusCell('roomA', '09:00');
        keydown('Enter');
        expect(onCellClick).toHaveBeenCalledTimes(1);
        expect(onCellClick.mock.calls[0][0]).toMatchObject({ column: 'roomA', slot: '09:00' });
        expect(grid.getSelection()).toMatchObject({ start: '09:00', end: '09:30', confirmed: false });
        expect(onSelect).not.toHaveBeenCalled();

        keydown('ArrowDown', { shiftKey: true });
        keydown('ArrowDown', { shiftKey: true });
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '10:00'));
        expect(grid.getSelection()).toMatchObject({ start: '09:00', end: '10:30', confirmed: false });
        keydown('ArrowUp', { shiftKey: true });
        expect(grid.getSelection()).toMatchObject({ start: '09:00', end: '10:00' });
        keydown('ArrowDown', { shiftKey: true });

        keydown('Enter');
        expect(onSelect).toHaveBeenCalledTimes(1);
        expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ column: 'roomA', start: '09:00', end: '10:30' }));
        expect(onCellClick).toHaveBeenCalledTimes(1);
        expect(grid.getSelection().confirmed).toBe(true);
        expect(grid.element.querySelector('[aria-live="polite"]').textContent).toContain('Room A');
    });

    it('Shift+arrows start a selection from the focused cell and stop at occupied cells', () => {
        const onSelect = vi.fn();
        const grid = mounted({
            selectable: true,
            onSelect,
            items: [{ id: 'busy', column: 'roomB', start: '10:00', end: '11:00', title: 'Busy' }]
        });
        grid.focusCell('roomB', '09:00');
        keydown('PageDown', { shiftKey: true });
        expect(grid.getSelection()).toMatchObject({ column: 'roomB', start: '09:00', end: '10:00' });
        expect(document.activeElement).toBe(cellOf(grid, 'roomB', '09:30'));
        keydown('Enter');
        expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ start: '09:00', end: '10:00' }));
    });

    it('Escape cancels a pending keyboard selection and plain navigation abandons it', () => {
        const onSelect = vi.fn();
        const grid = mounted({ selectable: true, onSelect });
        grid.focusCell('roomA', '09:00');
        keydown('Enter');
        keydown('ArrowDown', { shiftKey: true });
        keydown('Escape');
        expect(grid.getSelection()).toBeNull();
        expect(grid.element.querySelector('[aria-live="polite"]').textContent).not.toBe('');

        keydown('Enter');
        expect(grid.getSelection()).not.toBeNull();
        keydown('ArrowRight');
        expect(grid.getSelection()).toBeNull();
        expect(onSelect).not.toHaveBeenCalled();
    });

    it('Enter on a covered cell focuses its items; arrows switch lanes; Enter clicks; Escape returns to the cell', () => {
        const onItemClick = vi.fn();
        const grid = mounted({
            onItemClick,
            items: [
                { id: 'L', column: 'roomA', start: '09:00', end: '10:00', title: 'Left' },
                { id: 'R', column: 'roomA', start: '09:30', end: '10:30', title: 'Right' }
            ]
        });
        // 09:30 同時被兩個項目覆蓋（L 為延續格）
        grid.focusCell('roomA', '09:30');
        keydown('Enter');
        expect(document.activeElement).toBe(itemOf(grid, 'L'));
        expect(itemOf(grid, 'L').style.outline).toContain('solid');
        keydown('ArrowRight');
        expect(document.activeElement).toBe(itemOf(grid, 'R'));
        keydown('Enter');
        expect(onItemClick).toHaveBeenCalledTimes(1);
        expect(onItemClick.mock.calls[0][0]).toMatchObject({ id: 'R', title: 'Right' });
        keydown('Escape');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '09:30'));

        keydown('Enter');
        keydown('ArrowDown');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '10:00'));
    });

    it('fires onItemClick on pointer click without selecting', () => {
        const onItemClick = vi.fn();
        const onCellClick = vi.fn();
        const grid = mounted({
            selectable: true,
            onItemClick,
            onCellClick,
            items: [{ id: 7, column: 'roomC', start: '12:00', end: '13:00', title: 'Lunch talk', data: { seats: 8 } }]
        });
        itemOf(grid, 7).click();
        expect(onItemClick).toHaveBeenCalledTimes(1);
        expect(onItemClick.mock.calls[0][0]).toEqual({ id: 7, column: 'roomC', start: '12:00', end: '13:00', title: 'Lunch talk', data: { seats: 8 } });
        expect(onCellClick).not.toHaveBeenCalled();
    });
});

describe('TimeGrid — keyboard moving', () => {
    const baseItems = () => [{ id: 'm1', column: 'roomA', start: '09:00', end: '10:00', title: 'Planning' }];

    function grab(grid, id) {
        itemOf(grid, id).focus();
        keydown(' ');
    }

    it('Space picks an item up, arrows move the drop target and Enter drops it (accepted)', () => {
        const onItemMove = vi.fn(() => true);
        const grid = mounted({ editable: true, onItemMove, items: baseItems() });
        expect(itemOf(grid, 'm1').getAttribute('aria-grabbed')).toBe('false');
        grab(grid, 'm1');
        expect(grid.snapshot().mode).toBe('grabbed');
        expect(itemOf(grid, 'm1').getAttribute('aria-grabbed')).toBe('true');
        expect(grid.element.querySelector('[aria-live="polite"]').textContent).toContain('Planning');

        keydown('ArrowRight');
        keydown('ArrowDown');
        keydown('ArrowDown');
        const drop = grid.element.querySelector('.b4a-timegrid__drop');
        expect(drop.parentElement).toBe(cellOf(grid, 'roomB', '10:00'));
        expect(drop.getAttribute('aria-hidden')).toBe('true');
        expect(onItemMove).not.toHaveBeenCalled();

        keydown('Enter');
        expect(onItemMove).toHaveBeenCalledTimes(1);
        const payload = onItemMove.mock.calls[0][0];
        expect(payload.item).toMatchObject({ id: 'm1', column: 'roomA', start: '09:00', end: '10:00' });
        expect(payload.from).toEqual({ column: 'roomA', start: '09:00', end: '10:00', startTime: '09:00', endTime: '10:00', slots: ['09:00', '09:30'] });
        expect(payload.to).toEqual({ column: 'roomB', start: '10:00', end: '11:00', startTime: '10:00', endTime: '11:00', slots: ['10:00', '10:30'] });

        expect(grid.getItems()[0]).toMatchObject({ column: 'roomB', start: '10:00', end: '11:00' });
        const moved = itemOf(grid, 'm1');
        expect(moved.parentElement).toBe(cellOf(grid, 'roomB', '10:00'));
        expect(document.activeElement).toBe(moved);
        expect(moved.getAttribute('aria-grabbed')).toBe('false');
        expect(grid.snapshot().mode).toBe('idle');
        expect(grid.element.querySelector('.b4a-timegrid__drop')).toBeNull();
    });

    it('keeps the original times format and shifts unaligned times when possible', () => {
        const grid = mounted({
            editable: true,
            items: [
                { id: 'u', column: 'roomA', start: '09:15', end: '09:45', title: 'Unaligned' },
                { id: 'k', column: 'roomB', start: '09:00', title: 'Single' }
            ]
        });
        grab(grid, 'u');
        keydown('ArrowDown');
        keydown('Enter');
        expect(grid.getItems().find((i) => i.id === 'u')).toMatchObject({ column: 'roomA', start: '09:45', end: '10:15' });

        grab(grid, 'k');
        keydown('ArrowLeft');
        keydown('Enter');
        const single = grid.getItems().find((i) => i.id === 'k');
        expect(single).toMatchObject({ column: 'roomA', start: '09:00' });
        expect(single.end).toBeUndefined();
    });

    it('snaps back when onItemMove returns false', () => {
        const onItemMove = vi.fn(() => false);
        const grid = mounted({ editable: true, onItemMove, items: baseItems() });
        grab(grid, 'm1');
        keydown('ArrowDown');
        keydown('Enter');
        expect(onItemMove).toHaveBeenCalledTimes(1);
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomA', start: '09:00', end: '10:00' });
        expect(itemOf(grid, 'm1').parentElement).toBe(cellOf(grid, 'roomA', '09:00'));
        expect(document.activeElement).toBe(itemOf(grid, 'm1'));
        expect(grid.snapshot().mode).toBe('idle');
    });

    it('waits for an async onItemMove and applies the move only after it resolves true', async () => {
        let resolveMove;
        const onItemMove = vi.fn(() => new Promise((resolve) => { resolveMove = resolve; }));
        const grid = mounted({ editable: true, onItemMove, items: baseItems() });
        grab(grid, 'm1');
        keydown('ArrowRight');
        keydown('Enter');
        const root = grid.element.querySelector('[role="grid"]');
        expect(grid.snapshot().mode).toBe('pending');
        expect(root.getAttribute('aria-busy')).toBe('true');
        expect(itemOf(grid, 'm1').parentElement).toBe(cellOf(grid, 'roomA', '09:00'));
        // 等待期間不可再拿起
        keydown(' ');
        expect(grid.snapshot().mode).toBe('pending');

        resolveMove(true);
        await flush();
        expect(root.hasAttribute('aria-busy')).toBe(false);
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomB', start: '09:00', end: '10:00' });
        expect(itemOf(grid, 'm1').parentElement).toBe(cellOf(grid, 'roomB', '09:00'));
    });

    it('snaps back when an async onItemMove resolves false or rejects', async () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        let answer = Promise.resolve(false);
        const grid = mounted({ editable: true, onItemMove: () => answer, items: baseItems() });
        grab(grid, 'm1');
        keydown('ArrowDown');
        keydown('Enter');
        await flush();
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomA', start: '09:00' });
        expect(grid.snapshot().mode).toBe('idle');
        expect(grid.element.querySelector('[aria-live="polite"]').textContent).toContain('Planning');

        answer = Promise.reject(new Error('conflict'));
        grab(grid, 'm1');
        keydown('ArrowDown');
        keydown('Enter');
        await flush();
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomA', start: '09:00' });
        expect(warn).toHaveBeenCalled();
    });

    it('keeps the grid navigable while a move is pending and does not steal focus back', async () => {
        let resolveMove;
        const grid = mounted({ editable: true, onItemMove: () => new Promise((resolve) => { resolveMove = resolve; }), items: baseItems() });
        grab(grid, 'm1');
        keydown('ArrowRight');
        keydown('Enter');
        expect(grid.snapshot().mode).toBe('pending');
        // 從項目按 ↓：以進入時的格子（roomA 09:00）為起點往下
        keydown('ArrowDown');
        expect(document.activeElement).toBe(cellOf(grid, 'roomA', '09:30'));
        keydown('ArrowRight');
        expect(document.activeElement).toBe(cellOf(grid, 'roomB', '09:30'));
        resolveMove(true);
        await flush();
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomB', start: '09:00' });
        expect(document.activeElement).toBe(cellOf(grid, 'roomB', '09:30'));
    });

    it('does not apply an accepted move when the caller already replaced the data meanwhile', async () => {
        let resolveMove;
        const grid = mounted({ editable: true, onItemMove: () => new Promise((resolve) => { resolveMove = resolve; }), items: baseItems() });
        grab(grid, 'm1');
        keydown('ArrowRight');
        keydown('Enter');
        grid.setItems([{ id: 'm1', column: 'roomC', start: '15:00', end: '16:00', title: 'Planning' }]);
        resolveMove(true);
        await flush();
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomC', start: '15:00' });
        expect(itemOf(grid, 'm1').parentElement).toBe(cellOf(grid, 'roomC', '15:00'));
    });

    it('Escape cancels a keyboard move without calling onItemMove', () => {
        const onItemMove = vi.fn(() => true);
        const grid = mounted({ editable: true, onItemMove, items: baseItems() });
        grab(grid, 'm1');
        keydown('ArrowDown');
        keydown('Escape');
        expect(onItemMove).not.toHaveBeenCalled();
        expect(grid.snapshot().mode).toBe('idle');
        expect(itemOf(grid, 'm1').getAttribute('aria-grabbed')).toBe('false');
        expect(grid.element.querySelector('.b4a-timegrid__drop')).toBeNull();
    });

    it('clamps the drop target to the grid and treats dropping in place as a cancel', () => {
        const onItemMove = vi.fn(() => true);
        const grid = mounted({ editable: true, onItemMove, items: [{ id: 'end', column: 'roomC', start: '17:00', end: '18:00', title: 'Late' }] });
        grab(grid, 'end');
        keydown('ArrowDown');
        keydown('ArrowRight');
        keydown('Enter');
        expect(onItemMove).not.toHaveBeenCalled();
        expect(grid.snapshot().mode).toBe('idle');
    });

    it('does not pick up items when editing is off or the item is not draggable', () => {
        const onItemMove = vi.fn(() => true);
        const grid = mounted({
            editable: true,
            onItemMove,
            items: [
                ...baseItems(),
                { id: 'fixed', column: 'roomB', start: '09:00', end: '10:00', title: 'Fixed', draggable: false }
            ]
        });
        expect(itemOf(grid, 'fixed').hasAttribute('aria-grabbed')).toBe(false);
        grab(grid, 'fixed');
        expect(grid.snapshot().mode).toBe('idle');

        const plain = mounted({ items: baseItems() });
        grab(plain, 'm1');
        expect(plain.snapshot().mode).toBe('idle');
        expect(itemOf(plain, 'm1').hasAttribute('aria-grabbed')).toBe(false);
    });
});

describe('TimeGrid — keyboard moving details', () => {
    const items = () => [
        { id: 'mv', column: 'roomA', start: '09:15', end: '09:45', title: 'Unaligned' },
        { id: 'fixed', column: 'roomB', start: '09:00', end: '10:00', title: 'Fixed', draggable: false }
    ];

    it('keeps native Space activation on items that cannot move and blocks it on movable ones', () => {
        const grid = mounted({ editable: true, items: items() });
        itemOf(grid, 'fixed').focus();
        expect(keydown(' ').defaultPrevented).toBe(false);
        const upFixed = new KeyboardEvent('keyup', { key: ' ', bubbles: true, cancelable: true });
        itemOf(grid, 'fixed').dispatchEvent(upFixed);
        expect(upFixed.defaultPrevented).toBe(false);

        itemOf(grid, 'mv').focus();
        expect(keydown(' ').defaultPrevented).toBe(true);
        const upMovable = new KeyboardEvent('keyup', { key: ' ', bubbles: true, cancelable: true });
        itemOf(grid, 'mv').dispatchEvent(upMovable);
        expect(upMovable.defaultPrevented).toBe(true);
        expect(grid.snapshot().mode).toBe('grabbed');
    });

    it('throttles announcements and reports the real new times after an accepted move', () => {
        vi.useFakeTimers();
        const grid = mounted({ editable: true, items: items() });
        const live = grid.element.querySelector('[aria-live="polite"]');
        itemOf(grid, 'mv').focus();
        keydown(' ');
        expect(live.textContent).toContain('已拿起 Unaligned');
        keydown('ArrowDown');
        keydown('ArrowDown');
        keydown('ArrowRight');
        // 250ms 內的連續變更只保留最後一則
        expect(live.textContent).toContain('已拿起 Unaligned');
        vi.advanceTimersByTime(250);
        expect(live.textContent).toBe('移到 Room B 10:00–11:00');
        keydown('Enter');
        vi.advanceTimersByTime(250);
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomB', start: '10:15', end: '10:45' });
        expect(live.textContent).toBe('已將 Unaligned 移到 Room B 10:15–10:45');
    });

    it('cancels a keyboard move on Tab, when focus leaves the grid, or on a pointer press', () => {
        const outside = document.createElement('button');
        host.appendChild(outside);
        const grid = mounted({ editable: true, items: items() });
        itemOf(grid, 'mv').focus();
        keydown(' ');
        keydown('Tab');
        expect(grid.snapshot().mode).toBe('idle');

        itemOf(grid, 'mv').focus();
        keydown(' ');
        outside.focus();
        expect(grid.snapshot().mode).toBe('idle');
        expect(itemOf(grid, 'mv').getAttribute('aria-grabbed')).toBe('false');

        itemOf(grid, 'mv').focus();
        keydown(' ');
        pointer(cellOf(grid, 'roomC', '12:00'), 'pointerdown');
        expect(grid.snapshot().mode).toBe('idle');
        expect(grid.element.querySelector('.b4a-timegrid__drop')).toBeNull();
    });

    it('scrolls the keyboard drop target and the focused cell into view', () => {
        vi.spyOn(HTMLElement.prototype, 'offsetLeft', 'get').mockImplementation(function left() {
            return this.dataset && this.dataset.col !== undefined ? 72 + Number(this.dataset.col) * 120 : 0;
        });
        vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockImplementation(function width() {
            return this.dataset && this.dataset.col !== undefined ? 120 : 0;
        });
        vi.spyOn(Element.prototype, 'clientWidth', 'get').mockImplementation(function width() {
            return this.classList && this.classList.contains('b4a-timegrid__scroller') ? 300 : 0;
        });
        vi.spyOn(Element.prototype, 'clientHeight', 'get').mockImplementation(function height() {
            return this.classList && this.classList.contains('b4a-timegrid__scroller') ? 200 : 0;
        });
        const grid = mounted({ editable: true, height: 200, items: items() });
        const scroller = grid.element.querySelector('.b4a-timegrid__scroller');
        itemOf(grid, 'mv').focus();
        keydown(' ');
        keydown('End');
        // 第 3 欄右緣 72 + 3×120 = 432，可視寬 300 → scrollLeft 132
        expect(scroller.scrollLeft).toBe(132);
        keydown('PageDown');
        keydown('PageDown');
        // 落點列 2 + 10 = 12 → 底緣 13×40 = 520，可視高 200 → scrollTop 320
        expect(scroller.scrollTop).toBe(320);
        keydown('Escape');

        grid.focusCell('roomA', '08:00');
        expect(scroller.scrollLeft).toBe(0);
        expect(scroller.scrollTop).toBe(0);
    });
});

describe('TimeGrid — pointer drag and drop', () => {
    it('drags an item to another column and slot keeping the grab offset (accepted)', async () => {
        mockGeometry();
        const onItemMove = vi.fn(() => true);
        const onItemClick = vi.fn();
        const grid = mounted({ editable: true, onItemMove, onItemClick, items: [{ id: 'd', column: 'roomA', start: '09:00', end: '10:00', title: 'Drag me' }] });
        const el = itemOf(grid, 'd');
        // 按在項目的第二個時段（偏移 1）
        pointer(el, 'pointerdown', at(0, ROW('09:30')));
        pointer(document, 'pointermove', at(0, ROW('09:30')));
        expect(grid.snapshot().mode).toBe('idle');
        pointer(document, 'pointermove', at(2, ROW('11:00')));
        expect(grid.snapshot().mode).toBe('dragging');
        expect(el.style.opacity).toBe('0.4');
        expect(grid.element.querySelector('.b4a-timegrid__drop').parentElement).toBe(cellOf(grid, 'roomC', '10:30'));
        pointer(document, 'pointerup', at(2, ROW('11:00')));

        expect(onItemMove).toHaveBeenCalledTimes(1);
        expect(onItemMove.mock.calls[0][0].to).toMatchObject({ column: 'roomC', start: '10:30', end: '11:30' });
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomC', start: '10:30', end: '11:30' });
        expect(itemOf(grid, 'd').parentElement).toBe(cellOf(grid, 'roomC', '10:30'));

        // 拖放後瀏覽器送出的 click 被吃掉，不觸發 onItemClick
        itemOf(grid, 'd').dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(onItemClick).not.toHaveBeenCalled();
        await flush();
        itemOf(grid, 'd').dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(onItemClick).toHaveBeenCalledTimes(1);
    });

    it('snaps back when a pointer drop is rejected', () => {
        mockGeometry();
        const onItemMove = vi.fn(() => false);
        const grid = mounted({ editable: true, onItemMove, items: [{ id: 'd', column: 'roomA', start: '09:00', end: '10:00', title: 'Drag me' }] });
        pointer(itemOf(grid, 'd'), 'pointerdown', at(0, ROW('09:00')));
        pointer(document, 'pointermove', at(1, ROW('12:00')));
        pointer(document, 'pointerup', at(1, ROW('12:00')));
        expect(onItemMove).toHaveBeenCalledTimes(1);
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomA', start: '09:00', end: '10:00' });
        expect(itemOf(grid, 'd').parentElement).toBe(cellOf(grid, 'roomA', '09:00'));
        expect(itemOf(grid, 'd').style.opacity).toBe('1');
        expect(grid.element.querySelector('.b4a-timegrid__drop')).toBeNull();
    });

    it('treats a press without movement as a click and Escape cancels an active drag', () => {
        mockGeometry();
        const onItemMove = vi.fn(() => true);
        const onItemClick = vi.fn();
        const grid = mounted({ editable: true, onItemMove, onItemClick, items: [{ id: 'd', column: 'roomA', start: '09:00', end: '10:00', title: 'Drag me' }] });
        const el = itemOf(grid, 'd');
        pointer(el, 'pointerdown', at(0, ROW('09:00')));
        pointer(document, 'pointerup', at(0, ROW('09:00')));
        el.dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(onItemClick).toHaveBeenCalledTimes(1);

        pointer(el, 'pointerdown', at(0, ROW('09:00')));
        pointer(document, 'pointermove', at(1, ROW('13:00')));
        expect(grid.snapshot().mode).toBe('dragging');
        keydown('Escape', {}, document);
        expect(grid.snapshot().mode).toBe('idle');
        pointer(document, 'pointerup', at(1, ROW('13:00')));
        expect(onItemMove).not.toHaveBeenCalled();
        expect(grid.getItems()[0]).toMatchObject({ column: 'roomA', start: '09:00' });
    });

    it('pointercancel aborts a drag', () => {
        mockGeometry();
        const onItemMove = vi.fn(() => true);
        const grid = mounted({ editable: true, onItemMove, items: [{ id: 'd', column: 'roomA', start: '09:00', end: '10:00', title: 'Drag me' }] });
        pointer(itemOf(grid, 'd'), 'pointerdown', at(0, ROW('09:00')));
        pointer(document, 'pointermove', at(1, ROW('13:00')));
        pointer(document, 'pointercancel', at(1, ROW('13:00')));
        expect(grid.snapshot().mode).toBe('idle');
        expect(onItemMove).not.toHaveBeenCalled();
    });
});

describe('TimeGrid — readonly', () => {
    it('disables selection and moving but still reports clicks', () => {
        mockGeometry();
        const onSelect = vi.fn();
        const onItemMove = vi.fn(() => true);
        const onItemClick = vi.fn();
        const onCellClick = vi.fn();
        const grid = mounted({
            readonly: true,
            selectable: true,
            editable: true,
            onSelect,
            onItemMove,
            onItemClick,
            onCellClick,
            items: [{ id: 'r', column: 'roomA', start: '09:00', end: '10:00', title: 'Read only' }]
        });
        const root = grid.element.querySelector('[role="grid"]');
        expect(root.getAttribute('aria-readonly')).toBe('true');
        expect(root.hasAttribute('aria-multiselectable')).toBe(false);
        expect(itemOf(grid, 'r').hasAttribute('aria-grabbed')).toBe(false);

        pointer(cellOf(grid, 'roomB', '09:00'), 'pointerdown', at(1, ROW('09:00')));
        pointer(document, 'pointerup', at(1, ROW('10:00')));
        expect(grid.getSelection()).toBeNull();

        grid.focusCell('roomB', '11:00');
        keydown('Enter');
        expect(onCellClick).toHaveBeenCalledTimes(1);
        expect(grid.getSelection()).toBeNull();

        itemOf(grid, 'r').focus();
        keydown(' ');
        expect(grid.snapshot().mode).toBe('idle');
        pointer(itemOf(grid, 'r'), 'pointerdown', at(0, ROW('09:00')));
        pointer(document, 'pointermove', at(2, ROW('14:00')));
        pointer(document, 'pointerup', at(2, ROW('14:00')));
        expect(onItemMove).not.toHaveBeenCalled();
        expect(onSelect).not.toHaveBeenCalled();

        itemOf(grid, 'r').click();
        expect(onItemClick).toHaveBeenCalledTimes(1);
    });
});

describe('TimeGrid — now indicator', () => {
    it("draws the current-time line in today's column and moves it every minute", () => {
        vi.useFakeTimers();
        let current = new Date(2026, 8, 28, 9, 15);
        const grid = mounted({
            nowIndicator: true,
            now: () => current,
            columns: [
                { key: 'mon', label: 'Mon', sublabel: '09/28', date: '2026-09-28' },
                { key: 'tue', label: 'Tue', sublabel: '09/29', date: '2026-09-29' }
            ]
        });
        let lines = grid.element.querySelectorAll('.b4a-timegrid__now');
        expect(lines).toHaveLength(1);
        expect(lines[0].parentElement).toBe(cellOf(grid, 'mon', '09:00'));
        expect(pct(lines[0].style.top)).toBe(50);
        expect(lines[0].getAttribute('aria-hidden')).toBe('true');
        const headers = grid.element.querySelectorAll('.b4a-timegrid__colheader');
        expect(headers[0].getAttribute('aria-current')).toBe('date');
        expect(headers[1].hasAttribute('aria-current')).toBe(false);
        expect(headers[0].textContent).toContain('09/28');

        current = new Date(2026, 8, 28, 9, 45);
        vi.advanceTimersByTime(59000);
        expect(grid.element.querySelector('.b4a-timegrid__now').parentElement).toBe(cellOf(grid, 'mon', '09:00'));
        vi.advanceTimersByTime(1000);
        lines = grid.element.querySelectorAll('.b4a-timegrid__now');
        expect(lines).toHaveLength(1);
        expect(lines[0].parentElement).toBe(cellOf(grid, 'mon', '09:30'));

        // 跨日：線移到隔天的欄
        current = new Date(2026, 8, 29, 8, 0);
        vi.advanceTimersByTime(60000);
        lines = grid.element.querySelectorAll('.b4a-timegrid__now');
        expect(lines[0].parentElement).toBe(cellOf(grid, 'tue', '08:00'));
        expect(pct(lines[0].style.top)).toBe(0);
        expect(headers[0].hasAttribute('aria-current')).toBe(false);
        expect(headers[1].getAttribute('aria-current')).toBe('date');

        // 超出時段範圍：不畫線
        current = new Date(2026, 8, 29, 19, 0);
        vi.advanceTimersByTime(60000);
        expect(grid.element.querySelector('.b4a-timegrid__now')).toBeNull();

        expect(vi.getTimerCount()).toBe(1);
        grid.destroy();
        expect(vi.getTimerCount()).toBe(0);
    });

    it('starts no timer when disabled, unmounted or when no column carries a date', () => {
        vi.useFakeTimers();
        create({ nowIndicator: true, columns: [{ key: 'd', label: 'D', date: '2026-09-28' }] });
        mounted({ nowIndicator: false, columns: [{ key: 'd', label: 'D', date: '2026-09-28' }] });
        mounted({ nowIndicator: true });
        expect(vi.getTimerCount()).toBe(0);
    });
});

describe('TimeGrid — dynamic updates', () => {
    it('setItems / addItem / updateItem / removeItem / getItems keep the DOM in sync', () => {
        const grid = mounted({ items: [{ id: 1, column: 'roomA', start: '09:00', end: '10:00', title: 'One' }] });
        grid.setItems([
            { id: 2, column: 'roomB', start: '10:00', end: '11:00', title: 'Two' },
            { id: 3, column: 'roomB', start: '10:00', end: '11:00', title: 'Three' }
        ]);
        expect(itemOf(grid, 1)).toBeNull();
        expect(pct(itemOf(grid, 2).style.width)).toBe(50);

        grid.addItem({ id: 4, column: 'roomC', start: '14:00', title: 'Four' });
        expect(itemOf(grid, 4).parentElement).toBe(cellOf(grid, 'roomC', '14:00'));

        grid.updateItem(3, { column: 'roomA', title: 'Three moved', variant: 'danger', id: 'ignored' });
        expect(itemOf(grid, 3).parentElement).toBe(cellOf(grid, 'roomA', '10:00'));
        expect(itemOf(grid, 3).getAttribute('aria-label')).toContain('Three moved');
        expect(itemOf(grid, 3).dataset.variant).toBe('danger');
        // roomB 剩一個項目 → 恢復整欄寬
        expect(pct(itemOf(grid, 2).style.width)).toBe(100);

        grid.removeItem(2);
        expect(itemOf(grid, 2)).toBeNull();
        expect(grid.getItems().map((item) => item.id)).toEqual([3, 4]);

        const copy = grid.getItems()[0];
        copy.title = 'mutated';
        expect(grid.getItems()[0].title).toBe('Three moved');

        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        grid.addItem({ id: 4, column: 'roomA', start: '08:00', title: 'Duplicate' });
        expect(warn).toHaveBeenCalledTimes(1);
        expect(grid.getItems()).toHaveLength(2);
    });

    it('setColumns, setSlots and setTimeRange rebuild the grid and keep focus on the same keys', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        // 預設時段沒有 key 'a' → 建構時警告一次
        const grid = mounted({ items: [{ id: 'x', column: 'roomB', start: 'a', title: 'By key' }] });
        expect(warn).toHaveBeenCalledTimes(1);
        expect(itemOf(grid, 'x')).toBeNull();
        grid.focusCell('roomB', '09:00');
        grid.setColumns([{ key: 'roomB', label: 'Room B (renamed)' }, { key: 'roomD', label: 'Room D' }]);
        expect(grid.element.querySelector('.b4a-timegrid__colheader').textContent).toBe('Room B (renamed)');
        expect(document.activeElement).toBe(cellOf(grid, 'roomB', '09:00'));

        grid.setSlots([{ key: 'a', start: '09:00', end: '12:00', label: 'Morning' }, { key: 'b', start: '13:00', end: '17:00', label: 'Afternoon' }]);
        expect([...grid.element.querySelectorAll('[role="rowheader"]')].map((el) => el.textContent)).toEqual(['Morning', 'Afternoon']);
        expect(itemOf(grid, 'x').parentElement).toBe(cellOf(grid, 'roomB', 'a'));

        grid.setTimeRange({ start: '06:00', end: '08:00', step: 60 });
        expect([...grid.element.querySelectorAll('[role="rowheader"]')].map((el) => el.textContent)).toEqual(['06:00–07:00', '07:00–08:00']);
        expect(grid.element.querySelector('[role="grid"]').getAttribute('aria-rowcount')).toBe('3');
        // 'a' 又不是時段 key → 略過，但同一個未知 key 只回報一次
        expect(itemOf(grid, 'x')).toBeNull();
        expect(warn).toHaveBeenCalledTimes(1);
        grid.setSlots(null);
        expect(grid.element.querySelectorAll('[role="rowheader"]')).toHaveLength(2);
    });

    it('keeps a keyboard-focused item focused when unrelated items change', () => {
        const grid = mounted({ items: [{ id: 'f', column: 'roomA', start: '09:00', end: '10:00', title: 'Focused' }] });
        itemOf(grid, 'f').focus();
        grid.addItem({ id: 'g', column: 'roomA', start: '09:30', title: 'Neighbour' });
        expect(document.activeElement).toBe(itemOf(grid, 'f'));
        grid.setItems(grid.getItems());
        expect(document.activeElement).toBe(itemOf(grid, 'f'));
    });

    it('scrollToSlot scrolls the container and focusCell focuses the cell', () => {
        const grid = mounted({ height: 200 });
        const scroller = grid.element.querySelector('.b4a-timegrid__scroller');
        grid.scrollToSlot('12:00');
        expect(scroller.scrollTop).toBe(ROW('12:00') * 40);
        grid.scrollToSlot('nope');
        expect(scroller.scrollTop).toBe(ROW('12:00') * 40);
        grid.focusCell('roomC', '12:30');
        expect(document.activeElement).toBe(cellOf(grid, 'roomC', '12:30'));
        grid.focusCell('missing', '12:30');
        expect(document.activeElement).toBe(cellOf(grid, 'roomC', '12:30'));
    });
});

describe('TimeGrid — destroy', () => {
    it('removes the DOM, every document/window listener and all timers; later calls are safe', () => {
        vi.useFakeTimers();
        mockGeometry();
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const grid = mounted({
            selectable: true,
            editable: true,
            nowIndicator: true,
            now: () => new Date(2026, 8, 28, 10, 0),
            columns: [{ key: 'day', label: 'Day', date: '2026-09-28' }],
            items: [{ id: 'z', column: 'day', start: '11:00', title: 'Item' }]
        });
        // 進行中的指標操作與播報計時器
        grid.focusCell('day', '09:00');
        keydown('Enter');
        keydown('ArrowDown', { shiftKey: true });
        pointer(itemOf(grid, 'z'), 'pointerdown', at(0, ROW('11:00')));
        pointer(document, 'pointermove', at(0, ROW('13:00')));
        expect(grid.snapshot().mode).toBe('dragging');
        // jsdom 的 focus() 自己會排 0ms 計時器；先跑掉，只留下元件的：每分鐘更新 + 節流中的播報
        vi.advanceTimersByTime(1);
        expect(vi.getTimerCount()).toBe(2);

        const root = grid.element;
        grid.destroy();
        expect(host.contains(root)).toBe(false);
        expect(host.children).toHaveLength(0);
        expect(vi.getTimerCount()).toBe(0);
        const net = (add, remove) => {
            const count = new Map();
            for (const [type, fn] of add.mock.calls) count.set(`${type}`, (count.get(`${type}`) || 0) + 1);
            for (const [type] of remove.mock.calls) count.set(`${type}`, (count.get(`${type}`) || 0) - 1);
            return [...count.values()].every((value) => value === 0);
        };
        expect(net(docAdd, docRemove)).toBe(true);
        expect(winAdd.mock.calls.filter(([type]) => type === 'locale-changed')).toHaveLength(1);
        expect(winRemove.mock.calls.filter(([type]) => type === 'locale-changed')).toHaveLength(1);

        expect(() => {
            grid.destroy();
            grid.mount(host);
            grid.setItems([]);
            grid.addItem({ id: 'late', column: 'day', start: '09:00' });
            grid.updateItem('z', { title: 'x' });
            grid.removeItem('z');
            grid.setColumns(COLUMNS);
            grid.setSlots(null);
            grid.setTimeRange({ start: '08:00', end: '09:00', step: 30 });
            grid.clearSelection();
            grid.focusCell('day', '09:00');
            grid.scrollToSlot('09:00');
            Locale.setLang('en');
        }).not.toThrow();
        expect(grid.getItems()).toEqual([]);
        expect(grid.getSelection()).toBeNull();
        expect(grid.snapshot().lifecycle).toBe('destroyed');
        expect(host.children).toHaveLength(0);
        pointer(document, 'pointerup', at(0, ROW('13:00')));
    });

    it('ignores an async onItemMove that settles after destroy', async () => {
        let resolveMove;
        const grid = mounted({ editable: true, onItemMove: () => new Promise((resolve) => { resolveMove = resolve; }), items: [{ id: 'm', column: 'roomA', start: '09:00', title: 'Late' }] });
        itemOf(grid, 'm').focus();
        keydown(' ');
        keydown('ArrowDown');
        keydown('Enter');
        grid.destroy();
        resolveMove(true);
        await flush();
        expect(grid.getItems()).toEqual([]);
    });
});

describe('TimeGrid — i18n', () => {
    it('re-renders its strings when the locale changes', () => {
        const grid = mounted({ items: [{ id: 'w', column: 'roomA', start: '09:00', end: '10:00', title: 'Weekly sync' }] });
        const root = grid.element.querySelector('[role="grid"]');
        expect(root.getAttribute('aria-label')).toBe('時段表');
        expect(grid.element.querySelector('.b4a-timegrid__corner').textContent).toBe('時段');
        expect(itemOf(grid, 'w').getAttribute('aria-label')).toBe('Weekly sync，Room A，09:00–10:00');

        Locale.setLang('en');
        expect(root.getAttribute('aria-label')).toBe('Time grid');
        expect(grid.element.querySelector('.b4a-timegrid__corner').textContent).toBe('Time');
        expect(itemOf(grid, 'w').getAttribute('aria-label')).toBe('Weekly sync, Room A, 09:00–10:00');
        expect(Locale.t('timeGrid.moveRejected', { title: 'X' })).toContain('Could not move X');

        Locale.setLang('zh-TW');
        expect(root.getAttribute('aria-label')).toBe('時段表');
    });

    it('uses the locale for untitled items and announcements', () => {
        Locale.setLang('en');
        const grid = mounted({ editable: true, items: [{ id: 'u', column: 'roomB', start: '09:00' }] });
        expect(itemOf(grid, 'u').getAttribute('aria-label')).toBe('(untitled), Room B, 09:00–09:30');
        itemOf(grid, 'u').focus();
        keydown(' ');
        expect(grid.element.querySelector('[aria-live="polite"]').textContent).toBe('Picked up (untitled). Use arrow keys to move, Enter to drop, Escape to cancel.');
    });
});

describe('TimeGrid — performance', () => {
    it('renders 1,000 items across 7×30 cells in one pass without reading layout', () => {
        const columns = Array.from({ length: 7 }, (_, i) => ({ key: `c${i}`, label: `Column ${i + 1}` }));
        let seed = 42;
        const random = () => {
            seed = (seed * 1103515245 + 12345) % 2147483648;
            return seed / 2147483648;
        };
        const items = Array.from({ length: 1000 }, (_, i) => {
            const start = Math.floor(random() * 28);
            const span = 1 + Math.floor(random() * 3);
            return { id: i, column: `c${Math.floor(random() * 7)}`, start: `s${start}`, end: `s${Math.min(30, start + span)}`, title: `Task ${i}`, variant: TimeGrid.VARIANTS[i % 6], ghost: i % 50 === 0 };
        });
        const slots = Array.from({ length: 30 }, (_, i) => {
            const minutes = 7 * 60 + i * 30;
            const fmt = (m) => `${String(Math.floor(m / 60)).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
            return { key: `s${i}`, start: fmt(minutes), end: fmt(minutes + 30) };
        });
        // s30 不存在：end 為最後一格時改用時間
        for (const item of items) if (item.end === 's30') item.end = '22:00';

        const rect = vi.spyOn(Element.prototype, 'getBoundingClientRect');
        const offsetHeight = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get');
        const offsetWidth = vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get');
        const offsetLeft = vi.spyOn(HTMLElement.prototype, 'offsetLeft', 'get');
        const started = performance.now();
        const grid = mounted({ columns, slots, items });
        const elapsed = performance.now() - started;

        expect(grid.element.querySelectorAll('[role="gridcell"]')).toHaveLength(210);
        expect(grid.element.querySelectorAll('[data-item-id]')).toHaveLength(1000);
        expect(rect).not.toHaveBeenCalled();
        expect(offsetHeight).not.toHaveBeenCalled();
        expect(offsetWidth).not.toHaveBeenCalled();
        expect(offsetLeft).not.toHaveBeenCalled();
        expect(elapsed).toBeLessThan(5000);

        const updateStarted = performance.now();
        grid.updateItem(1, { column: 'c0' });
        expect(performance.now() - updateStarted).toBeLessThan(1000);
        expect(rect).not.toHaveBeenCalled();
    });
});
