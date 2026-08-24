import { describe, expect, it } from 'vitest';
import { PageDefinitionAdapter } from '../../page-generator/PageDefinitionAdapter.js';

describe('PageDefinitionAdapter structured default values', () => {
    it('preserves and detaches object/array defaults when converting to old format', () => {
        const sourceDefault = {
            profile: { enabled: true },
            tags: ['alpha', { label: 'beta' }],
        };
        const converted = PageDefinitionAdapter.toOldFormat({
            page: { entity: 'sample', view: 'form' },
            fields: [{
                fieldName: 'settings',
                fieldType: 'text',
                label: 'Settings',
                defaultValue: sourceDefault,
            }],
        });

        expect(converted.fields[0].default).toEqual(sourceDefault);
        expect(converted.fields[0].default).not.toBe(sourceDefault);
        expect(converted.fields[0].default.profile).not.toBe(sourceDefault.profile);
        expect(converted.fields[0].default.tags).not.toBe(sourceDefault.tags);

        converted.fields[0].default.profile.enabled = false;
        converted.fields[0].default.tags[1].label = 'changed';
        expect(sourceDefault).toEqual({
            profile: { enabled: true },
            tags: ['alpha', { label: 'beta' }],
        });
    });

    it('preserves and detaches object/array defaults when converting to new format', () => {
        const sourceDefault = [{ id: 1, filters: { active: true } }];
        const converted = PageDefinitionAdapter.toNewFormat({
            name: 'SamplePage',
            type: 'form',
            fields: [{
                name: 'items',
                type: 'list',
                label: 'Items',
                default: sourceDefault,
            }],
        });

        expect(converted.fields[0].defaultValue).toEqual(sourceDefault);
        expect(converted.fields[0].defaultValue).not.toBe(sourceDefault);
        expect(converted.fields[0].defaultValue[0]).not.toBe(sourceDefault[0]);
        expect(converted.fields[0].defaultValue).not.toBe('[object Object]');

        converted.fields[0].defaultValue[0].filters.active = false;
        expect(sourceDefault[0].filters.active).toBe(true);
    });

    it('keeps primitive conversion compatibility', () => {
        const converted = PageDefinitionAdapter.toNewFormat({
            name: 'PrimitivePage',
            type: 'form',
            fields: [
                { name: 'enabled', type: 'checkbox', default: true },
                { name: 'count', type: 'number', default: 3 },
            ],
        });

        expect(converted.fields.map((field) => field.defaultValue)).toEqual(['true', '3']);
    });

    it('rejects cyclic or accessor-backed structured defaults without invoking getters', () => {
        const cyclic = {};
        cyclic.self = cyclic;
        expect(() => PageDefinitionAdapter._cloneDefaultValue(cyclic)).toThrow('cycles');

        let getterInvoked = false;
        const accessorDefault = {};
        Object.defineProperty(accessorDefault, 'secret', {
            enumerable: true,
            get() {
                getterInvoked = true;
                return 'unsafe';
            },
        });

        expect(() => PageDefinitionAdapter._cloneDefaultValue(accessorDefault)).toThrow('data property');
        expect(getterInvoked).toBe(false);
    });
});
