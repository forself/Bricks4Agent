import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { ResultList } from '../../ui_components/common/ResultList/ResultList.js';
import { List } from '../../ui_components/common/List/List.js';
import { DescriptionList } from '../../ui_components/common/DescriptionList/DescriptionList.js';
import { FilterBar } from '../../ui_components/common/FilterBar/FilterBar.js';
import { StatGrid } from '../../ui_components/common/StatGrid/StatGrid.js';
import { CardGrid } from '../../ui_components/common/CardGrid/CardGrid.js';
import { StepIndicator } from '../../ui_components/common/StepIndicator/StepIndicator.js';
import { DropdownMenu } from '../../ui_components/common/DropdownMenu/DropdownMenu.js';
import { Form } from '../../ui_components/form/Form/Form.js';
import { TagInput } from '../../ui_components/form/TagInput/TagInput.js';
import { EditableTable } from '../../ui_components/layout/EditableTable/EditableTable.js';
import { PageHeader } from '../../ui_components/sections/PageHeader/PageHeader.js';
import { PageFooter } from '../../ui_components/sections/PageFooter/PageFooter.js';
import { BannerSection } from '../../ui_components/sections/BannerSection/BannerSection.js';
import { ContentSection } from '../../ui_components/sections/ContentSection/ContentSection.js';

let container;
beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
afterEach(() => { container.remove(); });

describe('smoke: 每個新複合/區段都能建構 + mount + destroy', () => {
    const cases = [
        () => new ResultList({ items: [{ title: 'A', url: '/a', snippet: 's', meta: 'm', tags: ['x'] }] }),
        () => new List({ items: [{ primary: 'p', secondary: 's' }] }),
        () => new DescriptionList({ items: [{ label: 'k', value: 'v' }] }),
        () => new FilterBar({ filters: [{ key: 'c', label: '類別', type: 'select', options: [{ label: 'A', value: 'a' }] }] }),
        () => new StatGrid({ stats: [{ label: 'L', value: 1 }], columns: 2 }),
        () => new CardGrid({ cards: [{ title: 'T', body: 'B' }], columns: 2 }),
        () => new StepIndicator({ steps: [{ label: 's1' }, { label: 's2' }], current: 1 }),
        () => new DropdownMenu({ label: 'M', items: [{ label: 'x', onClick: () => {} }] }),
        () => new Form({ fields: [{ name: 'n', label: 'N', type: 'text' }] }),
        () => new TagInput({ tags: ['a'] }),
        () => new EditableTable({ columns: [{ key: 'id', label: 'ID', sortable: true }], rows: [{ id: 2 }, { id: 1 }] }),
        () => new PageHeader({ brand: 'B', navLinks: [{ label: 'Home', href: '/' }] }),
        () => new PageFooter({ links: [{ label: 'L', href: '/l' }], copyright: '(c)' }),
        () => new BannerSection({ title: 'T', body: 'B', actionLabel: 'Go', actionUrl: '/go' }),
        () => new ContentSection({ title: 'T', body: 'B' })
    ];
    it.each(cases)('case %#', (make) => {
        const c = make().mount(container);
        expect(container.children.length).toBeGreaterThan(0);
        c.destroy();
        expect(container.children.length).toBe(0);
    });
});

describe('行為抽查', () => {
    it('ResultList 計數 / 空集顯示 emptyText', () => {
        expect(new ResultList({ items: [{ title: 'a' }, { title: 'b' }] }).count()).toBe(2);
        new ResultList({ items: [], emptyText: '無結果' }).mount(container);
        expect(container.textContent).toContain('無結果');
    });

    it('EditableTable 排序確定性 + 編輯回寫', () => {
        const t = new EditableTable({
            columns: [{ key: 'id', label: 'ID', sortable: true }, { key: 'name', label: 'Name', editable: true }],
            rows: [{ id: 3, name: 'c' }, { id: 1, name: 'a' }, { id: 2, name: 'b' }]
        }).mount(container);
        t.send('SORT', { key: 'id' });
        expect(t.getRows().map((r) => r.id)).toEqual([1, 2, 3]);
        t.send('EDIT', { index: 0, key: 'name', value: 'z' });
        expect(t.getRows()[0].name).toBe('z');
    });

    it('Form 必填驗證擋送出', () => {
        let submitted = null;
        const f = new Form({ fields: [{ name: 'n', label: 'N', type: 'text', required: true }], onSubmit: (v) => { submitted = v; } }).mount(container);
        f._submit();
        expect(submitted).toBeNull();        // 空必填 -> 擋
        f._fields.find((x) => x.def)?.input.setValue('hi');
        f._submit();
        expect(submitted).toEqual({ n: 'hi' });
    });

    it('TagInput 新增 / 移除', () => {
        const ti = new TagInput({ tags: ['a'] }).mount(container);
        ti.addTag('b');
        expect(ti.getTags()).toEqual(['a', 'b']);
        ti.removeTag('a');
        expect(ti.getTags()).toEqual(['b']);
    });

    it('DropdownMenu 開合', () => {
        const m = new DropdownMenu({ label: 'M', items: [{ label: 'x' }] }).mount(container);
        expect(m.isOpen()).toBe(false);
        m.open();
        expect(m.isOpen()).toBe(true);
    });

    it('BannerSection 不含 hero 字樣(中性命名)', () => {
        new BannerSection({ title: 'X', body: 'Y' }).mount(container);
        expect(container.querySelector('.cl-banner')).not.toBeNull();
        expect(container.innerHTML.toLowerCase()).not.toContain('hero');
    });
});
