import { describe, it, expect, beforeEach } from 'vitest';

describe('Theme Switching', () => {
    beforeEach(() => {
        // 重設為預設 light 主題
        document.documentElement.setAttribute('data-theme', 'light');
    });

    it('預設 data-theme 為 light', () => {
        expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    });

    it('切換為 dark 主題', () => {
        document.documentElement.setAttribute('data-theme', 'dark');
        expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    });

    it('從 dark 切換回 light', () => {
        document.documentElement.setAttribute('data-theme', 'dark');
        expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
        document.documentElement.setAttribute('data-theme', 'light');
        expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    });

    it('移除 data-theme 屬性', () => {
        document.documentElement.setAttribute('data-theme', 'dark');
        document.documentElement.removeAttribute('data-theme');
        expect(document.documentElement.getAttribute('data-theme')).toBeNull();
    });

    it('可設定為自訂主題', () => {
        document.documentElement.setAttribute('data-theme', 'high-contrast');
        expect(document.documentElement.getAttribute('data-theme')).toBe('high-contrast');
    });

    it('多次快速切換後狀態正確', () => {
        document.documentElement.setAttribute('data-theme', 'dark');
        document.documentElement.setAttribute('data-theme', 'light');
        document.documentElement.setAttribute('data-theme', 'dark');
        document.documentElement.setAttribute('data-theme', 'light');
        expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    });
});
