import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { BasicButton } from '../../ui_components/common/BasicButton/BasicButton.js';

describe('BasicButton', () => {
    let container;

    beforeEach(() => {
        container = document.createElement('div');
        document.body.appendChild(container);
    });

    afterEach(() => {
        container.remove();
    });

    it('constructor 建立後 element 存在', () => {
        const btn = new BasicButton({ type: 'confirm' });
        expect(btn.element).toBeDefined();
        expect(btn.element).toBeInstanceOf(HTMLElement);
        expect(btn.element.tagName).toBe('BUTTON');
    });

    it('mount(container) 後 DOM 中有 button', () => {
        const btn = new BasicButton({ type: 'confirm' });
        btn.mount(container);
        const found = container.querySelector('button');
        expect(found).not.toBeNull();
        expect(found).toBe(btn.element);
    });

    it('mount 支援 CSS selector 字串', () => {
        container.id = 'test-btn-container';
        const btn = new BasicButton({ type: 'cancel' });
        btn.mount('#test-btn-container');
        expect(container.querySelector('button')).toBe(btn.element);
    });

    it('mount 回傳 this 以支援鏈式呼叫', () => {
        const btn = new BasicButton();
        const result = btn.mount(container);
        expect(result).toBe(btn);
    });

    it('destroy() 後 DOM 清空', () => {
        const btn = new BasicButton({ type: 'confirm' });
        btn.mount(container);
        expect(container.querySelector('button')).not.toBeNull();
        btn.destroy();
        expect(container.querySelector('button')).toBeNull();
    });

    it('setDisabled(true) 設定 button disabled 屬性', () => {
        const btn = new BasicButton({ type: 'confirm' });
        btn.mount(container);
        expect(btn.button.disabled).toBe(false);
        btn.setDisabled(true);
        expect(btn.button.disabled).toBe(true);
        expect(btn.button.style.opacity).toBe('0.5');
        expect(btn.button.style.cursor).toBe('not-allowed');
    });

    it('setDisabled(false) 恢復 button 可用狀態', () => {
        const btn = new BasicButton({ type: 'confirm', disabled: true });
        btn.mount(container);
        btn.setDisabled(false);
        expect(btn.button.disabled).toBe(false);
        expect(btn.button.style.opacity).toBe('1');
        expect(btn.button.style.cursor).toBe('pointer');
    });

    it('setLoading(true) 設定 loading 狀態', () => {
        const btn = new BasicButton({ type: 'confirm' });
        btn.mount(container);
        btn.setLoading(true);
        expect(btn.options.loading).toBe(true);
        expect(btn.button.disabled).toBe(true);
        expect(btn.button.classList.contains('basic-btn--loading')).toBe(true);
    });

    it('setLoading(false) 恢復非 loading 狀態', () => {
        const btn = new BasicButton({ type: 'confirm' });
        btn.mount(container);
        btn.setLoading(true);
        btn.setLoading(false);
        expect(btn.options.loading).toBe(false);
        expect(btn.button.disabled).toBe(false);
        expect(btn.button.classList.contains('basic-btn--loading')).toBe(false);
    });

    it('建立時帶 disabled: true 則按鈕預設為 disabled', () => {
        const btn = new BasicButton({ type: 'confirm', disabled: true });
        expect(btn.button.disabled).toBe(true);
    });

    it('onClick 回呼在點擊時觸發', () => {
        let clicked = false;
        let receivedType = null;
        const btn = new BasicButton({
            type: 'save',
            onClick: (e, info) => {
                clicked = true;
                receivedType = info.type;
            }
        });
        btn.mount(container);
        btn.button.click();
        expect(clicked).toBe(true);
        expect(receivedType).toBe('save');
    });

    it('按鈕具有正確的 CSS class', () => {
        const btn = new BasicButton({ type: 'search', variant: 'secondary' });
        expect(btn.button.className).toContain('basic-btn--search');
        expect(btn.button.className).toContain('basic-btn--secondary');
    });

    it('customLabel 覆蓋預設標籤', () => {
        const btn = new BasicButton({ type: 'confirm', customLabel: 'Go' });
        const label = btn.button.querySelector('.basic-btn__label');
        expect(label).not.toBeNull();
        expect(label.textContent).toBe('Go');
    });

    it('TYPES 靜態屬性包含所有預設類型', () => {
        expect(BasicButton.TYPES.CONFIRM).toBe('confirm');
        expect(BasicButton.TYPES.CANCEL).toBe('cancel');
        expect(BasicButton.TYPES.SEARCH).toBe('search');
        expect(BasicButton.TYPES.SAVE).toBe('save');
        expect(BasicButton.TYPES.DELETE).toBe('delete');
    });
});
