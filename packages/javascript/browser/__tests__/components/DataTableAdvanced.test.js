import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { DataTable } from '../../ui_components/layout/DataTable/DataTable.js';
import Locale from '../../ui_components/i18n/index.js';
import { nextUid, resetUid } from '../../ui_components/utils/uid.js';

// ── Baseline DOM captured from the implementation before the advanced options existed
// (commit 4f1caa3). Default configurations must keep rendering exactly this markup.
const BASELINE_AUDIT_HTML = "<div><div class=\"b4a-dt b4a-dt--default\"><div class=\"b4a-dt__toolbar\">\n            <div><span class=\"b4a-dt__title\">Rooms</span></div>\n            <div class=\"b4a-dt__toolbar-actions\"><div class=\"b4a-dt__quick-search\">\n                <input class=\"b4a-dt__quick-search-input\" type=\"search\" data-action=\"quick-search\" aria-label=\"篩選已載入結果\" placeholder=\"篩選已載入結果\" value=\"\">\n                <button class=\"b4a-dt__quick-search-submit\" type=\"button\" data-action=\"quick-search-submit\" aria-label=\"套用篩選\" title=\"套用篩選\"><span aria-hidden=\"true\">🔍</span></button>\n                <span class=\"b4a-dt__quick-search-count\" aria-live=\"polite\">顯示 3 / 共 3 筆</span>\n            </div></div>\n        </div><div class=\"b4a-dt__scroll\"><table class=\"b4a-dt__table\"><thead><tr><th class=\"b4a-dt__th b4a-dt__th--select\"><input type=\"checkbox\" class=\"b4a-dt__checkbox\" data-action=\"select-all\"></th><th class=\"b4a-dt__th b4a-dt__th--sortable\" data-action=\"sort\" data-col=\"1\" style=\"width: 160px;\"><div class=\"b4a-dt__th-inner\">Name</div></th><th class=\"b4a-dt__th b4a-dt__th--sortable\" data-action=\"sort\" data-col=\"2\"><div class=\"b4a-dt__th-inner\">Floor ▲</div></th><th class=\"b4a-dt__th b4a-dt__th--sortable\" data-action=\"sort\" data-col=\"3\"><div class=\"b4a-dt__th-inner\">Owner</div></th></tr></thead><tbody><tr class=\"b4a-dt__tr\" data-row-index=\"1\"><td class=\"b4a-dt__td b4a-dt__td--select\"><input type=\"checkbox\" class=\"b4a-dt__checkbox\" data-action=\"select-row\" data-index=\"1\"></td><td class=\"b4a-dt__td\" data-col=\"1\" style=\"width: 160px;\">Beta Room</td><td class=\"b4a-dt__td\" data-col=\"2\">1</td><td class=\"b4a-dt__td\" data-col=\"3\">Lee (2)</td></tr><tr class=\"b4a-dt__tr b4a-dt__tr--even\" data-row-index=\"2\"><td class=\"b4a-dt__td b4a-dt__td--select\"><input type=\"checkbox\" class=\"b4a-dt__checkbox\" data-action=\"select-row\" data-index=\"2\"></td><td class=\"b4a-dt__td\" data-col=\"1\" style=\"width: 160px;\">Gamma Hall</td><td class=\"b4a-dt__td\" data-col=\"2\">2</td><td class=\"b4a-dt__td\" data-col=\"3\">Park (3)</td></tr></tbody></table></div><div class=\"b4a-dt__pagination\">\n            <div class=\"b4a-dt__pagination-group\">\n                <span>每頁筆數:</span>\n                <select data-action=\"rows-per-page\" class=\"b4a-dt__page-size\">\n                    <option value=\"10\">10</option><option value=\"20\">20</option><option value=\"100\">100</option><option value=\"500\">500</option><option value=\"1000\">1000</option>\n                </select>\n            </div>\n            <span>1-2 共 3</span>\n            <div class=\"b4a-dt__page-btns\">\n                <button class=\"b4a-dt__page-btn\" data-action=\"page-first\" disabled=\"\" title=\"第一頁\">⟨⟨</button>\n                <button class=\"b4a-dt__page-btn\" data-action=\"page-prev\" disabled=\"\" title=\"上一頁\">⟨</button>\n                <button class=\"b4a-dt__page-btn\" data-action=\"page-next\" title=\"下一頁\">⟩</button>\n                <button class=\"b4a-dt__page-btn\" data-action=\"page-last\" title=\"最後一頁\">⟩⟩</button>\n            </div>\n        </div></div></div>";
const BASELINE_STANDARD_EMPTY_HTML = "<div><div class=\"b4a-dt b4a-dt--search\"><div class=\"b4a-dt__toolbar\">\n            <div></div>\n            <div class=\"b4a-dt__toolbar-actions\"></div>\n        </div><div class=\"b4a-dt__scroll\" style=\"max-height: 240px; overflow-y: auto;\"><table class=\"b4a-dt__table\"><thead><tr><th class=\"b4a-dt__th b4a-dt__th--sortable\" data-action=\"sort\" data-col=\"0\" style=\"width: 80px;\"><div class=\"b4a-dt__th-inner\">Code</div></th><th class=\"b4a-dt__th b4a-dt__th--sortable\" data-action=\"sort\" data-col=\"1\"><div class=\"b4a-dt__th-inner\">Label</div></th></tr></thead><tbody><tr><td colspan=\"2\" class=\"b4a-dt__td b4a-dt__td--empty\">無查詢結果</td></tr></tbody></table></div></div></div>";

const rooms = () => ([
    { id: 1, name: 'Alpha Room', floor: 3, owner: 'Kim' },
    { id: 2, name: 'Beta Room', floor: 1, owner: 'Lee' },
    { id: 3, name: 'Gamma Hall', floor: 2, owner: 'Park' },
]);

const roomColumns = () => ([
    { key: 'name', title: 'Name' },
    { key: 'floor', title: 'Floor' },
    { key: 'owner', title: 'Owner' },
]);

const tasks = (count) => Array.from({ length: count }, (_, i) => ({ id: `t${i + 1}`, name: `Task ${String(i + 1).padStart(2, '0')}` }));

const bodyRows = (root) => [...root.querySelectorAll('tbody tr[data-row-index]')];
const firstColumnTexts = (root) => bodyRows(root).map(tr => tr.querySelector('td[data-col]').textContent);

// dataSource / timers settle through a few microtask turns
const settle = async (turns = 6) => {
    for (let i = 0; i < turns; i++) await Promise.resolve();
};

function deferred() {
    let resolve;
    let reject;
    const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}

function netListeners(addSpy, removeSpy) {
    const counts = {};
    for (const [type] of addSpy.mock.calls) counts[type] = (counts[type] || 0) + 1;
    for (const [type] of removeSpy.mock.calls) counts[type] = (counts[type] || 0) - 1;
    return Object.fromEntries(Object.entries(counts).filter(([, n]) => n !== 0));
}

function keydown(target, key, init = {}) {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
}

describe('DataTable advanced options', () => {
    let host;
    let created;
    const make = (...args) => {
        const table = new DataTable(...args);
        created.push(table);
        return table;
    };

    beforeEach(() => {
        host = document.createElement('div');
        document.body.appendChild(host);
        created = [];
    });

    afterEach(() => {
        created.forEach(table => table.destroy());
        host.remove();
        document.querySelectorAll('.b4a-dt__column-menu').forEach(node => node.remove());
        vi.useRealTimers();
        vi.restoreAllMocks();
        vi.unstubAllGlobals();
        Locale.setLang('zh-TW');
    });

    describe('defaults', () => {
        it('renders byte-identical markup for existing configurations', () => {
            make(host, {
                title: 'Rooms',
                columns: [
                    { key: 'id', hidden: true },
                    { key: 'name', title: 'Name', width: '160px' },
                    { key: 'floor', title: 'Floor' },
                    { key: 'owner', title: 'Owner', render: (v, row) => `${v} (${row.id})` },
                ],
                data: rooms(),
                pageSize: 2,
                search: true,
                selectableRows: 'multiple',
                sortOrder: { name: 'floor', direction: 'asc' },
            });
            expect(host.innerHTML).toBe(BASELINE_AUDIT_HTML);

            const second = document.createElement('div');
            document.body.appendChild(second);
            make({
                container: second,
                variant: 'search',
                columns: [
                    { name: 'code', label: 'Code', options: { setCellProps: () => ({ style: { width: '80px' } }) } },
                    { name: 'label', label: 'Label', options: { customBodyRender: (v) => `<${v}>` } },
                ],
                data: [],
                options: { selectableRows: 'none', tableBodyHeight: '240px' },
            });
            expect(second.innerHTML).toBe(BASELINE_STANDARD_EMPTY_HTML);
            second.remove();
        });

        it('adds no toolbar controls, ids, observers or global listeners unless an option asks for them', () => {
            const observers = [];
            vi.stubGlobal('ResizeObserver', class { constructor() { observers.push(this); } observe() {} disconnect() {} });
            const docAdd = vi.spyOn(document, 'addEventListener');
            const winAdd = vi.spyOn(window, 'addEventListener');
            resetUid();

            const table = make(host, { columns: roomColumns(), data: rooms(), search: true });

            expect(host.querySelector('.b4a-dt__column-toggle, .b4a-dt__status, .b4a-dt__th--expand, [aria-busy]')).toBeNull();
            expect(host.querySelector('[class*="sticky"]')).toBeNull();
            expect(docAdd).not.toHaveBeenCalled();
            expect(winAdd).not.toHaveBeenCalled();
            expect(observers).toHaveLength(0);
            // the shared id counter is untouched, so ids of other components do not shift
            expect(nextUid('probe')).toBe('probe-1');
            // selection API keeps returning data indexes
            const cb = host.querySelector('[data-action="select-row"]');
            cb.checked = true;
            cb.dispatchEvent(new Event('change'));
            expect(table.getSelectedRows()).toEqual([0]);
            expect(table.getSelectedKeys()).toEqual([0]);
        });

        it('keeps reload() and getQuery() usable in client mode', async () => {
            const table = make(host, { columns: roomColumns(), data: rooms(), pageSize: 2, sortOrder: { name: 'floor', direction: 'desc' } });
            host.querySelector('[data-action="page-next"]').click();
            expect(table.getQuery()).toEqual({ page: 2, pageSize: 2, sort: { key: 'floor', direction: 'desc' }, search: '' });
            await expect(table.reload()).resolves.toBe(true);
            expect(firstColumnTexts(host)).toEqual(['Beta Room']);
        });
    });

    describe('server-side mode', () => {
        it('sends page/pageSize/sort/search/signal to dataSource and never sorts, filters or pages locally', async () => {
            const onQueryChange = vi.fn();
            const dataSource = vi.fn(async () => ({
                // deliberately not in the requested sort order and not matching the search text
                rows: [rooms()[1], rooms()[0]],
                total: 57,
            }));
            const table = make(host, {
                columns: roomColumns(),
                serverSide: true,
                dataSource,
                onQueryChange,
                pageSize: 10,
                search: true,
                sortOrder: { name: 'floor', direction: 'desc' },
            });

            // first render is the loading state; the request is issued after the constructor returns
            expect(dataSource).not.toHaveBeenCalled();
            expect(host.querySelector('.b4a-dt__scroll').getAttribute('aria-busy')).toBe('true');
            expect(host.querySelector('.b4a-dt__status').textContent).toContain('載入中');
            await settle();

            expect(dataSource).toHaveBeenCalledTimes(1);
            const query = dataSource.mock.calls[0][0];
            expect(query).toMatchObject({ page: 1, pageSize: 10, sort: { key: 'floor', direction: 'desc' }, search: '' });
            expect(query.signal).toBeInstanceOf(AbortSignal);
            expect(onQueryChange).toHaveBeenCalledWith(query);
            expect(onQueryChange.mock.invocationCallOrder[0]).toBeLessThan(dataSource.mock.invocationCallOrder[0]);

            expect(host.querySelector('.b4a-dt__scroll').hasAttribute('aria-busy')).toBe(false);
            expect(firstColumnTexts(host)).toEqual(['Beta Room', 'Alpha Room']);
            expect(host.querySelector('.b4a-dt__pagination').textContent).toContain('1-10 共 57');
            expect(host.querySelector('.b4a-dt__quick-search-count').textContent).toBe('共 57 筆');
            expect(host.querySelector('[data-action="quick-search"]').getAttribute('placeholder')).toBe('搜尋');

            table.setSearchText('no-such-room');
            await settle();
            expect(dataSource.mock.calls.at(-1)[0]).toMatchObject({ page: 1, search: 'no-such-room' });
            expect(bodyRows(host)).toHaveLength(2);

            host.querySelector('[data-action="page-next"]').click();
            await settle();
            expect(dataSource.mock.calls.at(-1)[0]).toMatchObject({ page: 2, pageSize: 10 });
            expect(host.querySelector('.b4a-dt__pagination').textContent).toContain('11-20 共 57');

            host.querySelector('[data-action="page-last"]').click();
            await settle();
            expect(dataSource.mock.calls.at(-1)[0].page).toBe(6);

            host.querySelector('th[data-action="sort"][data-col="0"]').click();
            await settle();
            expect(dataSource.mock.calls.at(-1)[0].sort).toEqual({ key: 'name', direction: 'asc' });

            const size = host.querySelector('[data-action="rows-per-page"]');
            size.value = '20';
            size.dispatchEvent(new Event('change'));
            await settle();
            expect(dataSource.mock.calls.at(-1)[0]).toMatchObject({ page: 1, pageSize: 20 });
            expect(table.getQuery()).toEqual({ page: 1, pageSize: 20, sort: { key: 'name', direction: 'asc' }, search: 'no-such-room' });
            expect(onQueryChange).toHaveBeenCalledTimes(dataSource.mock.calls.length);
        });

        it('aborts the previous request and ignores stale responses', async () => {
            const pending = [];
            const dataSource = vi.fn((query) => {
                const d = deferred();
                pending.push({ query, ...d });
                return d.promise;
            });
            const table = make(host, { columns: roomColumns(), serverSide: true, dataSource, search: true });
            await settle();
            expect(pending).toHaveLength(1);

            const reloadResult = table.reload();
            expect(pending[0].query.signal.aborted).toBe(true);
            table.setSearchText('beta');
            expect(pending[1].query.signal.aborted).toBe(true);
            expect(pending).toHaveLength(3);

            pending[2].resolve({ rows: [rooms()[1]], total: 1 });
            await settle();
            expect(firstColumnTexts(host)).toEqual(['Beta Room']);

            // both older requests answer late (a dataSource that ignores the signal)
            pending[0].resolve({ rows: rooms(), total: 3 });
            pending[1].resolve({ rows: rooms(), total: 3 });
            await settle();
            expect(firstColumnTexts(host)).toEqual(['Beta Room']);
            await expect(reloadResult).resolves.toBe(false);
        });

        it('debounces quick search input, skips IME composition and resets to page 1', async () => {
            vi.useFakeTimers();
            const dataSource = vi.fn(async ({ page, pageSize }) => ({
                rows: tasks(100).slice((page - 1) * pageSize, page * pageSize),
                total: 100,
            }));
            make(host, {
                columns: [{ key: 'name', title: 'Name' }],
                serverSide: true,
                dataSource,
                search: true,
                pageSize: 10,
            });
            await settle();
            host.querySelector('[data-action="page-next"]').click();
            await settle();
            host.querySelector('[data-action="page-next"]').click();
            await settle();
            expect(dataSource.mock.calls.at(-1)[0].page).toBe(3);
            dataSource.mockClear();

            const input = host.querySelector('[data-action="quick-search"]');
            input.value = 'ta';
            input.dispatchEvent(new Event('input'));
            vi.advanceTimersByTime(200);
            input.value = 'tas';
            input.dispatchEvent(new Event('input'));
            vi.advanceTimersByTime(299);
            expect(dataSource).not.toHaveBeenCalled();
            vi.advanceTimersByTime(1);
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(1);
            expect(dataSource.mock.calls[0][0]).toMatchObject({ search: 'tas', page: 1 });

            // composition: no request while composing, one after compositionend
            const composing = host.querySelector('[data-action="quick-search"]');
            composing.dispatchEvent(new CompositionEvent('compositionstart'));
            composing.value = 'task 0';
            composing.dispatchEvent(new Event('input'));
            vi.advanceTimersByTime(1000);
            expect(dataSource).toHaveBeenCalledTimes(1);
            composing.dispatchEvent(new CompositionEvent('compositionend'));
            vi.advanceTimersByTime(300);
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(2);
            expect(dataSource.mock.calls[1][0].search).toBe('task 0');

            // typing and deleting back to the applied text does not query again
            const again = host.querySelector('[data-action="quick-search"]');
            again.value = 'task 0x';
            again.dispatchEvent(new Event('input'));
            again.value = 'task 0';
            again.dispatchEvent(new Event('input'));
            vi.advanceTimersByTime(300);
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(2);
        });

        it('keeps the search box focused and does not rebuild it mid-composition when a response arrives', async () => {
            const first = deferred();
            const dataSource = vi.fn(() => first.promise);
            make(host, { columns: roomColumns(), serverSide: true, dataSource, search: true });
            await settle();

            const input = host.querySelector('[data-action="quick-search"]');
            input.focus();
            input.dispatchEvent(new CompositionEvent('compositionstart'));
            first.resolve({ rows: rooms(), total: 3 });
            await settle();
            expect(host.querySelector('[data-action="quick-search"]')).toBe(input);
            expect(bodyRows(host)).toHaveLength(0);

            input.value = '北';
            input.dispatchEvent(new CompositionEvent('compositionend'));
            expect(bodyRows(host)).toHaveLength(3);
            const rebuilt = host.querySelector('[data-action="quick-search"]');
            expect(rebuilt).not.toBe(input);
            expect(document.activeElement).toBe(rebuilt);
            expect(rebuilt.value).toBe('北');
        });

        it('shows an error with a working retry button and resolves reload() by outcome', async () => {
            let fail = true;
            const dataSource = vi.fn(async () => {
                if (fail) throw new Error('backend unavailable');
                return { rows: rooms(), total: 3 };
            });
            const table = make(host, { columns: roomColumns(), serverSide: true, dataSource });
            await settle();

            const alert = host.querySelector('[role="alert"]');
            expect(alert.textContent).toContain('資料載入失敗');
            expect(alert.textContent).not.toContain('backend unavailable');
            expect(host.querySelector('.b4a-dt__scroll').hasAttribute('aria-busy')).toBe(false);
            await expect(table.reload()).resolves.toBe(false);

            fail = false;
            const retry = host.querySelector('[data-action="retry"]');
            expect(retry.tagName).toBe('BUTTON');
            expect(retry.textContent).toBe('重試');
            retry.click();
            expect(host.querySelector('.b4a-dt__scroll').getAttribute('aria-busy')).toBe('true');
            await settle();
            expect(host.querySelector('[role="alert"]')).toBeNull();
            expect(bodyRows(host)).toHaveLength(3);
            await expect(table.reload()).resolves.toBe(true);
        });

        it('supports caller-driven loading through onQueryChange and setData(rows, total)', async () => {
            const queries = [];
            const table = make(host, {
                columns: roomColumns(),
                serverSide: true,
                pageSize: 2,
                onQueryChange: query => queries.push(query),
            });
            await settle();
            expect(queries).toHaveLength(1);
            expect(queries[0].page).toBe(1);

            table.setData(rooms().slice(0, 2), 5);
            expect(bodyRows(host)).toHaveLength(2);
            expect(host.querySelector('.b4a-dt__pagination').textContent).toContain('1-2 共 5');

            host.querySelector('[data-action="page-next"]').click();
            expect(queries.at(-1).page).toBe(2);
            table.setData([rooms()[2]], 5);
            // setData in server mode keeps the requested page
            expect(table.getQuery().page).toBe(2);
            expect(host.querySelector('.b4a-dt__pagination').textContent).toContain('3-4 共 5');
            await expect(table.reload()).resolves.toBe(false);
            expect(queries.at(-1).page).toBe(2);
        });

        it('lets pushed rows win over an in-flight dataSource request', async () => {
            const pending = deferred();
            const dataSource = vi.fn(() => pending.promise);
            const table = make(host, { columns: roomColumns(), serverSide: true, dataSource });
            await settle();
            const { signal } = dataSource.mock.calls[0][0];

            table.setData([rooms()[2]], 1);
            expect(signal.aborted).toBe(true);
            pending.resolve({ rows: rooms(), total: 3 });
            await settle();
            expect(firstColumnTexts(host)).toEqual(['Gamma Hall']);
        });

        it('does not re-query or leave the page when the search text is effectively unchanged', async () => {
            const dataSource = vi.fn(async ({ page }) => ({ rows: [{ ...rooms()[0], name: `Page ${page}` }], total: 30 }));
            const table = make(host, { columns: roomColumns(), serverSide: true, dataSource, search: true, pageSize: 10 });
            await settle();
            table.setSearchText('Alpha');
            await settle();
            host.querySelector('[data-action="page-next"]').click();
            await settle();
            const calls = dataSource.mock.calls.length;

            table.setSearchText('  Alpha ');
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(calls);
            expect(table.getQuery()).toMatchObject({ page: 2, search: 'Alpha' });
            expect(firstColumnTexts(host)).toEqual(['Page 2']);

            // case is significant for the server: it is a new query
            table.setSearchText('alpha');
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(calls + 1);
            expect(dataSource.mock.calls.at(-1)[0]).toMatchObject({ page: 1, search: 'alpha' });
        });

        it('moves to the last available page when the total shrinks under the current page', async () => {
            let total = 30;
            const dataSource = vi.fn(async ({ page, pageSize }) => {
                const rows = tasks(total).slice((page - 1) * pageSize, page * pageSize);
                return { rows, total };
            });
            const table = make(host, { columns: [{ key: 'name', title: 'Name' }], serverSide: true, dataSource, pageSize: 10 });
            await settle();
            host.querySelector('[data-action="page-last"]').click();
            await settle();
            expect(table.getQuery().page).toBe(3);

            total = 12;
            await table.reload();
            await settle();
            expect(dataSource.mock.calls.at(-1)[0].page).toBe(2);
            expect(table.getQuery().page).toBe(2);
            expect(firstColumnTexts(host)).toEqual(['Task 11', 'Task 12']);
        });

        it('returns keyboard focus to the retry button when a retry fails again', async () => {
            const dataSource = vi.fn(async () => { throw new Error('still down'); });
            make(host, { columns: roomColumns(), serverSide: true, dataSource });
            await settle();
            const retry = host.querySelector('[data-action="retry"]');
            retry.focus();
            retry.click();
            // while loading the button is gone; it gets focus back once the error is shown again
            expect(host.querySelector('[data-action="retry"]')).toBeNull();
            await settle();
            const again = host.querySelector('[data-action="retry"]');
            expect(again).not.toBe(retry);
            expect(document.activeElement).toBe(again);
        });

        it('re-issues the first query when mounted again after an early destroy', async () => {
            const dataSource = vi.fn(async () => ({ rows: rooms(), total: 3 }));
            const table = make({ columns: roomColumns(), serverSide: true, dataSource });
            table.destroy();
            await settle();
            expect(dataSource).not.toHaveBeenCalled();

            table.mount(host);
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(1);
            expect(bodyRows(host)).toHaveLength(3);
        });

        it('clears index-based selection when a new page arrives and reports it', async () => {
            const onRowSelectionChange = vi.fn();
            const dataSource = vi.fn(async ({ page, pageSize }) => ({ rows: tasks(30).slice((page - 1) * pageSize, page * pageSize), total: 30 }));
            const table = make(host, {
                columns: [{ key: 'name', title: 'Name' }],
                serverSide: true,
                dataSource,
                pageSize: 10,
                selectableRows: 'multiple',
                options: { onRowSelectionChange },
            });
            await settle();
            const cb = host.querySelector('[data-action="select-row"]');
            cb.checked = true;
            cb.dispatchEvent(new Event('change'));
            expect(table.getSelectedRows()).toEqual([0]);

            host.querySelector('[data-action="page-next"]').click();
            await settle();
            expect(table.getSelectedRows()).toEqual([]);
            expect(onRowSelectionChange).toHaveBeenLastCalledWith([], [], []);
        });
    });

    describe('sticky header', () => {
        it('marks the table and limits the scroll area so the body scrolls under the header', () => {
            make(host, { columns: roomColumns(), data: rooms(), stickyHeader: true });
            expect(host.querySelector('.b4a-dt').classList.contains('b4a-dt--sticky-header')).toBe(true);
            const scroll = host.querySelector('.b4a-dt__scroll');
            expect(scroll.style.maxHeight).toBe('60vh');
            expect(scroll.style.overflowY).toBe('auto');

            const second = document.createElement('div');
            document.body.appendChild(second);
            make(second, { columns: roomColumns(), data: rooms(), stickyHeader: true, maxHeight: '320px' });
            expect(second.querySelector('.b4a-dt__scroll').style.maxHeight).toBe('320px');
            make(second, { columns: roomColumns(), data: rooms(), options: { stickyHeader: true, maxHeight: 200 } });
            expect(second.querySelector('.b4a-dt__scroll').style.maxHeight).toBe('200px');
            second.remove();
        });
    });

    describe('sticky columns', () => {
        const stickyColumns = () => ([
            { key: 'id', title: 'ID', width: '60px', sticky: 'left' },
            { key: 'name', title: 'Name', width: '150px', sticky: 'left' },
            { key: 'floor', title: 'Floor', sticky: 'left' }, // no declared width: still part of the leading run
            { key: 'owner', title: 'Owner' },
            { key: 'note', title: 'Note', sticky: 'left' }, // not contiguous from the left edge: ignored
            { key: 'price', title: 'Price', width: '90px', sticky: 'right' },
        ]);

        it('computes left/right offsets from declared widths and pins the selection column', () => {
            make(host, { columns: stickyColumns(), data: rooms(), selectableRows: 'multiple' });
            const head = [...host.querySelectorAll('thead th')];
            // [select, id, name, floor, owner, note, price]
            expect(head.map(th => th.classList.contains('b4a-dt__cell--sticky-left'))).toEqual([true, true, true, true, false, false, false]);
            expect(head.slice(0, 4).map(th => th.style.left)).toEqual(['0px', '48px', '108px', '258px']);
            expect(head[3].classList.contains('b4a-dt__cell--sticky-left-edge')).toBe(true);
            expect(head[2].classList.contains('b4a-dt__cell--sticky-left-edge')).toBe(false);
            expect(head[6].classList.contains('b4a-dt__cell--sticky-right')).toBe(true);
            expect(head[6].classList.contains('b4a-dt__cell--sticky-right-edge')).toBe(true);
            expect(head[6].style.right).toBe('0px');
            // declared width styles are kept alongside the offset
            expect(head[1].style.width).toBe('60px');

            const cells = bodyRows(host)[0].cells;
            expect(cells[2].style.left).toBe('108px');
            expect(cells[2].classList.contains('b4a-dt__cell--sticky-left')).toBe(true);
            expect(cells[5].classList.contains('b4a-dt__cell--sticky-left')).toBe(false);
            expect(cells[6].style.right).toBe('0px');
        });

        it('measures rendered widths when available and re-measures on resize', () => {
            const observers = [];
            vi.stubGlobal('ResizeObserver', class {
                constructor(callback) { this.callback = callback; this.targets = []; this.disconnected = false; observers.push(this); }
                observe(target) { this.targets.push(target); }
                disconnect() { this.disconnected = true; }
            });
            const widths = { select: 0, id: 0, name: 0, floor: 0 };
            vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function () {
                const col = this.getAttribute('data-col');
                const key = this.classList.contains('b4a-dt__th--select') ? 'select' : ['id', 'name', 'floor'][Number(col)];
                return { width: widths[key] || 0, height: 20, top: 0, left: 0, right: 0, bottom: 20, x: 0, y: 0 };
            });

            const table = make(host, {
                columns: [
                    { key: 'id', title: 'ID', width: '60px', sticky: 'left' },
                    { key: 'name', title: 'Name', sticky: 'left' },
                    { key: 'floor', title: 'Floor', sticky: 'left' },
                    { key: 'owner', title: 'Owner' },
                ],
                data: rooms(),
                selectableRows: 'multiple',
            });
            const head = () => [...host.querySelectorAll('thead th')];
            // nothing measurable yet: declared width for ID, 0 for the undeclared Name
            expect(head().slice(0, 4).map(th => th.style.left)).toEqual(['0px', '48px', '108px', '108px']);
            expect(observers).toHaveLength(1);
            expect(observers[0].targets[0]).toBe(host.querySelector('table'));

            Object.assign(widths, { select: 62, id: 66, name: 173.5, floor: 80 });
            observers[0].callback([]);
            expect(head().slice(0, 4).map(th => th.style.left)).toEqual(['0px', '62px', '128px', '301.5px']);
            expect(bodyRows(host)[1].cells[3].style.left).toBe('301.5px');

            table.destroy();
            expect(observers[0].disconnected).toBe(true);
        });
    });

    describe('column visibility', () => {
        const columns = () => ([
            { key: 'id', title: 'ID', hidden: true },
            { key: 'name', title: 'Name', hideable: false },
            { key: 'floor', title: 'Floor' },
            { key: 'owner', title: 'Owner' },
            { key: 'internal', title: 'Internal', hidden: true, hideable: false },
        ]);

        it('opens an accessible menu from the toolbar and toggles columns with the keyboard', () => {
            const onColumnVisibilityChange = vi.fn();
            const docAdd = vi.spyOn(document, 'addEventListener');
            const docRemove = vi.spyOn(document, 'removeEventListener');
            const table = make(host, { columns: columns(), data: rooms(), columnToggle: true, onColumnVisibilityChange, selectableRows: 'none' });

            let trigger = host.querySelector('[data-action="column-toggle"]');
            expect(trigger.tagName).toBe('BUTTON');
            expect(trigger.getAttribute('aria-haspopup')).toBe('menu');
            expect(trigger.getAttribute('aria-expanded')).toBe('false');
            expect(trigger.textContent).toContain('欄位');
            expect(netListeners(docAdd, docRemove)).toEqual({});

            trigger.focus();
            keydown(trigger, 'ArrowDown');
            const menu = document.querySelector('.b4a-dt__column-menu');
            expect(menu.parentNode).toBe(document.body);
            expect(menu.getAttribute('role')).toBe('menu');
            expect(menu.getAttribute('aria-label')).toBe('顯示欄位');
            expect(menu.style.position).toBe('fixed');
            expect(trigger.getAttribute('aria-expanded')).toBe('true');
            expect(trigger.getAttribute('aria-controls')).toBe(menu.id);
            expect(netListeners(docAdd, docRemove)).toEqual({ click: 1 });

            const items = [...menu.querySelectorAll('[role="menuitemcheckbox"]')];
            expect(items.map(item => item.querySelector('.b4a-dt__column-menu-label').textContent)).toEqual(['ID', 'Name', 'Floor', 'Owner']);
            expect(items.map(item => item.getAttribute('aria-checked'))).toEqual(['false', 'true', 'true', 'true']);
            expect(items[1].getAttribute('aria-disabled')).toBe('true');
            expect(document.activeElement).toBe(items[0]);

            keydown(document.activeElement, 'ArrowDown');
            expect(document.activeElement).toBe(items[1]);
            keydown(document.activeElement, ' ');
            expect(items[1].getAttribute('aria-checked')).toBe('true');
            expect(onColumnVisibilityChange).not.toHaveBeenCalled();

            keydown(document.activeElement, 'ArrowDown');
            keydown(document.activeElement, ' ');
            expect(onColumnVisibilityChange).toHaveBeenCalledWith({ id: false, name: true, floor: false, owner: true, internal: false });
            expect([...host.querySelectorAll('thead th')].map(th => th.textContent)).toEqual(['Name', 'Owner']);
            // the menu survives the table re-render and keeps focus on the toggled item
            expect(document.querySelector('.b4a-dt__column-menu')).toBe(menu);
            expect(document.activeElement).toBe(items[2]);
            expect(items[2].getAttribute('aria-checked')).toBe('false');
            trigger = host.querySelector('[data-action="column-toggle"]');
            expect(trigger.getAttribute('aria-expanded')).toBe('true');

            keydown(document.activeElement, 'Home');
            expect(document.activeElement).toBe(items[0]);
            keydown(document.activeElement, 'Enter');
            expect(table.getColumnVisibility().id).toBe(true);
            keydown(document.activeElement, 'End');
            expect(document.activeElement).toBe(items[3]);
            keydown(document.activeElement, 'ArrowDown');
            expect(document.activeElement).toBe(items[0]);

            const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
            const outerEscape = vi.fn();
            document.addEventListener('keydown', outerEscape);
            document.activeElement.dispatchEvent(escape);
            document.removeEventListener('keydown', outerEscape);
            expect(outerEscape).not.toHaveBeenCalled();
            expect(document.querySelector('.b4a-dt__column-menu')).toBeNull();
            trigger = host.querySelector('[data-action="column-toggle"]');
            expect(document.activeElement).toBe(trigger);
            expect(trigger.getAttribute('aria-expanded')).toBe('false');
            expect(trigger.hasAttribute('aria-controls')).toBe(false);
            expect(netListeners(docAdd, docRemove)).toEqual({});
        });

        it('opens on click, stays open for that click, and closes on an outside click', () => {
            make(host, { columns: columns(), data: rooms(), columnToggle: true });
            const trigger = host.querySelector('[data-action="column-toggle"]');
            trigger.click();
            const menu = document.querySelector('.b4a-dt__column-menu');
            expect(menu).not.toBeNull();
            expect(document.activeElement).toBe(menu.querySelector('[role="menuitemcheckbox"]'));
            menu.querySelectorAll('[role="menuitemcheckbox"]')[3].click();
            expect(document.querySelector('.b4a-dt__column-menu')).toBe(menu);
            document.body.click();
            expect(document.querySelector('.b4a-dt__column-menu')).toBeNull();
            keydown(host.querySelector('[data-action="column-toggle"]'), 'ArrowUp');
            const reopened = document.querySelector('.b4a-dt__column-menu');
            const items = reopened.querySelectorAll('[role="menuitemcheckbox"]');
            expect(document.activeElement).toBe(items[items.length - 1]);
        });

        it('exposes setColumnVisible/getColumnVisibility and respects hideable:false', () => {
            const onColumnVisibilityChange = vi.fn();
            const table = make(host, { columns: columns(), data: rooms(), onColumnVisibilityChange, selectableRows: 'none' });
            expect(table.getColumnVisibility()).toEqual({ id: false, name: true, floor: true, owner: true, internal: false });

            table.setColumnVisible('owner', false);
            table.setColumnVisible('name', false);
            table.setColumnVisible('internal', true);
            expect(table.getColumnVisibility()).toEqual({ id: false, name: true, floor: true, owner: false, internal: false });
            expect([...host.querySelectorAll('thead th')].map(th => th.textContent)).toEqual(['Name', 'Floor']);
            expect(onColumnVisibilityChange).not.toHaveBeenCalled();

            table.setColumnVisible(0, true, { emit: true });
            expect(onColumnVisibilityChange).toHaveBeenCalledWith({ id: true, name: true, floor: true, owner: false, internal: false });
            table.setColumnVisible('unknown', false);
        });

        it('keeps quick search limited to displayed columns, as it already was for hidden ones', () => {
            const table = make(host, { columns: columns(), data: rooms(), search: true, selectableRows: 'multiple' });
            table.setSearchText('kim');
            expect(bodyRows(host)).toHaveLength(1);
            const cb = host.querySelector('[data-action="select-row"]');
            cb.checked = true;
            cb.dispatchEvent(new Event('change'));
            expect(table.getSelectedRows()).toEqual([0]);

            // hiding the matched column removes the match and the now-hidden selection
            table.setColumnVisible('owner', false);
            expect(bodyRows(host)).toHaveLength(0);
            expect(table.getSelectedRows()).toEqual([]);
            table.setColumnVisible('owner', true);
            expect(bodyRows(host)).toHaveLength(1);
        });
    });

    describe('row expansion', () => {
        it('renders an accessible toggle and plain-text details, with key-based methods', () => {
            const table = make(host, {
                columns: roomColumns(),
                data: rooms(),
                rowKey: 'id',
                selectableRows: 'multiple',
                expandable: {
                    render: row => `<b>${row.name}</b> details`,
                    rowExpandable: row => row.id !== 2,
                },
            });
            const headCells = [...host.querySelectorAll('thead th')];
            expect(headCells[0].classList.contains('b4a-dt__th--expand')).toBe(true);
            expect(headCells[0].textContent).toBe('明細');
            expect(headCells[1].classList.contains('b4a-dt__th--select')).toBe(true);

            const buttons = host.querySelectorAll('[data-action="toggle-expand"]');
            expect(buttons).toHaveLength(2);
            const [first] = buttons;
            expect(first.tagName).toBe('BUTTON');
            expect(first.getAttribute('type')).toBe('button');
            expect(first.getAttribute('aria-label')).toBe('列明細');
            expect(first.getAttribute('aria-expanded')).toBe('false');
            expect(first.hasAttribute('aria-controls')).toBe(false);

            first.focus();
            first.click();
            const detail = host.querySelector('.b4a-dt__detail-row');
            expect(first.getAttribute('aria-expanded')).toBe('true');
            expect(first.getAttribute('aria-controls')).toBe(detail.id);
            expect(document.getElementById(first.getAttribute('aria-controls'))).toBe(detail);
            expect(detail.textContent).toBe('<b>Alpha Room</b> details');
            expect(detail.querySelector('b')).toBeNull();
            expect(detail.cells[0].colSpan).toBe(bodyRows(host)[0].cells.length);
            expect(detail.previousElementSibling).toBe(bodyRows(host)[0]);
            expect(document.activeElement).toBe(first);
            expect(table.getExpandedKeys()).toEqual([1]);

            table.collapseRow(1);
            expect(host.querySelector('.b4a-dt__detail-row')).toBeNull();
            expect(first.getAttribute('aria-expanded')).toBe('false');

            table.expandRow(2);
            expect(table.getExpandedKeys()).toEqual([]);
            // with rowKey, a key that is not loaded yet is kept for when the row arrives
            table.expandRow(42);
            expect(table.getExpandedKeys()).toEqual([42]);
            table.collapseRow(42);
            table.expandRow('3');
            expect(table.getExpandedKeys()).toEqual([3]);
            expect(host.querySelector('.b4a-dt__detail-row').textContent).toBe('<b>Gamma Hall</b> details');
            table.toggleRow(3);
            expect(table.getExpandedKeys()).toEqual([]);

            // expansion state follows the key through a re-sort
            table.expandRow(1);
            host.querySelector('th[data-action="sort"][data-col="1"]').click();
            const detailAfterSort = host.querySelector('.b4a-dt__detail-row');
            expect(detailAfterSort.previousElementSibling.getAttribute('data-row-index')).toBe('0');
            expect(bodyRows(host).map(tr => tr.getAttribute('data-row-index'))).toEqual(['1', '2', '0']);
        });

        it('inserts DOM nodes returned by render and supports expandOnRowClick', () => {
            make(host, {
                columns: [
                    { key: 'name', title: 'Name' },
                    { key: 'owner', title: 'Owner', render: () => '' },
                ],
                data: rooms(),
                selectableRows: 'multiple',
                expandable: {
                    expandOnRowClick: true,
                    render: (row, index) => {
                        const node = document.createElement('div');
                        node.className = 'detail-node';
                        node.textContent = `#${index} ${row.name}`;
                        return node;
                    },
                },
            });
            expect(host.querySelector('.b4a-dt').classList.contains('b4a-dt--row-expand')).toBe(true);
            const row = bodyRows(host)[1];
            row.querySelector('td[data-col="0"]').click();
            expect(host.querySelector('.detail-node').textContent).toBe('#1 Beta Room');

            // clicks on interactive controls inside the row do not toggle
            const checkbox = row.querySelector('[data-action="select-row"]');
            checkbox.click();
            expect(host.querySelectorAll('.b4a-dt__detail-row')).toHaveLength(1);
            row.querySelector('td[data-col="0"]').click();
            expect(host.querySelectorAll('.b4a-dt__detail-row')).toHaveLength(0);
        });

        it('falls back to data indexes without rowKey and resets on new data', () => {
            const table = make(host, { columns: roomColumns(), data: rooms(), expandable: { render: row => row.name } });
            table.expandRow(99);
            expect(table.getExpandedKeys()).toEqual([]);
            table.expandRow(1);
            expect(table.getExpandedKeys()).toEqual([1]);
            expect(host.querySelector('.b4a-dt__detail-row').textContent).toBe('Beta Room');
            table.setData(rooms().reverse());
            expect(table.getExpandedKeys()).toEqual([]);
            expect(host.querySelector('.b4a-dt__detail-row')).toBeNull();
        });
    });

    describe('key-based selection (rowKey)', () => {
        it('tracks selection across pages, sorting and quick search, and returns row objects', () => {
            const onRowSelectionChange = vi.fn();
            const data = tasks(25);
            const table = make(host, {
                columns: [{ key: 'name', title: 'Name' }],
                data,
                pageSize: 10,
                rowKey: 'id',
                search: true,
                selectableRows: 'multiple',
                options: { onRowSelectionChange },
            });
            const check = (index) => {
                const cb = bodyRows(host)[index].querySelector('[data-action="select-row"]');
                cb.checked = !cb.checked;
                cb.dispatchEvent(new Event('change'));
            };

            check(0);
            host.querySelector('[data-action="page-next"]').click();
            check(0);
            expect(table.getSelectedKeys()).toEqual(['t1', 't11']);
            expect(table.getSelectedRows()).toEqual([data[0], data[10]]);
            // client mode: every row is loaded, so the index arguments list both; the 4th argument lists the keys
            expect(onRowSelectionChange).toHaveBeenLastCalledWith(
                [],
                [{ index: 0, dataIndex: 0 }, { index: 10, dataIndex: 10 }],
                [0, 10],
                ['t1', 't11'],
            );

            // header checkbox reflects the current page only
            const selectAll = () => host.querySelector('[data-action="select-all"]');
            expect(selectAll().checked).toBe(false);
            expect(selectAll().indeterminate).toBe(true);

            host.querySelector('th[data-action="sort"][data-col="0"]').click();
            host.querySelector('th[data-action="sort"][data-col="0"]').click();
            expect(table.getSelectedKeys()).toEqual(['t1', 't11']);

            table.setSearchText('Task 2');
            expect(table.getSelectedKeys()).toEqual(['t1', 't11']);

            // select-all acts on the displayed page (after filtering) only:
            // '1' matches Task 01, 10–19 and 21 → 12 rows over two pages (sorted descending, Task 01 is on page 2)
            table.setSearchText('1');
            expect(bodyRows(host)).toHaveLength(10);
            const header = selectAll();
            header.checked = true;
            header.dispatchEvent(new Event('change'));
            expect(table.getSelectedKeys()).toHaveLength(11);
            expect(table.getSelectedKeys()).toEqual(expect.arrayContaining(['t1', 't11', 't21', 't12']));
            expect(selectAll().checked).toBe(true);
            expect(selectAll().indeterminate).toBe(false);
            header.checked = false;
            header.dispatchEvent(new Event('change'));
            // the page-2 row selected earlier is untouched
            expect(table.getSelectedKeys()).toEqual(['t1']);

            table.setSelectedKeys(['t5', 'missing']);
            expect(table.getSelectedKeys()).toEqual(['t5', 'missing']);
            expect(table.getSelectedRows()).toEqual([data[4]]);
            table.clearSelection();
            expect(table.getSelectedKeys()).toEqual([]);
            expect(host.querySelectorAll('tbody tr.b4a-dt__tr--selected')).toHaveLength(0);
        });

        it('selects every filtered row with selectAllScope "filtered" and shows the total in the selection toolbar', () => {
            const data = tasks(25);
            const table = make(host, {
                columns: [{ key: 'name', title: 'Name' }],
                data,
                pageSize: 10,
                rowKey: row => row.id,
                search: true,
                selectableRows: 'multiple',
                selectAllScope: 'filtered',
                customToolbarSelect: () => '<span class="bulk">bulk</span>',
            });
            table.setSearchText('1'); // 12 matching rows over two pages
            expect(bodyRows(host)).toHaveLength(10);
            const header = host.querySelector('[data-action="select-all"]');
            header.checked = true;
            header.dispatchEvent(new Event('change'));
            expect(table.getSelectedKeys()).toHaveLength(12);
            expect(host.querySelector('.b4a-dt__toolbar-text').textContent).toContain('12');
            expect(host.querySelector('.bulk')).not.toBeNull();
        });

        it('treats keys as data indexes when no rowKey is set', () => {
            const table = make(host, { columns: roomColumns(), data: rooms(), selectableRows: 'multiple' });
            table.setSelectedKeys([2, '0', 9, 'x']);
            expect(table.getSelectedKeys()).toEqual([2, 0]);
            expect(table.getSelectedRows()).toEqual([2, 0]);
            expect(host.querySelectorAll('tbody tr.b4a-dt__tr--selected')).toHaveLength(2);
            table.clearSelection();
            expect(table.getSelectedRows()).toEqual([]);
        });

        it('keeps keys through server-side page changes and reloads', async () => {
            const all = Array.from({ length: 30 }, (_, i) => ({ id: i + 1, name: `Order ${i + 1}` }));
            const dataSource = vi.fn(async ({ page, pageSize }) => ({
                rows: all.slice((page - 1) * pageSize, page * pageSize).map(row => ({ ...row })),
                total: all.length,
            }));
            const table = make(host, {
                columns: [{ key: 'name', title: 'Name' }],
                serverSide: true,
                dataSource,
                rowKey: 'id',
                pageSize: 10,
                selectableRows: 'multiple',
            });
            await settle();
            const checkFirst = () => {
                const cb = host.querySelector('[data-action="select-row"]');
                cb.checked = true;
                cb.dispatchEvent(new Event('change'));
            };
            checkFirst();
            host.querySelector('[data-action="page-next"]').click();
            await settle();
            checkFirst();
            expect(table.getSelectedKeys()).toEqual([1, 11]);

            await table.reload();
            expect(table.getSelectedKeys()).toEqual([1, 11]);
            expect(bodyRows(host)[0].classList.contains('b4a-dt__tr--selected')).toBe(true);
            expect(table.getSelectedRows().map(row => row.id)).toEqual([1, 11]);

            host.querySelector('[data-action="page-prev"]').click();
            await settle();
            expect(host.querySelector('[data-action="select-row"]').checked).toBe(true);
            expect(host.querySelectorAll('[data-action="select-row"]:checked')).toHaveLength(1);
        });
    });

    describe('destroy', () => {
        it('aborts pending work and leaves no DOM, listeners, observers or timers behind', async () => {
            vi.useFakeTimers();
            const observers = [];
            vi.stubGlobal('ResizeObserver', class {
                constructor() { this.disconnected = false; observers.push(this); }
                observe() {}
                disconnect() { this.disconnected = true; }
            });
            const docAdd = vi.spyOn(document, 'addEventListener');
            const docRemove = vi.spyOn(document, 'removeEventListener');
            const winAdd = vi.spyOn(window, 'addEventListener');
            const winRemove = vi.spyOn(window, 'removeEventListener');
            const dataSource = vi.fn(() => new Promise(() => {}));

            const table = make(host, {
                columns: [{ key: 'name', title: 'Name', sticky: 'left', width: '120px' }, { key: 'owner', title: 'Owner' }],
                data: rooms(),
                serverSide: true,
                dataSource,
                search: true,
                columnToggle: true,
                expandable: { render: row => row.name },
            });
            await settle();
            const { signal } = dataSource.mock.calls[0][0];

            host.querySelector('[data-action="column-toggle"]').click();
            expect(document.querySelector('.b4a-dt__column-menu')).not.toBeNull();
            const input = host.querySelector('[data-action="quick-search"]');
            input.value = 'alpha';
            input.dispatchEvent(new Event('input'));

            table.destroy();
            expect(signal.aborted).toBe(true);
            expect(document.querySelector('.b4a-dt__column-menu')).toBeNull();
            expect(host.innerHTML).toBe('');
            expect(table.element.innerHTML).toBe('');
            expect(netListeners(docAdd, docRemove)).toEqual({});
            expect(netListeners(winAdd, winRemove)).toEqual({});
            expect(observers.length).toBeGreaterThan(0);
            expect(observers.every(observer => observer.disconnected)).toBe(true);

            vi.advanceTimersByTime(5000);
            await settle();
            expect(dataSource).toHaveBeenCalledTimes(1);

            // idempotent and safe to call methods afterwards
            expect(() => table.destroy()).not.toThrow();
            await expect(table.reload()).resolves.toBe(false);
            expect(() => {
                table.setColumnVisible('owner', false);
                table.expandRow(0);
                table.clearSelection();
                table.getQuery();
            }).not.toThrow();
        });
    });

    describe('labels', () => {
        it('follows Locale switching and accepts textLabels overrides', async () => {
            Locale.setLang('en');
            const table = make(host, {
                columns: roomColumns(),
                data: rooms(),
                columnToggle: true,
                expandable: { render: () => 'x' },
            });
            expect(host.querySelector('[data-action="column-toggle"]').textContent).toContain('Columns');
            expect(host.querySelector('[data-action="toggle-expand"]').getAttribute('aria-label')).toBe('Row details');

            Locale.setLang('zh-TW');
            table.render();
            expect(host.querySelector('[data-action="column-toggle"]').textContent).toContain('欄位');

            const second = document.createElement('div');
            document.body.appendChild(second);
            const dataSource = vi.fn(async () => { throw new Error('offline'); });
            make(second, {
                columns: roomColumns(),
                serverSide: true,
                dataSource,
                columnToggle: true,
                options: { textLabels: { columns: { button: 'Fields' }, body: { loadError: 'Could not load', retry: 'Again' } } },
            });
            await settle();
            expect(second.querySelector('[data-action="column-toggle"]').textContent).toContain('Fields');
            expect(second.querySelector('[role="alert"]').textContent).toContain('Could not load');
            expect(second.querySelector('[data-action="retry"]').textContent).toBe('Again');
            second.remove();
        });
    });
});
