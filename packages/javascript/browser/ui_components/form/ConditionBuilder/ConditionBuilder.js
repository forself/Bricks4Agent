/**
 * ConditionBuilder — 巢狀布林篩選條件編輯器
 *
 * 產出的是純資料（{ combinator, not?, rules }），元件本身不執行任何程式碼、也不組查詢字串；
 * 把條件轉成查詢時，後端必須以參數化方式處理每個值。
 *
 * 值的形狀：
 *   { combinator: 'and'|'or', not?: boolean, rules: Array<{ field, operator, value } | Group> }
 *
 * 值編輯器沿用本庫既有輸入元件：TextInput、NumberInput、DatePicker、Dropdown（searchable）、
 * MultiSelectDropdown；between 使用兩個輸入框。
 *
 * @example
 * const builder = new ConditionBuilder({
 *     fields: [
 *         { key: 'title', label: '主旨', type: 'text' },
 *         { key: 'priority', label: '優先度', type: 'number' },
 *         { key: 'status', label: '狀態', type: 'select', options: [{ value: 'open', label: '進行中' }] }
 *     ],
 *     onChange: (value) => console.log(value)
 * });
 * builder.mount('#host');
 */
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';
import { nextUid } from '../../utils/uid.js';
import { TextInput } from '../TextInput/index.js';
import { NumberInput } from '../NumberInput/index.js';
import { DatePicker } from '../DatePicker/index.js';
import { Dropdown } from '../Dropdown/index.js';
import { MultiSelectDropdown } from '../MultiSelectDropdown/index.js';
import './locale.js';

/** 每個運算子需要的值形狀：none 無值、single 單一值、range 起迄兩值、list 多值陣列。 */
const OPERATOR_ARITY = Object.freeze({
    eq: 'single',
    ne: 'single',
    contains: 'single',
    notContains: 'single',
    startsWith: 'single',
    endsWith: 'single',
    isEmpty: 'none',
    isNotEmpty: 'none',
    gt: 'single',
    gte: 'single',
    lt: 'single',
    lte: 'single',
    between: 'range',
    before: 'single',
    after: 'single',
    in: 'list',
    notIn: 'list',
    containsAny: 'list',
    containsAll: 'list',
    isTrue: 'none',
    isFalse: 'none'
});

/** 各欄位型別的預設運算子（欄位的 operators 只能是這個清單的子集，可調整順序）。 */
const DEFAULT_OPERATORS = Object.freeze({
    text: Object.freeze(['eq', 'ne', 'contains', 'notContains', 'startsWith', 'endsWith', 'isEmpty', 'isNotEmpty']),
    number: Object.freeze(['eq', 'ne', 'gt', 'gte', 'lt', 'lte', 'between', 'isEmpty', 'isNotEmpty']),
    date: Object.freeze(['eq', 'before', 'after', 'between', 'isEmpty', 'isNotEmpty']),
    select: Object.freeze(['eq', 'ne', 'in', 'notIn']),
    multiselect: Object.freeze(['containsAny', 'containsAll']),
    boolean: Object.freeze(['isTrue', 'isFalse'])
});

const COMBINATORS = ['and', 'or'];
const RULE_KEYS = new Set(['field', 'operator', 'value']);
const GROUP_KEYS = new Set(['combinator', 'not', 'rules']);
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const FOCUSABLE = 'input:not([disabled]), select:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])';
const VISUALLY_HIDDEN = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';
const CONTROL_STYLE = 'height:32px;max-width:100%;min-width:0;box-sizing:border-box;padding:0 8px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font-size:var(--cl-font-size-md);font-family:inherit;';
const BUTTON_STYLE = 'display:inline-flex;align-items:center;gap:4px;height:30px;padding:0 10px;box-sizing:border-box;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font-size:var(--cl-font-size-sm);font-family:inherit;line-height:1;cursor:pointer;white-space:nowrap;';

function isObjectLike(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** 深拷貝純資料；函式、Symbol 與循環參照會被捨棄，Date 轉成 ISO 字串。 */
function cloneData(value, seen = new WeakSet()) {
    if (value === null || value === undefined) return value ?? null;
    const type = typeof value;
    if (type === 'function' || type === 'symbol') return undefined;
    if (type !== 'object') return value;
    if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value.toISOString();
    if (seen.has(value)) return undefined;
    seen.add(value);
    let result;
    if (Array.isArray(value)) {
        result = value.map((item) => {
            const cloned = cloneData(item, seen);
            return cloned === undefined ? null : cloned;
        });
    } else {
        result = {};
        for (const key of Object.keys(value)) {
            const cloned = cloneData(value[key], seen);
            if (cloned !== undefined) result[key] = cloned;
        }
    }
    seen.delete(value);
    return result;
}

function pickExtras(raw, knownKeys) {
    const extras = {};
    for (const key of Object.keys(raw)) {
        if (knownKeys.has(key)) continue;
        const cloned = cloneData(raw[key]);
        if (cloned !== undefined) extras[key] = cloned;
    }
    return extras;
}

function toIsoDate(date) {
    if (!(date instanceof Date) || Number.isNaN(date.getTime())) return null;
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${date.getFullYear()}-${month}-${day}`;
}

function isValidIsoDate(value) {
    if (typeof value !== 'string') return false;
    const match = ISO_DATE.exec(value);
    if (!match) return false;
    const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
    const date = new Date(year, month - 1, day);
    return date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
}

function sameValue(left, right) {
    if (left === right) return true;
    if (left === null || left === undefined || right === null || right === undefined) return false;
    return String(left) === String(right);
}

function isMissing(value) {
    return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

function defaultValueFor(arity) {
    if (arity === 'range') return [null, null];
    if (arity === 'list') return [];
    return null;
}

function convertArity(value, from, to) {
    if (from === to) return value;
    if (to === 'none' || from === 'none') return defaultValueFor(to);
    const list = Array.isArray(value) ? value.filter((item) => !isMissing(item)) : (isMissing(value) ? [] : [value]);
    if (to === 'single') return list.length ? list[0] : null;
    if (to === 'range') return [list[0] ?? null, list[1] ?? null];
    return list;
}

export class ConditionBuilder {
    static OPERATOR_ARITY = OPERATOR_ARITY;
    static DEFAULT_OPERATORS = DEFAULT_OPERATORS;

    constructor(options = {}) {
        this.options = {
            fields: [],         // [{ key, label, type, options?, operators?, editorOptions? }]
            value: null,        // 初始條件；null 視為 { combinator: 'and', rules: [] }
            maxDepth: 3,        // 群組巢狀層數上限（根群組為第 1 層）
            maxRules: 50,       // 整棵樹的條件（葉節點）數上限
            allowNot: false,    // 群組可勾選 NOT（反轉）
            disabled: false,    // 停用
            onChange: null,     // (value) => void，使用者每次修改後觸發
            ...options
        };

        this._uid = nextUid('cl-condition-builder');
        this._destroyed = false;
        this._localeListening = false;
        this._nodeSeq = 0;
        this._editors = new Map();
        this._ruleRefs = new Map();
        this._groupRefs = new Map();
        this._focusTargets = new Map();

        const { list, map } = this._normalizeFields(this.options.fields);
        this._fields = list;
        this._fieldMap = map;
        this._root = this._importValue(this.options.value, { warn: true });

        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled'
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled' })
        });

        this._onLocaleChange = () => {
            if (!this._destroyed) this._render();
        };

        this.element = this._createElement();
        this._render();
    }

    // ── 欄位與資料模型 ──────────────────────────────────────

    _normalizeFields(fields) {
        const list = [];
        const map = new Map();
        for (const raw of Array.isArray(fields) ? fields : []) {
            if (!isObjectLike(raw)) continue;
            const key = raw.key === null || raw.key === undefined ? '' : String(raw.key);
            if (!key) continue;
            if (map.has(key)) {
                console.warn(`[ConditionBuilder] Duplicate field key "${key}" ignored.`);
                continue;
            }
            let type = raw.type ?? 'text';
            if (!DEFAULT_OPERATORS[type]) {
                console.warn(`[ConditionBuilder] Unknown field type "${type}" for "${key}"; using "text".`);
                type = 'text';
            }
            const allowed = DEFAULT_OPERATORS[type];
            let operators = Array.isArray(raw.operators)
                ? raw.operators.map(String).filter((operator, index, all) => allowed.includes(operator) && all.indexOf(operator) === index)
                : [...allowed];
            if (!operators.length) {
                console.warn(`[ConditionBuilder] Field "${key}" lists no operator valid for type "${type}"; using the defaults.`);
                operators = [...allowed];
            }
            const options = Array.isArray(raw.options)
                ? raw.options.filter(isObjectLike).map((option) => ({
                    value: option.value,
                    label: option.label === null || option.label === undefined ? String(option.value ?? '') : String(option.label)
                }))
                : [];
            const field = {
                key,
                label: raw.label === null || raw.label === undefined || raw.label === '' ? key : String(raw.label),
                type,
                operators,
                options,
                editorOptions: isObjectLike(raw.editorOptions) ? { ...raw.editorOptions } : {}
            };
            list.push(field);
            map.set(key, field);
        }
        return { list, map };
    }

    _maxDepth() {
        const value = Number(this.options.maxDepth);
        return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 3;
    }

    _maxRules() {
        const value = Number(this.options.maxRules);
        return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 50;
    }

    _nextId() {
        this._nodeSeq += 1;
        return `n${this._nodeSeq}`;
    }

    _newGroup(combinator = 'and') {
        return { id: this._nextId(), kind: 'group', combinator, not: false, rules: [], extra: {} };
    }

    _importValue(value, { warn = false } = {}) {
        if (value === null || value === undefined) return this._newGroup();
        if (!isObjectLike(value) || !Array.isArray(value.rules)) {
            if (warn) console.warn('[ConditionBuilder] Invalid value shape; expected { combinator, rules: [] }. Using an empty group.');
            return this._newGroup();
        }
        return this._importGroup(value, warn);
    }

    _importGroup(raw, warn) {
        const group = this._newGroup();
        const combinator = typeof raw.combinator === 'string' ? raw.combinator.toLowerCase() : raw.combinator;
        if (COMBINATORS.includes(combinator)) {
            group.combinator = combinator;
        } else if (raw.combinator !== undefined && warn) {
            console.warn(`[ConditionBuilder] Invalid combinator "${raw.combinator}"; using "and".`);
        }
        group.not = raw.not === true;
        group.extra = pickExtras(raw, GROUP_KEYS);
        for (const child of Array.isArray(raw.rules) ? raw.rules : []) {
            if (isObjectLike(child) && Array.isArray(child.rules)) {
                group.rules.push(this._importGroup(child, warn));
            } else if (isObjectLike(child) && Object.prototype.hasOwnProperty.call(child, 'field')) {
                group.rules.push(this._importRule(child));
            } else if (warn) {
                console.warn('[ConditionBuilder] Dropped a node that is neither a rule nor a group.', child);
            }
        }
        return group;
    }

    _importRule(raw) {
        const field = raw.field === null || raw.field === undefined ? '' : String(raw.field);
        let operator = raw.operator === null || raw.operator === undefined ? '' : String(raw.operator);
        const definition = this._fieldMap.get(field);
        let value = cloneData(raw.value);
        if (value === undefined) value = null;
        if (definition && !operator) {
            operator = definition.operators[0];
            value = defaultValueFor(OPERATOR_ARITY[operator]);
        }
        if (definition && definition.operators.includes(operator)) {
            value = this._normalizeRuleValue(definition, operator, raw.value);
        }
        return { id: this._nextId(), kind: 'rule', field, operator, value, extra: pickExtras(raw, RULE_KEYS) };
    }

    /** 依運算子把值整理成對應形狀；日期轉 YYYY-MM-DD，選項值對齊到選項本身的值。 */
    _normalizeRuleValue(field, operator, rawValue) {
        const arity = OPERATOR_ARITY[operator];
        if (arity === 'none') return null;
        const one = (item) => {
            if (item === undefined) return null;
            if (field.type === 'date' && item instanceof Date) return toIsoDate(item);
            if ((field.type === 'select' || field.type === 'multiselect') && !isMissing(item)) {
                const option = field.options.find((entry) => sameValue(entry.value, item));
                if (option) return option.value;
            }
            const cloned = cloneData(item);
            return cloned === undefined ? null : cloned;
        };
        if (arity === 'single') return Array.isArray(rawValue) ? one(rawValue[0]) : one(rawValue);
        if (arity === 'range') {
            const pair = Array.isArray(rawValue) ? rawValue : [rawValue, null];
            return [one(pair[0]), one(pair[1])];
        }
        if (rawValue === null || rawValue === undefined) return [];
        return (Array.isArray(rawValue) ? rawValue : [rawValue]).map(one);
    }

    _exportGroup(group) {
        const result = { combinator: group.combinator };
        if (this.options.allowNot || group.not) result.not = Boolean(group.not);
        result.rules = group.rules.map((node) => (node.kind === 'group' ? this._exportGroup(node) : this._exportRule(node)));
        return { ...result, ...cloneData(group.extra) };
    }

    _exportRule(rule) {
        const value = cloneData(rule.value);
        return { field: rule.field, operator: rule.operator, value: value === undefined ? null : value, ...cloneData(rule.extra) };
    }

    _countRules(group = this._root) {
        let count = 0;
        for (const node of group.rules) count += node.kind === 'group' ? this._countRules(node) : 1;
        return count;
    }

    _findParent(id, group = this._root) {
        for (const node of group.rules) {
            if (node.id === id) return group;
            if (node.kind === 'group') {
                const found = this._findParent(id, node);
                if (found) return found;
            }
        }
        return null;
    }

    _createDefaultRule() {
        const field = this._fields[0];
        if (!field) return null;
        const operator = field.operators[0];
        return { id: this._nextId(), kind: 'rule', field: field.key, operator, value: defaultValueFor(OPERATOR_ARITY[operator]), extra: {} };
    }

    /** 值是否符合欄位型別（null 視為「尚未填」而符合）。 */
    _fits(field, item) {
        if (item === null || item === undefined) return true;
        switch (field.type) {
            case 'number':
                return typeof item === 'number' && Number.isFinite(item);
            case 'date':
                return isValidIsoDate(item);
            case 'select':
            case 'multiselect':
                return field.options.some((option) => sameValue(option.value, item));
            case 'boolean':
                return true;
            default:
                return typeof item === 'string';
        }
    }

    _valueFits(field, operator, value) {
        const arity = OPERATOR_ARITY[operator];
        if (arity === 'none') return true;
        if (arity === 'single') return !Array.isArray(value) && this._fits(field, value);
        if (arity === 'range') return Array.isArray(value) && value.length === 2 && value.every((item) => this._fits(field, item));
        return Array.isArray(value) && value.every((item) => this._fits(field, item));
    }

    _isDisabled() {
        return this._state.snapshot().availability === 'disabled';
    }

    // ── DOM ────────────────────────────────────────────────

    _createElement() {
        const root = document.createElement('div');
        root.className = 'cl-condition-builder';
        root.setAttribute('role', 'group');
        root.style.cssText = 'position:relative;display:flex;flex-direction:column;gap:6px;width:100%;max-width:100%;min-width:0;box-sizing:border-box;font-family:var(--cl-font-family);color:var(--cl-text);';

        const tree = document.createElement('div');
        tree.className = 'cl-condition-builder__tree';
        tree.style.cssText = 'min-width:0;';
        this._tree = tree;

        const live = document.createElement('div');
        live.className = 'cl-condition-builder__live';
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = VISUALLY_HIDDEN;
        this._live = live;

        root.append(tree, live);
        return root;
    }

    _button(text, className, { ariaLabel = '', variant = 'default' } = {}) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = className;
        button.textContent = text;
        button.style.cssText = BUTTON_STYLE;
        if (variant === 'danger') button.style.color = 'var(--cl-danger)';
        if (ariaLabel) {
            button.setAttribute('aria-label', ariaLabel);
            button.title = ariaLabel;
        }
        return button;
    }

    _setEnabled(control, enabled) {
        control.disabled = !enabled;
        control.style.opacity = enabled ? '1' : '0.5';
        control.style.cursor = enabled ? 'pointer' : 'not-allowed';
    }

    _registerFocus(nodeId, role, element) {
        element.dataset.focusNode = nodeId;
        element.dataset.focusRole = role;
        this._focusTargets.set(`${nodeId}:${role}`, element);
        if (!this._focusTargets.has(`${nodeId}:first`)) this._focusTargets.set(`${nodeId}:first`, element);
    }

    _render({ focus = null } = {}) {
        let request = focus;
        const active = document.activeElement;
        if (!request && active && this._tree.contains(active)) {
            const anchor = active.closest('[data-focus-node]');
            if (anchor) request = { id: anchor.dataset.focusNode, role: anchor.dataset.focusRole };
        }

        this._destroyAllEditors();
        this._ruleRefs.clear();
        this._groupRefs.clear();
        this._focusTargets.clear();

        const state = this._state.snapshot();
        this.element.style.display = state.visibility === 'hidden' ? 'none' : 'flex';
        this.element.setAttribute('aria-label', Locale.t('conditionBuilder.label'));
        if (state.availability === 'disabled') this.element.setAttribute('aria-disabled', 'true');
        else this.element.removeAttribute('aria-disabled');

        const context = { ruleIndex: 0, total: this._countRules(), disabled: state.availability === 'disabled' };
        this._tree.replaceChildren(this._renderGroup(this._root, 1, '', context));
        if (request) this._focusNode(request);
    }

    _focusNode({ id, role }) {
        const element = this._focusTargets.get(`${id}:${role}`) || this._focusTargets.get(`${id}:first`);
        if (!element) return;
        const target = element.matches(FOCUSABLE) ? element : element.querySelector(FOCUSABLE);
        target?.focus();
    }

    _renderGroup(group, depth, path, context) {
        const isRoot = depth === 1;
        const element = document.createElement('div');
        element.className = 'cl-condition-builder__group';
        element.setAttribute('role', 'group');
        element.setAttribute('aria-label', Locale.t('conditionBuilder.groupLabel', { depth }));
        element.dataset.depth = String(depth);
        element.dataset.nodeId = group.id;
        element.dataset.path = path;
        element.style.cssText = 'display:flex;flex-direction:column;gap:var(--cl-space-lg, 8px);min-width:0;box-sizing:border-box;padding:var(--cl-space-lg, 8px) var(--cl-space-xl, 12px);border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);';
        element.style.borderLeft = `3px solid ${isRoot ? 'var(--cl-primary)' : 'var(--cl-border-dark)'}`;
        element.style.background = depth % 2 === 0 ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';

        const header = document.createElement('div');
        header.className = 'cl-condition-builder__group-header';
        header.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:var(--cl-space-lg, 8px);min-width:0;';

        const refs = { element, combinatorButtons: {} };
        if (this.options.allowNot) {
            const notLabel = document.createElement('label');
            notLabel.className = 'cl-condition-builder__not';
            notLabel.style.cssText = 'display:inline-flex;align-items:center;gap:4px;font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);cursor:pointer;';
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.className = 'cl-condition-builder__not-toggle';
            checkbox.checked = Boolean(group.not);
            checkbox.disabled = context.disabled;
            checkbox.setAttribute('aria-label', Locale.t('conditionBuilder.notLabel', { depth }));
            checkbox.style.cssText = 'margin:0;width:16px;height:16px;accent-color:var(--cl-primary);';
            checkbox.addEventListener('change', () => this._setNot(group, checkbox.checked));
            const text = document.createElement('span');
            text.textContent = Locale.t('conditionBuilder.not');
            notLabel.append(checkbox, text);
            header.appendChild(notLabel);
            this._registerFocus(group.id, 'not', checkbox);
        } else if (group.not) {
            const badge = document.createElement('span');
            badge.className = 'cl-condition-builder__not-badge';
            badge.textContent = Locale.t('conditionBuilder.not');
            badge.title = Locale.t('conditionBuilder.notLabel', { depth });
            badge.style.cssText = 'padding:2px 8px;border-radius:var(--cl-radius-sm);background:var(--cl-warning-light);color:var(--cl-warning-dark);font-size:var(--cl-font-size-sm);font-weight:600;';
            header.appendChild(badge);
        }

        const combinator = document.createElement('div');
        combinator.className = 'cl-condition-builder__combinator';
        combinator.setAttribute('role', 'group');
        combinator.setAttribute('aria-label', Locale.t('conditionBuilder.combinatorLabel', { depth }));
        combinator.style.cssText = 'display:inline-flex;';
        COMBINATORS.forEach((value, index) => {
            const option = this._button(Locale.t(`conditionBuilder.${value}`), 'cl-condition-builder__combinator-option');
            option.dataset.combinator = value;
            option.style.borderRadius = index === 0
                ? 'var(--cl-radius-md) 0 0 var(--cl-radius-md)'
                : '0 var(--cl-radius-md) var(--cl-radius-md) 0';
            if (index > 0) option.style.marginLeft = '-1px';
            option.addEventListener('click', () => this._setCombinator(group, value));
            refs.combinatorButtons[value] = option;
            combinator.appendChild(option);
            this._registerFocus(group.id, `combinator-${value}`, option);
        });
        header.appendChild(combinator);
        this._paintCombinator(group, refs, context.disabled);

        const spacer = document.createElement('span');
        spacer.style.cssText = 'flex:1 1 auto;';
        header.appendChild(spacer);

        const canAdd = !context.disabled && this._fields.length > 0 && context.total < this._maxRules();
        const addRule = this._button(`+ ${Locale.t('conditionBuilder.addRule')}`, 'cl-condition-builder__add-rule', {
            ariaLabel: Locale.t('conditionBuilder.addRuleLabel', { depth })
        });
        this._setEnabled(addRule, canAdd);
        addRule.addEventListener('click', () => this._addRule(group));
        header.appendChild(addRule);
        this._registerFocus(group.id, 'add-rule', addRule);

        if (depth < this._maxDepth()) {
            const addGroup = this._button(`+ ${Locale.t('conditionBuilder.addGroup')}`, 'cl-condition-builder__add-group', {
                ariaLabel: Locale.t('conditionBuilder.addGroupLabel', { depth })
            });
            this._setEnabled(addGroup, canAdd);
            addGroup.addEventListener('click', () => this._addGroup(group, depth));
            header.appendChild(addGroup);
            this._registerFocus(group.id, 'add-group', addGroup);
        }

        if (!isRoot) {
            const remove = this._button(Locale.t('conditionBuilder.removeGroup'), 'cl-condition-builder__remove-group', {
                ariaLabel: Locale.t('conditionBuilder.removeGroupLabel', { depth }),
                variant: 'danger'
            });
            this._setEnabled(remove, !context.disabled);
            remove.addEventListener('click', () => this._removeNode(group));
            header.appendChild(remove);
            this._registerFocus(group.id, 'remove', remove);
        }
        element.appendChild(header);

        if (!group.rules.length) {
            const empty = document.createElement('p');
            empty.className = 'cl-condition-builder__empty';
            empty.textContent = Locale.t('conditionBuilder.emptyGroup');
            empty.style.cssText = 'margin:0;font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);';
            element.appendChild(empty);
        } else {
            const children = document.createElement('ul');
            children.className = 'cl-condition-builder__children';
            children.style.cssText = 'display:flex;flex-direction:column;gap:var(--cl-space-md, 6px);margin:0;margin-left:var(--cl-space-lg, 8px);padding:0;padding-left:var(--cl-space-xl, 12px);list-style:none;border-left:1px dashed var(--cl-border);min-width:0;';
            group.rules.forEach((node, index) => {
                const childPath = path ? `${path}.rules[${index}]` : `rules[${index}]`;
                const item = document.createElement('li');
                item.className = 'cl-condition-builder__item';
                item.style.cssText = 'min-width:0;';
                item.appendChild(node.kind === 'group'
                    ? this._renderGroup(node, depth + 1, childPath, context)
                    : this._renderRule(node, childPath, context));
                children.appendChild(item);
            });
            element.appendChild(children);
        }

        this._groupRefs.set(group.id, refs);
        return element;
    }

    _paintCombinator(group, refs, disabled) {
        COMBINATORS.forEach((value) => {
            const button = refs.combinatorButtons[value];
            const pressed = group.combinator === value;
            button.setAttribute('aria-pressed', pressed ? 'true' : 'false');
            button.style.background = pressed ? 'var(--cl-primary)' : 'var(--cl-bg)';
            button.style.color = pressed ? 'var(--cl-text-inverse)' : 'var(--cl-text)';
            button.style.borderColor = pressed ? 'var(--cl-primary)' : 'var(--cl-border)';
            button.style.fontWeight = pressed ? '600' : '400';
            this._setEnabled(button, !disabled);
        });
    }

    _renderRule(rule, path, context) {
        context.ruleIndex += 1;
        const index = context.ruleIndex;
        const field = this._fieldMap.get(rule.field);

        const row = document.createElement('div');
        row.className = 'cl-condition-builder__rule';
        row.setAttribute('role', 'group');
        row.setAttribute('aria-label', Locale.t('conditionBuilder.ruleLabel', { index }));
        row.dataset.nodeId = rule.id;
        row.dataset.path = path;
        row.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:var(--cl-space-lg, 8px);min-width:0;box-sizing:border-box;padding:var(--cl-space-md, 6px) var(--cl-space-lg, 8px);border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);background:var(--cl-bg);';

        const fieldSelect = document.createElement('select');
        fieldSelect.className = 'cl-condition-builder__field';
        fieldSelect.setAttribute('aria-label', Locale.t('conditionBuilder.fieldLabel', { index }));
        fieldSelect.style.cssText = CONTROL_STYLE;
        fieldSelect.style.flex = '0 1 180px';
        fieldSelect.disabled = context.disabled;
        fieldSelect.addEventListener('change', () => this._changeField(rule, fieldSelect.value));
        this._registerFocus(rule.id, 'field', fieldSelect);

        const operatorSelect = document.createElement('select');
        operatorSelect.className = 'cl-condition-builder__operator';
        operatorSelect.setAttribute('aria-label', Locale.t('conditionBuilder.operatorLabel', { index }));
        operatorSelect.style.cssText = CONTROL_STYLE;
        operatorSelect.style.flex = '0 1 160px';
        operatorSelect.addEventListener('change', () => this._changeOperator(rule, operatorSelect.value));
        this._registerFocus(rule.id, 'operator', operatorSelect);

        const valueArea = document.createElement('div');
        valueArea.className = 'cl-condition-builder__value';
        valueArea.style.cssText = 'display:flex;flex-wrap:wrap;align-items:center;gap:var(--cl-space-md, 6px);flex:1 1 240px;min-width:0;';

        const remove = this._button('✕', 'cl-condition-builder__remove-rule', {
            ariaLabel: Locale.t('conditionBuilder.removeRuleLabel', { index }),
            variant: 'danger'
        });
        this._setEnabled(remove, !context.disabled);
        remove.addEventListener('click', () => this._removeNode(rule));
        this._registerFocus(rule.id, 'remove', remove);

        const notice = document.createElement('div');
        notice.className = 'cl-condition-builder__notice';
        notice.id = `${this._uid}-notice-${rule.id}`;
        notice.style.cssText = 'flex:1 1 100%;font-size:var(--cl-font-size-sm);color:var(--cl-warning-dark);';

        row.append(fieldSelect, operatorSelect, valueArea, remove, notice);
        const refs = { row, fieldSelect, operatorSelect, valueArea, notice, index };
        this._ruleRefs.set(rule.id, refs);

        this._fillFieldOptions(rule, refs);
        this._fillOperatorOptions(rule, refs, field);
        this._renderValueEditor(rule, refs);
        this._updateRuleFlags(rule, refs);
        return row;
    }

    _addOption(select, value, label) {
        const option = document.createElement('option');
        option.value = value;
        option.textContent = label;
        select.appendChild(option);
    }

    _fillFieldOptions(rule, refs) {
        const select = refs.fieldSelect;
        select.replaceChildren();
        if (!this._fieldMap.has(rule.field)) {
            this._addOption(select, rule.field, Locale.t('conditionBuilder.unknownField', { field: rule.field || '—' }));
        }
        this._fields.forEach((field) => this._addOption(select, field.key, field.label));
        select.value = rule.field;
    }

    _fillOperatorOptions(rule, refs, field = this._fieldMap.get(rule.field)) {
        const select = refs.operatorSelect;
        select.replaceChildren();
        if (!field) {
            this._addOption(select, rule.operator, rule.operator
                ? (OPERATOR_ARITY[rule.operator] ? Locale.t(`conditionBuilder.operators.${rule.operator}`) : rule.operator)
                : '—');
            select.value = rule.operator;
            select.disabled = true;
            return;
        }
        if (!field.operators.includes(rule.operator)) {
            this._addOption(select, rule.operator, Locale.t('conditionBuilder.unknownOperator', { operator: rule.operator || '—' }));
        }
        field.operators.forEach((operator) => this._addOption(select, operator, Locale.t(`conditionBuilder.operators.${operator}`)));
        select.value = rule.operator;
        select.disabled = this._isDisabled();
    }

    _updateRuleFlags(rule, refs) {
        const field = this._fieldMap.get(rule.field);
        let message = '';
        let flag = '';
        if (!field) {
            message = Locale.t('conditionBuilder.unknownFieldNotice', { field: rule.field });
            flag = 'unknown-field';
        } else if (!field.operators.includes(rule.operator)) {
            message = Locale.t('conditionBuilder.unknownOperatorNotice', { operator: rule.operator });
            flag = 'unknown-operator';
        }
        refs.notice.textContent = message;
        refs.notice.style.display = message ? 'block' : 'none';
        if (flag) refs.row.dataset.flag = flag;
        else delete refs.row.dataset.flag;
        refs.row.style.borderColor = flag ? 'var(--cl-warning)' : 'var(--cl-border-light)';
        const flagged = flag === 'unknown-field' ? refs.fieldSelect : refs.operatorSelect;
        [refs.fieldSelect, refs.operatorSelect].forEach((select) => {
            if (flag && select === flagged) {
                select.setAttribute('aria-describedby', refs.notice.id);
                select.setAttribute('aria-invalid', 'true');
            } else {
                select.removeAttribute('aria-describedby');
                select.removeAttribute('aria-invalid');
            }
        });
    }

    // ── 值編輯器 ────────────────────────────────────────────

    _destroyEditors(ruleId) {
        const editors = this._editors.get(ruleId);
        if (!editors) return;
        editors.forEach((instance) => instance.destroy?.());
        this._editors.delete(ruleId);
    }

    _destroyAllEditors() {
        [...this._editors.keys()].forEach((ruleId) => this._destroyEditors(ruleId));
    }

    _renderValueEditor(rule, refs) {
        this._destroyEditors(rule.id);
        refs.valueArea.replaceChildren();
        const field = this._fieldMap.get(rule.field);
        if (!field || !field.operators.includes(rule.operator)) {
            // 未知欄位或運算子：保留原值並以唯讀文字顯示
            if (!isMissing(rule.value) && !(Array.isArray(rule.value) && !rule.value.length)) {
                const raw = document.createElement('span');
                raw.className = 'cl-condition-builder__raw';
                raw.textContent = Array.isArray(rule.value)
                    ? rule.value.map((item) => String(item ?? '')).join(', ')
                    : (typeof rule.value === 'object' ? JSON.stringify(rule.value) : String(rule.value));
                raw.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);overflow-wrap:anywhere;';
                refs.valueArea.appendChild(raw);
            }
            return;
        }

        const arity = OPERATOR_ARITY[rule.operator];
        if (arity === 'none') return;
        const editors = [];
        if (arity === 'list') {
            editors.push(this._createListEditor(rule, field, refs));
        } else if (arity === 'range') {
            editors.push(this._createSingleEditor(rule, field, refs, 0));
            const separator = document.createElement('span');
            separator.className = 'cl-condition-builder__separator';
            separator.setAttribute('aria-hidden', 'true');
            separator.textContent = Locale.t('conditionBuilder.rangeSeparator');
            separator.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);';
            refs.valueArea.appendChild(separator);
            editors.push(this._createSingleEditor(rule, field, refs, 1));
        } else {
            editors.push(this._createSingleEditor(rule, field, refs, null));
        }
        this._editors.set(rule.id, editors);
    }

    _editorHost(rule, refs, role) {
        const host = document.createElement('div');
        host.className = 'cl-condition-builder__editor';
        host.style.cssText = 'flex:1 1 140px;min-width:0;';
        refs.valueArea.appendChild(host);
        this._registerFocus(rule.id, role, host);
        return host;
    }

    _setRuleValue(rule, part, next) {
        if (part === null) {
            rule.value = next;
        } else {
            const pair = Array.isArray(rule.value) ? [rule.value[0] ?? null, rule.value[1] ?? null] : [null, null];
            pair[part] = next;
            rule.value = pair;
        }
        this._emitChange();
    }

    _createSingleEditor(rule, field, refs, part) {
        const { index } = refs;
        const labelKey = part === null ? 'valueLabel' : (part === 0 ? 'fromLabel' : 'toLabel');
        const label = Locale.t(`conditionBuilder.${labelKey}`, { index });
        const current = part === null ? rule.value : (Array.isArray(rule.value) ? rule.value[part] : null);
        const disabled = this._isDisabled();
        const host = this._editorHost(rule, refs, part === 1 ? 'value-1' : 'value-0');
        const commit = (next) => this._setRuleValue(rule, part, next);

        if (field.type === 'number') {
            const editor = new NumberInput({
                min: -Number.MAX_SAFE_INTEGER,
                max: Number.MAX_SAFE_INTEGER,
                showButtons: false,
                size: 'small',
                ...field.editorOptions,
                value: typeof current === 'number' && Number.isFinite(current) ? current : null,
                disabled,
                onChange: (next) => commit(typeof next === 'number' && Number.isFinite(next) ? next : null)
            });
            editor.mount(host);
            editor.input?.setAttribute('aria-label', label);
            // 空白時按 ↑/↓：從 0（夾在 min/max 內）開始調整，而不是從極小的 min 開始
            host.addEventListener('keydown', (event) => {
                if (this._isDisabled()) return;
                if ((event.key === 'ArrowUp' || event.key === 'ArrowDown') && editor.getValue() === null) {
                    editor.setValue(Math.min(Math.max(0, editor.options.min), editor.options.max));
                }
            }, true);
            return editor;
        }

        if (field.type === 'date') {
            const editor = new DatePicker({
                size: 'small',
                ...field.editorOptions,
                value: isValidIsoDate(current) ? current : null,
                disabled,
                onChange: (date) => {
                    commit(toIsoDate(date));
                    this._afterDateSelected(editor, label);
                }
            });
            editor.mount(host);
            this._enhanceDatePicker(editor, label);
            return editor;
        }

        if (field.type === 'select') {
            const editor = new Dropdown({
                variant: 'searchable',
                size: 'small',
                ...field.editorOptions,
                items: field.options.map((option) => ({ value: option.value, label: option.label })),
                value: isMissing(current) ? null : current,
                disabled,
                onChange: (next) => commit(isMissing(next) ? null : next)
            });
            editor.mount(host);
            editor.input?.setAttribute('aria-label', label);
            if (!editor.input) {
                editor.selector?.setAttribute('aria-label', label);
            }
            return editor;
        }

        const editor = new TextInput({
            size: 'small',
            placeholder: Locale.t('conditionBuilder.valuePlaceholder'),
            ...field.editorOptions,
            value: current === null || current === undefined ? '' : String(current),
            disabled,
            onChange: (next) => commit(next === '' ? null : next)
        });
        editor.mount(host);
        editor.input?.setAttribute('aria-label', label);
        return editor;
    }

    _createListEditor(rule, field, refs) {
        const label = Locale.t('conditionBuilder.valueLabel', { index: refs.index });
        const host = this._editorHost(rule, refs, 'value-0');
        const editor = new MultiSelectDropdown({
            size: 'small',
            ...field.editorOptions,
            items: field.options.map((option) => ({ value: option.value, label: option.label })),
            values: Array.isArray(rule.value) ? rule.value : [],
            disabled: this._isDisabled(),
            onChange: (values) => this._setRuleValue(rule, null, [...values])
        });
        editor.mount(host);
        host.querySelector('.msd__input')?.setAttribute('aria-label', label);
        return editor;
    }

    /**
     * DatePicker 目前只支援滑鼠操作；這裡補上鍵盤：觸發區可 Tab 聚焦，Enter/Space/↓ 開啟並把焦點
     * 移到日期按鈕，方向鍵在日期間移動，Escape 或 Tab 離開月曆時關閉並回到觸發區。
     */
    _enhanceDatePicker(picker, label) {
        const trigger = picker.inputWrapper;
        const calendar = picker.calendar;
        if (!trigger || !calendar) return;
        if (!trigger.hasAttribute('tabindex')) trigger.tabIndex = this._isDisabled() ? -1 : 0;
        if (!trigger.hasAttribute('role')) trigger.setAttribute('role', 'button');
        trigger.setAttribute('aria-haspopup', 'dialog');
        this._labelDateTrigger(picker, label);

        const focusDay = () => {
            const selected = picker.getValue();
            let day = null;
            if (selected && selected.getMonth() === picker.currentMonth && selected.getFullYear() === picker.currentYear) {
                day = calendar.querySelector(`.dp-day[data-day="${selected.getDate()}"]`);
            }
            day = day || calendar.querySelector('.dp-day[data-disabled="false"]') || calendar.querySelector('button');
            day?.focus();
        };
        const closeAndReturn = () => {
            picker.close();
            trigger.focus();
        };

        trigger.addEventListener('keydown', (event) => {
            if (picker.options.disabled) return;
            if (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar' || event.key === 'ArrowDown') {
                event.preventDefault();
                if (!picker.isOpen) picker.open();
                if (picker.isOpen) focusDay();
            } else if (event.key === 'Escape' && picker.isOpen) {
                event.preventDefault();
                picker.close();
            }
        });

        calendar.addEventListener('keydown', (event) => {
            if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                closeAndReturn();
                return;
            }
            if (event.key === 'Tab') {
                const focusables = [...calendar.querySelectorAll('button, select')].filter((element) => !element.disabled);
                const first = focusables[0];
                const last = focusables[focusables.length - 1];
                if ((event.shiftKey && event.target === first) || (!event.shiftKey && event.target === last)) {
                    event.preventDefault();
                    closeAndReturn();
                }
                return;
            }
            const deltas = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 };
            if (event.target?.classList?.contains('dp-day') && deltas[event.key] !== undefined) {
                const days = [...calendar.querySelectorAll('.dp-day')];
                const next = days[days.indexOf(event.target) + deltas[event.key]];
                event.preventDefault();
                next?.focus();
            }
        });

        calendar.addEventListener('focusout', (event) => {
            const next = event.relatedTarget;
            if (picker.isOpen && next && !calendar.contains(next) && next !== trigger) picker.close();
        });
    }

    _labelDateTrigger(picker, label) {
        const trigger = picker.inputWrapper;
        if (!trigger) return;
        const text = picker.getFormattedValue?.() || '';
        trigger.setAttribute('aria-label', text ? Locale.t('conditionBuilder.dateTriggerLabel', { label, value: text }) : label);
    }

    _afterDateSelected(picker, label) {
        this._labelDateTrigger(picker, label);
        const active = document.activeElement;
        if (!active || active === document.body || picker.calendar?.contains(active)) picker.inputWrapper?.focus();
    }

    // ── 使用者操作 ──────────────────────────────────────────

    _emitChange() {
        if (typeof this.options.onChange === 'function') this.options.onChange(this.getValue());
    }

    _announce(message) {
        this._live.textContent = '';
        this._live.textContent = message;
    }

    _setCombinator(group, value) {
        if (this._isDisabled() || group.combinator === value || !COMBINATORS.includes(value)) return;
        group.combinator = value;
        const refs = this._groupRefs.get(group.id);
        if (refs) this._paintCombinator(group, refs, false);
        this._emitChange();
    }

    _setNot(group, checked) {
        if (this._isDisabled()) return;
        group.not = Boolean(checked);
        this._emitChange();
    }

    _addRule(group) {
        if (this._isDisabled()) return;
        if (this._countRules() >= this._maxRules()) {
            this._announce(Locale.t('conditionBuilder.maxRulesReached', { max: this._maxRules() }));
            return;
        }
        const rule = this._createDefaultRule();
        if (!rule) return;
        group.rules.push(rule);
        this._render({ focus: { id: rule.id, role: 'field' } });
        this._announce(Locale.t('conditionBuilder.ruleAdded'));
        this._emitChange();
    }

    _addGroup(parent, depth) {
        if (this._isDisabled() || depth >= this._maxDepth()) return;
        if (this._countRules() >= this._maxRules()) {
            this._announce(Locale.t('conditionBuilder.maxRulesReached', { max: this._maxRules() }));
            return;
        }
        const rule = this._createDefaultRule();
        if (!rule) return;
        const group = this._newGroup();
        group.rules.push(rule);
        parent.rules.push(group);
        this._render({ focus: { id: rule.id, role: 'field' } });
        this._announce(Locale.t('conditionBuilder.groupAdded'));
        this._emitChange();
    }

    _removeNode(node) {
        if (this._isDisabled()) return;
        const parent = this._findParent(node.id);
        if (!parent) return;
        const index = parent.rules.indexOf(node);
        parent.rules.splice(index, 1);
        const sibling = parent.rules[index] || parent.rules[index - 1];
        const focus = sibling ? { id: sibling.id, role: 'first' } : { id: parent.id, role: 'add-rule' };
        this._render({ focus });
        this._announce(Locale.t(node.kind === 'group' ? 'conditionBuilder.groupRemoved' : 'conditionBuilder.ruleRemoved'));
        this._emitChange();
    }

    _changeField(rule, key) {
        const next = this._fieldMap.get(key);
        if (this._isDisabled() || !next || key === rule.field) return;
        const previous = this._fieldMap.get(rule.field);
        rule.field = key;
        if (!next.operators.includes(rule.operator)) {
            rule.operator = next.operators[0];
            rule.value = defaultValueFor(OPERATOR_ARITY[rule.operator]);
        } else if (!previous || previous.type !== next.type || !this._valueFits(next, rule.operator, rule.value)) {
            rule.value = defaultValueFor(OPERATOR_ARITY[rule.operator]);
        }
        const refs = this._ruleRefs.get(rule.id);
        if (refs) {
            this._fillFieldOptions(rule, refs);
            this._fillOperatorOptions(rule, refs, next);
            this._renderValueEditor(rule, refs);
            this._updateRuleFlags(rule, refs);
        }
        this._emitChange();
    }

    _changeOperator(rule, operator) {
        const field = this._fieldMap.get(rule.field);
        if (this._isDisabled() || !field || !field.operators.includes(operator) || operator === rule.operator) return;
        const wasKnown = field.operators.includes(rule.operator);
        const previousArity = OPERATOR_ARITY[rule.operator];
        const nextArity = OPERATOR_ARITY[operator];
        rule.operator = operator;
        if (!wasKnown) rule.value = defaultValueFor(nextArity);
        else if (previousArity !== nextArity) rule.value = convertArity(rule.value, previousArity, nextArity);
        const refs = this._ruleRefs.get(rule.id);
        if (refs) {
            if (!wasKnown) this._fillOperatorOptions(rule, refs, field);
            if (!wasKnown || previousArity !== nextArity) this._renderValueEditor(rule, refs);
            this._updateRuleFlags(rule, refs);
        }
        this._emitChange();
    }

    // ── 驗證與描述 ──────────────────────────────────────────

    _validateRule(rule, path, errors) {
        const field = this._fieldMap.get(rule.field);
        if (!field) {
            errors.push({ path, code: 'unknownField', message: Locale.t('conditionBuilder.errors.unknownField', { field: rule.field }) });
            return;
        }
        if (!field.operators.includes(rule.operator)) {
            errors.push({ path, code: 'unknownOperator', message: Locale.t('conditionBuilder.errors.unknownOperator', { field: field.label, operator: rule.operator }) });
            return;
        }
        const arity = OPERATOR_ARITY[rule.operator];
        const push = (code) => errors.push({ path, code, message: Locale.t(`conditionBuilder.errors.${code}`, { field: field.label }) });
        if (arity === 'none') return;
        if (arity === 'single') {
            if (isMissing(rule.value)) push('missingValue');
            else if (!this._valueFits(field, rule.operator, rule.value)) push('invalidValue');
            return;
        }
        if (arity === 'range') {
            const pair = Array.isArray(rule.value) ? rule.value : [rule.value, null];
            if (pair.length !== 2 || isMissing(pair[0]) || isMissing(pair[1])) push('missingValue');
            else if (!this._fits(field, pair[0]) || !this._fits(field, pair[1])) push('invalidValue');
            else if (pair[0] > pair[1]) push('badRange');
            return;
        }
        const list = Array.isArray(rule.value) ? rule.value : [];
        if (!list.length) push('missingValue');
        else if (!list.every((item) => !isMissing(item) && this._fits(field, item))) push('invalidValue');
    }

    _validateGroup(group, depth, path, errors) {
        if (depth > this._maxDepth()) {
            errors.push({ path, code: 'maxDepth', message: Locale.t('conditionBuilder.errors.maxDepth', { max: this._maxDepth() }) });
        }
        if (depth > 1 && !group.rules.length) {
            errors.push({ path, code: 'emptyGroup', message: Locale.t('conditionBuilder.errors.emptyGroup') });
        }
        group.rules.forEach((node, index) => {
            const childPath = path ? `${path}.rules[${index}]` : `rules[${index}]`;
            if (node.kind === 'group') this._validateGroup(node, depth + 1, childPath, errors);
            else this._validateRule(node, childPath, errors);
        });
    }

    _describeValue(field, arity, value) {
        const one = (item) => {
            if (isMissing(item)) return Locale.t('conditionBuilder.describe.missing');
            if (field && (field.type === 'select' || field.type === 'multiselect')) {
                const option = field.options.find((entry) => sameValue(entry.value, item));
                return option ? option.label : String(item);
            }
            if (!field || field.type === 'text') return Locale.t('conditionBuilder.describe.text', { value: String(item) });
            return typeof item === 'object' ? JSON.stringify(item) : String(item);
        };
        const separator = Locale.t('conditionBuilder.describe.listSeparator');
        if (arity === 'range') {
            const pair = Array.isArray(value) ? value : [value, null];
            return Locale.t('conditionBuilder.describe.range', { from: one(pair[0]), to: one(pair[1]) });
        }
        if (arity === 'list' || Array.isArray(value)) {
            const list = Array.isArray(value) ? value : (isMissing(value) ? [] : [value]);
            return list.length ? list.map(one).join(separator) : Locale.t('conditionBuilder.describe.missing');
        }
        return one(value);
    }

    _describeRule(rule) {
        const field = this._fieldMap.get(rule.field);
        const fieldLabel = field ? field.label : rule.field;
        const known = Boolean(OPERATOR_ARITY[rule.operator]);
        const operatorLabel = known ? Locale.t(`conditionBuilder.operators.${rule.operator}`) : rule.operator;
        const arity = known ? OPERATOR_ARITY[rule.operator] : 'single';
        if (arity === 'none') return Locale.t('conditionBuilder.describe.unary', { field: fieldLabel, operator: operatorLabel });
        return Locale.t('conditionBuilder.describe.rule', {
            field: fieldLabel,
            operator: operatorLabel,
            value: this._describeValue(field, arity, rule.value)
        });
    }

    _describeGroup(group, isRoot) {
        const parts = group.rules
            .map((node) => (node.kind === 'group' ? this._describeGroup(node, false) : this._describeRule(node)))
            .filter(Boolean);
        if (!parts.length) return '';
        const inner = parts.join(Locale.t(`conditionBuilder.describe.${group.combinator}`));
        if (group.not) return Locale.t('conditionBuilder.describe.not', { inner });
        return isRoot ? inner : Locale.t('conditionBuilder.describe.group', { inner });
    }

    // ── 公開 API ────────────────────────────────────────────

    snapshot() {
        return this._state.snapshot();
    }

    getValue() {
        return this._exportGroup(this._root);
    }

    /**
     * 設定條件。形狀不正確的節點會被略過（console.warn）；未知欄位或運算子的條件會保留，
     * 並在畫面上標示、由 validate() 回報。預設不觸發 onChange；{ emit: true } 時觸發。
     */
    setValue(value, { emit = false } = {}) {
        if (this._destroyed) return this;
        this._root = this._importValue(value, { warn: true });
        this._render();
        if (emit) this._emitChange();
        return this;
    }

    /** 清成空的根群組；不觸發 onChange。 */
    clear() {
        if (this._destroyed) return this;
        this._root = this._newGroup();
        this._render();
        return this;
    }

    setDisabled(disabled) {
        if (this._destroyed) return this;
        this._state.send('SET_DISABLED', { disabled: Boolean(disabled) });
        this.options.disabled = Boolean(disabled);
        this._render();
        return this;
    }

    /**
     * 檢查條件是否完整，回傳 [{ path, code, message }]；沒有問題時為空陣列。
     * path 形如 'rules[1].rules[0]'（根群組為 ''）。可傳入其他條件值檢查，預設檢查目前的值。
     */
    validate(value) {
        const root = value === undefined ? this._root : this._importValue(value);
        const errors = [];
        const total = this._countRules(root);
        if (total > this._maxRules()) {
            errors.push({ path: '', code: 'maxRules', message: Locale.t('conditionBuilder.errors.maxRules', { max: this._maxRules() }) });
        }
        this._validateGroup(root, 1, '', errors);
        return errors;
    }

    /**
     * 以目前語系把條件轉成可讀文字，例如「狀態 等於 進行中 且 (優先度 大於 3 或 主旨 包含 「會議」)」。
     * 未傳入 value 時描述目前的值。
     */
    describe(value) {
        const root = value === undefined ? this._root : this._importValue(value);
        return this._describeGroup(root, true) || Locale.t('conditionBuilder.describe.empty');
    }

    /**
     * 標示欄位錯誤；空訊息等同 clearError()。
     * display:false 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層（FormField、SearchForm）使用。
     */
    setError(message, { display = true } = {}) {
        if (this._destroyed) return this;
        setFieldError(this, message, { target: this.element, container: this.element, display });
        return this;
    }

    /** 清除 setError 的標示與文字。 */
    clearError() {
        clearFieldError(this);
        return this;
    }

    get [FIELD_ERROR_CONTRACT]() {
        return true;
    }

    show() {
        if (this._destroyed) return this;
        this._state.send('SHOW');
        this.element.style.display = 'flex';
        return this;
    }

    hide() {
        if (this._destroyed) return this;
        this._state.send('HIDE');
        this.element.style.display = 'none';
        return this;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target || this._destroyed) return this;
        target.appendChild(this.element);
        this._state.send('MOUNT');
        if (!this._localeListening) {
            window.addEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = true;
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        this._state.send('DESTROY');
        this._destroyAllEditors();
        clearFieldError(this);
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        this._ruleRefs.clear();
        this._groupRefs.clear();
        this._focusTargets.clear();
        this.element?.remove();
    }
}

export default ConditionBuilder;
