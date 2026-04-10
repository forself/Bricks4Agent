import { describe, expect, it } from 'vitest';

import createRoutes, { createRoutes as createRoutesNamed } from '../../../../../templates/spa/frontend/pages/routes.js';

describe('SPA route creation', () => {
    it('exports createRoutes as the default export so callers cannot bypass definition routing with a legacy array', () => {
        expect(createRoutes).toBe(createRoutesNamed);
        expect(typeof createRoutes).toBe('function');
    });

    it('throws when a route references an unknown page_id', () => {
        expect(() => createRoutes({
            routes: [
                {
                    path: '/mystery',
                    page_id: 'unknown_page',
                    page_kind: 'content_page',
                    title: 'Mystery',
                    surfaces: [],
                },
            ],
        }, { includeGeneratedRoutes: false })).toThrow('Unsupported page_id: unknown_page for route /mystery');
    });
});
