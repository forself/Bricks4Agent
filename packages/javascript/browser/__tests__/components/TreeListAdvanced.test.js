import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { TreeList } from '../../ui_components/common/TreeList/TreeList.js';
import Locale from '../../ui_components/i18n/index.js';

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((res, rej) => {
        resolve = res;
        reject = rej;
    });
    return { promise, resolve, reject };
}

function staffData() {
    return [
        {
            id: 'dept',
            label: 'Departments',
            children: [
                {
                    id: 'sales',
                    label: 'Sales',
                    children: [
                        { id: 'amy', label: 'Amy' },
                        { id: 'ben', label: 'Ben' },
                    ],
                },
                { id: 'ops', label: 'Operations' },
            ],
        },
        { id: 'rooms', label: 'Meeting rooms' },
    ];
}

function lazyData() {
    return [
        { id: 'projects', label: 'Projects' },
        { id: 'archive', label: 'Archive', isLeaf: true },
        { id: 'teams', label: 'Teams', hasChildren: true, children: [] },
        { id: 'static', label: 'Static', children: [{ id: 'static-1', label: 'Static child' }] },
    ];
}

const rowOf = (tree, id) => tree.element.querySelector(`.tree-node-row[data-node-id="${id}"]`);
const toggleOf = (tree, id) => tree.element.querySelector(`.tree-node-toggle[data-node-id="${id}"]`);
const checkboxOf = (tree, id) => tree.element.querySelector(`.tree-node-checkbox[data-node-id="${id}"]`);
const tabStops = (tree) => tree.element.querySelectorAll('.tree-node-row[tabindex="0"]');

function press(target, key, init = {}) {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
    target.dispatchEvent(event);
    return event;
}

describe('TreeList — advanced (checkable, lazy loading, keyboard)', () => {
    let host;

    beforeEach(() => {
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
        vi.restoreAllMocks();
        Locale.setLang('zh-TW');
    });

    describe('defaults stay unchanged', () => {
        it('declares every new capability as opt-in', () => {
            const tree = new TreeList({ data: staffData() });
            expect(tree.options.checkable).toBe(false);
            expect(tree.options.checkStrictly).toBe(false);
            expect(tree.options.checkedKeys).toEqual([]);
            expect(tree.options.onCheck).toBeNull();
            expect(tree.options.loadChildren).toBeNull();
            expect(tree.options.ariaLabel).toBe('');
            expect(tree.options.theme).toBe('modern');
            expect(tree.options.width).toBe('260px');
            tree.destroy();
        });

        it('renders the existing classes and row structure without checkboxes', () => {
            const tree = new TreeList({ data: staffData() }).mount(host);
            expect(tree.element.className).toBe('tree-list theme-modern');
            expect(tree.element.querySelectorAll('.tree-node-wrapper')).toHaveLength(2);
            expect(tree.element.querySelectorAll('.tree-node-checkbox')).toHaveLength(0);
            const row = rowOf(tree, 'dept');
            expect(row.children).toHaveLength(3);
            expect(row.children[0].className).toBe('tree-node-toggle');
            expect(row.children[0].dataset.nodeId).toBe('dept');
            expect(row.children[2].textContent).toBe('Departments');
            expect(row.hasAttribute('aria-checked')).toBe(false);
            expect(tree.element.querySelector('.tree-node-loading')).toBeNull();
            tree.destroy();
        });

        it('keeps nodes without children as leaves when loadChildren is not set', () => {
            const tree = new TreeList({
                data: [
                    { id: 'plain', label: 'Plain' },
                    { id: 'flagged', label: 'Flagged', hasChildren: true },
                ],
            }).mount(host);
            for (const id of ['plain', 'flagged']) {
                expect(toggleOf(tree, id).style.opacity).toBe('0');
                expect(rowOf(tree, id).hasAttribute('aria-expanded')).toBe(false);
                toggleOf(tree, id).click();
                expect(tree.expandedIds.has(id)).toBe(false);
            }
            tree.destroy();
        });

        it('keeps row click = select and toggle click = expand', () => {
            const onSelect = vi.fn();
            const tree = new TreeList({ data: staffData(), onSelect }).mount(host);
            rowOf(tree, 'dept').click();
            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(onSelect.mock.calls[0][0].id).toBe('dept');
            expect(tree.expandedIds.has('dept')).toBe(false);

            toggleOf(tree, 'dept').click();
            expect(tree.expandedIds.has('dept')).toBe(true);
            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(rowOf(tree, 'sales')).not.toBeNull();
            tree.destroy();
        });
    });

    describe('WAI-ARIA tree pattern', () => {
        it('exposes tree, treeitem and group roles with level, size and position', () => {
            const tree = new TreeList({ data: staffData(), ariaLabel: 'Staff directory', activeId: 'amy' }).mount(host);
            expect(tree.element.getAttribute('role')).toBe('tree');
            expect(tree.element.getAttribute('aria-label')).toBe('Staff directory');

            const dept = rowOf(tree, 'dept');
            expect(dept.getAttribute('role')).toBe('treeitem');
            expect(dept.getAttribute('aria-level')).toBe('1');
            expect(dept.getAttribute('aria-posinset')).toBe('1');
            expect(dept.getAttribute('aria-setsize')).toBe('2');
            expect(dept.getAttribute('aria-expanded')).toBe('true');
            expect(dept.parentNode.children[1].getAttribute('role')).toBe('group');

            const ben = rowOf(tree, 'ben');
            expect(ben.getAttribute('aria-level')).toBe('3');
            expect(ben.getAttribute('aria-posinset')).toBe('2');
            expect(ben.getAttribute('aria-setsize')).toBe('2');
            expect(ben.hasAttribute('aria-expanded')).toBe(false);

            expect(rowOf(tree, 'amy').getAttribute('aria-selected')).toBe('true');
            expect(ben.getAttribute('aria-selected')).toBe('false');
            expect(rowOf(tree, 'amy').getAttribute('tabindex')).toBe('0');
            expect(tabStops(tree)).toHaveLength(1);

            const toggle = toggleOf(tree, 'dept');
            expect(toggle.getAttribute('aria-hidden')).toBe('true');
            tree.destroy();
        });

        it('keeps aria-selected, aria-expanded and the single tab stop in sync', () => {
            const tree = new TreeList({ data: staffData() }).mount(host);
            expect(rowOf(tree, 'dept').getAttribute('tabindex')).toBe('0');
            expect(rowOf(tree, 'rooms').getAttribute('tabindex')).toBe('-1');

            rowOf(tree, 'rooms').click();
            expect(rowOf(tree, 'rooms').getAttribute('aria-selected')).toBe('true');

            toggleOf(tree, 'dept').click();
            expect(rowOf(tree, 'dept').getAttribute('aria-expanded')).toBe('true');
            toggleOf(tree, 'dept').click();
            expect(rowOf(tree, 'dept').getAttribute('aria-expanded')).toBe('false');

            tree.setActive('ben');
            expect(rowOf(tree, 'ben').getAttribute('aria-selected')).toBe('true');
            expect(rowOf(tree, 'rooms').getAttribute('aria-selected')).toBe('false');
            expect(rowOf(tree, 'ben').getAttribute('tabindex')).toBe('0');
            expect(tabStops(tree)).toHaveLength(1);
            tree.destroy();
        });

        it('moves the tab stop to a row that receives focus', () => {
            const tree = new TreeList({ data: staffData() }).mount(host);
            rowOf(tree, 'rooms').focus();
            expect(document.activeElement).toBe(rowOf(tree, 'rooms'));
            expect(rowOf(tree, 'rooms').getAttribute('tabindex')).toBe('0');
            expect(rowOf(tree, 'dept').getAttribute('tabindex')).toBe('-1');
            tree.destroy();
        });
    });

    describe('keyboard', () => {
        it('moves, expands and collapses with arrow keys, Home and End', () => {
            const tree = new TreeList({ data: staffData() }).mount(host);
            const dept = rowOf(tree, 'dept');
            dept.focus();

            expect(press(dept, 'ArrowDown').defaultPrevented).toBe(true);
            expect(document.activeElement).toBe(rowOf(tree, 'rooms'));
            press(document.activeElement, 'ArrowUp');
            expect(document.activeElement).toBe(dept);

            press(dept, 'ArrowRight');
            expect(tree.expandedIds.has('dept')).toBe(true);
            expect(document.activeElement).toBe(dept);
            press(dept, 'ArrowRight');
            expect(document.activeElement).toBe(rowOf(tree, 'sales'));

            press(document.activeElement, 'ArrowLeft');
            expect(document.activeElement).toBe(dept);
            press(dept, 'ArrowLeft');
            expect(tree.expandedIds.has('dept')).toBe(false);
            expect(document.activeElement).toBe(dept);

            press(dept, 'End');
            expect(document.activeElement).toBe(rowOf(tree, 'rooms'));
            press(document.activeElement, 'Home');
            expect(document.activeElement).toBe(dept);
            expect(tabStops(tree)).toHaveLength(1);
            expect(tabStops(tree)[0]).toBe(dept);
            tree.destroy();
        });

        it('selects with Enter and, when not checkable, with Space', () => {
            const onSelect = vi.fn();
            const tree = new TreeList({ data: staffData(), onSelect }).mount(host);
            const rooms = rowOf(tree, 'rooms');
            rooms.focus();
            press(rooms, 'Enter');
            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(tree.activeId).toBe('rooms');

            const dept = rowOf(tree, 'dept');
            dept.focus();
            expect(press(dept, ' ').defaultPrevented).toBe(true);
            expect(onSelect).toHaveBeenCalledTimes(2);
            expect(tree.activeId).toBe('dept');
            tree.destroy();
        });

        it('ignores modified keys and keys it does not handle', () => {
            const tree = new TreeList({ data: staffData() }).mount(host);
            const dept = rowOf(tree, 'dept');
            dept.focus();
            expect(press(dept, 'ArrowDown', { ctrlKey: true }).defaultPrevented).toBe(false);
            expect(press(dept, 'a').defaultPrevented).toBe(false);
            expect(document.activeElement).toBe(dept);
            tree.destroy();
        });
    });

    describe('checkable with cascading', () => {
        it('renders a checkbox per row and keeps the row structure otherwise', () => {
            const tree = new TreeList({ data: staffData(), checkable: true }).mount(host);
            const row = rowOf(tree, 'dept');
            const checkbox = checkboxOf(tree, 'dept');
            expect(row.children).toHaveLength(4);
            expect(row.children[1]).toBe(checkbox);
            expect(checkbox.type).toBe('checkbox');
            expect(checkbox.tabIndex).toBe(-1);
            expect(checkbox.getAttribute('aria-hidden')).toBe('true');
            expect(row.getAttribute('aria-checked')).toBe('false');
            tree.destroy();
        });

        it('checking a parent checks all loaded descendants, including collapsed ones', () => {
            const onCheck = vi.fn();
            const onSelect = vi.fn();
            const tree = new TreeList({ data: staffData(), checkable: true, onCheck, onSelect }).mount(host);
            checkboxOf(tree, 'dept').click();

            expect(tree.getCheckedKeys()).toEqual(['dept', 'sales', 'amy', 'ben', 'ops']);
            expect(onCheck).toHaveBeenCalledTimes(1);
            const [keys, info] = onCheck.mock.calls[0];
            expect(keys).toEqual(['dept', 'sales', 'amy', 'ben', 'ops']);
            expect(info.node.id).toBe('dept');
            expect(info.checked).toBe(true);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('true');
            expect(checkboxOf(tree, 'dept').checked).toBe(true);
            // 勾選不等於選取
            expect(onSelect).not.toHaveBeenCalled();
            expect(tree.activeId).toBeNull();

            tree.setActive('amy');
            expect(rowOf(tree, 'amy').getAttribute('aria-checked')).toBe('true');
            expect(checkboxOf(tree, 'ben').checked).toBe(true);
            tree.destroy();
        });

        it('derives checked and mixed parents from their children', () => {
            const tree = new TreeList({ data: staffData(), checkable: true, activeId: 'amy' }).mount(host);
            checkboxOf(tree, 'amy').click();
            expect(rowOf(tree, 'sales').getAttribute('aria-checked')).toBe('mixed');
            expect(checkboxOf(tree, 'sales').indeterminate).toBe(true);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('mixed');

            checkboxOf(tree, 'ben').click();
            expect(rowOf(tree, 'sales').getAttribute('aria-checked')).toBe('true');
            expect(checkboxOf(tree, 'sales').indeterminate).toBe(false);
            expect(checkboxOf(tree, 'sales').checked).toBe(true);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('mixed');

            // 半選節點點一下 = 全部勾選
            checkboxOf(tree, 'dept').click();
            expect(tree.getCheckedKeys()).toEqual(['dept', 'sales', 'amy', 'ben', 'ops']);

            checkboxOf(tree, 'amy').click();
            expect(rowOf(tree, 'sales').getAttribute('aria-checked')).toBe('mixed');
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('mixed');
            expect(tree.getCheckedKeys()).toEqual(['ben', 'ops']);
            tree.destroy();
        });

        it('applies initial checkedKeys with cascading', () => {
            const tree = new TreeList({ data: staffData(), checkable: true, checkedKeys: ['sales'] }).mount(host);
            expect(tree.getCheckedKeys()).toEqual(['sales', 'amy', 'ben']);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('mixed');
            tree.destroy();
        });
    });

    describe('checkStrictly', () => {
        it('checks nodes independently without mixed states', () => {
            const tree = new TreeList({ data: staffData(), checkable: true, checkStrictly: true, activeId: 'amy' }).mount(host);
            checkboxOf(tree, 'dept').click();
            expect(tree.getCheckedKeys()).toEqual(['dept']);

            checkboxOf(tree, 'amy').click();
            checkboxOf(tree, 'ben').click();
            expect(rowOf(tree, 'sales').getAttribute('aria-checked')).toBe('false');
            expect(tree.getCheckedKeys({ includeIndeterminate: true })).toEqual(['dept', 'amy', 'ben']);

            checkboxOf(tree, 'dept').click();
            expect(tree.getCheckedKeys()).toEqual(['amy', 'ben']);

            tree.setCheckedKeys(['sales']);
            expect(tree.getCheckedKeys()).toEqual(['sales']);
            expect(rowOf(tree, 'amy').getAttribute('aria-checked')).toBe('false');
            tree.destroy();
        });
    });

    describe('disabled nodes', () => {
        function disabledData() {
            return [{
                id: 'dept',
                label: 'Departments',
                children: [
                    { id: 'sales', label: 'Sales', disableCheckbox: true },
                    { id: 'ops', label: 'Operations' },
                    { id: 'hr', label: 'HR', disabled: true, children: [{ id: 'hr-1', label: 'Recruiting' }] },
                ],
            }];
        }

        it('cannot be toggled by click or Space and are skipped by cascades', () => {
            const onCheck = vi.fn();
            const tree = new TreeList({ data: disabledData(), checkable: true, onCheck, activeId: 'hr-1' }).mount(host);
            expect(checkboxOf(tree, 'sales').disabled).toBe(true);
            expect(checkboxOf(tree, 'hr').disabled).toBe(true);

            checkboxOf(tree, 'sales').dispatchEvent(new MouseEvent('click', { bubbles: true }));
            const sales = rowOf(tree, 'sales');
            sales.focus();
            press(sales, ' ');
            expect(onCheck).not.toHaveBeenCalled();
            expect(sales.getAttribute('aria-checked')).toBe('false');
            expect(checkboxOf(tree, 'sales').checked).toBe(false);

            checkboxOf(tree, 'dept').click();
            expect(tree.getCheckedKeys()).toEqual(['dept', 'ops']);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('true');
            expect(rowOf(tree, 'hr').getAttribute('aria-checked')).toBe('false');
            // 連動不會穿過停用節點
            expect(rowOf(tree, 'hr-1').getAttribute('aria-checked')).toBe('false');

            // 停用節點底下的可用節點仍可勾選,但不會往上推導停用節點
            checkboxOf(tree, 'hr-1').click();
            expect(rowOf(tree, 'hr').getAttribute('aria-checked')).toBe('false');
            expect(tree.getCheckedKeys()).toEqual(['dept', 'ops', 'hr-1']);
            tree.destroy();
        });

        it('keeps programmatic state of disabled nodes and stays selectable', () => {
            const onSelect = vi.fn();
            const tree = new TreeList({ data: disabledData(), checkable: true, onSelect, activeId: 'ops' }).mount(host);
            tree.setCheckedKeys(['sales']);
            expect(rowOf(tree, 'sales').getAttribute('aria-checked')).toBe('true');

            checkboxOf(tree, 'dept').click();
            checkboxOf(tree, 'dept').click();
            expect(tree.getCheckedKeys()).toEqual(['sales']);

            tree.checkAll();
            expect(tree.getCheckedKeys()).toEqual(['dept', 'sales', 'ops', 'hr-1']);
            tree.uncheckAll();
            expect(tree.getCheckedKeys()).toEqual(['sales']);

            // 為維持既有行為,disabled 只影響勾選,不阻擋單選
            rowOf(tree, 'hr').click();
            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(tree.activeId).toBe('hr');
            tree.destroy();
        });
    });

    describe('Space / Enter on checkable trees', () => {
        it('Space toggles the check of the focused node while Enter keeps selecting', () => {
            const onSelect = vi.fn();
            const onCheck = vi.fn();
            const tree = new TreeList({ data: staffData(), checkable: true, onSelect, onCheck }).mount(host);
            const rooms = rowOf(tree, 'rooms');
            rooms.focus();

            expect(press(rooms, ' ').defaultPrevented).toBe(true);
            expect(onCheck).toHaveBeenCalledTimes(1);
            expect(onCheck.mock.calls[0][0]).toEqual(['rooms']);
            expect(onCheck.mock.calls[0][1]).toMatchObject({ checked: true });
            expect(onSelect).not.toHaveBeenCalled();
            expect(rooms.getAttribute('aria-checked')).toBe('true');
            expect(checkboxOf(tree, 'rooms').checked).toBe(true);

            press(rooms, 'Enter');
            expect(onSelect).toHaveBeenCalledTimes(1);
            expect(onCheck).toHaveBeenCalledTimes(1);
            expect(rooms.getAttribute('aria-checked')).toBe('true');

            press(rooms, ' ');
            expect(onCheck).toHaveBeenCalledTimes(2);
            expect(onCheck.mock.calls[1][1]).toMatchObject({ checked: false });
            expect(tree.getCheckedKeys()).toEqual([]);
            tree.destroy();
        });
    });

    describe('getCheckedKeys and programmatic API', () => {
        it('supports leafOnly and includeIndeterminate in tree order', () => {
            const tree = new TreeList({ data: staffData(), checkable: true, checkedKeys: ['amy', 'ops'] });
            expect(tree.getCheckedKeys()).toEqual(['amy', 'ops']);
            expect(tree.getCheckedKeys({ includeIndeterminate: true })).toEqual(['dept', 'sales', 'amy', 'ops']);
            tree.setCheckedKeys(['sales']);
            expect(tree.getCheckedKeys()).toEqual(['sales', 'amy', 'ben']);
            expect(tree.getCheckedKeys({ leafOnly: true })).toEqual(['amy', 'ben']);
            expect(tree.getCheckedKeys({ leafOnly: true, includeIndeterminate: true })).toEqual(['amy', 'ben']);
            tree.destroy();
        });

        it('setCheckedKeys, checkAll and uncheckAll never fire onCheck', () => {
            const onCheck = vi.fn();
            const tree = new TreeList({ data: staffData(), checkable: true, onCheck }).mount(host);
            expect(tree.setCheckedKeys(['amy', 'ben', 'ops'])).toBe(tree);
            expect(tree.getCheckedKeys()).toEqual(['dept', 'sales', 'amy', 'ben', 'ops']);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('true');

            tree.setCheckedKeys(['ghost']);
            expect(tree.getCheckedKeys()).toEqual(['ghost']);
            expect(rowOf(tree, 'dept').getAttribute('aria-checked')).toBe('false');

            expect(tree.checkAll()).toBe(tree);
            expect(tree.getCheckedKeys()).toEqual(['dept', 'sales', 'amy', 'ben', 'ops', 'rooms', 'ghost']);
            expect(tree.uncheckAll()).toBe(tree);
            expect(tree.getCheckedKeys()).toEqual([]);
            expect(rowOf(tree, 'rooms').getAttribute('aria-checked')).toBe('false');

            tree.setCheckedKeys(null);
            expect(tree.getCheckedKeys()).toEqual([]);
            expect(onCheck).not.toHaveBeenCalled();
            tree.destroy();
        });
    });

    describe('lazy loading', () => {
        it('shows expanders only for lazy candidates and loads once on expand', async () => {
            const load = deferred();
            const loadChildren = vi.fn(() => load.promise);
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);

            expect(toggleOf(tree, 'projects').style.opacity).toBe('0.7');
            expect(rowOf(tree, 'projects').getAttribute('aria-expanded')).toBe('false');
            expect(toggleOf(tree, 'archive').style.opacity).toBe('0');
            expect(rowOf(tree, 'archive').hasAttribute('aria-expanded')).toBe(false);
            expect(rowOf(tree, 'teams').getAttribute('aria-expanded')).toBe('false');
            expect(loadChildren).not.toHaveBeenCalled();

            toggleOf(tree, 'projects').click();
            expect(loadChildren).toHaveBeenCalledTimes(1);
            expect(loadChildren.mock.calls[0][0].id).toBe('projects');
            expect(rowOf(tree, 'projects').getAttribute('aria-busy')).toBe('true');
            expect(rowOf(tree, 'projects').getAttribute('aria-expanded')).toBe('true');
            expect(tree.element.querySelector('.tree-node-loading').textContent).toBe('載入中...');

            load.resolve([{ id: 'p1', label: 'Alpha' }, { id: 'p2', label: 'Beta' }]);
            await flush();
            expect(tree.element.querySelector('.tree-node-loading')).toBeNull();
            expect(rowOf(tree, 'projects').hasAttribute('aria-busy')).toBe(false);
            expect(rowOf(tree, 'p1').getAttribute('aria-level')).toBe('2');
            expect(rowOf(tree, 'p2').getAttribute('aria-posinset')).toBe('2');
            expect(rowOf(tree, 'p2').getAttribute('aria-setsize')).toBe('2');

            toggleOf(tree, 'projects').click();
            expect(rowOf(tree, 'p1')).toBeNull();
            toggleOf(tree, 'projects').click();
            expect(rowOf(tree, 'p1')).not.toBeNull();
            expect(loadChildren).toHaveBeenCalledTimes(1);

            // hasChildren: true 且 children 為空陣列也會延遲載入;靜態子節點不會
            toggleOf(tree, 'teams').click();
            expect(loadChildren).toHaveBeenCalledTimes(2);
            expect(loadChildren.mock.calls[1][0].id).toBe('teams');
            toggleOf(tree, 'static').click();
            expect(rowOf(tree, 'static-1')).not.toBeNull();
            expect(loadChildren).toHaveBeenCalledTimes(2);
            tree.destroy();
        });

        it('turns a node that loads no children into a leaf', async () => {
            const tree = new TreeList({ data: [{ id: 'projects', label: 'Projects' }], loadChildren: async () => [] }).mount(host);
            toggleOf(tree, 'projects').click();
            await flush();
            expect(toggleOf(tree, 'projects').style.opacity).toBe('0');
            expect(rowOf(tree, 'projects').hasAttribute('aria-expanded')).toBe(false);
            expect(tree.element.querySelector('[role="group"]')).toBeNull();
            tree.destroy();
        });

        it('shows an inline error with a retry control and stays collapsible', async () => {
            const loadChildren = vi.fn()
                .mockImplementationOnce(() => Promise.reject(new Error('offline')))
                .mockImplementationOnce(() => Promise.resolve([{ id: 'p1', label: 'Alpha' }]));
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);
            toggleOf(tree, 'projects').click();
            await flush();

            const error = tree.element.querySelector('.tree-node-error');
            expect(error.getAttribute('role')).toBe('alert');
            expect(error.textContent).toContain('子項目載入失敗');
            const retry = error.querySelector('.tree-node-retry');
            expect(retry.type).toBe('button');
            expect(retry.textContent).toBe('重試');
            expect(retry.getAttribute('aria-label')).toBe('重試載入「Projects」的子項目');
            expect(rowOf(tree, 'projects').getAttribute('aria-expanded')).toBe('true');
            expect(rowOf(tree, 'projects').hasAttribute('aria-busy')).toBe(false);

            retry.click();
            expect(loadChildren).toHaveBeenCalledTimes(2);
            expect(tree.element.querySelector('.tree-node-loading')).not.toBeNull();
            expect(rowOf(tree, 'projects').getAttribute('tabindex')).toBe('0');
            await flush();
            expect(rowOf(tree, 'p1')).not.toBeNull();
            expect(tree.element.querySelector('.tree-node-error')).toBeNull();
            tree.destroy();
        });

        it('collapses after an error and retries when expanded again', async () => {
            const loadChildren = vi.fn()
                .mockImplementationOnce(() => { throw new Error('sync failure'); })
                .mockImplementationOnce(() => Promise.resolve([{ id: 'p1', label: 'Alpha' }]));
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);
            toggleOf(tree, 'projects').click();
            await flush();
            expect(tree.element.querySelector('.tree-node-error')).not.toBeNull();

            toggleOf(tree, 'projects').click();
            expect(tree.element.querySelector('.tree-node-error')).toBeNull();
            expect(rowOf(tree, 'projects').getAttribute('aria-expanded')).toBe('false');

            toggleOf(tree, 'projects').click();
            expect(loadChildren).toHaveBeenCalledTimes(2);
            await flush();
            expect(rowOf(tree, 'p1')).not.toBeNull();
            tree.destroy();
        });

        it('treats a non-array result as a load failure', async () => {
            const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
            const tree = new TreeList({ data: lazyData(), loadChildren: async () => null }).mount(host);
            toggleOf(tree, 'projects').click();
            await flush();
            expect(tree.element.querySelector('.tree-node-error')).not.toBeNull();
            expect(warn).toHaveBeenCalled();
            tree.destroy();
        });

        it('keeps keyboard focus on the node whose children finished loading', async () => {
            const load = deferred();
            const tree = new TreeList({ data: lazyData(), loadChildren: () => load.promise }).mount(host);
            const row = rowOf(tree, 'projects');
            row.focus();
            press(row, 'ArrowRight');
            expect(rowOf(tree, 'projects').getAttribute('aria-busy')).toBe('true');

            load.resolve([{ id: 'p1', label: 'Alpha' }]);
            await flush();
            const rebuilt = rowOf(tree, 'projects');
            expect(document.activeElement).toBe(rebuilt);
            press(rebuilt, 'ArrowRight');
            expect(document.activeElement).toBe(rowOf(tree, 'p1'));
            expect(tabStops(tree)).toHaveLength(1);
            tree.destroy();
        });
    });

    describe('lazy loading races', () => {
        it('collapse / re-expand while loading does not load twice or render into a collapsed node', async () => {
            const load = deferred();
            const loadChildren = vi.fn(() => load.promise);
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);
            toggleOf(tree, 'projects').click();
            toggleOf(tree, 'projects').click();
            expect(tree.element.querySelector('.tree-node-loading')).toBeNull();
            expect(rowOf(tree, 'projects').getAttribute('aria-busy')).toBe('true');

            toggleOf(tree, 'projects').click();
            expect(loadChildren).toHaveBeenCalledTimes(1);
            expect(tree.element.querySelector('.tree-node-loading')).not.toBeNull();

            toggleOf(tree, 'projects').click();
            load.resolve([{ id: 'p1', label: 'Alpha' }]);
            await flush();
            expect(rowOf(tree, 'p1')).toBeNull();
            expect(rowOf(tree, 'projects').getAttribute('aria-expanded')).toBe('false');
            expect(rowOf(tree, 'projects').hasAttribute('aria-busy')).toBe(false);

            toggleOf(tree, 'projects').click();
            expect(rowOf(tree, 'p1')).not.toBeNull();
            expect(loadChildren).toHaveBeenCalledTimes(1);
            tree.destroy();
        });

        it('ignores a result that resolves after destroy', async () => {
            const load = deferred();
            const loadChildren = vi.fn(() => load.promise);
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);
            toggleOf(tree, 'projects').click();
            tree.destroy();

            load.resolve([{ id: 'p1', label: 'Alpha' }]);
            await flush();
            expect(tree._icons).toHaveLength(0);
            expect(tree.element.querySelector('.tree-node-row[data-node-id="p1"]')).toBeNull();
            expect(host.contains(tree.element)).toBe(false);
            expect(loadChildren).toHaveBeenCalledTimes(1);
        });

        it('drops a stale result when reloadNode or setData supersedes it', async () => {
            const first = deferred();
            const second = deferred();
            const third = deferred();
            const loadChildren = vi.fn()
                .mockReturnValueOnce(first.promise)
                .mockReturnValueOnce(second.promise)
                .mockReturnValueOnce(third.promise);
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);
            toggleOf(tree, 'projects').click();
            const reloading = tree.reloadNode('projects');
            expect(loadChildren).toHaveBeenCalledTimes(2);

            first.resolve([{ id: 'old', label: 'Old' }]);
            await flush();
            expect(rowOf(tree, 'old')).toBeNull();
            expect(tree.element.querySelector('.tree-node-loading')).not.toBeNull();

            second.resolve([{ id: 'new', label: 'New' }]);
            await expect(reloading).resolves.toBe(true);
            expect(rowOf(tree, 'new')).not.toBeNull();

            // setData 清除快取;仍展開的延遲節點重新載入,舊結果不會回填
            tree.setData(lazyData());
            expect(loadChildren).toHaveBeenCalledTimes(3);
            third.resolve([{ id: 'fresh', label: 'Fresh' }]);
            await flush();
            expect(rowOf(tree, 'new')).toBeNull();
            expect(rowOf(tree, 'fresh')).not.toBeNull();
            tree.destroy();
        });
    });

    describe('reloadNode', () => {
        it('reloads an expanded node, only clears the cache of a collapsed one', async () => {
            let version = 0;
            const loadChildren = vi.fn(async (node) => {
                version += 1;
                return [{ id: `${node.id}-v${version}`, label: `Version ${version}` }];
            });
            const tree = new TreeList({ data: lazyData(), loadChildren }).mount(host);
            toggleOf(tree, 'projects').click();
            await flush();
            expect(rowOf(tree, 'projects-v1')).not.toBeNull();

            await expect(tree.reloadNode('projects')).resolves.toBe(true);
            expect(loadChildren).toHaveBeenCalledTimes(2);
            expect(rowOf(tree, 'projects-v1')).toBeNull();
            expect(rowOf(tree, 'projects-v2')).not.toBeNull();

            toggleOf(tree, 'projects').click();
            await expect(tree.reloadNode('projects')).resolves.toBe(false);
            expect(loadChildren).toHaveBeenCalledTimes(2);
            toggleOf(tree, 'projects').click();
            expect(loadChildren).toHaveBeenCalledTimes(3);
            await flush();
            expect(rowOf(tree, 'projects-v3')).not.toBeNull();

            await expect(tree.reloadNode('static')).resolves.toBe(false);
            await expect(tree.reloadNode('missing')).resolves.toBe(false);
            tree.destroy();
        });
    });

    describe('checked state of lazily loaded children', () => {
        const children = () => [
            { id: 'c1', label: 'One' },
            { id: 'c2', label: 'Two' },
            { id: 'c3', label: 'Three', disableCheckbox: true },
        ];

        it('inherits a checked parent when cascading (disabled children excepted)', async () => {
            const tree = new TreeList({
                data: [{ id: 'root', label: 'Root' }],
                checkable: true,
                checkedKeys: ['root'],
                loadChildren: async () => children(),
            }).mount(host);
            toggleOf(tree, 'root').click();
            await flush();
            expect(tree.getCheckedKeys()).toEqual(['root', 'c1', 'c2']);
            expect(rowOf(tree, 'c1').getAttribute('aria-checked')).toBe('true');
            expect(rowOf(tree, 'c3').getAttribute('aria-checked')).toBe('false');
            expect(rowOf(tree, 'root').getAttribute('aria-checked')).toBe('true');
            tree.destroy();
        });

        it('does not inherit when checkStrictly is on', async () => {
            const tree = new TreeList({
                data: [{ id: 'root', label: 'Root' }],
                checkable: true,
                checkStrictly: true,
                checkedKeys: ['root'],
                loadChildren: async () => children(),
            }).mount(host);
            toggleOf(tree, 'root').click();
            await flush();
            expect(tree.getCheckedKeys()).toEqual(['root']);
            expect(rowOf(tree, 'c1').getAttribute('aria-checked')).toBe('false');
            tree.destroy();
        });

        it('applies keys set before loading and derives the parent afterwards', async () => {
            const tree = new TreeList({
                data: [{ id: 'root', label: 'Root' }],
                checkable: true,
                checkedKeys: ['c1'],
                loadChildren: async () => children(),
            }).mount(host);
            expect(tree.getCheckedKeys()).toEqual(['c1']);
            expect(rowOf(tree, 'root').getAttribute('aria-checked')).toBe('false');

            toggleOf(tree, 'root').click();
            await flush();
            expect(rowOf(tree, 'c1').getAttribute('aria-checked')).toBe('true');
            expect(rowOf(tree, 'root').getAttribute('aria-checked')).toBe('mixed');
            expect(tree.getCheckedKeys({ includeIndeterminate: true })).toEqual(['root', 'c1']);
            tree.destroy();
        });

        it('lets an explicit uncheck of an unloaded parent win over keys set earlier', async () => {
            const tree = new TreeList({
                data: [{ id: 'root', label: 'Root' }],
                checkable: true,
                checkedKeys: ['c1'],
                loadChildren: async () => children(),
            }).mount(host);
            checkboxOf(tree, 'root').click();
            checkboxOf(tree, 'root').click();
            toggleOf(tree, 'root').click();
            await flush();
            expect(tree.getCheckedKeys()).toEqual([]);
            expect(rowOf(tree, 'c1').getAttribute('aria-checked')).toBe('false');
            tree.destroy();
        });
    });

    describe('destroy', () => {
        it('removes DOM, container listeners and icons without touching document/window listeners', async () => {
            const docAdd = vi.spyOn(document, 'addEventListener');
            const winAdd = vi.spyOn(window, 'addEventListener');
            const tree = new TreeList({
                data: lazyData(),
                checkable: true,
                loadChildren: async () => [{ id: 'p1', label: 'Alpha' }],
            }).mount(host);
            toggleOf(tree, 'projects').click();
            await flush();
            checkboxOf(tree, 'p1').click();
            const row = rowOf(tree, 'p1');
            row.focus();
            press(row, 'ArrowUp');

            const removeSpy = vi.spyOn(tree.element, 'removeEventListener');
            tree.destroy();
            expect(host.contains(tree.element)).toBe(false);
            expect(tree._icons).toHaveLength(0);
            expect(removeSpy.mock.calls.map(([type]) => type).sort()).toEqual(['focusin', 'keydown']);
            expect(docAdd).not.toHaveBeenCalled();
            expect(winAdd).not.toHaveBeenCalled();

            expect(() => {
                tree.destroy();
                tree.setData(lazyData());
                tree.setActive('projects');
                tree.setTheme('classic');
                tree.setCheckedKeys(['projects']);
                tree.checkAll();
                tree.uncheckAll();
                tree.getCheckedKeys();
            }).not.toThrow();
            await expect(tree.reloadNode('projects')).resolves.toBe(false);
            expect(tree._icons).toHaveLength(0);
        });
    });

    describe('Locale', () => {
        it('renders loading and error strings in the active language', async () => {
            Locale.setLang('en');
            const pending = new TreeList({ data: lazyData(), loadChildren: () => new Promise(() => {}) }).mount(host);
            toggleOf(pending, 'projects').click();
            expect(pending.element.querySelector('.tree-node-loading').textContent).toBe('Loading...');
            pending.destroy();

            const failing = new TreeList({ data: lazyData(), loadChildren: () => Promise.reject(new Error('x')) }).mount(host);
            toggleOf(failing, 'projects').click();
            await flush();
            const error = failing.element.querySelector('.tree-node-error');
            expect(error.textContent).toContain('Failed to load child items');
            expect(error.querySelector('.tree-node-retry').textContent).toBe('Retry');
            expect(error.querySelector('.tree-node-retry').getAttribute('aria-label')).toBe('Retry loading child items of Projects');

            Locale.setLang('zh-TW');
            failing.setTheme('minimal');
            const rerendered = failing.element.querySelector('.tree-node-error');
            expect(rerendered.textContent).toContain('子項目載入失敗');
            expect(rerendered.querySelector('.tree-node-retry').textContent).toBe('重試');
            failing.destroy();
        });
    });
});
