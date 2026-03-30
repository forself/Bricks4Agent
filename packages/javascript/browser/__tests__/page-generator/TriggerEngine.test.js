import { describe, it, expect, beforeEach } from 'vitest';
import { TriggerEngine } from '../../page-generator/TriggerEngine.js';

describe('TriggerEngine', () => {
    let engine;

    beforeEach(() => {
        engine = new TriggerEngine();
    });

    it('內建 8 種 action 已註冊', () => {
        expect(engine._actions.size).toBe(8);
    });

    it('包含所有內建 action 名稱', () => {
        const builtinActions = [
            'clear', 'setValue', 'show', 'hide',
            'setReadonly', 'setRequired', 'reload', 'reloadOptions'
        ];
        builtinActions.forEach(action => {
            expect(engine._actions.has(action)).toBe(true);
        });
    });

    it('每個內建 action 都是 function', () => {
        for (const [, handler] of engine._actions) {
            expect(typeof handler).toBe('function');
        }
    });

    it('registerAction 新增自訂 action', () => {
        const handler = () => {};
        engine.registerAction('customAction', handler);
        expect(engine._actions.has('customAction')).toBe(true);
        expect(engine._actions.get('customAction')).toBe(handler);
    });

    it('registerAction 後 action 總數增加', () => {
        engine.registerAction('myAction', () => {});
        expect(engine._actions.size).toBe(9);
    });

    it('registerAction 可覆蓋已有的 action', () => {
        const newHandler = () => 'overridden';
        engine.registerAction('clear', newHandler);
        expect(engine._actions.get('clear')).toBe(newHandler);
        expect(engine._actions.size).toBe(8); // 數量不變
    });

    it('execute 呼叫正確的 action handler', () => {
        let called = false;
        let receivedParams = null;

        engine.registerAction('testAction', (source, target, params) => {
            called = true;
            receivedParams = params;
        });

        // 設置 fieldMap 中的模擬目標
        engine._fieldMap.set('source', {
            component: { getValue: () => 'src' },
            formField: {}
        });
        engine._fieldMap.set('target', {
            component: { setValue: () => {} },
            formField: {}
        });

        engine.execute('testAction', 'source', 'target', { key: 'value' });
        expect(called).toBe(true);
        expect(receivedParams).toEqual({ key: 'value' });
    });

    it('execute 對未知 action 發出 console.warn', () => {
        const warnMessages = [];
        const origWarn = console.warn;
        console.warn = (...args) => warnMessages.push(args.join(' '));

        try {
            engine.execute('nonExistentAction', 'src', 'tgt');
            expect(warnMessages.some(m => m.includes('nonExistentAction'))).toBe(true);
        } finally {
            console.warn = origWarn;
        }
    });

    it('execute 對找不到的目標欄位發出 console.warn', () => {
        const warnMessages = [];
        const origWarn = console.warn;
        console.warn = (...args) => warnMessages.push(args.join(' '));

        try {
            engine.execute('clear', 'src', 'nonExistentTarget');
            expect(warnMessages.some(m => m.includes('nonExistentTarget'))).toBe(true);
        } finally {
            console.warn = origWarn;
        }
    });

    it('clear action 呼叫目標的 clear 方法', () => {
        let cleared = false;
        engine._fieldMap.set('src', { component: {}, formField: {} });
        engine._fieldMap.set('tgt', {
            component: { clear: () => { cleared = true; } },
            formField: {}
        });

        engine.execute('clear', 'src', 'tgt');
        expect(cleared).toBe(true);
    });

    it('setValue action 使用 params.value 設定值', () => {
        let setVal = null;
        engine._fieldMap.set('src', { component: {}, formField: {} });
        engine._fieldMap.set('tgt', {
            component: { setValue: (v) => { setVal = v; } },
            formField: {}
        });

        engine.execute('setValue', 'src', 'tgt', { value: 'hello' });
        expect(setVal).toBe('hello');
    });

    it('setValue action 使用 fromField 複製值', () => {
        let setVal = null;
        engine._fieldMap.set('src', {
            component: { getValue: () => 'source-value' },
            formField: {}
        });
        engine._fieldMap.set('tgt', {
            component: { setValue: (v) => { setVal = v; } },
            formField: {}
        });

        engine.execute('setValue', 'src', 'tgt', { fromField: 'src' });
        expect(setVal).toBe('source-value');
    });

    it('show action 呼叫 formField.show', () => {
        let shown = false;
        engine._fieldMap.set('src', { component: {}, formField: {} });
        engine._fieldMap.set('tgt', {
            component: {},
            formField: { show: () => { shown = true; } }
        });

        engine.execute('show', 'src', 'tgt');
        expect(shown).toBe(true);
    });

    it('hide action 呼叫 formField.hide', () => {
        let hidden = false;
        engine._fieldMap.set('src', { component: {}, formField: {} });
        engine._fieldMap.set('tgt', {
            component: {},
            formField: { hide: () => { hidden = true; } }
        });

        engine.execute('hide', 'src', 'tgt');
        expect(hidden).toBe(true);
    });

    it('unbind 清空 fieldMap 和 cleanups', () => {
        engine._fieldMap.set('field1', {});
        engine._cleanups.push(() => {});
        engine.unbind();
        expect(engine._fieldMap.size).toBe(0);
        expect(engine._cleanups.length).toBe(0);
    });

    it('destroy 清空 actions 和 fieldMap', () => {
        engine.destroy();
        expect(engine._actions.size).toBe(0);
        expect(engine._fieldMap.size).toBe(0);
    });
});
