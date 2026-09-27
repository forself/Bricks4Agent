import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { NumberInput } from '../../ui_components/form/NumberInput/NumberInput.js';

describe('NumberInput commit events', () => {
    let host;

    beforeEach(() => {
        host = document.createElement('div');
        document.body.appendChild(host);
    });

    afterEach(() => {
        host.remove();
    });

    const type = (control, text) => {
        control.input.value = text;
        control.input.dispatchEvent(new Event('input'));
    };

    it('fires onChange once when typed input is committed by blur', () => {
        const onChange = vi.fn();
        const control = new NumberInput({ onChange, min: 0, max: 100 });
        control.mount(host);
        type(control, '7');
        control.input.dispatchEvent(new Event('blur'));
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenLastCalledWith(7);

        control.input.dispatchEvent(new Event('blur'));
        expect(onChange).toHaveBeenCalledTimes(1);
    });

    it('fires onChange when typed input is committed by Enter and not again on the following blur', () => {
        const onChange = vi.fn();
        const control = new NumberInput({ onChange, min: 0, max: 100 });
        control.mount(host);
        type(control, '12');
        control.input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
        control.input.dispatchEvent(new Event('blur'));
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(onChange).toHaveBeenLastCalledWith(12);
    });

    it('reports the clamped value and null for a cleared field', () => {
        const onChange = vi.fn();
        const control = new NumberInput({ onChange, min: 0, max: 100 });
        control.mount(host);
        type(control, '150');
        control.input.dispatchEvent(new Event('blur'));
        expect(onChange).toHaveBeenLastCalledWith(100);

        type(control, '');
        control.input.dispatchEvent(new Event('blur'));
        expect(onChange).toHaveBeenLastCalledWith(null);
    });

    it('does not fire for programmatic setValue unless emit is requested', () => {
        const onChange = vi.fn();
        const control = new NumberInput({ onChange });
        control.mount(host);
        control.setValue(3);
        expect(onChange).not.toHaveBeenCalled();
        control.setValue(4, { emit: true });
        expect(onChange).toHaveBeenCalledWith(4);
    });
});
