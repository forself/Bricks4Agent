import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TextInput } from '../../ui_components/form/TextInput/TextInput.js';

describe('TextInput', () => {
    let container;

    beforeEach(() => {
        container = document.createElement('div');
        document.body.appendChild(container);
    });

    afterEach(() => {
        container.remove();
    });

    it('mount 後 input 元素存在', () => {
        const input = new TextInput({ placeholder: 'test' });
        input.mount(container);
        const el = container.querySelector('input.text-input');
        expect(el).not.toBeNull();
        expect(el.type).toBe('text');
    });

    it('mount 回傳 this 以支援鏈式呼叫', () => {
        const input = new TextInput();
        const result = input.mount(container);
        expect(result).toBe(input);
    });

    it('getValue 回傳初始值', () => {
        const input = new TextInput({ value: 'hello' });
        expect(input.getValue()).toBe('hello');
    });

    it('setValue 設定值後 getValue 回傳新值', () => {
        const input = new TextInput();
        input.mount(container);
        input.setValue('world');
        expect(input.getValue()).toBe('world');
        expect(input.input.value).toBe('world');
    });

    it('clear 清空值', () => {
        const input = new TextInput({ value: 'data' });
        input.mount(container);
        input.clear();
        expect(input.getValue()).toBe('');
    });

    it('setDisabled(true) 設定 disabled 屬性', () => {
        const input = new TextInput();
        input.mount(container);
        input.setDisabled(true);
        expect(input.input.disabled).toBe(true);
        const state = input.snapshot();
        expect(state.availability).toBe('disabled');
    });

    it('setDisabled(false) 恢復可用狀態', () => {
        const input = new TextInput({ disabled: true });
        input.mount(container);
        input.setDisabled(false);
        expect(input.input.disabled).toBe(false);
        const state = input.snapshot();
        expect(state.availability).toBe('enabled');
    });

    it('setError 顯示錯誤訊息', () => {
        const input = new TextInput();
        input.mount(container);
        input.setError('Required field');
        const state = input.snapshot();
        expect(state.validation.status).toBe('error');
        expect(state.validation.message).toBe('Required field');
        // 錯誤訊息元素應存在
        expect(input.message).not.toBeNull();
        expect(input.message.textContent).toBe('Required field');
        expect(input.message.className).toBe('text-input__error');
    });

    it('clearError 清除錯誤', () => {
        const input = new TextInput();
        input.mount(container);
        input.setError('error');
        input.clearError();
        const state = input.snapshot();
        expect(state.validation.status).not.toBe('error');
    });

    it('destroy 後從 DOM 移除', () => {
        const input = new TextInput();
        input.mount(container);
        expect(container.querySelector('.text-input-container')).not.toBeNull();
        input.destroy();
        expect(container.querySelector('.text-input-container')).toBeNull();
        const state = input.snapshot();
        expect(state.lifecycle).toBe('destroyed');
    });

    it('show/hide 切換可見性', () => {
        const input = new TextInput();
        input.mount(container);
        input.hide();
        expect(input.snapshot().visibility).toBe('hidden');
        expect(input.element.style.display).toBe('none');
        input.show();
        expect(input.snapshot().visibility).toBe('visible');
        expect(input.element.style.display).toBe('');
    });

    it('建立時帶 label 會產生 label 元素', () => {
        const input = new TextInput({ label: 'Name', required: true });
        input.mount(container);
        const label = container.querySelector('.text-input__label');
        expect(label).not.toBeNull();
        expect(label.textContent).toContain('Name');
    });

    it('建立時帶 hint 顯示提示訊息', () => {
        const input = new TextInput({ hint: 'Enter your name' });
        expect(input.message).not.toBeNull();
        expect(input.message.textContent).toBe('Enter your name');
        expect(input.message.className).toBe('text-input__hint');
    });

    it('type 為 email 時 input type 正確', () => {
        const input = new TextInput({ type: 'email' });
        expect(input.input.type).toBe('email');
    });

    it('maxLength 限制正確設定', () => {
        const input = new TextInput({ maxLength: 50 });
        expect(input.input.maxLength).toBe(50);
    });
});
