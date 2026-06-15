import { describe, it, expect, beforeEach } from 'vitest';
import { nextUid, resetUid } from '../../ui_components/utils/uid.js';
import { Notification } from '../../ui_components/common/Notification/Notification.js';

beforeEach(() => { resetUid(); document.body.innerHTML = ''; });

describe('determinism-clean IDs (Stage 3)', () => {
    it('nextUid 單調遞增、可重置,無 random/Date', () => {
        resetUid();
        expect(nextUid('x')).toBe('x-1');
        expect(nextUid('x')).toBe('x-2');
        expect(nextUid('y')).toBe('y-3');
        resetUid();
        expect(nextUid('x')).toBe('x-1');
    });

    it('Notification.id 在相同建構序列下可重現(無 epoch-ms / random)', () => {
        resetUid();
        const a1 = new Notification({ message: 'a' }).id;
        const a2 = new Notification({ message: 'b' }).id;
        resetUid();
        const b1 = new Notification({ message: 'a' }).id;
        const b2 = new Notification({ message: 'b' }).id;

        expect([a1, a2]).toEqual([b1, b2]);
        expect(a1).toBe('notification-1');
        expect(a1).not.toMatch(/\d{13}/);   // 不含 13 位 epoch 毫秒
    });

    it('Notification.id 可由 options 注入覆寫', () => {
        expect(new Notification({ message: 'a', id: 'fixed-id' }).id).toBe('fixed-id');
    });
});
