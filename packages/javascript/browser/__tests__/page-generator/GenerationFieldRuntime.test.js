/**
 * 受治理生成的型錄（tools/generation/field-types.mjs）對每個開放欄位型別宣稱的 required、
 * 限制鍵（validation）與預設值（default），在執行期的表單渲染器上確實生效。
 * 型錄新增宣稱而執行期沒有對應行為時，這裡會失敗。
 */
import { afterEach, describe, expect, it } from 'vitest';
import { DynamicFormRenderer } from '../../page-generator/DynamicFormRenderer.js';
import { PageDefinitionAdapter } from '../../page-generator/PageDefinitionAdapter.js';
import {
    computeSliceFieldTypes,
    DEFAULT_KINDS,
    FIELD_TYPE_NOTES,
    OPTION_TYPES,
    supportsRequired
} from '../../../../../tools/generation/field-types.mjs';

const OPTIONS = [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }];
const { allowed } = computeSliceFieldTypes();
const mounted = [];

async function renderField(field) {
    const definition = PageDefinitionAdapter.toNewFormat({
        name: 'ProbeFormPage',
        type: 'form',
        fields: [{ name: 'f', label: 'F', ...field, ...(OPTION_TYPES.has(field.type) ? { options: OPTIONS } : {}) }]
    });
    const renderer = new DynamicFormRenderer({ definition, onSave: () => {} });
    await renderer.init();
    document.body.appendChild(renderer.element);
    mounted.push(renderer);
    return { renderer, component: renderer._fieldInstances.get('f').component };
}

afterEach(() => {
    mounted.splice(0).forEach(renderer => renderer.destroy());
});

function sameLocalDate(value, year, month, day) {
    const date = value instanceof Date ? value : new Date(value);
    return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
}

// 每個預設值形式的樣本值與執行期應取得的值
const DEFAULT_SAMPLES = {
    string: { value: 'abc', check: actual => actual === 'abc' },
    number: { value: 2.5, check: actual => actual === 2.5 },
    boolean: { value: true, check: actual => actual === true },
    option: { value: 'b', check: actual => actual === 'b' },
    date: { value: '2026-01-31', check: actual => sameLocalDate(actual, 2026, 1, 31) },
    time: { value: '09:30', check: actual => actual === '09:30' }
};

// 每個限制鍵在執行期的檢查
const LIMIT_CHECKS = {
    async maxLength(type) {
        const { renderer } = await renderField({ type, validation: { maxLength: 3 } });
        renderer.setValues({ f: 'abcd' });
        expect(renderer.validate(), `${type} rejects a value over maxLength`).toBe(false);
        renderer.setValues({ f: 'abc' });
        expect(renderer.validate(), `${type} accepts a value at maxLength`).toBe(true);
    },
    async min(type) {
        const { renderer, component } = await renderField({ type, validation: { min: 1, max: 5 } });
        component.input.value = '0';
        component.input.dispatchEvent(new Event('blur'));
        expect(renderer.getValues().f, `${type} keeps entered values at or above min`).toBe(1);
    },
    async max(type) {
        const { renderer, component } = await renderField({ type, validation: { min: 1, max: 5 } });
        component.input.value = '9';
        component.input.dispatchEvent(new Event('blur'));
        expect(renderer.getValues().f, `${type} keeps entered values at or below max`).toBe(5);
    },
    async maxItems(type) {
        const { renderer, component } = await renderField({ type, validation: { maxItems: 2 } });
        for (let i = 0; i < 5; i += 1) component.addButton.click();
        expect(renderer.getValues().f.length, `${type} stops adding rows at maxItems`).toBe(2);
    }
};

describe('generation field type claims hold at runtime', () => {
    it('only claims limit keys that have a runtime check, and never minItems', () => {
        for (const type of allowed) {
            for (const key of FIELD_TYPE_NOTES[type].validation) {
                expect(Object.keys(LIMIT_CHECKS), `${type}.validation.${key}`).toContain(key);
            }
            expect(FIELD_TYPE_NOTES[type].validation).not.toContain('minItems');
        }
        expect(Object.keys(DEFAULT_SAMPLES).sort()).toEqual([...DEFAULT_KINDS].sort());
    });

    for (const type of allowed) {
        const notes = FIELD_TYPE_NOTES[type];

        if (supportsRequired(type)) {
            it(`${type}: required blocks an untouched field`, async () => {
                const { renderer } = await renderField({ type, required: true });
                expect(renderer.validate()).toBe(false);
            });
        }

        for (const key of notes.validation) {
            it(`${type}: validation.${key} takes effect`, async () => {
                await LIMIT_CHECKS[key](type);
            });
        }

        if (notes.default) {
            it(`${type}: a ${notes.default} default is applied`, async () => {
                const sample = DEFAULT_SAMPLES[notes.default];
                const { renderer } = await renderField({ type, default: sample.value });
                const actual = renderer.getValues().f;
                expect(sample.check(actual), `${type} default ${JSON.stringify(sample.value)} gave ${JSON.stringify(actual)}`).toBe(true);
            });
        }
    }

    it('a date default of "today" is today', async () => {
        const { renderer } = await renderField({ type: 'date', default: 'today' });
        const now = new Date();
        expect(sameLocalDate(renderer.getValues().f, now.getFullYear(), now.getMonth() + 1, now.getDate())).toBe(true);
    });
});
