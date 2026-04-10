import { mkdtempSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

import {
    validateArchitecture,
    validateBackendDefinition,
    validateFrontendDefinition,
} from '../../definition-json/validators.js';
import { loadDefinitionBundle } from '../../definition-json/load-definition.mjs';

describe('Definition validation', () => {
    it('accepts the minimal architecture contract', () => {
        const result = validateArchitecture({
            schema_version: 1,
            project: { id: 'commerce-proof', name: 'Bricks Commerce', mode: 'frontend_backend_bundle' },
            frontend: { enabled: true, definition_file: 'frontend-definition.json' },
            backend: { enabled: true, definition_file: 'backend-definition.json' },
            docs: { deployment: true, implementation_plan: true },
        });

        expect(result.valid).toBe(true);
        expect(result.errors).toEqual([]);
    });

    it('rejects a route without surfaces', () => {
        const result = validateFrontendDefinition({
            schema_version: 1,
            app: { title: 'x', subtitle: 'y', shell: 'default' },
            auth: {
                token_storage: 'local_storage',
                login_route: '/login',
                logout_action: 'auth.logout',
                roles: ['guest'],
            },
            navigation: { items: [] },
            shared_resources: { enums: {}, messages: {} },
            routes: [
                {
                    path: '/products',
                    page_id: 'shop_products',
                    page_kind: 'resource_list_page',
                    title: 'Products',
                    access: { requiresAuth: false, requiresAdmin: false },
                },
            ],
        });

        expect(result.valid).toBe(false);
        expect(result.errors).toContain('Route /products must declare surfaces');
    });

    it('rejects unsupported backend tier', () => {
        const result = validateBackendDefinition({
            schema_version: 1,
            enabled: true,
            tier: 'N9',
            template: 'x',
            persistence: { enabled: true, orm: 'BaseOrm', database: 'sqlite' },
            security: { auth_required: true, auth_mode: 'local_jwt', role_model: ['member'] },
            entities: [],
            modules: [],
            seed: {},
        });

        expect(result.valid).toBe(false);
        expect(result.errors).toContain('Unsupported backend tier: N9');
    });

    it('loads and validates a definition bundle deterministically', () => {
        const rootDir = mkdtempSync(path.join(os.tmpdir(), 'definition-json-'));
        writeFileSync(
            path.join(rootDir, 'architecture.json'),
            JSON.stringify({
                schema_version: 1,
                project: { id: 'commerce-proof', name: 'Bricks Commerce', mode: 'frontend_backend_bundle' },
                frontend: { enabled: true, definition_file: 'frontend-definition.json' },
                backend: { enabled: true, definition_file: 'backend-definition.json' },
                docs: { deployment: true, implementation_plan: true },
            }),
            'utf8',
        );
        writeFileSync(
            path.join(rootDir, 'frontend-definition.json'),
            JSON.stringify({
                schema_version: 1,
                app: { title: 'x', subtitle: 'y', shell: 'default' },
                auth: {
                    token_storage: 'local_storage',
                    login_route: '/login',
                    logout_action: 'auth.logout',
                    roles: ['guest'],
                },
                navigation: { items: [] },
                shared_resources: { enums: {}, messages: {} },
                routes: [
                    {
                        path: '/products',
                        page_id: 'shop_products',
                        page_kind: 'resource_list_page',
                        title: 'Products',
                        surfaces: [{ surface_kind: 'data_table' }],
                        access: { roles: ['guest'] },
                    },
                ],
            }),
            'utf8',
        );
        writeFileSync(
            path.join(rootDir, 'backend-definition.json'),
            JSON.stringify({
                schema_version: 1,
                enabled: true,
                tier: 'N1',
                template: 'default',
                persistence: { enabled: true, orm: 'BaseOrm', database: 'sqlite' },
                security: { auth_required: true, auth_mode: 'local_jwt', role_model: ['member'] },
                entities: [],
                modules: [],
                seed: {},
            }),
            'utf8',
        );

        const bundle = loadDefinitionBundle(rootDir, 'architecture.json');

        expect(bundle.architecture.project.id).toBe('commerce-proof');
        expect(bundle.frontend.routes).toHaveLength(1);
        expect(bundle.backend.tier).toBe('N1');
    });

    it('loads the checked-in proof definition bundle', () => {
        const testFilePath = fileURLToPath(import.meta.url);
        const architecturePath = path.resolve(
            path.dirname(testFilePath),
            '../../../../../templates/spa/frontend/definition/architecture.json',
        );
        const rootDir = path.dirname(architecturePath);

        const bundle = loadDefinitionBundle(rootDir, 'architecture.json');

        expect(bundle.architecture.project.id).toBe('spa-commerce-proof');
        expect(bundle.architecture.project.mode).toBe('frontend_backend_bundle');
        expect(bundle.frontend.routes.length).toBeGreaterThan(0);
        expect(bundle.backend.tier).toBe('N2');
    });
});
