import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { ChainedInput } from '../../ui_components/input/ChainedInput/ChainedInput.js';

let container;
beforeEach(() => { container = document.createElement('div'); document.body.appendChild(container); });
afterEach(() => { container.remove(); });

const avail = (ci, name) => ci.snapshot().fields[name].availability;

describe('ChainedInput.optionsFrom (同步衍生級聯)', () => {
    const DISTRICTS = { taipei: [{ label: '中正', value: 'zz' }], taichung: [] };

    it('父變→子依 optionsFrom(allValues) 同步啟用;父無對應→子保持 disabled', async () => {
        const ci = new ChainedInput({ fields: [
            { name: 'county', type: 'select', options: [{ label: '台北', value: 'taipei' }, { label: '台中', value: 'taichung' }] },
            { name: 'district', type: 'select', optionsFrom: (v) => DISTRICTS[v.county] ?? [] }
        ] }).mount(container);

        expect(avail(ci, 'district')).toBe('disabled');
        await ci._handleFieldChange('county', 'taipei');
        expect(avail(ci, 'district')).toBe('enabled');       // 同步、無 async
        await ci._handleFieldChange('county', 'taichung');
        expect(avail(ci, 'district')).toBe('disabled');      // 空結果 → fail-safe disabled
    });

    it('多父:optionsFrom 拿得到全部祖先值(非只直接父)', async () => {
        const ci = new ChainedInput({ fields: [
            { name: 'a', type: 'select', options: [{ label: 'A', value: 'a' }] },
            { name: 'b', type: 'select', options: [{ label: 'B', value: 'b' }] },
            { name: 'c', type: 'select', optionsFrom: (v) => (v.a && v.b) ? [{ label: 'C', value: 'c' }] : [] }
        ] }).mount(container);

        await ci._handleFieldChange('a', 'a');
        expect(avail(ci, 'b')).toBe('enabled');
        expect(avail(ci, 'c')).toBe('disabled');             // 還缺 b
        await ci._handleFieldChange('b', 'b');
        expect(avail(ci, 'c')).toBe('enabled');              // optionsFrom 看到 a 且 b
    });
});

describe('ChainedInput.loadOptions 競態安全(latest-wins)', () => {
    it('快速改父值,慢回的 stale 結果被丟棄', async () => {
        const MAP = { a: [], b: [{ label: 'B1', value: 'b1' }] };   // a→空(會 disable),b→非空(enable)
        const ci = new ChainedInput({ fields: [
            { name: 'p', type: 'select', options: [{ label: 'A', value: 'a' }, { label: 'B', value: 'b' }] },
            { name: 'child', type: 'select', loadOptions: (v) => new Promise((r) => setTimeout(() => r(MAP[v]), v === 'a' ? 30 : 0)) }
        ] }).mount(container);

        const pa = ci._handleFieldChange('p', 'a');   // token1,30ms 後回 []
        const pb = ci._handleFieldChange('p', 'b');   // token2,0ms 回 [B1]
        await Promise.all([pa, pb]);
        await new Promise((r) => setTimeout(r, 50));  // 讓 a 的慢回觸發

        expect(ci.getValues().p).toBe('b');
        expect(avail(ci, 'child')).toBe('enabled');   // b 的非空生效;a 的 stale 空被丟棄(否則會被 disable)
    });
});
