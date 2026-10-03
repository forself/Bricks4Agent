import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { Transfer } from '../../ui_components/form/Transfer/Transfer.js';
import TransferDefault from '../../ui_components/form/Transfer/index.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';
import Locale from '../../ui_components/i18n/index.js';

const ROOMS = [
    { value: 'a', label: 'Alpha room' },
    { value: 'b', label: 'Bravo room', description: 'Second floor' },
    { value: 'c', label: 'Charlie room' },
    { value: 'd', label: 'Delta room' },
    { value: 'e', label: 'Échelle room' },
];

let host;
let created = [];
beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
});
afterEach(() => {
    created.forEach((instance) => instance.destroy());
    created = [];
    host.remove();
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
});

const create = (options = {}) => {
    const instance = new Transfer({ items: ROOMS, ...options }).mount(host);
    created.push(instance);
    return instance;
};
const listOf = (transfer, side) => transfer.element.querySelector(`.cl-transfer__panel--${side} [role="listbox"]`);
const rowsOf = (transfer, side) => [...listOf(transfer, side).querySelectorAll('[role="option"]')];
const valuesOf = (transfer, side) => rowsOf(transfer, side).map((row) => row.dataset.value);
const rowFor = (transfer, side, value) => rowsOf(transfer, side).find((row) => row.dataset.value === value);
const checkedOf = (transfer, side) => rowsOf(transfer, side)
    .filter((row) => row.getAttribute('aria-selected') === 'true')
    .map((row) => row.dataset.value);
const key = (target, keyName, init = {}) => target.dispatchEvent(new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true, ...init }));
const click = (target, init = {}) => target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ...init }));
const button = (transfer, name) => transfer.element.querySelector(`.cl-transfer__button--${name}`);
const countOf = (transfer, side) => transfer.element.querySelector(`.cl-transfer__panel--${side} .cl-transfer__count`).textContent;
const searchOf = (transfer, side) => transfer.element.querySelector(`.cl-transfer__panel--${side} .cl-transfer__search`);
const typeSearch = (transfer, side, text) => {
    const input = searchOf(transfer, side);
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
};

function trackGlobalListeners() {
    const added = [];
    const removed = [];
    for (const target of [window, document]) {
        const originalAdd = target.addEventListener;
        const originalRemove = target.removeEventListener;
        vi.spyOn(target, 'addEventListener').mockImplementation(function (type, listener, options) {
            added.push({ target, type, listener, capture: typeof options === 'boolean' ? options : Boolean(options?.capture) });
            return originalAdd.call(this, type, listener, options);
        });
        vi.spyOn(target, 'removeEventListener').mockImplementation(function (type, listener, options) {
            removed.push({ target, type, listener, capture: typeof options === 'boolean' ? options : Boolean(options?.capture) });
            return originalRemove.call(this, type, listener, options);
        });
    }
    return {
        added,
        active: () => added.filter((entry) => !removed.some((gone) => gone.target === entry.target
            && gone.type === entry.type && gone.listener === entry.listener && gone.capture === entry.capture)),
    };
}

describe('Transfer — defaults and structure', () => {
    it('exports the class as default and named export', () => {
        expect(TransferDefault).toBe(Transfer);
        expect(Transfer.prototype[FIELD_ERROR_CONTRACT]).toBe(true);
    });

    it('renders two multi-select listboxes with localized titles and counters', () => {
        const transfer = create({ value: ['c', 'a'] });
        for (const side of ['source', 'target']) {
            const list = listOf(transfer, side);
            expect(list.getAttribute('role')).toBe('listbox');
            expect(list.getAttribute('aria-multiselectable')).toBe('true');
            const title = document.getElementById(list.getAttribute('aria-labelledby'));
            expect(title.textContent).toBe(side === 'source' ? '可選項目' : '已選項目');
            expect(document.getElementById(list.getAttribute('aria-describedby')).textContent).toContain('空白鍵');
        }
        expect(valuesOf(transfer, 'source')).toEqual(['b', 'd', 'e']);
        // 已選清單維持 value 的順序
        expect(valuesOf(transfer, 'target')).toEqual(['c', 'a']);
        expect(countOf(transfer, 'source')).toBe('0/3');
        expect(countOf(transfer, 'target')).toBe('0/2');
        expect(transfer.getValue()).toEqual(['c', 'a']);
        expect(transfer.element.querySelectorAll('.cl-transfer__search')).toHaveLength(2);
        expect(transfer.element.querySelectorAll('.cl-transfer__select-all')).toHaveLength(2);
        expect(transfer.element.querySelector('.cl-transfer__sort')).toBeNull();
    });

    it('renders text with textContent, including description, and never parses markup', () => {
        const transfer = create({ items: [{ value: 'x', label: '<img src=x onerror=alert(1)>', description: '<b>bold</b>' }] });
        const row = rowsOf(transfer, 'source')[0];
        expect(row.querySelector('img')).toBeNull();
        expect(row.querySelector('b')).toBeNull();
        expect(row.querySelector('.cl-transfer__label').textContent).toBe('<img src=x onerror=alert(1)>');
        expect(row.querySelector('.cl-transfer__description').textContent).toBe('<b>bold</b>');
    });

    it('drops unknown and duplicate values and keeps only one roving tab stop per list', () => {
        const transfer = create({ value: ['b', 'zzz', 'b'] });
        expect(transfer.getValue()).toEqual(['b']);
        const tabStops = rowsOf(transfer, 'source').filter((row) => row.tabIndex === 0);
        expect(tabStops).toHaveLength(1);
        expect(tabStops[0].dataset.value).toBe('a');
    });

    it('honours searchable/showSelectAll/height/disabled options', () => {
        const transfer = create({ searchable: false, showSelectAll: false, height: 150, disabled: true, value: ['a'] });
        expect(transfer.element.querySelector('.cl-transfer__search')).toBeNull();
        expect(transfer.element.querySelector('.cl-transfer__select-all')).toBeNull();
        expect(listOf(transfer, 'source').style.height).toBe('150px');
        expect(transfer.element.getAttribute('aria-disabled')).toBe('true');
        expect(rowsOf(transfer, 'target')[0].getAttribute('aria-disabled')).toBe('true');
        expect(button(transfer, 'to-source').disabled).toBe(true);
        const custom = create({ height: '10rem' });
        expect(listOf(custom, 'target').style.height).toBe('10rem');
    });

    it('uses custom titles and renderItem nodes', () => {
        const transfer = create({
            titles: ['Pool', 'Chosen'],
            renderItem: (item) => {
                const node = document.createElement('strong');
                node.textContent = `#${item.value}`;
                return node;
            },
        });
        expect(transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__title').textContent).toBe('Pool');
        const row = rowsOf(transfer, 'source')[0];
        expect(row.querySelector('strong').textContent).toBe('#a');
        expect(row.getAttribute('aria-label')).toBe('Alpha room');
    });
});

describe('Transfer — keyboard', () => {
    it('moves roving focus with arrows/Home/End and toggles with Space', () => {
        const transfer = create();
        const list = listOf(transfer, 'source');
        rowsOf(transfer, 'source')[0].focus();
        key(document.activeElement, 'ArrowDown');
        expect(document.activeElement.dataset.value).toBe('b');
        expect(document.activeElement.tabIndex).toBe(0);
        expect(rowsOf(transfer, 'source').filter((row) => row.tabIndex === 0)).toHaveLength(1);
        key(document.activeElement, 'End');
        expect(document.activeElement.dataset.value).toBe('e');
        key(document.activeElement, 'Home');
        expect(document.activeElement.dataset.value).toBe('a');
        key(document.activeElement, ' ');
        expect(checkedOf(transfer, 'source')).toEqual(['a']);
        expect(countOf(transfer, 'source')).toBe('1/5');
        key(document.activeElement, ' ');
        expect(checkedOf(transfer, 'source')).toEqual([]);
        expect(list.contains(document.activeElement)).toBe(true);
    });

    it('extends the checked range with Shift+Arrow and Shift+Space', () => {
        const transfer = create();
        rowsOf(transfer, 'source')[1].focus();
        key(document.activeElement, ' ');
        key(document.activeElement, 'ArrowDown', { shiftKey: true });
        key(document.activeElement, 'ArrowDown', { shiftKey: true });
        expect(checkedOf(transfer, 'source')).toEqual(['b', 'c', 'd']);
        key(document.activeElement, 'Home');
        key(document.activeElement, ' ', { shiftKey: true });
        expect(checkedOf(transfer, 'source')).toEqual(['a', 'b', 'c', 'd']);
    });

    it('Ctrl/Cmd+A checks every visible item and unchecks when all are checked', () => {
        const transfer = create();
        rowsOf(transfer, 'source')[0].focus();
        key(document.activeElement, 'a', { ctrlKey: true });
        expect(checkedOf(transfer, 'source')).toEqual(['a', 'b', 'c', 'd', 'e']);
        const selectAll = transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__select-all');
        expect(selectAll.checked).toBe(true);
        key(document.activeElement, 'A', { metaKey: true });
        expect(checkedOf(transfer, 'source')).toEqual([]);
        expect(selectAll.checked).toBe(false);
    });

    it('Enter moves the checked items, or the focused item when nothing is checked', () => {
        const onChange = vi.fn();
        const transfer = create({ onChange });
        rowsOf(transfer, 'source')[2].focus();
        key(document.activeElement, 'Enter');
        expect(onChange).toHaveBeenLastCalledWith(['c'], { moved: ['c'], direction: 'right' });
        // 焦點留在來源清單，落在原位置的下一項
        expect(listOf(transfer, 'source').contains(document.activeElement)).toBe(true);
        expect(document.activeElement.dataset.value).toBe('d');

        key(document.activeElement, ' ');
        key(document.activeElement, 'ArrowUp');
        key(document.activeElement, ' ');
        key(document.activeElement, 'Enter');
        // 新移入的項目依可選清單順序附加在最後
        expect(onChange).toHaveBeenLastCalledWith(['c', 'b', 'd'], { moved: ['b', 'd'], direction: 'right' });
        expect(valuesOf(transfer, 'target')).toEqual(['c', 'b', 'd']);

        rowFor(transfer, 'target', 'b').focus();
        key(document.activeElement, 'Enter');
        expect(onChange).toHaveBeenLastCalledWith(['c', 'd'], { moved: ['b'], direction: 'left' });
        expect(valuesOf(transfer, 'source')).toEqual(['a', 'b', 'e']);
    });

    it('moves focus to the first remaining item after the focused one', () => {
        const transfer = create();
        click(rowFor(transfer, 'source', 'a'));
        click(rowFor(transfer, 'source', 'c'));
        key(document.activeElement, 'Enter');
        // 焦點原在 c（被移走）→ 落在其後第一個留下的 d，而不是沿用索引落到 e
        expect(transfer.getValue()).toEqual(['a', 'c']);
        expect(document.activeElement.dataset.value).toBe('d');
        click(rowFor(transfer, 'source', 'e'));
        key(document.activeElement, 'Enter');
        expect(document.activeElement.dataset.value).toBe('d');
    });

    it('keeps focus on the list after moving its last item', () => {
        const transfer = create({ items: ROOMS.slice(0, 1) });
        rowsOf(transfer, 'source')[0].focus();
        key(document.activeElement, 'Enter');
        expect(transfer.getValue()).toEqual(['a']);
        expect(document.activeElement).toBe(listOf(transfer, 'source'));
        expect(transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__empty').textContent).toBe('無資料');
    });

    it('ArrowDown in the search box moves focus into the list; Escape clears the search', () => {
        const transfer = create();
        typeSearch(transfer, 'source', 'room');
        const search = searchOf(transfer, 'source');
        search.focus();
        key(search, 'ArrowDown');
        expect(document.activeElement.dataset.value).toBe('a');
        typeSearch(transfer, 'source', 'bravo');
        search.focus();
        const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
        search.dispatchEvent(escape);
        expect(escape.defaultPrevented).toBe(true);
        expect(search.value).toBe('');
        expect(valuesOf(transfer, 'source')).toHaveLength(5);
        const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
        search.dispatchEvent(enter);
        expect(enter.defaultPrevented).toBe(true);
    });
});

describe('Transfer — pointer interaction and move buttons', () => {
    it('click toggles, Shift+click checks a range, double-click moves one item', () => {
        const onChange = vi.fn();
        const transfer = create({ onChange });
        click(rowFor(transfer, 'source', 'a'));
        click(rowFor(transfer, 'source', 'c'), { shiftKey: true });
        expect(checkedOf(transfer, 'source')).toEqual(['a', 'b', 'c']);
        click(rowFor(transfer, 'source', 'b'));
        expect(checkedOf(transfer, 'source')).toEqual(['a', 'c']);

        const row = rowFor(transfer, 'source', 'e');
        click(row);
        click(row);
        row.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
        expect(onChange).toHaveBeenLastCalledWith(['e'], { moved: ['e'], direction: 'right' });
        expect(checkedOf(transfer, 'source')).toEqual(['a', 'c']);
    });

    it('move buttons are labelled and disabled when nothing applies', () => {
        const onChange = vi.fn();
        const transfer = create({ value: ['d'], onChange });
        const toTarget = button(transfer, 'to-target');
        const toSource = button(transfer, 'to-source');
        expect(toTarget.getAttribute('aria-label')).toBe('將勾選的項目移到已選項目');
        expect(toSource.getAttribute('aria-label')).toBe('將勾選的項目移回可選項目');
        expect(toTarget.disabled).toBe(true);
        expect(toSource.disabled).toBe(true);

        click(rowFor(transfer, 'source', 'b'));
        click(rowFor(transfer, 'source', 'a'));
        expect(toTarget.disabled).toBe(false);
        toTarget.focus();
        toTarget.click();
        expect(transfer.getValue()).toEqual(['d', 'a', 'b']);
        expect(onChange).toHaveBeenLastCalledWith(['d', 'a', 'b'], { moved: ['a', 'b'], direction: 'right' });
        expect(toTarget.disabled).toBe(true);
        // 觸發的按鈕變成停用時，焦點移到來源清單而不是遺失
        expect(listOf(transfer, 'source').contains(document.activeElement)).toBe(true);
        expect(transfer.element.querySelector('.cl-transfer__live').textContent).toBe('已將 2 項移到已選項目');

        click(rowFor(transfer, 'target', 'd'));
        expect(toSource.disabled).toBe(false);
        toSource.click();
        expect(transfer.getValue()).toEqual(['a', 'b']);
    });

    it('select-all checkbox follows visible items and becomes indeterminate', () => {
        const transfer = create();
        const selectAll = transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__select-all');
        expect(selectAll.getAttribute('aria-label')).toBe('全選可選項目中顯示的項目');
        click(rowFor(transfer, 'source', 'a'));
        expect(selectAll.indeterminate).toBe(true);
        typeSearch(transfer, 'source', 'charlie');
        selectAll.checked = true;
        selectAll.dispatchEvent(new Event('change', { bubbles: true }));
        typeSearch(transfer, 'source', '');
        expect(checkedOf(transfer, 'source')).toEqual(['a', 'c']);
    });

    it('disabled items cannot be checked or moved', () => {
        const items = [{ value: 'x', label: 'Locked', disabled: true }, { value: 'y', label: 'Free' }];
        const transfer = create({ items });
        const locked = rowFor(transfer, 'source', 'x');
        expect(locked.getAttribute('aria-disabled')).toBe('true');
        click(locked);
        locked.focus();
        key(locked, ' ');
        key(locked, 'Enter');
        locked.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
        key(locked, 'a', { ctrlKey: true });
        expect(checkedOf(transfer, 'source')).toEqual(['y']);
        expect(transfer.getValue()).toEqual([]);
    });
});

describe('Transfer — sortable', () => {
    it('reorders checked items with the up/down buttons and disables them at the edges', () => {
        const onChange = vi.fn();
        const transfer = create({ sortable: true, value: ['a', 'b', 'c', 'd'], onChange });
        const up = button(transfer, 'up');
        const down = button(transfer, 'down');
        expect(up.getAttribute('aria-label')).toBe('將勾選的項目上移');
        expect(up.disabled).toBe(true);
        expect(down.disabled).toBe(true);

        click(rowFor(transfer, 'target', 'c'));
        click(rowFor(transfer, 'target', 'd'));
        expect(up.disabled).toBe(false);
        expect(down.disabled).toBe(true);
        up.click();
        expect(transfer.getValue()).toEqual(['a', 'c', 'd', 'b']);
        expect(onChange).toHaveBeenLastCalledWith(['a', 'c', 'd', 'b'], { moved: ['c', 'd'], direction: 'up' });
        up.click();
        expect(transfer.getValue()).toEqual(['c', 'd', 'a', 'b']);
        expect(up.disabled).toBe(true);
        expect(down.disabled).toBe(false);
        down.click();
        expect(valuesOf(transfer, 'target')).toEqual(['a', 'c', 'd', 'b']);
    });

    it('Alt+ArrowUp/Down moves the focused item and keeps focus on it', () => {
        const onChange = vi.fn();
        const transfer = create({ sortable: true, value: ['a', 'b', 'c'], onChange });
        rowFor(transfer, 'target', 'c').focus();
        key(document.activeElement, 'ArrowUp', { altKey: true });
        expect(transfer.getValue()).toEqual(['a', 'c', 'b']);
        expect(document.activeElement.dataset.value).toBe('c');
        key(document.activeElement, 'ArrowUp', { altKey: true });
        expect(transfer.getValue()).toEqual(['c', 'a', 'b']);
        key(document.activeElement, 'ArrowUp', { altKey: true });
        expect(onChange).toHaveBeenCalledTimes(2);
        key(document.activeElement, 'ArrowDown', { altKey: true });
        expect(onChange).toHaveBeenLastCalledWith(['a', 'c', 'b'], { moved: ['c'], direction: 'down' });
    });

    it('only permutes visible items when the selected list is filtered', () => {
        const items = [
            { value: 'a', label: 'Alpha' },
            { value: 'b', label: 'Bravo' },
            { value: 'c', label: 'Charlie' },
            { value: 'd', label: 'Delta' },
        ];
        const transfer = create({ items, sortable: true, value: ['a', 'b', 'c', 'd'] });
        // 含 l 的只有 Alpha、Charlie、Delta；Bravo 被隱藏且必須維持原位
        typeSearch(transfer, 'target', 'l');
        expect(valuesOf(transfer, 'target')).toEqual(['a', 'c', 'd']);
        click(rowFor(transfer, 'target', 'd'));
        button(transfer, 'up').click();
        expect(transfer.getValue()).toEqual(['a', 'b', 'd', 'c']);
    });

    it('Alt+Arrow does nothing when sortable is off', () => {
        const onChange = vi.fn();
        const transfer = create({ value: ['a', 'b'], onChange });
        rowFor(transfer, 'target', 'b').focus();
        key(document.activeElement, 'ArrowUp', { altKey: true });
        expect(transfer.getValue()).toEqual(['a', 'b']);
        expect(onChange).not.toHaveBeenCalled();
    });
});

describe('Transfer — search', () => {
    it('filters each list independently, ignoring case and diacritics', () => {
        const transfer = create({ value: ['a'] });
        typeSearch(transfer, 'source', 'ECHELLE');
        expect(valuesOf(transfer, 'source')).toEqual(['e']);
        typeSearch(transfer, 'source', 'échelle');
        expect(valuesOf(transfer, 'source')).toEqual(['e']);
        typeSearch(transfer, 'source', 'second');
        expect(valuesOf(transfer, 'source')).toEqual(['b']);
        expect(valuesOf(transfer, 'target')).toEqual(['a']);
        typeSearch(transfer, 'source', 'nothing');
        expect(valuesOf(transfer, 'source')).toEqual([]);
        expect(transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__empty').textContent).toBe('無符合項目');
        // 全形字母也能比對（NFKD 相容分解）
        typeSearch(transfer, 'source', 'ＤＥＬＴＡ');
        expect(valuesOf(transfer, 'source')).toEqual(['d']);
    });
});

describe('Transfer — maxSelected', () => {
    it('moves only up to the limit, announces it and disables the button when full', () => {
        const onChange = vi.fn();
        const transfer = create({ maxSelected: 2, value: ['a'], onChange });
        expect(transfer.element.querySelector('.cl-transfer__max').textContent).toBe('最多 2 項');
        click(rowFor(transfer, 'source', 'b'));
        click(rowFor(transfer, 'source', 'c'));
        button(transfer, 'to-target').click();
        expect(onChange).toHaveBeenLastCalledWith(['a', 'b'], { moved: ['b'], direction: 'right' });
        expect(checkedOf(transfer, 'source')).toEqual(['c']);
        expect(transfer.element.querySelector('.cl-transfer__live').textContent).toContain('最多可選 2 項');
        expect(button(transfer, 'to-target').disabled).toBe(true);

        rowFor(transfer, 'source', 'c').focus();
        key(document.activeElement, 'Enter');
        expect(transfer.getValue()).toEqual(['a', 'b']);
        expect(transfer.element.querySelector('.cl-transfer__live').textContent).toBe('已達上限，最多可選 2 項');
    });
});

describe('Transfer — value contract', () => {
    it('setValue does not emit unless asked; setItems/clear/setDisabled work', () => {
        const onChange = vi.fn();
        const transfer = create({ onChange });
        transfer.setValue(['e', 'b', 'nope']);
        expect(transfer.getValue()).toEqual(['e', 'b']);
        expect(valuesOf(transfer, 'target')).toEqual(['e', 'b']);
        expect(onChange).not.toHaveBeenCalled();
        transfer.setValue(['a'], { emit: true });
        expect(onChange).toHaveBeenCalledWith(['a'], { moved: [], direction: null });

        transfer.setItems([{ value: 'a', label: 'Alpha' }, { value: 'z', label: 'Zulu' }]);
        expect(transfer.getValue()).toEqual(['a']);
        expect(valuesOf(transfer, 'source')).toEqual(['z']);
        transfer.setItems(ROOMS.slice(1));
        expect(transfer.getValue()).toEqual([]);

        transfer.setValue(['b', 'c']);
        typeSearch(transfer, 'source', 'delta');
        transfer.clear();
        expect(transfer.getValue()).toEqual([]);
        expect(searchOf(transfer, 'source').value).toBe('');
        expect(valuesOf(transfer, 'source')).toEqual(['b', 'c', 'd', 'e']);
        expect(onChange).toHaveBeenCalledTimes(1);

        transfer.setDisabled(true);
        expect(transfer.element.getAttribute('aria-disabled')).toBe('true');
        expect(searchOf(transfer, 'source').disabled).toBe(true);
        expect(rowsOf(transfer, 'source').every((row) => row.getAttribute('aria-disabled') === 'true' && row.tabIndex === -1)).toBe(true);
        click(rowFor(transfer, 'source', 'b'));
        key(rowFor(transfer, 'source', 'b'), 'Enter');
        expect(checkedOf(transfer, 'source')).toEqual([]);
        expect(transfer.getValue()).toEqual([]);
        transfer.setDisabled(false);
        click(rowFor(transfer, 'source', 'b'));
        expect(checkedOf(transfer, 'source')).toEqual(['b']);
    });

    it('supports the field-error contract', () => {
        const transfer = create();
        transfer.setError('至少選擇一個會議室');
        const message = transfer.element.querySelector('.b4a-field-error');
        expect(message.textContent).toBe('至少選擇一個會議室');
        expect(message.getAttribute('role')).toBe('alert');
        for (const side of ['source', 'target']) {
            const list = listOf(transfer, side);
            expect(list.getAttribute('aria-invalid')).toBe('true');
            expect(list.getAttribute('aria-describedby')).toContain(message.id);
        }
        transfer.setError('只標示', { display: false });
        expect(transfer.element.querySelector('.b4a-field-error')).toBeNull();
        expect(listOf(transfer, 'target').getAttribute('aria-invalid')).toBe('true');
        transfer.clearError();
        expect(listOf(transfer, 'target').hasAttribute('aria-invalid')).toBe(false);
        expect(listOf(transfer, 'target').getAttribute('aria-describedby')).not.toContain('b4a-field-error');
        expect(transfer[FIELD_ERROR_CONTRACT]).toBe(true);
    });
});

describe('Transfer — large lists', () => {
    const many = Array.from({ length: 2000 }, (_, index) => ({ value: `v${index}`, label: `Item ${index}` }));

    it('renders incrementally and stays responsive with 2,000 items', () => {
        const onChange = vi.fn();
        const started = performance.now();
        const transfer = create({ items: many, onChange });
        const list = listOf(transfer, 'source');
        expect(rowsOf(transfer, 'source')).toHaveLength(100);
        expect(rowsOf(transfer, 'source')[0].getAttribute('aria-setsize')).toBe('2000');
        expect(countOf(transfer, 'source')).toBe('0/2000');

        list.dispatchEvent(new Event('scroll'));
        expect(rowsOf(transfer, 'source')).toHaveLength(200);

        rowsOf(transfer, 'source')[0].focus();
        key(document.activeElement, 'End');
        expect(document.activeElement.dataset.value).toBe('v1999');
        expect(document.activeElement.getAttribute('aria-posinset')).toBe('2000');

        key(document.activeElement, 'a', { ctrlKey: true });
        expect(countOf(transfer, 'source')).toBe('2000/2000');
        button(transfer, 'to-target').click();
        expect(transfer.getValue()).toHaveLength(2000);
        expect(onChange.mock.calls[0][1].moved).toHaveLength(2000);
        expect(rowsOf(transfer, 'target')).toHaveLength(100);

        typeSearch(transfer, 'target', 'item 1999');
        expect(valuesOf(transfer, 'target')).toEqual(['v1999']);
        // jsdom 很慢；這個門檻只擋住平方級的退化
        expect(performance.now() - started).toBeLessThan(5000);
    });
});

describe('Transfer — lifecycle and locale', () => {
    it('destroy removes the DOM and every global listener, and is idempotent', () => {
        const tracker = trackGlobalListeners();
        const transfer = create({ sortable: true, maxSelected: 3 });
        click(rowFor(transfer, 'source', 'a'));
        transfer.setError('x');
        expect(tracker.added.length).toBeGreaterThan(0);
        transfer.destroy();
        expect(host.children).toHaveLength(0);
        expect(document.querySelector('.cl-transfer')).toBeNull();
        expect(tracker.active()).toEqual([]);
        expect(() => {
            transfer.destroy();
            transfer.setValue(['a']);
            transfer.setItems([]);
            transfer.clear();
            transfer.setDisabled(true);
            transfer.setError('late');
            transfer.clearError();
            transfer.show();
            transfer.hide();
            transfer.getValue();
        }).not.toThrow();
    });

    it('switches strings live when the locale changes', () => {
        const transfer = create();
        expect(transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__title').textContent).toBe('可選項目');
        Locale.setLang('en');
        expect(transfer.element.querySelector('.cl-transfer__panel--source .cl-transfer__title').textContent).toBe('Available');
        expect(transfer.element.querySelector('.cl-transfer__panel--target .cl-transfer__title').textContent).toBe('Selected');
        expect(button(transfer, 'to-target').getAttribute('aria-label')).toBe('Move checked items to Selected');
        expect(searchOf(transfer, 'source').placeholder).toBe('Search');
        const empty = create({ items: [] });
        expect(empty.element.querySelector('.cl-transfer__empty').textContent).toBe('No data');
    });

    it('hide/show toggles visibility', () => {
        const transfer = create();
        transfer.hide();
        expect(transfer.element.style.display).toBe('none');
        expect(transfer.snapshot().visibility).toBe('hidden');
        transfer.show();
        expect(transfer.element.style.display).toBe('flex');
    });
});
