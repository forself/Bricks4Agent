import { beforeEach, describe, expect, it, vi } from 'vitest';

import {
    __resetCategoryOptionsForTests,
    ensureCategoryOptions,
    getCategoryLabel,
    getCategoryOptions,
} from '../../../../../templates/spa/frontend/pages/commerce.constants.js';

describe('SPA commerce category options', () => {
    beforeEach(() => {
        __resetCategoryOptionsForTests();
    });

    it('loads category options from the backend and uses them for labels instead of fixed fallback IDs', async () => {
        const api = {
            get: vi.fn().mockResolvedValue([
                { id: 7, name: 'Seasonal Boxes', status: 'active' },
                { id: 42, name: 'VIP Services', status: 'active' },
            ]),
        };

        await expect(ensureCategoryOptions(api)).resolves.toEqual([
            { value: 7, label: 'Seasonal Boxes' },
            { value: 42, label: 'VIP Services' },
        ]);

        expect(api.get).toHaveBeenCalledWith('/shop/categories');
        expect(getCategoryOptions()).toEqual([
            { value: 7, label: 'Seasonal Boxes' },
            { value: 42, label: 'VIP Services' },
        ]);
        expect(getCategoryLabel(42)).toBe('VIP Services');
    });
});
