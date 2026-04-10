import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { NumberInput } from '../../ui_components/form/NumberInput/NumberInput.js';

describe('NumberInput', () => {
    let container;

    beforeEach(() => {
        container = document.createElement('div');
        document.body.appendChild(container);
    });

    afterEach(() => {
        container.remove();
    });

    it('preserves typed value after blur validation', () => {
        const input = new NumberInput();
        input.mount(container);

        input.input.value = '99';
        input.input.dispatchEvent(new Event('input', { bubbles: true }));
        input.input.dispatchEvent(new Event('blur', { bubbles: true }));

        expect(input.getValue()).toBe(99);
        expect(input.input.value).toBe('99');
    });
});
