import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { DataTable } from '../../ui_components/layout/DataTable/DataTable.js';
import { raw } from '../../ui_components/utils/security.js';

const rows = () => ([
    { id: 1, name: 'Alpha Store', city: 'North' },
    { id: 2, name: 'Beta Market', city: 'South' },
    { id: 3, name: 'Gamma Shop', city: 'North' },
]);

const columns = () => ([
    { key: 'name', title: 'Name' },
    { key: 'city', title: 'City' },
]);

const bodyRows = (table) => [...table.element.querySelectorAll('tbody tr[data-row-index]')];

describe('DataTable quick search', () => {
    let host;

    beforeEach(() => {
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
    });

    it('is off by default and renders no search box', () => {
        const table = new DataTable(host, { columns: columns(), data: rows() });
        expect(host.querySelector('.b4a-dt__quick-search')).toBeNull();
        expect(bodyRows(table)).toHaveLength(3);
    });

    it('filters loaded rows, reports the count and exposes the programmatic API', () => {
        const table = new DataTable(host, { columns: columns(), data: rows(), search: true });
        expect(host.querySelector('.b4a-dt__quick-search-count').textContent).toBe('顯示 3 / 共 3 筆');

        table.setSearchText('north');
        expect(bodyRows(table)).toHaveLength(2);
        expect(table.getSearchText()).toBe('north');
        expect(host.querySelector('.b4a-dt__quick-search-count').textContent).toBe('顯示 2 / 共 3 筆');

        table.clearSearch();
        expect(bodyRows(table)).toHaveLength(3);
    });

    it('applies the typed text on Enter but not while an IME composition is active', () => {
        const table = new DataTable(host, { columns: columns(), data: rows(), search: true });
        const input = host.querySelector('[data-action="quick-search"]');

        input.dispatchEvent(new CompositionEvent('compositionstart'));
        input.value = 'beta';
        input.dispatchEvent(new Event('input'));
        input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', isComposing: true }));
        expect(bodyRows(table)).toHaveLength(3);

        input.dispatchEvent(new CompositionEvent('compositionend'));
        host.querySelector('[data-action="quick-search"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
        expect(bodyRows(table)).toHaveLength(1);
        expect(host.querySelector('[data-action="quick-search"]').value).toBe('beta');
    });

    it('skips columns marked searchable:false and matches rendered cell text', () => {
        const table = new DataTable(host, {
            columns: [
                { key: 'name', title: 'Name', render: value => raw(`<a href="#/items">${value}</a>`) },
                { key: 'city', title: 'City', searchable: false },
            ],
            data: rows(),
            search: true,
        });
        table.setSearchText('south');
        expect(bodyRows(table)).toHaveLength(0);
        table.setSearchText('gamma');
        expect(bodyRows(table)).toHaveLength(1);
    });

    it('clears the selection when the filter changes and select-all only covers visible rows', () => {
        const table = new DataTable(host, {
            columns: columns(),
            data: rows(),
            search: true,
            selectableRows: 'multiple',
        });
        table.setSearchText('north');

        const selectAll = host.querySelector('[data-action="select-all"]');
        selectAll.checked = true;
        selectAll.dispatchEvent(new Event('change'));

        const header = host.querySelector('[data-action="select-all"]');
        expect(header.checked).toBe(true);
        expect(bodyRows(table).every(tr => tr.classList.contains('b4a-dt__tr--selected'))).toBe(true);

        table.setSearchText('');
        expect(host.querySelectorAll('tbody tr.b4a-dt__tr--selected')).toHaveLength(0);
    });

    it('accepts custom text labels', () => {
        new DataTable(host, {
            columns: columns(),
            data: rows(),
            search: true,
            options: { textLabels: { search: { placeholder: 'Filter', resultCount: '{count}/{total}', buttonLabel: 'Go' } } },
        });
        const input = host.querySelector('[data-action="quick-search"]');
        expect(input.getAttribute('placeholder')).toBe('Filter');
        expect(host.querySelector('[data-action="quick-search-submit"]').getAttribute('title')).toBe('Go');
        expect(host.querySelector('.b4a-dt__quick-search-count').textContent).toBe('3/3');
    });
});
