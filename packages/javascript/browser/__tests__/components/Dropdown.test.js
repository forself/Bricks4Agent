import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Dropdown } from '../../ui_components/form/Dropdown/Dropdown.js';

describe('Dropdown', () => {
    let container;

    beforeEach(() => {
        container = document.createElement('div');
        document.body.appendChild(container);
    });

    afterEach(() => {
        container.remove();
    });

    it('hovering an option does not recreate menu nodes before click selection', () => {
        const dropdown = new Dropdown({
            variant: Dropdown.VARIANTS.SEARCHABLE,
            items: [
                { label: 'Digital Goods', value: '1' },
                { label: 'Member Services', value: '2' }
            ]
        });

        dropdown.mount(container);
        dropdown.open();

        const firstOptionBeforeHover = container.querySelector('.dropdown__option[data-value="1"]');
        expect(firstOptionBeforeHover).not.toBeNull();

        firstOptionBeforeHover.dispatchEvent(new MouseEvent('mouseenter', { bubbles: false }));

        const firstOptionAfterHover = container.querySelector('.dropdown__option[data-value="1"]');
        expect(firstOptionAfterHover).toBe(firstOptionBeforeHover);
    });

    it('does not display unmatched search text as a committed value after close', () => {
        const changes = [];
        const dropdown = new Dropdown({
            variant: Dropdown.VARIANTS.SEARCHABLE,
            placeholder: 'Select a service',
            items: [
                { label: 'Digital Goods', value: '1' },
                { label: 'Member Services', value: '2' }
            ],
            onChange: value => changes.push(value)
        });

        dropdown.mount(container);
        dropdown.input.value = 'Not an option';
        dropdown.input.dispatchEvent(new Event('input', { bubbles: true }));
        expect(dropdown.input.value).toBe('Not an option');

        dropdown.close();

        expect(dropdown.getValue()).toBeNull();
        expect(dropdown.input.value).toBe('');
        expect(changes).toEqual([]);
    });

    it('restores the selected label after a search is dismissed', () => {
        const dropdown = new Dropdown({
            variant: Dropdown.VARIANTS.SEARCHABLE,
            items: [
                { label: 'Digital Goods', value: '1' },
                { label: 'Member Services', value: '2' }
            ],
            value: '1'
        });

        dropdown.mount(container);
        dropdown.input.value = 'Member';
        dropdown.input.dispatchEvent(new Event('input', { bubbles: true }));
        dropdown.close();

        expect(dropdown.getValue()).toBe('1');
        expect(dropdown.input.value).toBe('Digital Goods');
    });
});
