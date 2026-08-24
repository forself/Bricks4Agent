const DEFAULT_COLUMNS = 12;
const MAX_ROWS = 1000;

export const FORM_DESIGNER_COMPONENTS = Object.freeze([
    'TextInput',
    'TextArea',
    'NumberInput',
    'Checkbox',
    'ToggleSwitch',
    'DatePicker',
    'DateTimeInput',
    'Dropdown',
    'MultiSelectDropdown',
    'Radio',
    'BatchUploader',
    'HiddenInput',
    'WebTextEditor',
    'ColorPicker',
    'Slider',
]);

export const FORM_DESIGNER_COMPONENT_ICONS = Object.freeze({
    TextInput: 'title',
    TextArea: 'notes',
    NumberInput: 'code',
    Dropdown: 'toc',
    Checkbox: 'check-box-outline-blank',
    ToggleSwitch: 'settings',
    DatePicker: 'calendar',
    DateTimeInput: 'calendar',
    MultiSelectDropdown: 'select-all',
    Radio: 'check',
    BatchUploader: 'upload',
    HiddenInput: 'visibility',
    WebTextEditor: 'edit',
    ColorPicker: 'image',
    Slider: 'straighten',
});

export const FORM_DESIGNER_COMPONENT_FIELD_TYPES = Object.freeze({
    TextInput: 'text',
    TextArea: 'textarea',
    NumberInput: 'number',
    Checkbox: 'checkbox',
    ToggleSwitch: 'toggle',
    DatePicker: 'date',
    DateTimeInput: 'datetime',
    Dropdown: 'select',
    MultiSelectDropdown: 'multiselect',
    Radio: 'radio',
    BatchUploader: 'file',
    HiddenInput: 'hidden',
    WebTextEditor: 'richtext',
    ColorPicker: 'color',
    Slider: 'slider',
});

function integer(value, fallback) {
    const number = Number(value);
    return Number.isInteger(number) ? number : fallback;
}

function clamp(value, minimum, maximum) {
    return Math.min(maximum, Math.max(minimum, value));
}

function normalizeColumns(value) {
    return clamp(integer(value, DEFAULT_COLUMNS), 1, 24);
}

function fieldIdCandidate(field, index) {
    const candidate = field?.field_id ?? field?.fieldId ?? field?.column_name
        ?? field?.columnName ?? field?.fieldName ?? field?.name;
    const normalized = String(candidate ?? '').trim().replace(/[^A-Za-z0-9_-]/g, '-');
    return normalized || `field-${index + 1}`;
}

function recommendedComponent(dbType) {
    const type = String(dbType || '').toLowerCase();
    if (/bool|bit/.test(type)) return 'ToggleSwitch';
    if (/timestamp|datetime/.test(type)) return 'DateTimeInput';
    if (/date/.test(type)) return 'DatePicker';
    if (/int|decimal|numeric|real|float|double/.test(type)) return 'NumberInput';
    if (/json|clob|longtext/.test(type)) return 'TextArea';
    return 'TextInput';
}

const BLOCKED_KEYS = new Set(['__proto__', 'prototype', 'constructor']);

function cloneJson(value, path = 'value', active = new WeakSet()) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) throw new TypeError(`${path} must contain finite numbers.`);
        return value;
    }
    if (typeof value !== 'object') throw new TypeError(`${path} must contain JSON-only data.`);
    if (active.has(value)) throw new TypeError(`${path} must not contain cycles.`);
    active.add(value);
    try {
        if (Array.isArray(value)) return value.map((entry, index) => cloneJson(entry, `${path}[${index}]`, active));
        const prototype = Object.getPrototypeOf(value);
        if (prototype !== Object.prototype && prototype !== null) {
            throw new TypeError(`${path} must use a plain object prototype.`);
        }
        const output = {};
        for (const key of Object.keys(value)) {
            if (BLOCKED_KEYS.has(key)) throw new TypeError(`${path} contains an unsafe key.`);
            const descriptor = Object.getOwnPropertyDescriptor(value, key);
            if (!descriptor || descriptor.get || descriptor.set) throw new TypeError(`${path}.${key} must be a data property.`);
            output[key] = cloneJson(descriptor.value, `${path}.${key}`, active);
        }
        return output;
    } finally {
        active.delete(value);
    }
}

export function cloneFormDesignerJson(value) {
    return cloneJson(value);
}

export function clampLayout(layout = {}, columns = DEFAULT_COLUMNS) {
    const safeColumns = normalizeColumns(columns);
    const columnSpan = clamp(integer(layout.column_span ?? layout.columnSpan, safeColumns), 1, safeColumns);
    const rowSpan = clamp(integer(layout.row_span ?? layout.rowSpan, 1), 1, 24);
    return {
        row: clamp(integer(layout.row, 1), 1, MAX_ROWS),
        column: clamp(integer(layout.column, 1), 1, safeColumns - columnSpan + 1),
        column_span: columnSpan,
        row_span: rowSpan,
    };
}

export function layoutsOverlap(left, right) {
    const leftEndColumn = left.column + left.column_span;
    const rightEndColumn = right.column + right.column_span;
    const leftEndRow = left.row + left.row_span;
    const rightEndRow = right.row + right.row_span;
    return left.column < rightEndColumn
        && right.column < leftEndColumn
        && left.row < rightEndRow
        && right.row < leftEndRow;
}

function cloneField(field) {
    const cloned = cloneJson(field, `field.${field.field_id || 'unknown'}`);
    return {
        ...cloned,
        field_id: field.field_id,
        column_name: field.column_name,
        display_name: field.display_name,
        db_type: field.db_type,
        icon: field.icon,
        input: { ...(cloned.input || {}), component: field.input.component },
        layout: { ...(cloned.layout || {}), ...field.layout },
        order: field.order,
    };
}

function firstAvailableLayout(fields, requested, columns, excludeId = null) {
    const desired = clampLayout(requested, columns);
    const occupied = fields.filter((field) => field.field_id !== excludeId);
    const fits = (layout) => !occupied.some((field) => layoutsOverlap(layout, field.layout));
    if (fits(desired)) return desired;

    for (let row = 1; row <= MAX_ROWS; row += 1) {
        for (let column = 1; column <= columns - desired.column_span + 1; column += 1) {
            const candidate = { ...desired, row, column };
            if (fits(candidate)) return candidate;
        }
    }
    throw new RangeError('FormDesigner could not place the field within the layout limit.');
}

export function normalizeFormDesignerFields(source, columns = DEFAULT_COLUMNS) {
    const safeColumns = normalizeColumns(columns);
    const input = Array.isArray(source) ? source : [];
    const identifiers = new Set();
    const normalized = input.map((field, index) => {
        const record = field && typeof field === 'object' && !Array.isArray(field) ? field : {};
        const baseId = fieldIdCandidate(record, index);
        let fieldId = baseId;
        let suffix = 2;
        while (identifiers.has(fieldId)) fieldId = `${baseId}-${suffix++}`;
        identifiers.add(fieldId);

        const columnName = String(
            record.column_name ?? record.columnName ?? record.fieldName ?? record.name ?? `field_${index + 1}`,
        );
        const displayName = String(record.display_name ?? record.displayName ?? record.label ?? columnName);
        const dbType = String(record.db_type ?? record.dbType ?? record.fieldType ?? record.type ?? 'text');
        const componentCandidate = record.input?.component ?? record.component ?? recommendedComponent(dbType);
        const component = FORM_DESIGNER_COMPONENTS.includes(componentCandidate)
            ? componentCandidate
            : recommendedComponent(dbType);
        const inputLayout = record.layout || {
            row: record.formRow,
            column: record.formColStart,
            column_span: record.formCol,
            row_span: record.formRowSpan,
        };

        const preserved = cloneJson(record, `fields[${index}]`);
        return {
            ...preserved,
            field_id: fieldId,
            column_name: columnName,
            display_name: displayName,
            db_type: dbType,
            icon: String(record.icon || FORM_DESIGNER_COMPONENT_ICONS[component] || 'help'),
            input: {
                ...(preserved.input && typeof preserved.input === 'object' ? preserved.input : {}),
                field_type: preserved.input?.field_type || FORM_DESIGNER_COMPONENT_FIELD_TYPES[component],
                component,
                options: preserved.input?.options || {},
            },
            layout: {
                ...(preserved.layout && typeof preserved.layout === 'object' ? preserved.layout : {}),
                ...clampLayout(inputLayout, safeColumns),
            },
            order: clamp(integer(record.order, index + 1), 1, Number.MAX_SAFE_INTEGER),
        };
    });

    return packFormDesignerFields(normalized, safeColumns);
}

export function packFormDesignerFields(fields, columns = DEFAULT_COLUMNS) {
    const safeColumns = normalizeColumns(columns);
    const ordered = fields.map(cloneField).sort((left, right) => (
        left.order - right.order || left.field_id.localeCompare(right.field_id)
    ));
    const packed = [];
    for (const field of ordered) {
        const next = cloneField(field);
        next.layout = firstAvailableLayout(packed, next.layout, safeColumns);
        packed.push(next);
    }
    return packed
        .sort((left, right) => (
            left.layout.row - right.layout.row
            || left.layout.column - right.layout.column
            || left.order - right.order
            || left.field_id.localeCompare(right.field_id)
        ))
        .map((field, index) => ({ ...field, order: index + 1 }));
}

function tryLayout(fields, fieldId, proposedLayout, columns) {
    const safeColumns = normalizeColumns(columns);
    const original = fields.map(cloneField);
    const index = original.findIndex((field) => field.field_id === fieldId);
    if (index < 0) return { fields: original, accepted: false };
    const candidate = clampLayout(proposedLayout, safeColumns);
    const collision = original.some((field, fieldIndex) => (
        fieldIndex !== index && layoutsOverlap(candidate, field.layout)
    ));
    if (collision) return { fields: original, accepted: false };

    original[index].layout = candidate;
    const spatial = original.sort((left, right) => (
        left.layout.row - right.layout.row
        || left.layout.column - right.layout.column
        || left.order - right.order
        || left.field_id.localeCompare(right.field_id)
    ));
    return {
        accepted: true,
        fields: spatial.map((field, order) => ({ ...field, order: order + 1 })),
    };
}

export function tryMoveFormDesignerField(fields, fieldId, target, columns = DEFAULT_COLUMNS) {
    const current = fields.find((field) => field.field_id === fieldId);
    if (!current) return { fields: fields.map(cloneField), accepted: false };
    return tryLayout(fields, fieldId, {
        ...current.layout,
        row: target?.row,
        column: target?.column,
    }, columns);
}

export function tryResizeFormDesignerField(fields, fieldId, size, columns = DEFAULT_COLUMNS) {
    const current = fields.find((field) => field.field_id === fieldId);
    if (!current) return { fields: fields.map(cloneField), accepted: false };
    return tryLayout(fields, fieldId, {
        ...current.layout,
        column_span: size?.column_span,
        row_span: size?.row_span,
    }, columns);
}

export function cloneFormDesignerFields(fields) {
    return fields.map(cloneField);
}
