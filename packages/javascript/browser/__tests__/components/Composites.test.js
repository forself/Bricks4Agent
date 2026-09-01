import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { Alert } from '../../ui_components/common/Alert/Alert.js';
import { EmptyState } from '../../ui_components/common/EmptyState/EmptyState.js';

let container;
beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
afterEach(() => { container.remove(); });

describe('Alert (composite = Icon + Text)', () => {
    it('mount 後由原子組成:variant canvas icon + 訊息文字', () => {
        new Alert({ variant: 'success', message: '完成' }).mount(container);
        const alert = container.querySelector('.cl-alert--success');
        expect(alert).not.toBeNull();
        expect(alert.querySelector('.cl-icon canvas.cl-icon__canvas')).not.toBeNull(); // Icon 原子
        expect(alert.querySelector('.cl-text')).not.toBeNull();        // Text 原子
        expect(alert.querySelector('.cl-text').textContent).toBe('完成');
    });

    it('closable 顯示關閉鈕,按下後隱藏', () => {
        const a = new Alert({ message: 'x', closable: true }).mount(container);
        const close = container.querySelector('.cl-alert-close');
        expect(close).not.toBeNull();
        close.click();
        expect(a.snapshot().visibility).toBe('hidden');
    });

    it('destroy 連同子原子一起清除', () => {
        const a = new Alert({ message: 'x' }).mount(container);
        a.destroy();
        expect(container.querySelector('.cl-alert')).toBeNull();
    });
});

describe('EmptyState (composite = Icon + Heading + Text + BasicButton)', () => {
    it('mount 後由原子組成', () => {
        new EmptyState({ icon: 'search', title: '無資料', description: '請調整條件' }).mount(container);
        const empty = container.querySelector('.cl-empty');
        expect(empty.querySelector('.cl-icon canvas.cl-icon__canvas')).not.toBeNull();
        expect(empty.querySelector('h4.cl-heading').textContent).toBe('無資料');
        expect(empty.querySelector('.cl-text').textContent).toBe('請調整條件');
    });

    it('actionLabel 產生按鈕並觸發 onAction', () => {
        let acted = false;
        new EmptyState({ title: 't', actionLabel: '重試', onAction: () => { acted = true; } }).mount(container);
        const btn = container.querySelector('.cl-empty-action button');
        expect(btn).not.toBeNull();
        expect(btn.textContent).toContain('重試');
        btn.click();
        expect(acted).toBe(true);
    });
});
