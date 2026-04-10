import { describe, expect, it } from 'vitest';

import { FrontendDefinitionMaterializer } from '../../../../../templates/spa/frontend/runtime/materializers/FrontendDefinitionMaterializer.js';
import { DefinitionRuntimePage } from '../../../../../templates/spa/frontend/runtime/DefinitionRuntimePage.js';

describe('FrontendDefinitionMaterializer', () => {
    const frontendDefinition = {
        schema_version: 1,
        app: { title: 'Bricks Commerce', subtitle: 'proof', shell: 'default' },
        auth: {
            token_storage: 'local_storage',
            login_route: '/login',
            logout_action: 'auth.logout',
            roles: ['guest', 'member', 'admin'],
        },
        navigation: { items: [] },
        shared_resources: {
            enums: {
                category_options: [{ label: 'Digital Goods', value: '1' }],
            },
            messages: {},
        },
        routes: [
            {
                path: '/products',
                page_id: 'shop_products',
                page_kind: 'resource_list_page',
                title: 'Products',
                access: { roles: ['guest', 'member', 'admin'] },
                data_sources: {
                    primary_list: { method: 'GET', endpoint: '/shop/products' },
                },
                state: { selected_item_key: 'selectedProductId' },
                surfaces: [
                    { surface_id: 'feedback', surface_kind: 'message_panel' },
                    {
                        surface_id: 'search',
                        surface_kind: 'search_form',
                        fields: [
                            { name: 'keyword', field_type: 'text', label: 'Keyword' },
                        ],
                    },
                    {
                        surface_id: 'table',
                        surface_kind: 'data_table',
                        source: 'primary_list',
                        columns: [{ key: 'name', label: 'Name' }],
                        row_actions: [],
                    },
                ],
            },
        ],
    };

    it('materializes a route map keyed by path', () => {
        const materializer = new FrontendDefinitionMaterializer(frontendDefinition);
        const result = materializer.materialize();

        expect(result.routes['/products']).toBeDefined();
        expect(result.routes['/products'].pageKind).toBe('resource_list_page');
        expect(result.routes['/products'].surfaces).toHaveLength(3);
    });

    it('throws for unsupported surface kinds', () => {
        const materializer = new FrontendDefinitionMaterializer({
            ...frontendDefinition,
            routes: [
                {
                    ...frontendDefinition.routes[0],
                    surfaces: [{ surface_id: 'x', surface_kind: 'workflow_grid' }],
                },
            ],
        });

        expect(() => materializer.materialize()).toThrow('Unsupported surface_kind: workflow_grid');
    });

    it('prefers definitionOverride over static definition', () => {
        class TestRuntimePage extends DefinitionRuntimePage {}

        TestRuntimePage.definition = {
            name: 'StaticPage',
            type: 'list',
            description: 'Static',
            fields: [],
        };

        const overrideDefinition = {
            name: 'OverridePage',
            type: 'form',
            description: 'Override',
            fields: [],
        };

        const page = new TestRuntimePage({ definitionOverride: overrideDefinition });

        expect(page._resolveDefinition()).toBe(overrideDefinition);
    });
});
