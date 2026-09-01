import { describe, it, expect, beforeEach, vi } from 'vitest';
import { FieldResolver } from '../../page-generator/FieldResolver.js';

describe('FieldResolver', () => {
    let resolver;

    beforeEach(() => {
        resolver = new FieldResolver();
    });

    it('_typeMap 包含目前 34 種 fieldType', () => {
        expect(resolver._typeMap.size).toBe(34);
    });

    it('preload 型別解析只選頁面需要的模組，未知型別回退全量', () => {
        expect(resolver._resolvePreloadKeys(['text', 'rocDate', 'text'])).toEqual(['TextInput', 'DatePicker']);
        expect(resolver._resolvePreloadKeys(['custom-widget'])).toBeNull();
    });

    it('_typeMap 包含所有基本 fieldType', () => {
        const basicTypes = ['text', 'email', 'password', 'number', 'textarea'];
        basicTypes.forEach(type => {
            expect(resolver._typeMap.has(type)).toBe(true);
        });
    });

    it('_typeMap 包含日期時間 fieldType', () => {
        const dateTypes = ['date', 'time', 'datetime'];
        dateTypes.forEach(type => {
            expect(resolver._typeMap.has(type)).toBe(true);
        });
    });

    it('_typeMap 包含選擇類 fieldType', () => {
        const selectionTypes = ['select', 'multiselect', 'checkbox', 'toggle', 'radio'];
        selectionTypes.forEach(type => {
            expect(resolver._typeMap.has(type)).toBe(true);
        });
    });

    it('_typeMap 包含進階 fieldType', () => {
        const advancedTypes = ['color', 'image', 'file', 'richtext', 'canvas'];
        advancedTypes.forEach(type => {
            expect(resolver._typeMap.has(type)).toBe(true);
        });
    });

    it('_typeMap 包含服務類 fieldType', () => {
        const serviceTypes = ['geolocation', 'weather'];
        serviceTypes.forEach(type => {
            expect(resolver._typeMap.has(type)).toBe(true);
        });
    });

    it('_typeMap 包含複合輸入 fieldType', () => {
        const compositeTypes = [
            'address', 'addresslist', 'chained', 'list',
            'personinfo', 'phonelist', 'socialmedia',
            'organization', 'student'
        ];
        compositeTypes.forEach(type => {
            expect(resolver._typeMap.has(type)).toBe(true);
        });
    });

    it('_typeMap 包含 hidden fieldType', () => {
        expect(resolver._typeMap.has('hidden')).toBe(true);
    });

    it('每個 _typeMap entry 值都是 function', () => {
        for (const [, factory] of resolver._typeMap) {
            expect(typeof factory).toBe('function');
        }
    });

    it('resolve 未知 fieldType 時 fallback 到 text (透過 _getModule)', () => {
        // 當 _moduleCache 不存在時 _getModule 回傳 {}，
        // 所以 _createTextInput 會因取不到 TextInput 而拋錯。
        // 我們測試 resolve 路徑：未知 type 走 fallback。
        // 要讓測試不因缺少模組載入而失敗，模擬 _moduleCache。
        const mockTextInput = class {
            constructor(opts) { this.opts = opts; this.element = document.createElement('div'); }
            mount() { return this; }
            destroy() {}
            getValue() { return ''; }
            setValue() {}
            clear() {}
        };
        resolver._moduleCache = new Map();
        resolver._moduleCache.set('TextInput', { TextInput: mockTextInput });

        // 也需要 mock FormField
        const origResolve = resolver.resolve.bind(resolver);
        // 直接測試 _typeMap 查找邏輯
        const factory = resolver._typeMap.get('unknownType');
        expect(factory).toBeUndefined();

        // 確認 resolve 方法中對未知 fieldType 會走 console.warn 路徑
        const warnMessages = [];
        const origWarn = console.warn;
        console.warn = (...args) => warnMessages.push(args.join(' '));

        try {
            // resolve 會調用 _createTextInput 作為 fallback，
            // 然後 new FormField - 我們只測到 warn 訊息
            resolver.resolve({ fieldType: 'unknownXYZ', fieldName: 'test', label: 'Test' });
            expect(warnMessages.some(m => m.includes('unknownXYZ'))).toBe(true);
        } finally {
            console.warn = origWarn;
        }
    });

    it('registerComponent 註冊自訂元件', () => {
        const factory = () => ({ element: document.createElement('div') });
        resolver.registerComponent('MyWidget', factory);
        expect(resolver._componentMap.has('MyWidget')).toBe(true);
        expect(resolver._componentMap.get('MyWidget')).toBe(factory);
    });

    it('resolve 指定 component 時優先使用 _componentMap', () => {
        const mockElement = document.createElement('div');
        const factory = (def) => ({
            element: mockElement,
            mount() { return this; },
            destroy() {},
            getValue() { return 'custom'; },
            setValue() {},
            clear() {},
            options: {}
        });
        resolver.registerComponent('CustomComp', factory);

        // 需要 mock FormField，使用簡易替代
        // resolve 呼叫 new FormField 但我們不測試 FormField 本身
        // 只確認走 _componentMap 路徑
        const result = resolver.resolve({
            fieldType: 'text',
            fieldName: 'f1',
            label: 'F1',
            component: 'CustomComp'
        });

        expect(result.component.getValue()).toBe('custom');
    });

    it('resolve 明示未註冊 component 時 fail-closed', () => {
        expect(() => resolver.resolve({
            fieldType: 'text',
            fieldName: 'f1',
            label: 'F1',
            component: 'MissingComponent'
        })).toThrow('未註冊的 component: MissingComponent');
    });

    it('resolve 明示既有 built-in component 時維持相容', () => {
        const mockTextInput = class {
            constructor(opts) {
                this.opts = opts;
                this.element = document.createElement('input');
            }
            mount(container) { container.appendChild(this.element); return this; }
            destroy() { this.element.remove(); }
            getValue() { return this.opts.value || ''; }
            setValue() {}
            clear() {}
        };
        resolver._moduleCache = new Map([
            ['TextInput', { TextInput: mockTextInput }]
        ]);

        const result = resolver.resolve({
            fieldType: 'email',
            fieldName: 'email',
            label: 'Email',
            component: 'TextInput'
        });

        expect(result.component).toBeInstanceOf(mockTextInput);
        expect(result.component.opts.type).toBe('email');
        result.formField.destroy();
    });

    it('resolve 在 FormField 建立失敗時清理已建立 component', () => {
        const destroy = vi.fn();
        resolver.registerComponent('BrokenMount', () => ({
            element: document.createElement('div'),
            mount() { throw new Error('mount failed'); },
            destroy,
        }));

        expect(() => resolver.resolve({
            fieldType: 'text',
            fieldName: 'broken',
            label: 'Broken',
            component: 'BrokenMount'
        })).toThrow('mount failed');
        expect(destroy).toHaveBeenCalledTimes(1);
    });

    it('resolveAll 在建立前拒絕 duplicate fieldName', () => {
        const resolveSpy = vi.spyOn(resolver, 'resolve');
        expect(() => resolver.resolveAll([
            { fieldName: 'duplicate', fieldType: 'text' },
            { fieldName: 'duplicate', fieldType: 'number' }
        ])).toThrow('重複的 fieldName: duplicate');
        expect(resolveSpy).not.toHaveBeenCalled();
    });

    it('resolveAll 遇錯時以反向順序清理先前建立的項目', () => {
        const destroyed = [];
        vi.spyOn(resolver, 'resolve').mockImplementation((def) => {
            if (def.fieldName === 'failure') throw new Error('resolution failed');
            return {
                component: { destroy: () => destroyed.push(`${def.fieldName}:component`) },
                formField: { destroy: () => destroyed.push(`${def.fieldName}:formField`) }
            };
        });

        expect(() => resolver.resolveAll([
            { fieldName: 'first' },
            { fieldName: 'second' },
            { fieldName: 'failure' }
        ])).toThrow('resolution failed');
        expect(destroyed).toEqual(['second:formField', 'first:formField']);
    });

    it('_resolveStaticOptions 解析靜態選項', () => {
        const items = [
            { label: 'A', value: 'a' },
            { label: 'B', value: 'b' }
        ];
        const result = resolver._resolveStaticOptions({
            optionsSource: { type: 'static', items }
        });
        expect(result).toEqual(items);
    });

    it('_resolveStaticOptions 無 optionsSource 回傳空陣列', () => {
        const result = resolver._resolveStaticOptions({});
        expect(result).toEqual([]);
    });

    it('_resolveStaticOptions API 型別回傳空陣列', () => {
        const result = resolver._resolveStaticOptions({
            optionsSource: { type: 'api', endpoint: '/api/items' }
        });
        expect(result).toEqual([]);
    });
});
