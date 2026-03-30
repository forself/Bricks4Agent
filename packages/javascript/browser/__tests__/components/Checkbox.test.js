import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Checkbox } from '../../ui_components/form/Checkbox/Checkbox.js';

describe('Checkbox', () => {
    let container;

    beforeEach(() => {
        container = document.createElement('div');
        document.body.appendChild(container);
    });

    afterEach(() => {
        container.remove();
    });

    it('mount 後 checkbox input 存在', () => {
        const cb = new Checkbox({ label: 'Accept' });
        cb.mount(container);
        const input = container.querySelector('input[type="checkbox"]');
        expect(input).not.toBeNull();
    });

    it('mount 回傳 this 以支援鏈式呼叫', () => {
        const cb = new Checkbox();
        const result = cb.mount(container);
        expect(result).toBe(cb);
    });

    it('預設 getValue 回傳 false', () => {
        const cb = new Checkbox();
        expect(cb.getValue()).toBe(false);
    });

    it('setValue(true) 後 getValue 回傳 true', () => {
        const cb = new Checkbox();
        cb.mount(container);
        cb.setValue(true);
        expect(cb.getValue()).toBe(true);
        expect(cb.input.checked).toBe(true);
    });

    it('setValue(false) 後 getValue 回傳 false', () => {
        const cb = new Checkbox({ checked: true });
        cb.mount(container);
        cb.setValue(false);
        expect(cb.getValue()).toBe(false);
    });

    it('isChecked 回傳正確的勾選狀態', () => {
        const cb = new Checkbox({ checked: false });
        expect(cb.isChecked()).toBe(false);
        cb.setChecked(true);
        expect(cb.isChecked()).toBe(true);
    });

    it('setChecked(true) 設定勾選', () => {
        const cb = new Checkbox();
        cb.mount(container);
        cb.setChecked(true);
        expect(cb.isChecked()).toBe(true);
        expect(cb.input.checked).toBe(true);
    });

    it('setChecked(false) 取消勾選', () => {
        const cb = new Checkbox({ checked: true });
        cb.mount(container);
        cb.setChecked(false);
        expect(cb.isChecked()).toBe(false);
        expect(cb.input.checked).toBe(false);
    });

    it('toggle 切換勾選狀態', () => {
        const cb = new Checkbox({ checked: false });
        cb.mount(container);
        cb.toggle();
        expect(cb.isChecked()).toBe(true);
        cb.toggle();
        expect(cb.isChecked()).toBe(false);
    });

    it('setDisabled(true) 設定 disabled', () => {
        const cb = new Checkbox();
        cb.mount(container);
        cb.setDisabled(true);
        expect(cb.input.disabled).toBe(true);
        const state = cb.snapshot();
        expect(state.availability).toBe('disabled');
    });

    it('setDisabled(false) 恢復可用', () => {
        const cb = new Checkbox({ disabled: true });
        cb.mount(container);
        cb.setDisabled(false);
        expect(cb.input.disabled).toBe(false);
        const state = cb.snapshot();
        expect(state.availability).toBe('enabled');
    });

    it('clear 恢復為未勾選', () => {
        const cb = new Checkbox({ checked: true });
        cb.mount(container);
        cb.clear();
        expect(cb.isChecked()).toBe(false);
    });

    it('show/hide 切換可見性', () => {
        const cb = new Checkbox();
        cb.mount(container);
        cb.hide();
        expect(cb.snapshot().visibility).toBe('hidden');
        expect(cb.element.style.display).toBe('none');
        cb.show();
        expect(cb.snapshot().visibility).toBe('visible');
        expect(cb.element.style.display).toBe('');
    });

    it('destroy 後從 DOM 移除', () => {
        const cb = new Checkbox();
        cb.mount(container);
        expect(container.querySelector('input[type="checkbox"]')).not.toBeNull();
        cb.destroy();
        expect(container.querySelector('input[type="checkbox"]')).toBeNull();
        expect(cb.snapshot().lifecycle).toBe('destroyed');
    });

    it('建構時帶 checked: true 預設勾選', () => {
        const cb = new Checkbox({ checked: true });
        expect(cb.isChecked()).toBe(true);
        expect(cb.input.checked).toBe(true);
    });

    it('label 文字正確顯示', () => {
        const cb = new Checkbox({ label: 'I agree' });
        cb.mount(container);
        const label = container.querySelector('.checkbox__label');
        expect(label).not.toBeNull();
        expect(label.textContent).toBe('I agree');
    });
});
