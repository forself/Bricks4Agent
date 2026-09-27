import { describe, it, expect, vi, afterEach } from 'vitest';
import { BasicButton } from '../../ui_components/common/BasicButton/BasicButton.js';
import { Dropdown } from '../../ui_components/form/Dropdown/Dropdown.js';
import { MultiSelectDropdown } from '../../ui_components/form/MultiSelectDropdown/MultiSelectDropdown.js';

const ITEMS = [{ label: 'A', value: 'a' }];
let host;

afterEach(() => {
    host?.remove();
    host = null;
});

function mountHost() {
    host = document.createElement('div');
    document.body.appendChild(host);
    return host;
}

describe('BasicButton created disabled', () => {
    it('becomes clickable after setDisabled(false)', () => {
        const onClick = vi.fn();
        const button = new BasicButton({ label: 'Save', disabled: true, onClick });
        button.mount(mountHost());

        button.button.click();
        expect(onClick).not.toHaveBeenCalled();

        button.setDisabled(false);
        button.button.click();
        expect(onClick).toHaveBeenCalledTimes(1);
    });

    it('ignores clicks and hover styling again after setDisabled(true)', () => {
        const onClick = vi.fn();
        const button = new BasicButton({ label: 'Save', onClick });
        button.mount(mountHost());
        button.setDisabled(true);

        const before = button.button.style.transform;
        button.button.dispatchEvent(new MouseEvent('mouseenter'));
        expect(button.button.style.transform).toBe(before);
        button.button.click();
        expect(onClick).not.toHaveBeenCalled();
    });
});

describe('dropdown input text colour', () => {
    it('uses the theme text colour so dark themes stay readable', () => {
        const dropdown = new Dropdown({ items: ITEMS, variant: 'searchable' });
        const select = new MultiSelectDropdown({ items: ITEMS });
        dropdown.mount(mountHost());
        select.mount(host);

        expect(dropdown.input.style.color).toBe('var(--cl-text)');
        expect(select._input.style.color).toBe('var(--cl-text)');
        dropdown.destroy();
        select.destroy();
    });
});
