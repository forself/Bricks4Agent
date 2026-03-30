import { describe, it, expect } from 'vitest';

describe('Test environment', () => {
    it('jsdom is configured', () => {
        expect(document).toBeDefined();
        expect(document.createElement).toBeInstanceOf(Function);
    });

    it('data-theme is set to light', () => {
        expect(document.documentElement.getAttribute('data-theme')).toBe('light');
    });
});
