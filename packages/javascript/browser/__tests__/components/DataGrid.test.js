import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { DataGrid } from '../../ui_components/layout/DataGrid/DataGrid.js';
import DataGridFromIndex from '../../ui_components/layout/DataGrid/index.js';
import Locale from '../../ui_components/i18n/index.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';

const ROOMS = [
    { value: 'r1', label: 'Room A' },
    { value: 'r2', label: 'Room B' },
    { value: 'r3', label: 'Board room' },
];

// 欄位索引：name 0、qty 1、price 2、room 3、due 4、done 5、total 6
function columns() {
    return [
        { key: 'name', label: 'Name', type: 'text', required: true, maxLength: 12 },
        { key: 'qty', label: 'Qty', type: 'number', min: 0, max: 100, precision: 0 },
        { key: 'price', label: 'Price', type: 'number', precision: 2 },
        { key: 'room', label: 'Room', type: 'select', options: ROOMS },
        { key: 'due', label: 'Due', type: 'date' },
        { key: 'done', label: 'Done', type: 'checkbox' },
        { key: 'total', label: 'Total', type: 'number', precision: 2, compute: (row) => (Number(row.qty) || 0) * (Number(row.price) || 0) },
    ];
}

function rows() {
    return [
        { id: 1, name: 'Alpha', qty: 2, price: 10, room: 'r1', due: '2026-01-05', done: false },
        { id: 2, name: 'Beta', qty: 1, price: 5.5, room: 'r2', due: '2026-02-10', done: true },
        { id: 3, name: 'Gamma', qty: 0, price: 1, room: null, due: null, done: false },
    ];
}

let host;
let outside;

beforeEach(() => {
    host = document.createElement('div');
    outside = document.createElement('button');
    outside.textContent = 'outside';
    document.body.append(host, outside);
});

afterEach(() => {
    host.remove();
    outside.remove();
    vi.restoreAllMocks();
    Locale.setLang('zh-TW');
});

const make = (options = {}) => new DataGrid({ columns: columns(), rows: rows(), ...options }).mount(host);
const gridEl = (grid) => grid.element.querySelector('[role="grid"]');
const bodyRows = (grid) => [...grid.element.querySelectorAll('.b4a-datagrid__body [role="row"]')];
const rowAt = (grid, index) => grid.element.querySelector(`.b4a-datagrid__body [role="row"][aria-rowindex="${index + 2}"]`);
const cellAt = (grid, row, col) => rowAt(grid, row).querySelectorAll('[role="gridcell"]')[col];
const textAt = (grid, row, col) => cellAt(grid, row, col).querySelector('.b4a-datagrid__value').textContent;
const editor = (grid) => grid.element.querySelector('.b4a-datagrid__editor');
const selectedCells = (grid) => grid.element.querySelectorAll('[aria-selected="true"]');
const liveText = (grid) => grid.element.querySelector('.b4a-datagrid__live').textContent;
const wait = (ms = 5) => new Promise((resolve) => setTimeout(resolve, ms));
const nextFrame = () => new Promise((resolve) => (typeof requestAnimationFrame === 'function'
    ? requestAnimationFrame(() => resolve())
    : setTimeout(resolve, 20)));

function press(key, init = {}) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    document.activeElement.dispatchEvent(event);
    return event;
}

function focusCell(grid, row, col) {
    const cell = cellAt(grid, row, col);
    cell.focus();
    return cell;
}

function clipboardEvent(type, text = '') {
    const store = { 'text/plain': text };
    const event = new Event(type, { bubbles: true, cancelable: true });
    Object.defineProperty(event, 'clipboardData', {
        value: { getData: (format) => store[format] ?? '', setData: (format, value) => { store[format] = value; } },
    });
    event.store = store;
    return event;
}

const paste = (text) => {
    const event = clipboardEvent('paste', text);
    document.activeElement.dispatchEvent(event);
    return event;
};

const mouse = (target, type, init = {}) => target.dispatchEvent(new MouseEvent(type, { bubbles: true, cancelable: true, button: 0, ...init }));

describe('DataGrid rendering', () => {
    it('is exported from index.js and renders an ARIA grid with counts and indices', () => {
        expect(DataGridFromIndex).toBe(DataGrid);
        const grid = make({ showRowNumbers: true });
        const element = gridEl(grid);
        expect(element.getAttribute('aria-rowcount')).toBe('4');
        expect(element.getAttribute('aria-colcount')).toBe('8');
        expect(element.getAttribute('aria-label')).toBe('資料表格');

        const headers = [...grid.element.querySelectorAll('[role="columnheader"]')];
        expect(headers.map((header) => header.getAttribute('aria-colindex'))).toEqual(['1', '2', '3', '4', '5', '6', '7', '8']);
        expect(headers[0].textContent).toBe('列號');
        expect(headers[1].textContent).toContain('Name');
        expect(headers[1].textContent).toContain('必填');
        expect(grid.element.querySelector('[role="row"][aria-rowindex="1"]')).not.toBeNull();

        expect(bodyRows(grid).map((row) => row.getAttribute('aria-rowindex'))).toEqual(['2', '3', '4']);
        const rowHeader = rowAt(grid, 1).querySelector('[role="rowheader"]');
        expect(rowHeader.textContent).toBe('2');
        expect(rowHeader.getAttribute('aria-colindex')).toBe('1');
        expect(cellAt(grid, 0, 0).getAttribute('aria-colindex')).toBe('2');
        grid.destroy();
    });

    it('displays each column type', () => {
        const grid = make();
        expect(textAt(grid, 0, 0)).toBe('Alpha');
        expect(textAt(grid, 0, 1)).toBe('2');
        expect(textAt(grid, 1, 2)).toBe('5.50');
        expect(textAt(grid, 1, 3)).toBe('Room B');
        expect(textAt(grid, 2, 3)).toBe('');
        expect(textAt(grid, 0, 4)).toBe('2026-01-05');
        const box = cellAt(grid, 1, 5).querySelector('[role="checkbox"]');
        expect(box.getAttribute('aria-checked')).toBe('true');
        expect(box.getAttribute('aria-label')).toBe('Done');
        expect(cellAt(grid, 0, 5).querySelector('[role="checkbox"]').getAttribute('aria-checked')).toBe('false');
        expect(textAt(grid, 0, 6)).toBe('20.00');
        expect(cellAt(grid, 0, 6).getAttribute('aria-readonly')).toBe('true');
        grid.destroy();
    });

    it('keeps exactly one tabbable cell (roving tabindex)', () => {
        const grid = make();
        const tabbable = grid.element.querySelectorAll('[tabindex="0"]');
        expect(tabbable).toHaveLength(1);
        expect(tabbable[0]).toBe(cellAt(grid, 0, 0));
        grid.destroy();
    });

    it('writes data and format output as plain text', () => {
        const grid = make({
            columns: [{ key: 'name', label: 'Name', format: (value) => `<img src=x>${value}` }],
            rows: [{ id: 1, name: '<b>bold</b>' }],
        });
        expect(textAt(grid, 0, 0)).toBe('<img src=x><b>bold</b>');
        expect(grid.element.querySelector('img, b')).toBeNull();
        grid.destroy();
    });

    it('shows an empty state and keeps the grid focusable', () => {
        const grid = make({ rows: [] });
        const empty = grid.element.querySelector('.b4a-datagrid__empty');
        expect(empty.style.display).toBe('');
        expect(empty.textContent).toBe('沒有資料');
        expect(gridEl(grid).tabIndex).toBe(0);
        expect(gridEl(grid).getAttribute('aria-rowcount')).toBe('1');
        grid.addRow({ id: 1, name: 'First' });
        expect(empty.style.display).toBe('none');
        expect(gridEl(grid).hasAttribute('tabindex')).toBe(false);
        expect(cellAt(grid, 0, 0).tabIndex).toBe(0);
        grid.destroy();
    });

    it('applies column widths, alignment and a clamped row height', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const grid = make({
            columns: [
                { key: 'name', label: 'Name', width: 90, maxLength: 5 },
                { key: 'qty', label: 'Qty', type: 'number', width: '12rem' },
                { key: 'done', label: 'Done', type: 'checkbox', width: 'calc(1px)' },
                { key: 'note', label: 'Note', align: 'center' },
            ],
            rows: [{ id: 1, name: 'Ab', qty: 1, done: true, note: 'n' }],
            showRowNumbers: true,
            rowHeight: 5,
        });
        const header = grid.element.querySelector('.b4a-datagrid__header-row');
        expect(header.style.getPropertyValue('grid-template-columns')).toBe('52px 90px 12rem minmax(72px, 1fr) minmax(140px, 1fr)');
        expect(header.style.height).toBe('20px');
        expect(rowAt(grid, 0).style.height).toBe('20px');
        expect(warn).toHaveBeenCalledWith(expect.stringContaining('calc(1px)'));
        // 非 px 寬度（12rem）無法換算，最小寬度以該型別預設值（number 100px）估計
        expect(gridEl(grid).style.minWidth).toBe(`${52 + 90 + 100 + 72 + 140}px`);
        const values = [...rowAt(grid, 0).querySelectorAll('.b4a-datagrid__value')].map((value) => value.style.textAlign);
        expect(values).toEqual(['left', 'right', 'center', 'center']);
        focusCell(grid, 0, 0);
        press('Enter');
        expect(editor(grid).maxLength).toBe(5);
        press('Escape');
        grid.destroy();
    });

    it('applies height with a sticky header and keeps generated keys out of the row', () => {
        const grid = make({ rows: [{ name: 'No key' }], height: 240, ariaLabel: 'Order lines' });
        expect(grid.element.querySelector('.b4a-datagrid__frame').style.height).toBe('240px');
        expect(grid.element.querySelector('.b4a-datagrid__head').style.position).toBe('sticky');
        expect(gridEl(grid).getAttribute('aria-label')).toBe('Order lines');
        const { rowKey } = grid.getActiveCell();
        expect(typeof rowKey).toBe('string');
        expect(grid.getRows()).toEqual([{ name: 'No key', total: 0 }]);
        expect(grid.getRow(rowKey)).toEqual({ name: 'No key', total: 0 });
        grid.destroy();
    });
});

describe('DataGrid keyboard navigation', () => {
    it('moves with arrows, Home/End and Ctrl+Home/End', () => {
        const grid = make();
        focusCell(grid, 0, 0);
        press('ArrowRight');
        expect(document.activeElement).toBe(cellAt(grid, 0, 1));
        press('ArrowDown');
        expect(document.activeElement).toBe(cellAt(grid, 1, 1));
        press('End');
        expect(document.activeElement).toBe(cellAt(grid, 1, 6));
        press('Home');
        expect(document.activeElement).toBe(cellAt(grid, 1, 0));
        press('End', { ctrlKey: true });
        expect(document.activeElement).toBe(cellAt(grid, 2, 6));
        press('Home', { ctrlKey: true });
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        expect(press('ArrowUp').defaultPrevented).toBe(true);
        press('ArrowLeft');
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        expect(grid.element.querySelectorAll('[tabindex="0"]')).toHaveLength(1);
        expect(grid.getActiveCell()).toEqual({ rowKey: 1, key: 'name' });
        grid.destroy();
    });

    it('moves by a page with PageUp and PageDown', () => {
        const many = Array.from({ length: 40 }, (_, index) => ({ id: index + 1, name: `Task ${index + 1}` }));
        const plain = make({ columns: [{ key: 'name', label: 'Name' }], rows: many });
        focusCell(plain, 0, 0);
        press('PageDown');
        expect(document.activeElement).toBe(cellAt(plain, 10, 0));
        press('PageDown');
        press('PageUp');
        expect(document.activeElement).toBe(cellAt(plain, 10, 0));
        plain.destroy();

        const scrolled = make({ columns: [{ key: 'name', label: 'Name' }], rows: many, height: '200px', rowHeight: 20 });
        focusCell(scrolled, 0, 0);
        press('PageDown');
        // 可視高度 200 - 表頭 20 = 9 列，翻頁保留一列 → 8
        expect(document.activeElement).toBe(cellAt(scrolled, 8, 0));
        expect(scrolled.element.querySelector('.b4a-datagrid__frame').scrollTop).toBeGreaterThanOrEqual(0);
        scrolled.destroy();
    });

    it('moves across cells with Tab, wraps rows and leaves the grid at the edges', () => {
        const grid = make({
            columns: [{ key: 'a', label: 'A' }, { key: 'b', label: 'B' }],
            rows: [{ id: 1, a: 'x', b: 'y' }, { id: 2, a: 'z', b: 'w' }],
        });
        focusCell(grid, 0, 0);
        expect(press('Tab').defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(cellAt(grid, 0, 1));
        expect(press('Tab').defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(cellAt(grid, 1, 0));
        press('Tab');
        expect(document.activeElement).toBe(cellAt(grid, 1, 1));
        expect(press('Tab').defaultPrevented).toBe(false);
        expect(document.activeElement).toBe(cellAt(grid, 1, 1));

        expect(press('Tab', { shiftKey: true }).defaultPrevented).toBe(true);
        expect(document.activeElement).toBe(cellAt(grid, 1, 0));
        press('Tab', { shiftKey: true });
        expect(document.activeElement).toBe(cellAt(grid, 0, 1));
        press('Tab', { shiftKey: true });
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        expect(press('Tab', { shiftKey: true }).defaultPrevented).toBe(false);
        grid.destroy();
    });

    it('extends a rectangular selection with Shift+arrows and collapses it with Escape', () => {
        const grid = make();
        focusCell(grid, 0, 0);
        press('ArrowRight', { shiftKey: true });
        press('ArrowDown', { shiftKey: true });
        expect(selectedCells(grid)).toHaveLength(4);
        expect(gridEl(grid).getAttribute('aria-multiselectable')).toBe('true');
        expect(document.activeElement).toBe(cellAt(grid, 1, 1));

        expect(press('Escape').defaultPrevented).toBe(true);
        expect(selectedCells(grid)).toHaveLength(0);
        expect(press('Escape').defaultPrevented).toBe(false);

        press('a', { ctrlKey: true });
        expect(selectedCells(grid)).toHaveLength(21);
        expect(liveText(grid)).toBe('已選取全部 3 列 × 7 欄');
        press('ArrowUp');
        expect(selectedCells(grid)).toHaveLength(0);
        grid.destroy();
    });

    it('selects with the mouse, drags a range and extends with Shift+click', () => {
        const grid = make();
        mouse(cellAt(grid, 0, 0), 'mousedown');
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        mouse(cellAt(grid, 1, 2), 'mouseover', { buttons: 1 });
        expect(selectedCells(grid)).toHaveLength(6);
        document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
        mouse(cellAt(grid, 2, 2), 'mouseover', { buttons: 1 });
        expect(selectedCells(grid)).toHaveLength(6);
        mouse(cellAt(grid, 2, 1), 'mousedown', { shiftKey: true });
        expect(selectedCells(grid)).toHaveLength(6);
        expect(document.activeElement).toBe(cellAt(grid, 2, 1));
        grid.destroy();
    });
});

describe('DataGrid editing', () => {
    it('opens a text editor with Enter and commits with Enter, moving down', () => {
        const onCellChange = vi.fn();
        const onChange = vi.fn();
        const grid = make({ onCellChange, onChange });
        focusCell(grid, 0, 0);
        expect(press('Enter').defaultPrevented).toBe(true);
        const input = editor(grid);
        expect(input.value).toBe('Alpha');
        expect(input.getAttribute('aria-label')).toBe('Name，第 1 列');
        expect(document.activeElement).toBe(input);
        expect(grid.snapshot().editing).toBe(true);

        input.value = 'Alpha 2';
        press('Enter');
        expect(editor(grid)).toBeNull();
        expect(grid.snapshot().editing).toBe(false);
        expect(textAt(grid, 0, 0)).toBe('Alpha 2');
        expect(document.activeElement).toBe(cellAt(grid, 1, 0));
        expect(onCellChange).toHaveBeenCalledWith({
            rowKey: 1, key: 'name', value: 'Alpha 2', oldValue: 'Alpha', row: expect.objectContaining({ id: 1, name: 'Alpha 2' }),
        });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange.mock.calls[0][0][0].name).toBe('Alpha 2');
        grid.destroy();
    });

    it('edits with F2 and cancels with Escape', () => {
        const onChange = vi.fn();
        const grid = make({ onChange });
        focusCell(grid, 1, 0);
        press('F2');
        editor(grid).value = 'Changed';
        const escape = press('Escape');
        expect(escape.defaultPrevented).toBe(true);
        expect(editor(grid)).toBeNull();
        expect(textAt(grid, 1, 0)).toBe('Beta');
        expect(document.activeElement).toBe(cellAt(grid, 1, 0));
        expect(onChange).not.toHaveBeenCalled();
        grid.destroy();
    });

    it('commits with Tab (moving right), on blur and when another cell is clicked', () => {
        const grid = make();
        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = 'Tabbed';
        expect(press('Tab').defaultPrevented).toBe(true);
        expect(grid.getRow(1).name).toBe('Tabbed');
        expect(document.activeElement).toBe(cellAt(grid, 0, 1));

        press('Enter');
        editor(grid).value = '7';
        outside.focus();
        expect(editor(grid)).toBeNull();
        expect(grid.getRow(1).qty).toBe(7);

        focusCell(grid, 1, 0);
        press('Enter');
        editor(grid).value = 'Clicked';
        mouse(cellAt(grid, 2, 2), 'mousedown');
        expect(grid.getRow(2).name).toBe('Clicked');
        expect(document.activeElement).toBe(cellAt(grid, 2, 2));
        grid.destroy();
    });

    it('commits with Tab on the last cell and lets focus leave the grid', () => {
        const grid = make({ columns: [{ key: 'a', label: 'A' }], rows: [{ id: 1, a: 'x' }] });
        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = 'y';
        expect(press('Tab').defaultPrevented).toBe(false);
        expect(grid.getRow(1).a).toBe('y');
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        grid.destroy();
    });

    it('starts editing with replacement when a printable character is typed', () => {
        const grid = make();
        focusCell(grid, 0, 0);
        expect(press('Z').defaultPrevented).toBe(true);
        expect(editor(grid).value).toBe('Z');
        press('Enter');
        expect(grid.getRow(1).name).toBe('Z');

        // IME 組字：開啟空白編輯器且不攔截事件，組字期間的 Enter 不提交
        focusCell(grid, 1, 0);
        expect(press('Process', { keyCode: 229 }).defaultPrevented).toBe(false);
        expect(editor(grid).value).toBe('');
        editor(grid).value = '會議';
        press('Enter', { isComposing: true });
        expect(editor(grid)).not.toBeNull();
        press('Enter');
        expect(grid.getRow(2).name).toBe('會議');
        grid.destroy();
    });

    it('opens editors by double click', () => {
        const grid = make();
        mouse(cellAt(grid, 1, 0), 'dblclick');
        expect(editor(grid).value).toBe('Beta');
        press('Escape');
        grid.destroy();
    });

    it('parses numbers, rounds to precision and clamps to min/max in the editor', () => {
        const grid = make();
        focusCell(grid, 0, 1);
        press('Enter');
        expect(editor(grid).value).toBe('2');
        expect(editor(grid).getAttribute('inputmode')).toBe('decimal');
        editor(grid).value = '150';
        press('Enter');
        expect(grid.getRow(1).qty).toBe(100);

        focusCell(grid, 0, 1);
        press('Enter');
        editor(grid).value = '3.6';
        press('Enter');
        expect(grid.getRow(1).qty).toBe(4);

        focusCell(grid, 0, 2);
        press('Enter');
        expect(editor(grid).value).toBe('10.00');
        editor(grid).value = '1,234.567';
        press('Enter');
        expect(grid.getRow(1).price).toBe(1234.57);
        expect(textAt(grid, 0, 2)).toBe('1234.57');

        focusCell(grid, 0, 1);
        press('5');
        expect(editor(grid).value).toBe('5');
        press('Escape');
        expect(grid.getRow(1).qty).toBe(4);

        focusCell(grid, 0, 2);
        press('Enter');
        editor(grid).value = '';
        press('Enter');
        expect(grid.getRow(1).price).toBeNull();
        grid.destroy();
    });

    it('keeps unparsable number text and marks it invalid', () => {
        const grid = make();
        focusCell(grid, 0, 1);
        press('Enter');
        editor(grid).value = 'abc';
        press('Enter');
        expect(grid.getRow(1).qty).toBe('abc');
        expect(cellAt(grid, 0, 1).getAttribute('aria-invalid')).toBe('true');
        expect(grid.getErrors()).toEqual([{ rowKey: 1, key: 'qty', message: '請輸入數字' }]);
        grid.destroy();
    });

    it('commits and cancels the select editor', () => {
        const grid = make();
        focusCell(grid, 0, 3);
        press('Enter');
        const select = editor(grid);
        expect(select.tagName).toBe('SELECT');
        expect([...select.options].map((option) => option.textContent)).toEqual(['（空白）', 'Room A', 'Room B', 'Board room']);
        expect(select.value).toBe('0');
        select.value = '1';
        press('Enter');
        expect(grid.getRow(1).room).toBe('r2');
        expect(textAt(grid, 0, 3)).toBe('Room B');

        focusCell(grid, 0, 3);
        press('Enter');
        editor(grid).value = '2';
        press('Escape');
        expect(grid.getRow(1).room).toBe('r2');

        focusCell(grid, 0, 3);
        expect(press('b').defaultPrevented).toBe(true);
        expect(editor(grid).value).toBe('2');
        press('Enter');
        expect(grid.getRow(1).room).toBe('r3');

        focusCell(grid, 0, 3);
        press('Enter');
        editor(grid).value = '';
        press('Enter');
        expect(grid.getRow(1).room).toBeNull();
        grid.destroy();
    });

    it('commits and cancels the date editor', () => {
        const grid = make();
        focusCell(grid, 0, 4);
        press('F2');
        const input = editor(grid);
        expect(input.type).toBe('date');
        expect(input.value).toBe('2026-01-05');
        input.value = '2026-03-01';
        press('Enter');
        expect(grid.getRow(1).due).toBe('2026-03-01');

        focusCell(grid, 0, 4);
        press('Enter');
        editor(grid).value = '2026-04-01';
        press('Escape');
        expect(grid.getRow(1).due).toBe('2026-03-01');

        focusCell(grid, 0, 4);
        expect(press('2').defaultPrevented).toBe(false);
        expect(editor(grid).value).toBe('');
        press('Escape');
        expect(grid.getRow(1).due).toBe('2026-03-01');
        grid.destroy();
    });

    it('toggles checkboxes with Space and click, without an editor', () => {
        const onCellChange = vi.fn();
        const grid = make({ onCellChange });
        focusCell(grid, 0, 5);
        expect(press('Enter').defaultPrevented).toBe(false);
        expect(editor(grid)).toBeNull();
        expect(press(' ').defaultPrevented).toBe(true);
        expect(grid.getRow(1).done).toBe(true);
        expect(cellAt(grid, 0, 5).querySelector('[role="checkbox"]').getAttribute('aria-checked')).toBe('true');
        expect(onCellChange).toHaveBeenLastCalledWith(expect.objectContaining({ rowKey: 1, key: 'done', value: true, oldValue: false }));

        cellAt(grid, 1, 5).querySelector('[role="checkbox"]').dispatchEvent(new MouseEvent('click', { bubbles: true }));
        expect(grid.getRow(2).done).toBe(false);

        // 多格選取時空白鍵把範圍內的 checkbox 設成同一個值
        focusCell(grid, 0, 5);
        press('ArrowDown', { shiftKey: true });
        press('ArrowDown', { shiftKey: true });
        press(' ');
        expect(grid.getRows().map((row) => row.done)).toEqual([true, true, true]);
        grid.destroy();
    });

    it('does not edit readonly, row-readonly or computed cells', () => {
        const grid = make({
            columns: [
                { key: 'code', label: 'Code', readonly: true },
                { key: 'note', label: 'Note', readonly: (row) => row.locked === true },
                { key: 'flag', label: 'Flag', type: 'checkbox', readonly: true },
                { key: 'len', label: 'Len', type: 'number', compute: (row) => String(row.note ?? '').length },
            ],
            rows: [
                { id: 1, code: 'A-1', note: 'open', locked: false, flag: false },
                { id: 2, code: 'A-2', note: 'closed', locked: true, flag: true },
            ],
        });
        focusCell(grid, 0, 0);
        expect(press('Enter').defaultPrevented).toBe(false);
        expect(editor(grid)).toBeNull();
        press('x');
        expect(editor(grid)).toBeNull();
        expect(cellAt(grid, 0, 0).getAttribute('aria-readonly')).toBe('true');

        focusCell(grid, 1, 1);
        press('Enter');
        expect(editor(grid)).toBeNull();
        expect(cellAt(grid, 1, 1).getAttribute('aria-readonly')).toBe('true');
        expect(cellAt(grid, 0, 1).hasAttribute('aria-readonly')).toBe(false);

        focusCell(grid, 0, 1);
        press('Enter');
        editor(grid).value = 'reopened';
        press('Enter');
        expect(grid.getRow(1).len).toBe(8);
        expect(textAt(grid, 0, 3)).toBe('8');

        focusCell(grid, 0, 2);
        press(' ');
        expect(grid.getRow(1).flag).toBe(false);
        expect(cellAt(grid, 0, 2).querySelector('[role="checkbox"]').getAttribute('aria-readonly')).toBe('true');

        focusCell(grid, 0, 3);
        press('Enter');
        expect(editor(grid)).toBeNull();
        press('Delete');
        expect(grid.getRow(1).len).toBe(8);

        focusCell(grid, 0, 0);
        press('Delete');
        expect(grid.getRow(1).code).toBe('A-1');
        grid.destroy();
    });

    it('recalculates computed columns after every change', () => {
        const grid = make();
        focusCell(grid, 0, 1);
        press('Enter');
        editor(grid).value = '3';
        press('Enter');
        expect(grid.getRow(1).total).toBe(30);
        expect(textAt(grid, 0, 6)).toBe('30.00');
        grid.updateRow(1, { price: 2 });
        expect(textAt(grid, 0, 6)).toBe('6.00');
        expect(cellAt(grid, 0, 6).hasAttribute('data-modified')).toBe(false);
        grid.destroy();
    });

    it('clears the selection with Delete in one change', () => {
        const onChange = vi.fn();
        const grid = make({ onChange });
        focusCell(grid, 0, 0);
        press('ArrowRight', { shiftKey: true });
        expect(press('Delete').defaultPrevented).toBe(true);
        expect(grid.getRow(1)).toMatchObject({ name: null, qty: null, total: 0 });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(liveText(grid)).toBe('已清除 2 個儲存格');
        expect(grid.getErrors()).toEqual([{ rowKey: 1, key: 'name', message: '此欄為必填' }]);
        grid.destroy();
    });

    it('patches only the edited row instead of re-rendering', async () => {
        const grid = make();
        const rowsBefore = bodyRows(grid);
        const untouched = cellAt(grid, 2, 0);
        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = 'Patched';

        const records = [];
        const observer = new MutationObserver((list) => records.push(...list));
        observer.observe(grid.element, { subtree: true, childList: true, attributes: true, characterData: true });
        outside.focus();
        await Promise.resolve();
        observer.disconnect();

        expect(grid.getRow(1).name).toBe('Patched');
        const rowsAfter = bodyRows(grid);
        expect(rowsAfter).toHaveLength(rowsBefore.length);
        expect(rowsAfter.every((row, index) => row === rowsBefore[index])).toBe(true);
        expect(cellAt(grid, 2, 0)).toBe(untouched);
        const firstRow = rowAt(grid, 0);
        const outsideFirstRow = records.filter((record) => {
            const node = record.target.nodeType === 1 ? record.target : record.target.parentElement;
            return !firstRow.contains(record.target) && !node?.closest('.b4a-datagrid__status, .b4a-datagrid__live');
        });
        expect(outsideFirstRow).toEqual([]);
        grid.destroy();
    });
});

describe('DataGrid validation', () => {
    it('marks invalid cells with aria-invalid, a hidden described message and a danger outline', () => {
        const onValidationChange = vi.fn();
        const grid = make({ onValidationChange });
        expect(grid.validate()).toBe(true);
        expect(onValidationChange).not.toHaveBeenCalled();

        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = '';
        press('Enter');
        const cell = cellAt(grid, 0, 0);
        expect(cell.getAttribute('aria-invalid')).toBe('true');
        const message = document.getElementById(cell.getAttribute('aria-describedby'));
        expect(message.textContent).toBe('此欄為必填');
        expect(message.hidden).toBe(true);
        expect(cell.contains(message)).toBe(true);
        expect(cell.style.boxShadow).toContain('var(--cl-danger)');
        expect(onValidationChange).toHaveBeenLastCalledWith([{ rowKey: 1, key: 'name', message: '此欄為必填' }]);

        const status = grid.element.querySelector('.b4a-datagrid__status');
        expect(status.style.display).toBe('none');
        press('ArrowUp');
        expect(status.textContent).toBe('第 1 列・Name：此欄為必填');
        expect(status.style.display).toBe('');
        expect(status.getAttribute('aria-hidden')).toBe('true');
        // 無效儲存格取得焦點時外框改用 danger 色
        expect(cell.style.outline).toBe('2px solid var(--cl-danger)');

        press('Enter');
        editor(grid).value = 'Fixed';
        press('Enter');
        expect(cell.hasAttribute('aria-invalid')).toBe(false);
        expect(cell.hasAttribute('aria-describedby')).toBe(false);
        expect(cell.style.outline).toBe('none');
        expect(cellAt(grid, 1, 0).style.outline).toBe('2px solid var(--cl-primary)');
        expect(onValidationChange).toHaveBeenLastCalledWith([]);
        grid.destroy();
    });

    it('checks range, length, type and custom rules', () => {
        const grid = make({
            columns: [
                ...columns(),
                { key: 'code', label: 'Code', validate: (value, row) => (value && !/^[A-Z]{2}-\d+$/.test(value) ? `Use AB-123 (${row.name})` : null) },
                { key: 'start', label: 'Start', type: 'date', min: '2026-01-01', max: '2026-12-31' },
            ],
        });
        grid.updateRow(1, { qty: -1, name: 'A name that is too long', code: 'bad', start: '2025-12-31' });
        expect(grid.getErrors()).toEqual([
            { rowKey: 1, key: 'name', message: '不可超過 12 個字' },
            { rowKey: 1, key: 'qty', message: '不可小於 0' },
            { rowKey: 1, key: 'code', message: 'Use AB-123 (A name that is too long)' },
            { rowKey: 1, key: 'start', message: '不可早於 2026-01-01' },
        ]);
        grid.updateRow(1, { qty: 101, name: 'Short', code: 'AB-1', start: '2027-01-01' });
        expect(grid.getErrors()).toEqual([
            { rowKey: 1, key: 'qty', message: '不可大於 100' },
            { rowKey: 1, key: 'start', message: '不可晚於 2026-12-31' },
        ]);

        grid.setRows([{ id: 5, name: 'Typed', qty: 'x', room: 'nowhere', due: '2026-13-40', done: 'maybe' }]);
        expect(grid.getErrors()).toEqual([]);
        expect(grid.validate()).toBe(false);
        expect(grid.getErrors()).toEqual([
            { rowKey: 5, key: 'qty', message: '請輸入數字' },
            { rowKey: 5, key: 'room', message: '不是可選的項目' },
            { rowKey: 5, key: 'due', message: '請輸入有效日期（YYYY-MM-DD）' },
            { rowKey: 5, key: 'done', message: '請輸入 TRUE 或 FALSE' },
        ]);
        grid.destroy();
    });

    it('validates untouched new rows only when validate() is called', () => {
        const grid = make();
        grid.addRow({ id: 9 });
        expect(grid.getErrors()).toEqual([]);
        expect(grid.validate()).toBe(false);
        expect(grid.getErrors()).toEqual([{ rowKey: 9, key: 'name', message: '此欄為必填' }]);
        grid.updateRow(9, { name: 'Delta' });
        expect(grid.getErrors()).toEqual([]);
        expect(grid.validate()).toBe(true);
        grid.destroy();
    });
});

describe('DataGrid dirty tracking', () => {
    it('marks edited cells with a corner marker and a hidden "modified" hint', () => {
        const grid = make();
        expect(grid.isDirty()).toBe(false);
        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = 'Alpha*';
        press('Enter');
        const cell = cellAt(grid, 0, 0);
        expect(grid.isDirty()).toBe(true);
        expect(cell.dataset.modified).toBe('true');
        expect(cell.querySelector('.b4a-datagrid__mark').getAttribute('aria-hidden')).toBe('true');
        expect(cell.querySelector('.b4a-datagrid__modified').textContent).toBe('已修改');

        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = 'Alpha';
        press('Enter');
        expect(cell.hasAttribute('data-modified')).toBe(false);
        expect(cell.querySelector('.b4a-datagrid__mark')).toBeNull();
        expect(grid.isDirty()).toBe(false);
        grid.destroy();
    });

    it('reports added, updated and removed rows', () => {
        const grid = make();
        grid.updateRow(1, { qty: 3 });
        const key = grid.addRow({ name: 'New' });
        expect(grid.removeRows([2])).toBe(1);
        const changes = grid.getChanges();
        expect(changes.added).toEqual([{ name: 'New', total: 0 }]);
        expect(changes.updated).toEqual([{ rowKey: 1, changes: { qty: { from: 2, to: 3 }, total: { from: 20, to: 30 } } }]);
        expect(changes.removed).toEqual([expect.objectContaining({ id: 2, name: 'Beta' })]);
        expect(grid.getRow(key)).toEqual({ name: 'New', total: 0 });
        expect(rowAt(grid, 2).dataset.rowState).toBe('added');
        expect(cellAt(grid, 2, 0).dataset.modified).toBe('true');
        grid.destroy();
    });

    it('accepts changes as a new baseline and reverts to it', () => {
        const grid = make();
        grid.updateRow(1, { name: 'Changed' });
        expect(cellAt(grid, 0, 0).dataset.modified).toBe('true');
        grid.acceptChanges();
        expect(grid.isDirty()).toBe(false);
        expect(cellAt(grid, 0, 0).hasAttribute('data-modified')).toBe(false);
        expect(grid.getChanges()).toEqual({ added: [], updated: [], removed: [] });

        grid.updateRow(1, { name: 'Again' });
        grid.addRow({ id: 10, name: 'Temp' });
        grid.removeRows(3);
        grid.updateRow(2, { qty: 'oops' });
        grid.revertChanges();
        expect(grid.getRows().map((row) => row.name)).toEqual(['Changed', 'Beta', 'Gamma']);
        expect(grid.isDirty()).toBe(false);
        expect(grid.getErrors()).toEqual([]);
        expect(bodyRows(grid)).toHaveLength(3);
        expect(grid.element.querySelectorAll('[data-modified]')).toHaveLength(0);
        grid.destroy();
    });

    it('setRows resets the baseline and errors', () => {
        const onValidationChange = vi.fn();
        const grid = make({ onValidationChange });
        grid.updateRow(1, { qty: -5 });
        expect(grid.getErrors()).toHaveLength(1);
        grid.setRows([{ id: 7, name: 'Fresh' }]);
        expect(grid.isDirty()).toBe(false);
        expect(grid.getErrors()).toEqual([]);
        expect(onValidationChange).toHaveBeenLastCalledWith([]);
        expect(bodyRows(grid)).toHaveLength(1);
        grid.destroy();
    });
});

describe('DataGrid TSV', () => {
    it('parses spreadsheet TSV including quoted fields and trailing newlines', () => {
        expect(DataGrid.parseTSV('')).toEqual([]);
        expect(DataGrid.parseTSV('a')).toEqual([['a']]);
        expect(DataGrid.parseTSV('a\tb\nc\td')).toEqual([['a', 'b'], ['c', 'd']]);
        expect(DataGrid.parseTSV('a\tb\r\nc\td\r\n')).toEqual([['a', 'b'], ['c', 'd']]);
        expect(DataGrid.parseTSV('a\t\n\tb')).toEqual([['a', ''], ['', 'b']]);
        expect(DataGrid.parseTSV('"x\ty"\tz')).toEqual([['x\ty', 'z']]);
        expect(DataGrid.parseTSV('"line1\nline2"\tb\nc\td')).toEqual([['line1\nline2', 'b'], ['c', 'd']]);
        expect(DataGrid.parseTSV('"line1\r\nline2"\n')).toEqual([['line1\nline2']]);
        expect(DataGrid.parseTSV('"say ""hi"""\tok')).toEqual([['say "hi"', 'ok']]);
        expect(DataGrid.parseTSV('5" screen\tok')).toEqual([['5" screen', 'ok']]);
        expect(DataGrid.parseTSV('"unclosed\tb')).toEqual([['"unclosed', 'b']]);
        expect(DataGrid.parseTSV('""\tb')).toEqual([['', 'b']]);
        expect(DataGrid.parseTSV('a\n\nb')).toEqual([['a'], [''], ['b']]);
        expect(DataGrid.parseTSV('\n')).toEqual([['']]);
    });

    it('serialises TSV that round-trips through the parser', () => {
        const matrix = [['plain', 'tab\there', 'line\nbreak', 'quote "q"'], ['', '0', 'x', 'y']];
        const text = DataGrid.toTSV(matrix);
        expect(text).toBe('plain\t"tab\there"\t"line\nbreak"\t"quote ""q"""\n\t0\tx\ty');
        expect(DataGrid.parseTSV(text)).toEqual(matrix);
    });
});

describe('DataGrid paste', () => {
    it('pastes from the active cell with per-column coercion and selects the block', () => {
        const onPaste = vi.fn();
        const onChange = vi.fn();
        const onCellChange = vi.fn();
        const grid = make({ onPaste, onChange, onCellChange });
        focusCell(grid, 0, 1);
        const event = paste('1,234\t12.345\tboard room\t2026/3/5\tyes\n7\t0.5\tr1\t20260102\tno\n');
        expect(event.defaultPrevented).toBe(true);
        expect(grid.getRow(1)).toMatchObject({ qty: 1234, price: 12.35, room: 'r3', due: '2026-03-05', done: true });
        expect(grid.getRow(2)).toMatchObject({ qty: 7, price: 0.5, room: 'r1', due: '2026-01-02', done: false });
        // 超出範圍的值不夾住，而是標示錯誤
        expect(cellAt(grid, 0, 1).getAttribute('aria-invalid')).toBe('true');
        expect(grid.getErrors()).toEqual([{ rowKey: 1, key: 'qty', message: '不可大於 100' }]);
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onCellChange).toHaveBeenCalledTimes(10);
        expect(onPaste).toHaveBeenCalledWith(expect.objectContaining({
            startRowKey: 1, startKey: 'qty', rows: 2, cols: 5, appliedRows: 2, appliedCols: 5, addedRows: 0, truncatedRows: 0,
        }));
        expect(selectedCells(grid)).toHaveLength(10);
        expect(document.activeElement).toBe(cellAt(grid, 0, 1));
        expect(liveText(grid)).toBe('已貼上 2 列 × 5 欄');
        grid.destroy();
    });

    it('keeps values that cannot be coerced and marks them invalid', () => {
        const grid = make();
        focusCell(grid, 0, 1);
        paste('abc\t\tUnknown room\t2026-02-30\tmaybe');
        expect(grid.getRow(1)).toMatchObject({ qty: 'abc', price: null, room: 'Unknown room', due: '2026-02-30', done: 'maybe' });
        expect(grid.getErrors().map((error) => error.key)).toEqual(['qty', 'room', 'due', 'done']);
        expect(textAt(grid, 0, 5)).toBe('maybe');
        expect(textAt(grid, 0, 3)).toBe('Unknown room');
        grid.destroy();
    });

    it('skips readonly and computed columns and drops columns beyond the grid', () => {
        const onPaste = vi.fn();
        const grid = make({ onPaste });
        focusCell(grid, 0, 5);
        paste('TRUE\t999\tExtra');
        expect(grid.getRow(1).done).toBe(true);
        expect(grid.getRow(1).total).toBe(20);
        expect(onPaste).toHaveBeenCalledWith(expect.objectContaining({ cols: 3, appliedCols: 2 }));
        grid.destroy();

        const locked = make({
            columns: [
                { key: 'code', label: 'Code', readonly: true },
                { key: 'note', label: 'Note', readonly: (row) => row.locked === true },
                { key: 'qty', label: 'Qty', type: 'number' },
            ],
            rows: [
                { id: 1, code: 'A-1', note: 'open', locked: false, qty: 1 },
                { id: 2, code: 'A-2', note: 'closed', locked: true, qty: 2 },
            ],
        });
        focusCell(locked, 0, 0);
        paste('X-1\tchanged\t5\nX-2\tchanged\t6');
        expect(locked.getRows()).toEqual([
            { id: 1, code: 'A-1', note: 'changed', locked: false, qty: 5 },
            { id: 2, code: 'A-2', note: 'closed', locked: true, qty: 6 },
        ]);
        locked.destroy();
    });

    it('truncates at the last row unless allowAddRowsOnPaste is set', () => {
        const onPaste = vi.fn();
        const grid = make({ onPaste });
        focusCell(grid, 2, 0);
        paste('X\nY\nZ');
        expect(grid.getRows().map((row) => row.name)).toEqual(['Alpha', 'Beta', 'X']);
        expect(onPaste).toHaveBeenCalledWith(expect.objectContaining({ appliedRows: 1, truncatedRows: 2, addedRows: 0 }));
        expect(liveText(grid)).toBe('已貼上 1 列 × 1 欄，2 列超出表格未貼上');
        grid.destroy();

        const onChange = vi.fn();
        const growing = make({ allowAddRowsOnPaste: true, onChange });
        focusCell(growing, 2, 0);
        paste('X\nY\nZ');
        expect(growing.getRows().map((row) => row.name)).toEqual(['Alpha', 'Beta', 'X', 'Y', 'Z']);
        expect(gridEl(growing).getAttribute('aria-rowcount')).toBe('6');
        expect(rowAt(growing, 4).dataset.rowState).toBe('added');
        expect(growing.getChanges().added).toEqual([{ name: 'Y', total: 0 }, { name: 'Z', total: 0 }]);
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(liveText(growing)).toBe('已貼上 3 列 × 1 欄，新增 2 列');
        growing.destroy();
    });

    it('fills a selected range with a single pasted value', () => {
        const grid = make();
        focusCell(grid, 0, 1);
        press('ArrowDown', { shiftKey: true });
        press('ArrowDown', { shiftKey: true });
        paste('9');
        expect(grid.getRows().map((row) => row.qty)).toEqual([9, 9, 9]);
        expect(grid.getRows().map((row) => row.total)).toEqual([90, 49.5, 9]);
        grid.destroy();
    });

    it('pastes into an empty grid only when adding rows is allowed', () => {
        const blocked = make({ rows: [] });
        gridEl(blocked).focus();
        paste('Delta\t4');
        expect(blocked.getRows()).toEqual([]);
        blocked.destroy();

        const grid = make({ rows: [], allowAddRowsOnPaste: true });
        gridEl(grid).focus();
        paste('Delta\t4\nEcho\t5');
        expect(grid.getRows()).toEqual([
            expect.objectContaining({ name: 'Delta', qty: 4 }),
            expect.objectContaining({ name: 'Echo', qty: 5 }),
        ]);
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        expect(grid.element.querySelector('.b4a-datagrid__empty').style.display).toBe('none');
        grid.destroy();
    });

    it('leaves paste inside an open editor to the browser', () => {
        const grid = make();
        focusCell(grid, 0, 0);
        press('Enter');
        const event = paste('Ignored\tValues');
        expect(event.defaultPrevented).toBe(false);
        expect(editor(grid)).not.toBeNull();
        expect(grid.getRow(1).name).toBe('Alpha');
        grid.destroy();
    });
});

describe('DataGrid copy', () => {
    it('copies the active cell and a Shift+arrow range as TSV', () => {
        const grid = make();
        focusCell(grid, 0, 0);
        let event = clipboardEvent('copy');
        document.activeElement.dispatchEvent(event);
        expect(event.defaultPrevented).toBe(true);
        expect(event.store['text/plain']).toBe('Alpha');

        press('ArrowRight', { shiftKey: true });
        press('ArrowRight', { shiftKey: true });
        press('ArrowRight', { shiftKey: true });
        press('ArrowDown', { shiftKey: true });
        event = clipboardEvent('copy');
        document.activeElement.dispatchEvent(event);
        expect(event.store['text/plain']).toBe('Alpha\t2\t10.00\tRoom A\nBeta\t1\t5.50\tRoom B');
        expect(liveText(grid)).toBe('已複製 2 列 × 4 欄');

        focusCell(grid, 1, 5);
        event = clipboardEvent('copy');
        document.activeElement.dispatchEvent(event);
        expect(event.store['text/plain']).toBe('TRUE');

        grid.updateRow(1, { name: 'a\tb' });
        focusCell(grid, 0, 0);
        event = clipboardEvent('copy');
        document.activeElement.dispatchEvent(event);
        expect(event.store['text/plain']).toBe('"a\tb"');
        grid.destroy();
    });

    it('cuts by copying and clearing editable cells', () => {
        const onChange = vi.fn();
        const grid = make({ onChange });
        focusCell(grid, 0, 0);
        const event = clipboardEvent('cut');
        document.activeElement.dispatchEvent(event);
        expect(event.store['text/plain']).toBe('Alpha');
        expect(grid.getRow(1).name).toBeNull();
        expect(onChange).toHaveBeenCalledTimes(1);
        grid.destroy();
    });

    it('falls back to the Clipboard API when Ctrl+C fires no copy event', async () => {
        const writeText = vi.fn(() => Promise.resolve());
        Object.defineProperty(window.navigator, 'clipboard', { value: { writeText }, configurable: true });
        try {
            const grid = make();
            focusCell(grid, 0, 0);
            press('c', { ctrlKey: true });
            await wait();
            expect(writeText).toHaveBeenCalledWith('Alpha');

            writeText.mockClear();
            press('c', { ctrlKey: true });
            document.activeElement.dispatchEvent(clipboardEvent('copy'));
            await wait();
            expect(writeText).not.toHaveBeenCalled();
            grid.destroy();
        } finally {
            delete window.navigator.clipboard;
        }
    });
});

describe('DataGrid row operations', () => {
    it('adds rows at an index and returns their key', () => {
        const grid = make();
        expect(grid.addRow({ id: 7, name: 'Inserted', qty: 1, price: 1 }, { index: 1 })).toBe(7);
        expect(grid.getRows().map((row) => row.id)).toEqual([1, 7, 2, 3]);
        expect(bodyRows(grid).map((row) => row.dataset.rowKey)).toEqual(['1', '7', '2', '3']);
        expect(bodyRows(grid).map((row) => row.getAttribute('aria-rowindex'))).toEqual(['2', '3', '4', '5']);
        expect(gridEl(grid).getAttribute('aria-rowcount')).toBe('5');
        expect(textAt(grid, 1, 6)).toBe('1.00');

        const generated = grid.addRow();
        expect(typeof generated).toBe('string');
        expect(grid.getRow(generated)).toEqual({ total: 0 });

        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const duplicate = grid.addRow({ id: 1, name: 'Duplicate' });
        expect(duplicate).not.toBe(1);
        expect(grid.getRow(duplicate).name).toBe('Duplicate');
        expect(warn).toHaveBeenCalled();
        grid.destroy();
    });

    it('removes rows by key and keeps focus inside the grid', () => {
        const grid = make();
        focusCell(grid, 1, 2);
        expect(grid.removeRows([2, 99])).toBe(1);
        expect(grid.getRows().map((row) => row.id)).toEqual([1, 3]);
        expect(bodyRows(grid).map((row) => row.getAttribute('aria-rowindex'))).toEqual(['2', '3']);
        expect(document.activeElement).toBe(cellAt(grid, 1, 2));
        expect(grid.removeRows(1)).toBe(1);
        expect(grid.removeRows(3)).toBe(1);
        expect(grid.getRows()).toEqual([]);
        expect(document.activeElement).toBe(gridEl(grid));
        expect(grid.element.querySelector('.b4a-datagrid__empty').style.display).toBe('');
        expect(grid.getActiveCell()).toBeNull();
        grid.destroy();
    });

    it('returns clean copies and updates rows by key', () => {
        const input = rows();
        const grid = make({ rows: input });
        const copy = grid.getRows();
        copy[0].name = 'Mutated';
        expect(grid.getRow(1).name).toBe('Alpha');
        expect(grid.updateRow(1, { name: 'Patched', extra: 'meta' })).toBe(true);
        expect(grid.getRow(1)).toMatchObject({ name: 'Patched', extra: 'meta' });
        expect(textAt(grid, 0, 0)).toBe('Patched');
        expect(input[0].name).toBe('Alpha');
        expect(grid.updateRow(404, { name: 'x' })).toBe(false);
        expect(grid.getRow(404)).toBeNull();
        grid.destroy();
    });

    it('supports rowKey as a function and focusCell / getActiveCell', () => {
        const grid = make({
            rowKey: (row) => `${row.code}-${row.line}`,
            columns: [{ key: 'code', label: 'Code' }, { key: 'line', label: 'Line', type: 'number' }],
            rows: [{ code: 'A', line: 1 }, { code: 'A', line: 2 }],
        });
        expect(grid.getRow('A-2')).toEqual({ code: 'A', line: 2 });
        expect(grid.focusCell('A-2', 'line')).toBe(true);
        expect(document.activeElement).toBe(cellAt(grid, 1, 1));
        expect(grid.getActiveCell()).toEqual({ rowKey: 'A-2', key: 'line' });
        expect(grid.focusCell('missing', 'line')).toBe(false);
        grid.destroy();
    });

    it('does not fire change callbacks for programmatic updates', () => {
        const onChange = vi.fn();
        const onCellChange = vi.fn();
        const grid = make({ onChange, onCellChange });
        grid.setValue(rows());
        grid.updateRow(1, { name: 'Quiet' });
        grid.addRow({ id: 8, name: 'Quiet too' });
        grid.removeRows([2]);
        grid.clear();
        expect(onChange).not.toHaveBeenCalled();
        expect(onCellChange).not.toHaveBeenCalled();
        grid.destroy();
    });
});

describe('DataGrid virtual mode', () => {
    const wideColumns = () => Array.from({ length: 10 }, (_, index) => ({
        key: `c${index}`,
        label: `C${index}`,
        type: index % 3 === 1 ? 'number' : 'text',
    }));
    const manyRows = (count = 2000) => Array.from({ length: count }, (_, row) => {
        const record = { id: row + 1 };
        for (let index = 0; index < 10; index += 1) record[`c${index}`] = index % 3 === 1 ? row * index : `R${row}C${index}`;
        return record;
    });

    it('renders a window of 2,000 x 10 rows and keeps aria-rowindex and navigation correct', () => {
        const started = performance.now();
        const grid = new DataGrid({ columns: wideColumns(), rows: manyRows(), virtual: true, height: '400px', rowHeight: 32 }).mount(host);
        const initial = bodyRows(grid);
        expect(initial.length).toBeGreaterThan(5);
        expect(initial.length).toBeLessThan(40);
        expect(gridEl(grid).getAttribute('aria-rowcount')).toBe('2001');
        expect(initial[0].getAttribute('aria-rowindex')).toBe('2');
        expect(grid.element.querySelector('.b4a-datagrid__body').style.height).toBe('64000px');

        focusCell(grid, 0, 0);
        press('End', { ctrlKey: true });
        let current = document.activeElement;
        expect(current.closest('[role="row"]').getAttribute('aria-rowindex')).toBe('2001');
        expect(current.getAttribute('aria-colindex')).toBe('10');
        expect(bodyRows(grid).length).toBeLessThan(40);

        press('PageUp');
        current = document.activeElement;
        expect(current.closest('[role="row"]').getAttribute('aria-rowindex')).toBe('1991');
        press('ArrowDown');
        press('ArrowDown');
        expect(document.activeElement.closest('[role="row"]').getAttribute('aria-rowindex')).toBe('1993');

        press('Enter');
        editor(grid).value = 'edited far below';
        press('Enter');
        expect(grid.getRows()[1991].c9).toBe('edited far below');

        press('Home', { ctrlKey: true });
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        const indices = bodyRows(grid).map((row) => Number(row.getAttribute('aria-rowindex')));
        expect(indices).toEqual([...indices].sort((a, b) => a - b));
        expect(indices.length).toBeLessThan(40);
        expect(performance.now() - started).toBeLessThan(3000);
        grid.destroy();
    });

    it('repositions rows and aria-rowindex after adding and removing rows', () => {
        const grid = new DataGrid({ columns: wideColumns(), rows: manyRows(100), virtual: true, height: '400px', rowHeight: 32 }).mount(host);
        expect(grid.removeRows([1, 2])).toBe(2);
        let rendered = bodyRows(grid);
        expect(rendered[0].dataset.rowKey).toBe('3');
        expect(rendered[0].getAttribute('aria-rowindex')).toBe('2');
        expect(rendered[0].style.top).toBe('0px');

        const key = grid.addRow({ c0: 'top' }, { index: 0 });
        rendered = bodyRows(grid);
        expect(rendered[0].dataset.rowKey).toBe(String(key));
        expect(rendered[1].dataset.rowKey).toBe('3');
        expect(rendered[1].getAttribute('aria-rowindex')).toBe('3');
        expect(rendered[1].style.top).toBe('32px');
        expect(gridEl(grid).getAttribute('aria-rowcount')).toBe('100');
        expect(grid.element.querySelector('.b4a-datagrid__body').style.height).toBe(`${99 * 32}px`);
        expect(grid.getActiveCell()).toEqual({ rowKey: 3, key: 'c0' });
        grid.destroy();
    });

    it('re-renders the window on scroll and keeps the focused row', async () => {
        const grid = new DataGrid({ columns: wideColumns(), rows: manyRows(), virtual: true, height: '400px', rowHeight: 32 }).mount(host);
        focusCell(grid, 0, 0);
        const frame = grid.element.querySelector('.b4a-datagrid__frame');
        frame.scrollTop = 32 * 1000;
        frame.dispatchEvent(new Event('scroll'));
        await nextFrame();
        await nextFrame();
        const indices = bodyRows(grid).map((row) => Number(row.getAttribute('aria-rowindex')));
        expect(indices).toContain(1002);
        expect(indices).toContain(2);
        expect(indices).toEqual([...indices].sort((a, b) => a - b));
        expect(document.activeElement).toBe(cellAt(grid, 0, 0));
        const top = rowAt(grid, 1000).style.top;
        expect(top).toBe('32000px');
        grid.destroy();
    });
});

describe('DataGrid value and field-error contract', () => {
    it('gets and sets the value without firing onChange', () => {
        const onChange = vi.fn();
        const grid = make({ onChange });
        expect(grid.getValue()).toEqual(grid.getRows());
        expect(grid.setValue([{ id: 4, name: 'Solo' }])).toBe(grid);
        expect(grid.getValue()).toEqual([{ id: 4, name: 'Solo', total: 0 }]);
        grid.clear();
        expect(grid.getValue()).toEqual([]);
        expect(onChange).not.toHaveBeenCalled();
        grid.destroy();
    });

    it('setDisabled blocks editing, paste and clearing but keeps navigation and copy', () => {
        const grid = make({ disabled: true });
        expect(gridEl(grid).getAttribute('aria-disabled')).toBe('true');
        focusCell(grid, 0, 0);
        press('Enter');
        press('x');
        expect(editor(grid)).toBeNull();
        press('Delete');
        paste('Other');
        expect(grid.getRow(1).name).toBe('Alpha');
        press('ArrowRight');
        expect(document.activeElement).toBe(cellAt(grid, 0, 1));
        const event = clipboardEvent('copy');
        document.activeElement.dispatchEvent(event);
        expect(event.store['text/plain']).toBe('2');
        focusCell(grid, 0, 5);
        press(' ');
        expect(grid.getRow(1).done).toBe(false);

        grid.setDisabled(false);
        expect(gridEl(grid).hasAttribute('aria-disabled')).toBe(false);
        focusCell(grid, 0, 0);
        press('Enter');
        expect(editor(grid)).not.toBeNull();
        editor(grid).value = 'Discarded';
        grid.setDisabled(true);
        expect(editor(grid)).toBeNull();
        expect(grid.getRow(1).name).toBe('Alpha');
        grid.destroy();
    });

    it('implements setError / clearError below the grid', () => {
        const grid = make();
        expect(grid[FIELD_ERROR_CONTRACT]).toBe(true);
        expect(grid.setError('至少需要一列')).toBe(grid);
        const message = grid.element.querySelector('.b4a-field-error');
        expect(message.textContent).toBe('至少需要一列');
        expect(message.getAttribute('role')).toBe('alert');
        expect(grid.element.lastElementChild).toBe(message);
        expect(gridEl(grid).getAttribute('aria-invalid')).toBe('true');
        expect(gridEl(grid).getAttribute('aria-describedby')).toContain(message.id);
        expect(grid.element.querySelector('.b4a-datagrid__frame').style.outline).toContain('var(--cl-danger)');

        grid.setError('至少需要一列', { display: false });
        expect(grid.element.querySelector('.b4a-field-error')).toBeNull();
        expect(gridEl(grid).getAttribute('aria-invalid')).toBe('true');

        expect(grid.clearError()).toBe(grid);
        expect(gridEl(grid).hasAttribute('aria-invalid')).toBe(false);
        expect(grid.element.querySelector('.b4a-datagrid__frame').style.outline).toBe('');

        grid.setError('再試一次');
        grid.destroy();
        expect(host.querySelector('.b4a-field-error')).toBeNull();
    });
});

describe('DataGrid destroy', () => {
    it('removes the DOM, timers and every document/window listener', () => {
        const docAdd = vi.spyOn(document, 'addEventListener');
        const docRemove = vi.spyOn(document, 'removeEventListener');
        const winAdd = vi.spyOn(window, 'addEventListener');
        const winRemove = vi.spyOn(window, 'removeEventListener');
        const grid = make({ virtual: true });
        mouse(cellAt(grid, 0, 0), 'mousedown');
        press('c', { ctrlKey: true });
        grid.destroy();

        expect(host.querySelector('.b4a-datagrid')).toBeNull();
        expect(docAdd.mock.calls.length).toBeGreaterThan(0);
        docAdd.mock.calls.forEach(([type, handler]) => {
            expect(docRemove.mock.calls.some(([t, h]) => t === type && h === handler)).toBe(true);
        });
        winAdd.mock.calls.forEach(([type, handler]) => {
            expect(winRemove.mock.calls.some(([t, h]) => t === type && h === handler)).toBe(true);
        });
        expect(grid.snapshot().lifecycle).toBe('destroyed');

        expect(() => {
            grid.destroy();
            grid.getRows();
            grid.setRows(rows());
            grid.addRow({ id: 50 });
            grid.updateRow(1, { name: 'x' });
            grid.removeRows([1]);
            grid.validate();
            grid.acceptChanges();
            grid.revertChanges();
            grid.setDisabled(true);
            grid.setError('x');
            grid.clearError();
            grid.focusCell(2, 'name');
            grid.getChanges();
        }).not.toThrow();
    });

    it('discards an open editor without committing', () => {
        const onChange = vi.fn();
        const grid = make({ onChange });
        focusCell(grid, 0, 0);
        press('Enter');
        editor(grid).value = 'Never saved';
        grid.destroy();
        expect(onChange).not.toHaveBeenCalled();
        expect(grid.getRow(1).name).toBe('Alpha');
    });
});

describe('DataGrid locale', () => {
    it('uses the active language for labels and messages', () => {
        Locale.setLang('en');
        const grid = make({ rows: [] });
        expect(gridEl(grid).getAttribute('aria-label')).toBe('Data grid');
        expect(grid.element.querySelector('.b4a-datagrid__empty').textContent).toBe('No rows');
        expect(grid.element.querySelectorAll('[role="columnheader"]')[0].textContent).toContain('required');
        grid.setRows([{ id: 1, name: '' }]);
        expect(grid.validate()).toBe(false);
        expect(grid.getErrors()[0].message).toBe('This field is required');

        Locale.setLang('zh-TW');
        grid.validate();
        expect(grid.getErrors()[0].message).toBe('此欄為必填');
        grid.destroy();
    });
});
