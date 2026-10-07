import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MultiSelectDropdown } from '../../ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js';

const items = [
    { label: 'Alpha', value: 'a' },
    { label: 'Beta', value: 'b' },
    { label: 'Gamma', value: 'c' },
];

const options = (control) => [...control._menu.querySelectorAll('.msd__option')];
const key = (control, name) => control._input.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true }));

/**
 * 真實瀏覽器中，游標停在選項上時 mouseenter 會一再觸發；每次都重建選單，選項節點就一直被換掉，
 * mousedown 與 mouseup 落不到同一個節點，點擊因此落空。重繪標籤時把輸入框移出 DOM，焦點也會遺失，
 * 方向鍵之後的 Enter 就沒有作用。這裡鎖住：反白只更新背景、輸入框永遠留在原位。
 * 真實滑鼠與鍵盤的驗證在 tools/scripts/definition-site-smoke.mjs（npm run test:definition-site:browser）。
 */
describe('MultiSelectDropdown rendering', () => {
    let host;

    beforeEach(() => {
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
    });

    it('keeps the option nodes when the pointer moves over the options', () => {
        const control = new MultiSelectDropdown({ items }).mount(host);
        control.open();
        const before = options(control);
        expect(before).toHaveLength(3);

        before[1].dispatchEvent(new MouseEvent('mouseenter'));
        before[1].dispatchEvent(new MouseEvent('mouseenter'));
        const after = options(control);
        expect(after[1]).toBe(before[1]);
        expect(after[0]).toBe(before[0]);
        expect(control.snapshot().highlightIndex).toBe(1);
        expect(after[1].style.background).toContain('--cl-bg-secondary');
        expect(after[0].style.background).toBe('transparent');

        before[2].dispatchEvent(new MouseEvent('mouseenter'));
        expect(options(control)[2]).toBe(before[2]);
        expect(before[1].style.background).toBe('transparent');
        expect(before[2].style.background).toContain('--cl-bg-secondary');
        control.destroy();
    });

    it('selects the option under the pointer on click and keeps the input in place', () => {
        const control = new MultiSelectDropdown({ items }).mount(host);
        const input = control._input;
        control.open();
        const target = options(control)[1];
        target.dispatchEvent(new MouseEvent('mouseenter'));
        target.click();

        expect(control.getValues()).toEqual(['b']);
        expect(control._input).toBe(input);
        expect(input.isConnected).toBe(true);
        expect(input.parentNode).toBe(control._tagsWrap);
        expect(control._tagsWrap.lastElementChild).toBe(input);
        expect(control._tagsWrap.querySelectorAll('.msd__tag')).toHaveLength(1);
        control.destroy();
    });

    it('keeps the focus on the input while the keyboard moves and selects', () => {
        const control = new MultiSelectDropdown({ items }).mount(host);
        control._input.focus();
        expect(control.snapshot().open).toBe(true);
        expect(document.activeElement).toBe(control._input);

        const menuBefore = options(control);
        key(control, 'ArrowDown');
        key(control, 'ArrowDown');
        expect(document.activeElement).toBe(control._input);
        expect(options(control)[0]).toBe(menuBefore[0]);
        expect(control.snapshot().highlightIndex).toBe(1);

        key(control, 'Enter');
        expect(control.getValues()).toEqual(['b']);
        expect(document.activeElement).toBe(control._input);

        // 選取的項目排到最前面（b、a、c），反白索引不變；再往下一格是 c。
        key(control, 'ArrowDown');
        key(control, 'Enter');
        expect(control.getValues()).toEqual(['b', 'c']);
        expect(document.activeElement).toBe(control._input);
        expect(control._tagsWrap.querySelectorAll('.msd__tag')).toHaveLength(2);
        control.destroy();
    });

    it('rebuilds the menu when the selection, the filter or the items change', () => {
        const control = new MultiSelectDropdown({ items }).mount(host);
        control.open();
        const first = options(control)[0];

        control.setValues(['c']);
        expect(options(control)[0]).not.toBe(first);
        expect(options(control)[0].dataset.value).toBe('c');

        control._input.value = 'alp';
        control._input.dispatchEvent(new Event('input'));
        expect(options(control).map((option) => option.dataset.value)).toEqual(['a']);

        control.setItems([{ label: 'Delta', value: 'd' }]);
        control._input.value = '';
        control._input.dispatchEvent(new Event('input'));
        expect(options(control).map((option) => option.dataset.value)).toEqual(['d']);
        expect(control.getValues()).toEqual([]);
        expect(control._tagsWrap.querySelectorAll('.msd__tag')).toHaveLength(0);
        control.destroy();
    });
});
