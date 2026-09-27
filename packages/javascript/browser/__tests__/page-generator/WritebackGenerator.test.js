import { describe, it, expect, afterEach } from 'vitest';
import { DynamicDetailRenderer } from '../../page-generator/DynamicDetailRenderer.js';
import { FieldResolver } from '../../page-generator/FieldResolver.js';
import { TEXTAREA_SIZING_STORAGE_KEY } from '../../ui_components/form/TextArea/TextArea.js';
import { normalizeTableTextLabels } from '../../page-generator/QueryDefinitionAdapter.js';

const subtableDefinition = (table = {}) => ({
    page: { id: 'detail.demo', title: 'Demo', view: 'detail' },
    fields: [],
    detail: {
        subtables: [{
            id: 'items',
            title: 'Items',
            source: 'items',
            fields: [
                { label: 'Name', source: 'name', link: { route: '#/items/{row.id}' } },
                { label: 'Code', source: 'code' },
                { label: 'Internal', source: 'secret', hidden: true },
            ],
            table,
        }],
    },
});

function renderDetail(definition, data) {
    const renderer = new DynamicDetailRenderer({ definition, data, lazyTabs: false });
    const host = document.createElement('div');
    document.body.appendChild(host);
    renderer.mount(host);
    return { renderer, host };
}

describe('DynamicDetailRenderer subtables', () => {
    afterEach(() => {
        document.body.innerHTML = '';
    });

    it('shows a load error instead of an empty table', () => {
        const { host, renderer } = renderDetail(subtableDefinition(), { subtableErrors: { items: 'Load failed' } });
        const alert = host.querySelector('.dynamic-detail__subtable-error');
        expect(alert).not.toBeNull();
        expect(alert.getAttribute('role')).toBe('alert');
        expect(alert.textContent).toBe('Load failed');
        expect(host.querySelector('.dynamic-detail__subtable-wrap')).toBeNull();
        renderer.destroy();
    });

    it('renders link templates, hides hidden fields and applies table options', () => {
        const data = {
            items: [
                { id: 7, name: 'Alpha', code: 'A1', secret: 's1' },
                { id: 0, name: 'Beta', code: 'B1', secret: 's2' },
            ],
        };
        const { host, renderer } = renderDetail(
            subtableDefinition({ titleTemplate: '{count} items', search: true }),
            data,
        );
        const section = host.querySelector('.dynamic-detail__subtable');
        expect(section.textContent).toContain('2 items');
        expect(section.querySelector('.b4a-dt__quick-search')).not.toBeNull();

        const headers = [...section.querySelectorAll('thead th')].map(th => th.dataset.fieldSource);
        expect(headers).toEqual(['name', 'code']);

        const links = [...section.querySelectorAll('a')].map(a => a.getAttribute('href'));
        expect(links).toContain('#/items/7');
        expect(links.some(href => href && href.includes('/0'))).toBe(false);
        expect(section.textContent).toContain('Beta');
        renderer.destroy();
    });
});

describe('FieldResolver textarea rows', () => {
    afterEach(() => {
        localStorage.removeItem(TEXTAREA_SIZING_STORAGE_KEY);
    });

    it('passes explicit rows through and otherwise keeps the TextArea default fixed box', async () => {
        const resolver = new FieldResolver();
        await resolver.preload(['textarea']);
        const plain = resolver.resolve({ fieldName: 'note', fieldType: 'textarea', validation: { maxLength: 2000 } });
        expect(plain.component.textarea.rows).toBe(5);
        expect(plain.component.getSizing()).toBe('fixed');

        const tall = resolver.resolve({ fieldName: 'memo', fieldType: 'textarea', rows: 8 });
        expect(tall.component.textarea.rows).toBe(8);
    });
});

describe('Query table text labels', () => {
    it('maps quick-search labels onto DataTable text labels', () => {
        const labels = normalizeTableTextLabels({
            textLabels: { search: { placeholder: 'Filter rows', resultCount: '{count} of {total}', buttonLabel: 'Apply' } },
        });
        expect(labels.search).toEqual({ placeholder: 'Filter rows', resultCount: '{count} of {total}', buttonLabel: 'Apply' });
    });

    it('omits the search block when no search label is configured', () => {
        expect(normalizeTableTextLabels({ textLabels: {} }).search).toBeUndefined();
    });
});
