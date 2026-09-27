import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { ConditionBuilder } from '../../ui_components/form/ConditionBuilder/ConditionBuilder.js';
import ConditionBuilderDefault from '../../ui_components/form/ConditionBuilder/index.js';
import { FIELD_ERROR_CONTRACT } from '../../ui_components/utils/field-error.js';
import Locale from '../../ui_components/i18n/index.js';

const FIELDS = [
    { key: 'title', label: '主旨', type: 'text' },
    { key: 'priority', label: '優先度', type: 'number' },
    { key: 'due', label: '到期日', type: 'date' },
    { key: 'status', label: '狀態', type: 'select', options: [{ value: 'open', label: '進行中' }, { value: 'done', label: '已完成' }] },
    { key: 'tags', label: '標籤', type: 'multiselect', options: [{ value: 'red', label: '紅' }, { value: 'blue', label: '藍' }] },
    { key: 'archived', label: '已封存', type: 'boolean' },
];

let host;
let created = [];
beforeEach(() => {
    Locale.setLang('zh-TW');
    host = document.createElement('div');
    document.body.appendChild(host);
});
afterEach(() => {
    created.forEach((instance) => instance.destroy());
    created = [];
    host.remove();
    Locale.setLang('zh-TW');
    vi.restoreAllMocks();
});

const create = (options = {}) => {
    const instance = new ConditionBuilder({ fields: FIELDS, ...options }).mount(host);
    created.push(instance);
    return instance;
};
const single = (rule) => ({ combinator: 'and', rules: [rule] });
const rows = (builder) => [...builder.element.querySelectorAll('.cl-condition-builder__rule')];
const optionValues = (select) => [...select.options].map((option) => option.value);
const optionLabels = (select) => [...select.options].map((option) => option.textContent);
const choose = (select, value) => {
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
};
const key = (target, keyName, init = {}) => target.dispatchEvent(new KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true, ...init }));
const typeInto = (input, text) => {
    input.value = text;
    input.dispatchEvent(new Event('input', { bubbles: true }));
};
const rootButton = (builder, name) => builder.element.querySelector(`.cl-condition-builder__group[data-depth="1"] > .cl-condition-builder__group-header .cl-condition-builder__${name}`);

function trackGlobalListeners() {
    const added = [];
    const removed = [];
    for (const target of [window, document]) {
        const originalAdd = target.addEventListener;
        const originalRemove = target.removeEventListener;
        vi.spyOn(target, 'addEventListener').mockImplementation(function (type, listener, options) {
            added.push({ target, type, listener, capture: typeof options === 'boolean' ? options : Boolean(options?.capture) });
            return originalAdd.call(this, type, listener, options);
        });
        vi.spyOn(target, 'removeEventListener').mockImplementation(function (type, listener, options) {
            removed.push({ target, type, listener, capture: typeof options === 'boolean' ? options : Boolean(options?.capture) });
            return originalRemove.call(this, type, listener, options);
        });
    }
    return {
        added,
        active: () => added.filter((entry) => !removed.some((gone) => gone.target === entry.target
            && gone.type === entry.type && gone.listener === entry.listener && gone.capture === entry.capture)),
    };
}

describe('ConditionBuilder — defaults', () => {
    it('starts with an empty AND root group and exposes the contract', () => {
        const builder = create();
        expect(ConditionBuilderDefault).toBe(ConditionBuilder);
        expect(builder.getValue()).toEqual({ combinator: 'and', rules: [] });
        expect(builder.element.getAttribute('role')).toBe('group');
        expect(builder.element.getAttribute('aria-label')).toBe('條件設定');
        const root = builder.element.querySelector('.cl-condition-builder__group');
        expect(root.getAttribute('aria-label')).toBe('條件群組（第 1 層）');
        expect(root.querySelector('.cl-condition-builder__empty').textContent).toBe('尚未設定條件');
        expect(rootButton(builder, 'add-rule').getAttribute('aria-label')).toBe('在第 1 層群組新增條件');
        expect(rootButton(builder, 'remove-group')).toBeNull();
        const pressed = [...builder.element.querySelectorAll('.cl-condition-builder__combinator-option')]
            .map((button) => [button.textContent, button.getAttribute('aria-pressed')]);
        expect(pressed).toEqual([['且', 'true'], ['或', 'false']]);
        expect(builder[FIELD_ERROR_CONTRACT]).toBe(true);
        expect(ConditionBuilder.DEFAULT_OPERATORS.select).toEqual(['eq', 'ne', 'in', 'notIn']);
    });

    it('adds a default rule, focuses it and announces it', () => {
        const onChange = vi.fn();
        const builder = create({ onChange });
        rootButton(builder, 'add-rule').click();
        expect(onChange).toHaveBeenLastCalledWith({ combinator: 'and', rules: [{ field: 'title', operator: 'eq', value: null }] });
        const [row] = rows(builder);
        expect(row.getAttribute('aria-label')).toBe('條件 1');
        expect(document.activeElement).toBe(row.querySelector('.cl-condition-builder__field'));
        expect(row.querySelector('.cl-condition-builder__field').getAttribute('aria-label')).toBe('條件 1 的欄位');
        expect(row.querySelector('.cl-condition-builder__operator').getAttribute('aria-label')).toBe('條件 1 的運算子');
        expect(row.querySelector('.cl-condition-builder__remove-rule').getAttribute('aria-label')).toBe('移除條件 1');
        expect(builder.element.querySelector('.cl-condition-builder__live').textContent).toBe('已新增條件');
    });
});

describe('ConditionBuilder — operators per type', () => {
    it('offers the default operators of each field type', () => {
        const builder = create();
        const expected = {
            title: ['eq', 'ne', 'contains', 'notContains', 'startsWith', 'endsWith', 'isEmpty', 'isNotEmpty'],
            priority: ['eq', 'ne', 'gt', 'gte', 'lt', 'lte', 'between', 'isEmpty', 'isNotEmpty'],
            due: ['eq', 'before', 'after', 'between', 'isEmpty', 'isNotEmpty'],
            status: ['eq', 'ne', 'in', 'notIn'],
            tags: ['containsAny', 'containsAll'],
            archived: ['isTrue', 'isFalse'],
        };
        for (const [field, operators] of Object.entries(expected)) {
            builder.setValue(single({ field }));
            const operatorSelect = rows(builder)[0].querySelector('.cl-condition-builder__operator');
            expect(optionValues(operatorSelect)).toEqual(operators);
            expect(builder.getValue().rules[0].operator).toBe(operators[0]);
        }
        builder.setValue(single({ field: 'priority', operator: 'gte', value: 1 }));
        expect(optionLabels(rows(builder)[0].querySelector('.cl-condition-builder__operator')).slice(0, 4))
            .toEqual(['等於', '不等於', '大於', '大於或等於']);
    });

    it('lets a field restrict and reorder its operators, dropping invalid ones', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const builder = create({
            fields: [
                { key: 'title', label: 'Title', type: 'text', operators: ['contains', 'eq', 'gt', 'contains'] },
                { key: 'size', label: 'Size', type: 'number', operators: ['nonsense'] },
                { key: 'odd', label: 'Odd', type: 'color' },
                { key: 'title', label: 'Duplicate', type: 'number' },
            ],
        });
        builder.setValue(single({ field: 'title' }));
        expect(optionValues(rows(builder)[0].querySelector('.cl-condition-builder__operator'))).toEqual(['contains', 'eq']);
        builder.setValue(single({ field: 'size' }));
        expect(optionValues(rows(builder)[0].querySelector('.cl-condition-builder__operator'))).toHaveLength(9);
        builder.setValue(single({ field: 'odd' }));
        expect(optionValues(rows(builder)[0].querySelector('.cl-condition-builder__operator'))).toContain('contains');
        expect(optionValues(rows(builder)[0].querySelector('.cl-condition-builder__field'))).toEqual(['title', 'size', 'odd']);
        expect(warn).toHaveBeenCalledTimes(3);
    });
});

describe('ConditionBuilder — value editors', () => {
    it('uses TextInput for text values and reports typing through onChange', () => {
        const onChange = vi.fn();
        const builder = create({ value: single({ field: 'title', operator: 'contains', value: null }), onChange });
        const input = rows(builder)[0].querySelector('input.text-input');
        expect(input.getAttribute('aria-label')).toBe('條件 1 的值');
        expect(input.placeholder).toBe('請輸入值');
        typeInto(input, '會議');
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'title', operator: 'contains', value: '會議' }));
        typeInto(input, '');
        expect(builder.getValue().rules[0].value).toBeNull();
    });

    it('uses NumberInput (unbounded, starting from 0 on arrow keys) and two inputs for between', () => {
        const onChange = vi.fn();
        const builder = create({ value: single({ field: 'priority', operator: 'gt', value: null }), onChange });
        const input = rows(builder)[0].querySelector('.number-input__wrapper input');
        expect(input.getAttribute('aria-label')).toBe('條件 1 的值');
        input.focus();
        key(input, 'ArrowUp');
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'priority', operator: 'gt', value: 1 }));
        input.value = '-250';
        key(input, 'Enter');
        expect(builder.getValue().rules[0].value).toBe(-250);

        choose(rows(builder)[0].querySelector('.cl-condition-builder__operator'), 'between');
        expect(builder.getValue().rules[0].value).toEqual([-250, null]);
        const inputs = [...rows(builder)[0].querySelectorAll('.number-input__wrapper input')];
        expect(inputs.map((element) => element.getAttribute('aria-label'))).toEqual(['條件 1 的起始值', '條件 1 的結束值']);
        inputs[1].value = '900';
        key(inputs[1], 'Enter');
        expect(builder.getValue().rules[0].value).toEqual([-250, 900]);
        expect(rows(builder)[0].querySelector('.cl-condition-builder__separator').textContent).toBe('至');
    });

    it('uses a keyboard-operable DatePicker for dates and stores YYYY-MM-DD', () => {
        const onChange = vi.fn();
        const builder = create({ value: single({ field: 'due', operator: 'eq', value: '2026-03-10' }), onChange });
        const trigger = rows(builder)[0].querySelector('.datepicker__input-wrapper');
        expect(trigger.tabIndex).toBe(0);
        expect(trigger.getAttribute('role')).toBe('button');
        expect(trigger.getAttribute('aria-label')).toBe('條件 1 的值：2026/03/10');

        trigger.focus();
        key(trigger, 'Enter');
        const calendar = document.querySelector('body > .datepicker__calendar');
        expect(calendar).not.toBeNull();
        expect(document.activeElement.dataset.day).toBe('10');
        key(document.activeElement, 'ArrowRight');
        expect(document.activeElement.dataset.day).toBe('11');
        key(document.activeElement, 'Escape');
        expect(document.querySelector('body > .datepicker__calendar')).toBeNull();
        expect(document.activeElement).toBe(trigger);

        key(trigger, ' ');
        document.querySelector('body > .datepicker__calendar .dp-day[data-day="12"]').click();
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'due', operator: 'eq', value: '2026-03-12' }));
        expect(document.activeElement).toBe(trigger);
        expect(trigger.getAttribute('aria-label')).toBe('條件 1 的值：2026/03/12');
    });

    it('uses a searchable Dropdown for single select values and MultiSelectDropdown for lists', () => {
        const onChange = vi.fn();
        const builder = create({ value: single({ field: 'status', operator: 'eq', value: 'open' }), onChange });
        const input = rows(builder)[0].querySelector('.dropdown__input');
        expect(input.getAttribute('aria-label')).toBe('條件 1 的值');
        expect(input.value).toBe('進行中');
        input.focus();
        input.dispatchEvent(new Event('focus'));
        rows(builder)[0].querySelector('.dropdown__option[data-value="done"]').click();
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'status', operator: 'eq', value: 'done' }));

        // eq → in：單一值轉成陣列並改用多選
        choose(rows(builder)[0].querySelector('.cl-condition-builder__operator'), 'in');
        expect(builder.getValue().rules[0].value).toEqual(['done']);
        const multi = rows(builder)[0].querySelector('.msd__input');
        expect(multi.getAttribute('aria-label')).toBe('條件 1 的值');
        multi.focus();
        key(multi, 'ArrowDown');
        key(multi, 'ArrowDown');
        key(multi, 'Enter');
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'status', operator: 'in', value: ['done', 'open'] }));

        builder.setValue(single({ field: 'tags', operator: 'containsAll', value: ['blue'] }));
        expect([...rows(builder)[0].querySelectorAll('.msd__tag')].map((tag) => tag.textContent)).toEqual(['藍×']);
    });

    it('shows no editor for boolean and emptiness operators', () => {
        const builder = create({ value: { combinator: 'and', rules: [
            { field: 'archived', operator: 'isTrue', value: 'ignored' },
            { field: 'title', operator: 'isEmpty', value: 'ignored' },
        ] } });
        expect(rows(builder).map((row) => row.querySelector('.cl-condition-builder__value').children.length)).toEqual([0, 0]);
        expect(builder.getValue().rules.map((rule) => rule.value)).toEqual([null, null]);
    });
});

describe('ConditionBuilder — field and operator changes', () => {
    it('resets operator and value when the new field is incompatible', () => {
        const onChange = vi.fn();
        const builder = create({ value: single({ field: 'title', operator: 'contains', value: 'abc' }), onChange });
        const fieldSelect = rows(builder)[0].querySelector('.cl-condition-builder__field');
        fieldSelect.focus();
        choose(fieldSelect, 'priority');
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'priority', operator: 'eq', value: null }));
        // 欄位下拉沒有被重建，焦點仍在原處
        expect(document.activeElement).toBe(fieldSelect);
        expect(rows(builder)[0].querySelector('.number-input__wrapper input')).not.toBeNull();

        choose(fieldSelect, 'status');
        expect(builder.getValue().rules[0]).toEqual({ field: 'status', operator: 'eq', value: null });
    });

    it('keeps operator and value when the new field is compatible', () => {
        const builder = create({
            fields: [
                { key: 'title', label: 'Title', type: 'text' },
                { key: 'note', label: 'Note', type: 'text' },
                { key: 'a', label: 'A', type: 'select', options: [{ value: 1, label: 'One' }] },
                { key: 'b', label: 'B', type: 'select', options: [{ value: 2, label: 'Two' }] },
            ],
            value: single({ field: 'title', operator: 'endsWith', value: 'abc' }),
        });
        choose(rows(builder)[0].querySelector('.cl-condition-builder__field'), 'note');
        expect(builder.getValue().rules[0]).toEqual({ field: 'note', operator: 'endsWith', value: 'abc' });

        builder.setValue(single({ field: 'a', operator: 'eq', value: '1' }));
        // 字串 '1' 對齊到選項本身的數值 1
        expect(builder.getValue().rules[0].value).toBe(1);
        choose(rows(builder)[0].querySelector('.cl-condition-builder__field'), 'b');
        expect(builder.getValue().rules[0]).toEqual({ field: 'b', operator: 'eq', value: null });
    });
});

describe('ConditionBuilder — nesting, limits and structure', () => {
    it('adds nested groups until maxDepth and indents them with tokens', () => {
        const onChange = vi.fn();
        const builder = create({ maxDepth: 2, onChange });
        rootButton(builder, 'add-group').click();
        expect(onChange).toHaveBeenLastCalledWith({
            combinator: 'and',
            rules: [{ combinator: 'and', rules: [{ field: 'title', operator: 'eq', value: null }] }],
        });
        const nested = builder.element.querySelector('.cl-condition-builder__group[data-depth="2"]');
        expect(nested.getAttribute('aria-label')).toBe('條件群組（第 2 層）');
        expect(nested.querySelector('.cl-condition-builder__add-group')).toBeNull();
        expect(nested.querySelector('.cl-condition-builder__remove-group').getAttribute('aria-label')).toBe('移除第 2 層群組');
        expect(document.activeElement).toBe(nested.querySelector('.cl-condition-builder__field'));
        const children = builder.element.querySelector('.cl-condition-builder__children');
        // 縮排用 --cl-space-* token（jsdom 的 cssstyle 不接受 padding 內的 var()，只驗導引線與底色 token）
        expect(children.style.cssText).toContain('border-left: 1px dashed var(--cl-border)');
        expect(nested.style.borderLeft).toContain('var(--cl-border-dark)');
        expect(nested.style.background).toBe('var(--cl-bg-secondary)');
        expect(nested.dataset.path).toBe('rules[0]');
        expect(nested.querySelector('.cl-condition-builder__rule').dataset.path).toBe('rules[0].rules[0]');
    });

    it('disables adding once maxRules is reached', () => {
        const builder = create({ maxRules: 2 });
        rootButton(builder, 'add-rule').click();
        rootButton(builder, 'add-group').click();
        expect(rootButton(builder, 'add-rule').disabled).toBe(true);
        expect(rootButton(builder, 'add-group').disabled).toBe(true);
        expect(builder.element.querySelector('[data-depth="2"] .cl-condition-builder__add-rule').disabled).toBe(true);
    });

    it('removes rules and groups and moves focus to a sensible neighbour', () => {
        const onChange = vi.fn();
        const builder = create({ onChange, value: { combinator: 'and', rules: [
            { field: 'title', operator: 'eq', value: 'a' },
            { field: 'title', operator: 'eq', value: 'b' },
            { combinator: 'or', rules: [{ field: 'priority', operator: 'gt', value: 1 }] },
        ] } });
        rows(builder)[1].querySelector('.cl-condition-builder__remove-rule').click();
        expect(builder.getValue().rules).toHaveLength(2);
        const nested = builder.element.querySelector('[data-depth="2"]');
        expect(nested.contains(document.activeElement)).toBe(true);
        nested.querySelector('.cl-condition-builder__remove-group').click();
        expect(onChange).toHaveBeenLastCalledWith(single({ field: 'title', operator: 'eq', value: 'a' }));
        expect(document.activeElement).toBe(rows(builder)[0].querySelector('.cl-condition-builder__field'));
        expect(builder.element.querySelector('.cl-condition-builder__live').textContent).toBe('已移除群組');
        rows(builder)[0].querySelector('.cl-condition-builder__remove-rule').click();
        expect(document.activeElement).toBe(rootButton(builder, 'add-rule'));
    });

    it('toggles the combinator and NOT flag', () => {
        const onChange = vi.fn();
        const builder = create({ allowNot: true, onChange });
        expect(builder.getValue()).toEqual({ combinator: 'and', not: false, rules: [] });
        const or = builder.element.querySelector('.cl-condition-builder__combinator-option[data-combinator="or"]');
        or.click();
        expect(or.getAttribute('aria-pressed')).toBe('true');
        expect(onChange).toHaveBeenLastCalledWith({ combinator: 'or', not: false, rules: [] });
        const not = builder.element.querySelector('.cl-condition-builder__not-toggle');
        expect(not.getAttribute('aria-label')).toBe('反轉第 1 層群組（NOT）');
        not.click();
        expect(onChange).toHaveBeenLastCalledWith({ combinator: 'or', not: true, rules: [] });
    });
});

describe('ConditionBuilder — validate', () => {
    it('reports missing values, bad ranges, unknown fields/operators and empty groups with paths', () => {
        const builder = create({ value: { combinator: 'and', rules: [
            { field: 'title', operator: 'contains', value: '  ' },
            { field: 'priority', operator: 'between', value: [9, 1] },
            { combinator: 'or', rules: [
                { field: 'ghost', operator: 'eq', value: 1 },
                { field: 'status', operator: 'contains', value: 'x' },
                { field: 'status', operator: 'in', value: ['open', 'nope'] },
            ] },
            { combinator: 'and', rules: [] },
            { field: 'due', operator: 'eq', value: '2026-02-30' },
            { field: 'due', operator: 'between', value: ['2026-01-01', null] },
            { field: 'archived', operator: 'isTrue', value: null },
            { field: 'tags', operator: 'containsAny', value: [] },
        ] } });
        expect(builder.validate()).toEqual([
            { path: 'rules[0]', code: 'missingValue', message: '主旨：請輸入值' },
            { path: 'rules[1]', code: 'badRange', message: '優先度：起始值不可大於結束值' },
            { path: 'rules[2].rules[0]', code: 'unknownField', message: '未知欄位「ghost」' },
            { path: 'rules[2].rules[1]', code: 'unknownOperator', message: '狀態：未知運算子「contains」' },
            { path: 'rules[2].rules[2]', code: 'invalidValue', message: '狀態：值不正確' },
            { path: 'rules[3]', code: 'emptyGroup', message: '群組內沒有任何條件' },
            { path: 'rules[4]', code: 'invalidValue', message: '到期日：值不正確' },
            { path: 'rules[5]', code: 'missingValue', message: '到期日：請輸入值' },
            { path: 'rules[7]', code: 'missingValue', message: '標籤：請輸入值' },
        ]);
        builder.setValue(single({ field: 'priority', operator: 'between', value: [1, 9] }));
        expect(builder.validate()).toEqual([]);
    });

    it('reports depth and rule-count limits, also for a value passed in', () => {
        const builder = create({ maxDepth: 2, maxRules: 2 });
        const deep = { combinator: 'and', rules: [
            { combinator: 'and', rules: [{ combinator: 'or', rules: [
                { field: 'title', operator: 'isEmpty' },
                { field: 'title', operator: 'isNotEmpty' },
                { field: 'archived', operator: 'isFalse' },
            ] }] },
        ] };
        expect(builder.validate(deep).map(({ path, code }) => ({ path, code }))).toEqual([
            { path: '', code: 'maxRules' },
            { path: 'rules[0].rules[0]', code: 'maxDepth' },
        ]);
        expect(builder.getValue()).toEqual({ combinator: 'and', rules: [] });
    });
});

describe('ConditionBuilder — describe', () => {
    const value = { combinator: 'and', rules: [
        { field: 'status', operator: 'eq', value: 'open' },
        { combinator: 'or', rules: [
            { field: 'priority', operator: 'gt', value: 3 },
            { field: 'title', operator: 'contains', value: '會議' },
        ] },
        { field: 'due', operator: 'between', value: ['2026-01-01', '2026-03-31'] },
        { field: 'tags', operator: 'containsAny', value: ['red', 'blue'] },
        { field: 'archived', operator: 'isFalse', value: null },
    ] };

    it('describes nested conditions in zh-TW', () => {
        const builder = create({ value });
        expect(builder.describe()).toBe('狀態 等於 進行中 且 (優先度 大於 3 或 主旨 包含 「會議」) 且 到期日 介於 2026-01-01 至 2026-03-31 且 標籤 包含任一 紅、藍 且 已封存 為否');
        expect(builder.describe({ combinator: 'or', not: true, rules: [
            { field: 'title', operator: 'isEmpty' },
            { field: 'priority', operator: 'lte', value: null },
        ] })).toBe('非 (主旨 為空 或 優先度 小於或等於 （未填）)');
        expect(builder.describe({ combinator: 'and', rules: [] })).toBe('（無條件）');
        expect(builder.describe({ combinator: 'and', rules: [{ combinator: 'or', rules: [] }] })).toBe('（無條件）');
    });

    it('describes nested conditions in en', () => {
        Locale.setLang('en');
        const builder = create({
            value,
            fields: [
                { key: 'status', label: 'Status', type: 'select', options: [{ value: 'open', label: 'Open' }] },
                { key: 'priority', label: 'Priority', type: 'number' },
                { key: 'title', label: 'Title', type: 'text' },
                { key: 'due', label: 'Due', type: 'date' },
                { key: 'tags', label: 'Tags', type: 'multiselect', options: [{ value: 'red', label: 'Red' }, { value: 'blue', label: 'Blue' }] },
                { key: 'archived', label: 'Archived', type: 'boolean' },
            ],
        });
        expect(builder.describe()).toBe('Status equals Open AND (Priority is greater than 3 OR Title contains "會議") AND Due is between 2026-01-01 and 2026-03-31 AND Tags contains any of Red, Blue AND Archived is false');
        expect(builder.describe({ combinator: 'or', rules: [{ field: 'ghost', operator: 'near', value: 'x' }] })).toBe('ghost near "x"');
    });
});

describe('ConditionBuilder — setValue', () => {
    it('keeps unknown fields and operators, flags them, and preserves extra keys', () => {
        const onChange = vi.fn();
        const builder = create({ onChange });
        const value = { combinator: 'or', id: 'root-1', rules: [
            { field: 'legacy', operator: 'eq', value: 'x', id: 'r1' },
            { field: 'status', operator: 'near', value: 'open', meta: { savedBy: 'staff' } },
        ] };
        builder.setValue(value);
        expect(onChange).not.toHaveBeenCalled();
        expect(builder.getValue()).toEqual(value);

        const [unknownField, unknownOperator] = rows(builder);
        expect(unknownField.dataset.flag).toBe('unknown-field');
        const notice = unknownField.querySelector('.cl-condition-builder__notice');
        expect(notice.textContent).toBe('未知欄位「legacy」，請重新選擇欄位。');
        const fieldSelect = unknownField.querySelector('.cl-condition-builder__field');
        expect(fieldSelect.getAttribute('aria-invalid')).toBe('true');
        expect(fieldSelect.getAttribute('aria-describedby')).toBe(notice.id);
        expect(fieldSelect.options[0].textContent).toBe('legacy（未知欄位）');
        expect(unknownField.querySelector('.cl-condition-builder__operator').disabled).toBe(true);
        expect(unknownField.querySelector('.cl-condition-builder__raw').textContent).toBe('x');
        expect(unknownOperator.dataset.flag).toBe('unknown-operator');
        expect(unknownOperator.querySelector('.cl-condition-builder__operator').options[0].textContent).toBe('near（未知運算子）');

        choose(fieldSelect, 'title');
        expect(builder.getValue().rules[0]).toEqual({ field: 'title', operator: 'eq', value: null, id: 'r1' });
        expect(rows(builder)[0].dataset.flag).toBeUndefined();
        expect(fieldSelect.hasAttribute('aria-invalid')).toBe(false);
        choose(rows(builder)[1].querySelector('.cl-condition-builder__operator'), 'ne');
        expect(builder.getValue().rules[1]).toEqual({ field: 'status', operator: 'ne', value: null, meta: { savedBy: 'staff' } });
    });

    it('validates the shape, normalizes values and emits only when asked', () => {
        const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
        const onChange = vi.fn();
        const builder = create({ onChange });
        builder.setValue('nonsense');
        expect(builder.getValue()).toEqual({ combinator: 'and', rules: [] });
        builder.setValue({ combinator: 'xor', rules: [42, { foo: 1 }, { field: 'title', operator: 'eq', value: 'a', fn: () => 1 }] });
        expect(builder.getValue()).toEqual(single({ field: 'title', operator: 'eq', value: 'a' }));
        expect(warn).toHaveBeenCalledTimes(4);

        builder.setValue({ combinator: 'AND', rules: [
            { field: 'status', operator: 'in', value: 'open' },
            { field: 'priority', operator: 'between', value: 5 },
            { field: 'due', operator: 'after', value: new Date(2026, 0, 5) },
            { field: 'title' },
        ] }, { emit: true });
        expect(onChange).toHaveBeenCalledTimes(1);
        expect(builder.getValue()).toEqual({ combinator: 'and', rules: [
            { field: 'status', operator: 'in', value: ['open'] },
            { field: 'priority', operator: 'between', value: [5, null] },
            { field: 'due', operator: 'after', value: '2026-01-05' },
            { field: 'title', operator: 'eq', value: null },
        ] });
    });

    it('clear and setDisabled behave like a value component', () => {
        const onChange = vi.fn();
        const builder = create({ onChange, value: single({ field: 'title', operator: 'eq', value: 'a' }) });
        builder.setDisabled(true);
        expect(builder.element.getAttribute('aria-disabled')).toBe('true');
        const controls = [...builder.element.querySelectorAll('select, button, input')];
        expect(controls.length).toBeGreaterThan(4);
        expect(controls.every((control) => control.disabled)).toBe(true);
        rootButton(builder, 'add-rule').click();
        expect(builder.getValue().rules).toHaveLength(1);
        builder.setDisabled(false);
        expect(rows(builder)[0].querySelector('input.text-input').disabled).toBe(false);
        builder.clear();
        expect(builder.getValue()).toEqual({ combinator: 'and', rules: [] });
        expect(onChange).not.toHaveBeenCalled();
    });

    it('starts disabled when the disabled option is set', () => {
        const builder = create({ disabled: true, value: single({ field: 'status', operator: 'eq', value: 'open' }) });
        expect(builder.snapshot().availability).toBe('disabled');
        expect(rows(builder)[0].querySelector('.dropdown__input').disabled).toBe(true);
        expect(rootButton(builder, 'add-group').disabled).toBe(true);
        builder.setDisabled(false);
        expect(rootButton(builder, 'add-group').disabled).toBe(false);
    });

    it('supports the field-error contract', () => {
        const builder = create();
        builder.setError('請至少設定一個條件');
        const message = builder.element.querySelector('.b4a-field-error');
        expect(message.textContent).toBe('請至少設定一個條件');
        expect(builder.element.getAttribute('aria-invalid')).toBe('true');
        expect(builder.element.getAttribute('aria-describedby')).toBe(message.id);
        // 重新渲染樹不會移除錯誤訊息
        rootButton(builder, 'add-rule').click();
        expect(builder.element.querySelector('.b4a-field-error')).toBe(message);
        builder.setError('x', { display: false });
        expect(builder.element.querySelector('.b4a-field-error')).toBeNull();
        builder.clearError();
        expect(builder.element.hasAttribute('aria-invalid')).toBe(false);
    });
});

describe('ConditionBuilder — lifecycle and locale', () => {
    it('destroy removes DOM, portaled calendars, child editors and global listeners', () => {
        const tracker = trackGlobalListeners();
        const builder = create({ value: { combinator: 'and', rules: [
            { field: 'due', operator: 'eq', value: '2026-03-10' },
            { field: 'status', operator: 'eq', value: 'open' },
            { field: 'tags', operator: 'containsAny', value: [] },
        ] } });
        // 先開多選清單，再以鍵盤開月曆（焦點移進月曆），兩個浮層同時開著
        builder.element.querySelector('.msd__input').focus();
        expect(builder.element.querySelector('.msd__menu').style.display).toBe('block');
        const trigger = builder.element.querySelector('.datepicker__input-wrapper');
        trigger.focus();
        key(trigger, 'Enter');
        expect(document.querySelector('body > .datepicker__calendar')).not.toBeNull();
        expect(tracker.active().length).toBeGreaterThan(0);

        builder.destroy();
        expect(host.children).toHaveLength(0);
        expect(document.body.querySelector('.datepicker__calendar')).toBeNull();
        expect(document.body.querySelector('.cl-condition-builder')).toBeNull();
        expect(tracker.active()).toEqual([]);
        expect(() => {
            builder.destroy();
            builder.setValue(single({ field: 'title' }));
            builder.clear();
            builder.setDisabled(true);
            builder.setError('late');
            builder.clearError();
            builder.show();
            builder.hide();
            builder.getValue();
            builder.validate();
            builder.describe();
        }).not.toThrow();
    });

    it('re-renders its strings when the locale changes', () => {
        const builder = create({ value: single({ field: 'priority', operator: 'gt', value: 2 }) });
        Locale.setLang('en');
        expect(builder.element.getAttribute('aria-label')).toBe('Conditions');
        expect(rootButton(builder, 'add-rule').textContent).toBe('+ Add rule');
        expect([...builder.element.querySelectorAll('.cl-condition-builder__combinator-option')].map((button) => button.textContent))
            .toEqual(['AND', 'OR']);
        const operatorSelect = rows(builder)[0].querySelector('.cl-condition-builder__operator');
        expect(operatorSelect.options[operatorSelect.selectedIndex].textContent).toBe('is greater than');
        expect(rows(builder)[0].querySelector('.number-input__wrapper input').getAttribute('aria-label')).toBe('Value of rule 1');
        expect(builder.getValue()).toEqual(single({ field: 'priority', operator: 'gt', value: 2 }));
    });

    it('hide/show toggles visibility', () => {
        const builder = create();
        builder.hide();
        expect(builder.element.style.display).toBe('none');
        builder.show();
        expect(builder.element.style.display).toBe('flex');
        expect(builder.snapshot().visibility).toBe('visible');
    });
});
