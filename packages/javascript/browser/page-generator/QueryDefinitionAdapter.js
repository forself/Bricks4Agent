/**
 * QueryDefinitionAdapter - TimWeb query/admin list page definition helpers.
 *
 * The TimWeb definition shape is a higher-level runtime schema:
 * page.view === "query" | "adminList", searchFields[], columns[], table,
 * api actions, row/toolbar actions, and optional modal form declarations.
 * These helpers materialize it into the field/list shape used by the dynamic
 * renderer while keeping payload/action logic deterministic and testable.
 */

const QUERY_VIEW = 'query';
const ADMIN_LIST_VIEW = 'adminlist';
const EXPORT_ACTION_KEYS = new Set(['download', 'export']);
const DEFAULT_DOWNLOAD_LABEL = '匯出Excel';

export function isQueryDefinition(definition) {
    const view = definition?.page?.view ?? definition?.type;
    return typeof view === 'string' && view.toLowerCase() === QUERY_VIEW;
}

export function isDeclarativeListDefinition(definition) {
    const view = definition?.page?.view ?? definition?.type;
    if (typeof view !== 'string') return false;
    const normalized = view.toLowerCase();
    return normalized === QUERY_VIEW || normalized === ADMIN_LIST_VIEW;
}

export function normalizeQueryDefinition(definition) {
    if (!isDeclarativeListDefinition(definition)) return definition;

    const searchFields = getQuerySearchFields(definition);
    const columns = getQueryColumns(definition);
    return {
        ...definition,
        type: 'list',
        fields: [...searchFields, ...columns],
        api: { ...(definition.api || {}) },
        actions: Array.isArray(definition.actions) ? definition.actions.map((action) => ({ ...action })) : undefined,
        modals: Array.isArray(definition.modals) ? definition.modals.map((modal) => ({ ...modal })) : undefined,
        table: { ...(definition.table || {}) },
        behaviors: { ...(definition.behaviors || {}) },
        fixtures: { ...(definition.fixtures || {}) },
    };
}

export function getQuerySearchFields(definition) {
    const source = Array.isArray(definition?.searchFields)
        ? definition.searchFields
        : (definition?.fields || []).filter((field) => field?.isSearchable);

    return source.map((field, index) => normalizeQuerySearchField(field, index));
}

export function getQueryColumns(definition) {
    const source = Array.isArray(definition?.columns)
        ? definition.columns
        : (definition?.fields || [])
            .filter((field) => Number(field?.listOrder || 0) > 0)
            .sort((a, b) => Number(a.listOrder || 0) - Number(b.listOrder || 0));

    return source.map((column, index) => normalizeQueryColumn(column, index));
}

export function normalizeQuerySearchField(field, index = 0) {
    const fieldName = field?.name || field?.fieldName || field?.key || '';
    const fieldType = field?.type || field?.fieldType || 'text';
    const optionsSource = normalizeOptionsSource(field?.optionsSource, field?.options);
    return {
        ...field,
        name: fieldName,
        fieldName,
        label: field?.label || fieldName,
        type: fieldType,
        fieldType,
        formRow: field?.formRow ?? index + 1,
        formCol: field?.formCol ?? 12,
        listOrder: 0,
        isSearchable: true,
        isRequired: field?.required === true || field?.isRequired === true,
        required: field?.required === true || field?.isRequired === true,
        placeholder: field?.placeholder || '',
        defaultValue: field?.defaultValue ?? field?.default ?? null,
        omitWhenEmpty: field?.omitWhenEmpty === true,
        payload: field?.payload || null,
        pairWith: field?.pairWith || null,
        separator: field?.separator || null,
        min: field?.min || null,
        max: field?.max || null,
        maxYearOffset: field?.maxYearOffset ?? null,
        options: normalizeOptionItems(field?.options || optionsSource?.items || [], field?.valueField, field?.labelField),
        optionsSource,
        valueField: field?.valueField || optionsSource?.valueField || 'value',
        labelField: field?.labelField || optionsSource?.labelField || 'label',
        dependsOn: field?.dependsOn || null,
        filter: field?.filter || null,
    };
}

export function normalizeQueryColumn(column, index = 0) {
    const fieldName = column?.key || column?.fieldName || column?.name || '';
    const fieldType = column?.type || column?.fieldType || 'text';
    const optionsSource = normalizeOptionsSource(column?.optionsSource, column?.options);
    return {
        ...column,
        key: fieldName,
        name: fieldName,
        fieldName,
        title: column?.title || column?.label || fieldName,
        label: column?.title || column?.label || fieldName,
        type: fieldType,
        fieldType,
        listOrder: Number(column?.listOrder || index + 1),
        isSearchable: false,
        hidden: column?.hidden === true,
        width: column?.width || null,
        format: column?.format || null,
        rocDateSource: column?.rocDateSource || column?.dateSource || null,
        isSelectionKey: column?.isSelectionKey === true,
        selectionKey: column?.selectionKey || null,
        sortable: column?.sortable === false ? false : true,
        sortOrder: column?.sortOrder || null,
        options: normalizeOptionItems(column?.options || optionsSource?.items || [], column?.valueField, column?.labelField),
        optionsSource,
        lookup: column?.lookup || null,
        valueField: column?.valueField || optionsSource?.valueField || column?.lookup?.valueField || 'value',
        labelField: column?.labelField || optionsSource?.labelField || column?.lookup?.labelField || 'label',
        link: column?.link || null,
        action: column?.action || null,
    };
}

export function validateQueryDefinition(definition) {
    const errors = [];
    if (!definition || typeof definition !== 'object') {
        return { valid: false, errors: ['query definition must be an object.'] };
    }

    const view = definition.page?.view;
    if (!definition.page || typeof definition.page !== 'object') {
        errors.push('query definition requires page metadata.');
    } else if (view !== QUERY_VIEW && view !== 'adminList') {
        errors.push('query definition page.view must be "query" or "adminList".');
    }

    if (view === QUERY_VIEW) {
        if (!Array.isArray(definition.searchFields)) {
            errors.push('query definition requires searchFields[].');
        } else {
            validateFieldList(definition.searchFields, errors, 'searchFields');
        }
    }

    if (!Array.isArray(definition.columns)) {
        errors.push('query definition requires columns[].');
    } else {
        const keys = new Set();
        for (const [index, column] of definition.columns.entries()) {
            const key = column?.key || column?.fieldName || column?.name;
            if (!key) {
                errors.push(`columns[${index}] requires key.`);
                continue;
            }
            if (keys.has(key)) errors.push(`column "${key}" is duplicated.`);
            keys.add(key);
        }
    }

    return { valid: errors.length === 0, errors };
}

function validateFieldList(fields, errors, path) {
    const names = new Set();
    for (const [index, field] of fields.entries()) {
        const name = field?.name || field?.fieldName;
        if (!name) {
            errors.push(`${path}[${index}] requires name.`);
            continue;
        }
        if (names.has(name)) errors.push(`search field "${name}" is duplicated.`);
        names.add(name);

        const type = field?.type || field?.fieldType;
        if (!type) errors.push(`search field "${name}" requires type.`);
        if (type === 'rocDate') {
            if (!field.payload || typeof field.payload !== 'object') {
                errors.push(`rocDate field "${name}" requires payload.`);
            } else {
                if (!field.payload.roc) errors.push(`rocDate field "${name}" requires payload.roc.`);
                if (!field.payload.western) errors.push(`rocDate field "${name}" requires payload.western.`);
            }
        }
    }

    for (const field of fields) {
        if (field?.pairWith && !names.has(field.pairWith)) {
            errors.push(`search field "${field.name}" pairWith "${field.pairWith}" does not exist.`);
        }
        if (field?.dependsOn && !names.has(field.dependsOn)) {
            errors.push(`search field "${field.name}" dependsOn "${field.dependsOn}" does not exist.`);
        }
    }
}

export function buildQueryPayload(searchFields, values = {}, options = {}) {
    const payload = {};
    const errors = [];
    const fields = (searchFields || []).map((field, index) => normalizeQuerySearchField(field, index));
    const requiredMessage = options.requiredMessage || '*必填欄位*';

    for (const field of fields) {
        const key = field.fieldName;
        const value = values[key];
        const empty = isEmptyValue(value);

        if (field.isRequired && empty) {
            errors.push({ field: key, message: field.requiredMessage || requiredMessage });
            continue;
        }

        if (field.fieldType === 'rocDate') {
            if (empty) continue;
            const date = coerceDate(value);
            if (!date) {
                errors.push({ field: key, message: 'Invalid rocDate value.' });
                continue;
            }
            const rocKey = field.payload?.roc || `${key}CH`;
            const westernKey = field.payload?.western || key;
            payload[rocKey] = formatRocDate(date);
            payload[westernKey] = formatWesternDate(date);
            continue;
        }

        if (field.omitWhenEmpty && empty) continue;
        payload[key] = value;
    }

    return { payload, errors };
}

export function getApiActionEntries(definition) {
    const api = definition?.api || {};
    const entries = [];
    for (const [key, value] of Object.entries(api)) {
        if (Array.isArray(value)) {
            value.forEach((action, index) => {
                if (action && typeof action === 'object') entries.push(normalizeApiAction(key, action, index));
            });
        } else if (value && typeof value === 'object') {
            entries.push(normalizeApiAction(key, value, 0));
        }
    }
    return entries;
}

export function getDownloadActions(definition) {
    return getApiActionEntries(definition)
        .filter((action) => action.type === 'export' || EXPORT_ACTION_KEYS.has(action.key));
}

export function getUiActions(definition, options = {}) {
    const downloads = getDownloadActions(definition).map((action) => ({
        ...action,
        placement: action.placement || 'toolbarSelect',
        requiresSelection: action.requiresSelection !== false,
        apiAction: action.id,
    }));
    const explicit = Array.isArray(definition?.actions)
        ? definition.actions.map((action, index) => normalizeUiAction(action, index, definition))
        : [];
    const actions = [...downloads, ...explicit];
    if (!options.placement) return actions;
    return actions.filter((action) => action.placement === options.placement);
}

export function findApiAction(definition, idOrKey) {
    if (!idOrKey) return null;
    const actions = getApiActionEntries(definition);
    return actions.find((action) => action.id === idOrKey)
        || actions.find((action) => action.key === idOrKey)
        || null;
}

export function buildDownloadRequest(definition, rows = [], selectedIndices = [], options = {}) {
    const actionId = options.actionId || options.id || null;
    const action = actionId
        ? getDownloadActions(definition).find((item) => item.id === actionId || item.key === actionId)
        : getDownloadActions(definition)[0];
    if (!action) return null;

    return buildActionRequest(definition, action, {
        rows,
        selectedIndices,
        now: options.now,
        row: options.row,
        searchValues: options.searchValues,
    });
}

export function buildActionRequest(definition, actionOrId, options = {}) {
    const uiAction = typeof actionOrId === 'string'
        ? getUiActions(definition).find((item) => item.id === actionOrId)
        : (actionOrId || {});
    const apiAction = uiAction?.apiAction
        ? findApiAction(definition, uiAction.apiAction)
        : (uiAction?.legacyPath ? uiAction : findApiAction(definition, uiAction?.id || uiAction?.key));
    const action = { ...(apiAction || {}), ...(uiAction || {}) };
    if (!action || (!action.legacyPath && !action.apiAction && !action.route && !action.modal)) return null;

    const columns = getQueryColumns(definition);
    const rows = Array.isArray(options.rows) ? options.rows : [];
    const selectedIndices = Array.isArray(options.selectedIndices) ? options.selectedIndices : [];
    const selectedRows = selectedIndices
        .map((index) => rows[index])
        .filter((row) => row !== undefined && row !== null);
    const selectionKey = action.selectionKey
        || action.selection?.key
        || columns.find((column) => column.isSelectionKey)?.fieldName
        || null;
    const context = {
        rows,
        row: options.row || selectedRows[0] || null,
        selectedRows,
        selectionKey,
        columns,
        searchValues: options.searchValues || {},
    };
    const payloadTemplate = {
        ...(apiAction?.payloadDefaults || {}),
        ...(action.payloadDefaults || {}),
        ...(apiAction?.payload || {}),
        ...(action.payload || {}),
    };

    return {
        id: action.id || action.key || '',
        type: action.type || inferActionType(action.key || ''),
        legacyPath: action.legacyPath || action.url || '',
        method: action.method || 'POST',
        fileName: action.fileName ? formatDownloadFileName(action.fileName, options.now || new Date()) : '',
        payload: resolvePayloadTemplate(payloadTemplate, context),
        confirmText: action.confirmText || '',
        selectionKey,
        selectionValues: selectionKey
            ? selectedRows
                .map((row) => readRowValue(row, selectionKey, columns))
                .filter((value) => value !== undefined)
            : [],
        label: action.label || '',
        action,
    };
}

export function normalizeTableTextLabels(table = {}) {
    const labels = table.textLabels || {};
    const result = {};

    const rowsPerPage = labels.rowsPerPage ?? labels.pagination?.rowsPerPage;
    const displayRows = labels.displayRows ?? labels.pagination?.displayRows;
    const noMatch = labels.noMatch ?? labels.body?.noMatch;
    const selectedUnit = labels.selectedUnit ?? labels.selectedRows?.text;

    if (rowsPerPage !== undefined || displayRows !== undefined) {
        result.pagination = {};
        if (rowsPerPage !== undefined) result.pagination.rowsPerPage = rowsPerPage;
        if (displayRows !== undefined) result.pagination.displayRows = displayRows;
    }
    if (noMatch !== undefined) result.body = { noMatch };
    if (selectedUnit !== undefined) result.selectedRows = { text: selectedUnit };

    return result;
}

export function formatTitleTemplate(table = {}, count = 0) {
    const template = table.titleTemplate || table.title || '';
    return String(template).replaceAll('{count}', String(count));
}

export function formatTimestamp(date = new Date()) {
    const d = coerceDateTime(date) || new Date();
    return `${d.getFullYear()}${pad2(d.getMonth() + 1)}${pad2(d.getDate())}`
        + `${pad2(d.getHours())}${pad2(d.getMinutes())}${pad2(d.getSeconds())}`;
}

export function formatDownloadFileName(template, date = new Date()) {
    return String(template || '').replaceAll('{timestamp}', formatTimestamp(date));
}

export function formatWesternDate(date) {
    const d = coerceDate(date);
    if (!d) return '';
    return `${d.getFullYear()}/${pad2(d.getMonth() + 1)}/${pad2(d.getDate())}`;
}

export function formatRocDate(date) {
    const d = coerceDate(date);
    if (!d) return '';
    return `${d.getFullYear() - 1911}/${pad2(d.getMonth() + 1)}/${pad2(d.getDate())}`;
}

export function formatRocDateTime(value) {
    const d = coerceDateTime(value);
    if (!d) return value == null ? '' : String(value);
    return `${d.getFullYear() - 1911}/${pad2(d.getMonth() + 1)}/${pad2(d.getDate())}`
        + ` ${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`;
}

export function coerceDate(value) {
    if (value instanceof Date && !Number.isNaN(value.getTime())) {
        return new Date(value.getFullYear(), value.getMonth(), value.getDate());
    }

    if (typeof value === 'string') {
        const trimmed = value.trim();
        if (!trimmed) return null;

        const rocMatch = /^(\d{2,3})[/-](\d{1,2})[/-](\d{1,2})$/.exec(trimmed);
        if (rocMatch && Number(rocMatch[1]) < 1912) {
            const year = Number(rocMatch[1]) + 1911;
            const month = Number(rocMatch[2]) - 1;
            const day = Number(rocMatch[3]);
            const date = new Date(year, month, day);
            if (date.getFullYear() === year && date.getMonth() === month && date.getDate() === day) {
                return date;
            }
            return null;
        }

        const westernMatch = /^(\d{4})[/-](\d{1,2})[/-](\d{1,2})$/.exec(trimmed);
        if (westernMatch) {
            const year = Number(westernMatch[1]);
            const month = Number(westernMatch[2]) - 1;
            const day = Number(westernMatch[3]);
            const date = new Date(year, month, day);
            if (date.getFullYear() === year && date.getMonth() === month && date.getDate() === day) {
                return date;
            }
            return null;
        }
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return null;
    return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function resolveFieldOptions(field, definition, values = {}) {
    const normalized = normalizeQuerySearchField(field);
    let items = [];
    if (Array.isArray(normalized.options) && normalized.options.length > 0) {
        items = normalized.options;
    } else if (normalized.optionsSource) {
        items = resolveOptionsSourceItems(normalized.optionsSource, definition);
    }
    if (!normalized.filter && !normalized.dependsOn) return normalizeOptionItems(items, normalized.valueField, normalized.labelField);

    const filter = normalized.filter || {};
    const parentKey = filter.equalsField || normalized.dependsOn;
    const parentValue = values[parentKey];
    if (isEmptyValue(parentValue)) return normalizeOptionItems(items, normalized.valueField, normalized.labelField);
    const parentValues = Array.isArray(parentValue) ? parentValue : [parentValue];
    const sourceField = filter.sourceField || filter.field || normalized.valueField;
    const filtered = items.filter((item) => parentValues.includes(item[sourceField]));
    return normalizeOptionItems(filtered, normalized.valueField, normalized.labelField);
}

export function resolveLookupLabel(column, value, definition) {
    const normalized = normalizeQueryColumn(column);
    const source = normalized.lookup?.optionsSource || normalized.optionsSource || normalized.lookup;
    let items = [];
    if (Array.isArray(normalized.options) && normalized.options.length > 0) {
        items = normalized.options;
    } else if (source) {
        items = resolveOptionsSourceItems(source, definition);
    }
    const valueField = normalized.lookup?.valueField || normalized.valueField || source?.valueField || 'value';
    const labelField = normalized.lookup?.labelField || normalized.labelField || source?.labelField || 'label';
    const match = items.find((item) => item?.[valueField] === value);
    if (!match) return normalized.lookup?.fallback ?? (value == null ? '' : String(value));
    return match[labelField] ?? normalized.lookup?.fallback ?? String(value);
}

export function resolveRouteTemplate(template, context = {}) {
    if (typeof template !== 'string') return '';
    return template.replace(/\{([A-Za-z0-9_.-]+)\}/g, (_match, key) => {
        const value = readContextValue(key, context);
        return encodeURIComponent(value == null ? '' : String(value));
    });
}

function normalizeApiAction(key, action, index) {
    const type = action.type || inferActionType(key);
    const id = action.id || (index > 0 ? `${key}-${index + 1}` : key);
    return {
        ...action,
        key,
        id,
        type,
        method: action.method || (type === 'export' || type === 'query' ? 'POST' : undefined),
        label: action.label || (type === 'export' ? DEFAULT_DOWNLOAD_LABEL : id),
        placement: action.placement || (type === 'export' ? 'toolbarSelect' : undefined),
    };
}

function normalizeUiAction(action, index, definition) {
    const apiAction = action.apiAction ? findApiAction(definition, action.apiAction) : null;
    const merged = { ...(apiAction || {}), ...action };
    return {
        ...merged,
        id: merged.id || `action-${index + 1}`,
        type: merged.type || inferActionType(apiAction?.key || merged.key || ''),
        placement: merged.placement || 'row',
        label: merged.label || merged.id || '',
        requiresSelection: merged.requiresSelection === true,
    };
}

function inferActionType(key) {
    if (key === 'download' || key === 'export' || key === 'report') return 'export';
    if (key === 'search' || key === 'searchlist' || key === 'search-list') return 'query';
    if (key === 'create') return 'create';
    if (key === 'update' || key === 'state-transition') return 'update';
    if (key === 'delete') return 'delete';
    if (key === 'upload' || key === 'import') return key;
    return 'api';
}

function normalizeOptionsSource(source, options) {
    if (source && typeof source === 'object') {
        return {
            ...source,
            items: Array.isArray(source.items)
                ? normalizeOptionItems(source.items, source.valueField, source.labelField)
                : source.items,
        };
    }
    if (Array.isArray(options)) {
        return { type: 'static', items: normalizeOptionItems(options) };
    }
    return null;
}

function resolveOptionsSourceItems(source, definition) {
    if (!source || typeof source !== 'object') return [];
    if (Array.isArray(source.items)) return source.items;
    const endpoint = source.endpoint || source.name || source.key;
    const fixtureLookups = definition?.fixtures?.lookups || definition?.fixtures?.lookupData || {};
    const fixtureOptions = definition?.fixtures?.options || {};
    const items = fixtureLookups[endpoint] || fixtureOptions[endpoint] || [];
    return Array.isArray(items) ? items : [];
}

function normalizeOptionItems(items, valueField = 'value', labelField = 'label') {
    if (!Array.isArray(items)) return [];
    return items.map((item) => {
        if (item && typeof item === 'object') {
            if ('value' in item && 'label' in item) return { ...item };
            return {
                ...item,
                value: item[valueField] ?? item.value,
                label: item[labelField] ?? item.label,
            };
        }
        return { value: item, label: String(item) };
    });
}

function coerceDateTime(value) {
    if (value instanceof Date && !Number.isNaN(value.getTime())) return value;
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? null : date;
}

function resolvePayloadTemplate(template, context) {
    if (template === undefined) return undefined;
    if (template === null) return null;
    if (typeof template === 'string') {
        if (template.startsWith('$selection.')) {
            const key = template.slice('$selection.'.length);
            return context.selectedRows
                .map((row) => readRowValue(row, key, context.columns))
                .filter((value) => value !== undefined);
        }
        if (template === '$selection') {
            return context.selectedRows.map((row) => cloneJsonValue(row));
        }
        if (template.startsWith('$row.')) {
            return readRowValue(context.row, template.slice('$row.'.length), context.columns);
        }
        if (template === '$row') {
            return cloneJsonValue(context.row);
        }
        if (template.startsWith('$search.')) {
            return context.searchValues?.[template.slice('$search.'.length)];
        }
        return template;
    }
    if (Array.isArray(template)) {
        return template
            .map((value) => resolvePayloadTemplate(value, context))
            .filter((value) => value !== undefined);
    }
    if (typeof template === 'object') {
        const result = {};
        for (const [key, value] of Object.entries(template)) {
            const resolved = resolvePayloadTemplate(value, context);
            if (resolved !== undefined) result[key] = resolved;
        }
        return result;
    }
    return template;
}

function readContextValue(key, context) {
    if (key.startsWith('row.')) return readRowValue(context.row, key.slice('row.'.length), context.columns);
    if (key.startsWith('search.')) return context.searchValues?.[key.slice('search.'.length)];
    return readRowValue(context.row, key, context.columns);
}

function readRowValue(row, key, columns) {
    if (!row) return undefined;
    if (Array.isArray(row)) {
        const index = columns.findIndex((column) => column.fieldName === key || column.key === key);
        return index >= 0 ? row[index] : undefined;
    }
    return row[key];
}

function cloneJsonValue(value) {
    if (value === null || typeof value !== 'object') return value;
    if (Array.isArray(value)) return value.map((item) => cloneJsonValue(item));
    return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, cloneJsonValue(item)]));
}

function isEmptyValue(value) {
    return value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0);
}

function pad2(value) {
    return String(value).padStart(2, '0');
}

export default {
    isQueryDefinition,
    isDeclarativeListDefinition,
    normalizeQueryDefinition,
    getQuerySearchFields,
    getQueryColumns,
    validateQueryDefinition,
    buildQueryPayload,
    getApiActionEntries,
    getDownloadActions,
    getUiActions,
    findApiAction,
    buildDownloadRequest,
    buildActionRequest,
    normalizeTableTextLabels,
    formatTitleTemplate,
    formatTimestamp,
    formatDownloadFileName,
    formatWesternDate,
    formatRocDate,
    formatRocDateTime,
    coerceDate,
    resolveFieldOptions,
    resolveLookupLabel,
    resolveRouteTemplate,
};
