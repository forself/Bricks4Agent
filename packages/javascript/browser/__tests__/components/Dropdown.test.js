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
});
