import { describe, it, expect, beforeEach } from 'vitest';
import { FieldResolver } from '../../page-generator/FieldResolver.js';

describe('FieldResolver', () => {
    let resolver;

    beforeEach(() => {
        resolver = new FieldResolver();
    });

    it('_typeMap 包含 30 種 fieldType', () => {
        expect(resolver._typeMap.size).toBe(30);
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
