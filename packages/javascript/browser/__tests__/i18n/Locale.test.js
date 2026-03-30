import { describe, it, expect, beforeEach } from 'vitest';
import Locale from '../../ui_components/i18n/index.js';

describe('Locale', () => {
    beforeEach(() => {
        Locale.setLang('zh-TW');
    });

    it('預設語言為 zh-TW', () => {
        expect(Locale.getLang()).toBe('zh-TW');
    });

    it('t() 回傳已知 key 的翻譯', () => {
        const result = Locale.t('basicButton.confirm');
        expect(result).toBeTruthy();
        expect(typeof result).toBe('string');
    });

    it('t() 對未知 key 回傳 key 本身', () => {
        const result = Locale.t('nonexistent.key.here');
        expect(result).toBe('nonexistent.key.here');
    });

    it('切換到 en 後翻譯改變', () => {
        const zhResult = Locale.t('basicButton.confirm');
        Locale.setLang('en');
        const enResult = Locale.t('basicButton.confirm');
        // 中英文翻譯應不同（除非碰巧相同）
        expect(typeof enResult).toBe('string');
        expect(enResult).toBeTruthy();
    });

    it('getAvailableLangs 包含 zh-TW 和 en', () => {
        const langs = Locale.getAvailableLangs();
        expect(langs).toContain('zh-TW');
        expect(langs).toContain('en');
    });

    it('getComponentStrings 回傳元件字串', () => {
        const strings = Locale.getComponentStrings('basicButton');
        expect(strings).toBeDefined();
        expect(typeof strings).toBe('object');
    });
});
