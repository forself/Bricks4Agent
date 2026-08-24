import { Notification } from '../../packages/javascript/browser/ui_components/common/index.js';
import {
    canonicalizeFormApplication,
    generateFormApplicationBundle,
    normalizeFormApplication,
    schemaToFormApplication,
} from '../../packages/javascript/browser/form-application/index.js';

const SAMPLE_SCHEMA_URL = new URL('./sample-schema.json', import.meta.url);
const MAX_IMPORT_BYTES = 1024 * 1024;

function clone(value) {
    return typeof structuredClone === 'function'
        ? structuredClone(value)
        : JSON.parse(JSON.stringify(value));
}

function toApplicationId(value) {
    const raw = String(value || 'form_application')
        .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
        .replace(/[^A-Za-z0-9_]+/g, '_')
        .replace(/^_+|_+$/g, '')
        .toLowerCase();
    const safe = /^[a-z]/.test(raw) ? raw : `form_${raw || 'application'}`;
    return safe.slice(0, 63);
}

function downloadText(filename, text, type = 'application/json') {
    const blob = new Blob([text], { type });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.hidden = true;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function schemaTableName(schema) {
    return String(schema?.name || schema?.table || '').trim();
}

export function createFormApplicationStudioController() {
    let renderer = null;
    let schemaText = '';
    let currentDefinition = null;
    let lastBundle = null;
    let connectionString = '';
    let selectedProvider = 'sqlite';
    let includeConnectionString = false;
    let selectedFieldId = '';

    function effectiveTarget() {
        const applicationId = currentDefinition?.application_id || 'form_application';
        if (!connectionString.trim()) return `有效目的地：SQLite · data/${applicationId}.db（本地 fallback）`;
        const name = currentDefinition?.persistence?.connection_string_name || 'DefaultConnection';
        return `有效目的地：${selectedProvider} · ConnectionStrings:${name}`;
    }

    function initialState() {
        return {
            form: {
                persistence: { provider: selectedProvider },
                effectiveTarget: effectiveTarget(),
                schemaText,
                definition: clone(currentDefinition),
                status: '尚未產生；目前只建立設計草稿，不會修改資料庫。',
                artifactPreview: JSON.stringify({ files: [], selected_field: selectedFieldId }, null, 2),
            },
        };
    }

    function setState(path, value) {
        renderer?.setState?.(path, value);
    }

    function syncDefinition() {
        setState('form.definition', clone(currentDefinition));
        setState('form.persistence.provider', selectedProvider);
        setState('form.effectiveTarget', effectiveTarget());
    }

    function effectiveDefinition() {
        const candidate = clone(currentDefinition);
        candidate.source = { ...candidate.source, dialect: selectedProvider };
        candidate.persistence = {
            ...candidate.persistence,
            provider: selectedProvider,
            connection_string: connectionString || null,
            sqlite_file: selectedProvider === 'sqlite'
                ? (candidate.persistence?.sqlite_file || `data/${candidate.application_id}.db`)
                : null,
        };
        return normalizeFormApplication(candidate, { connectionString });
    }

    function parseSchema(source = schemaText) {
        const schema = JSON.parse(source);
        const tableName = schemaTableName(schema);
        if (!tableName) throw new Error('schema 必須提供 name 或 table。');
        const applicationId = toApplicationId(schema.application_id || tableName);
        currentDefinition = schemaToFormApplication(schema, {
            applicationId,
            displayName: schema.display_name || tableName,
            provider: connectionString.trim() ? selectedProvider : 'sqlite',
            connectionString,
        });
        lastBundle = null;
        selectedFieldId = currentDefinition.fields.find((field) => !field.primary_key)?.field_id
            || currentDefinition.fields[0]?.field_id
            || '';
        syncDefinition();
        setState('form.status', `已匯入 ${currentDefinition.fields.length} 個欄位；尚未寫入資料庫。`);
        return currentDefinition;
    }

    function updateArtifactPreview(bundle) {
        const preview = {
            application_id: bundle.definition.application_id,
            provider: bundle.sql.provider,
            api: bundle.api,
            page: bundle.pageDefinition.name,
            files: Object.keys(bundle.files),
            includes_connection_string: includeConnectionString && Boolean(connectionString.trim()),
        };
        setState('form.artifactPreview', JSON.stringify(preview, null, 2));
        setState('form.status', `已產生 ${preview.files.length} 個檔案；provider=${preview.provider}，仍未套用資料庫。`);
    }

    function generate() {
        currentDefinition = effectiveDefinition();
        lastBundle = generateFormApplicationBundle(currentDefinition, {
            connectionString,
            includeConnectionString,
        });
        syncDefinition();
        updateArtifactPreview(lastBundle);
        return lastBundle;
    }

    function handleError(prefix, error) {
        const message = error instanceof Error ? error.message : String(error);
        setState('form.status', `${prefix}：${message}`);
        Notification.error(`${prefix}：${message}`);
    }

    const commands = {
        'form.provider-change': (_context, value) => {
            selectedProvider = String(value || 'sqlite').toLowerCase();
            setState('form.persistence.provider', selectedProvider);
            setState('form.effectiveTarget', effectiveTarget());
        },
        'form.connection-change': (_context, value) => {
            connectionString = String(value || '');
            setState('form.effectiveTarget', effectiveTarget());
        },
        'form.include-secret-change': (_context, checked) => {
            includeConnectionString = Boolean(checked);
        },
        'form.schema-input': (_context, value) => {
            schemaText = String(value || '');
            setState('form.schemaText', schemaText);
        },
        'form.schema-apply': () => {
            try {
                parseSchema();
                Notification.success('Schema 已轉為表單設計草稿。');
            } catch (error) {
                handleError('Schema 解析失敗', error);
            }
        },
        'form.schema-import': async (_context, files) => {
            const file = files?.[0];
            if (!file) return;
            if (files.length !== 1 || !String(file.name || '').toLowerCase().endsWith('.json') || file.size > MAX_IMPORT_BYTES) {
                Notification.error('只接受一個 1 MB 以下的 JSON 檔。');
                return;
            }
            try {
                const next = await file.text();
                JSON.parse(next);
                schemaText = next;
                setState('form.schemaText', schemaText);
                parseSchema(next);
                Notification.success('Schema JSON 已匯入。');
            } catch (error) {
                handleError('Schema 匯入失敗', error);
            }
        },
        'form.reset': async () => {
            const response = await fetch(SAMPLE_SCHEMA_URL);
            if (!response.ok) return handleError('重設失敗', new Error(`HTTP ${response.status}`));
            schemaText = await response.text();
            selectedProvider = 'sqlite';
            connectionString = '';
            includeConnectionString = false;
            parseSchema(schemaText);
            setState('form.schemaText', schemaText);
            renderer?.getComponent?.('connection-string')?.clear?.();
            renderer?.getComponent?.('include-secret')?.setChecked?.(false);
        },
        'form.design-change': (_context, value) => {
            try {
                currentDefinition = normalizeFormApplication(value, { connectionString });
                lastBundle = null;
                setState('form.status', '設計已更新；請重新產生產物。');
            } catch (error) {
                handleError('設計更新失敗', error);
            }
        },
        'form.design-select': (_context, fieldOrId) => {
            selectedFieldId = String(fieldOrId?.field_id || fieldOrId || '');
        },
        'form.add-field': () => {
            try {
                renderer?.getComponent?.('form-designer')?.addField?.();
            } catch (error) {
                handleError('新增欄位失敗', error);
            }
        },
        'form.generate': () => {
            try {
                generate();
                renderer?.getComponent?.('workflow-tabs')?.activateTab?.('artifacts');
                Notification.success('表單、API 與資料表產物已建立。');
            } catch (error) {
                handleError('產生失敗', error);
            }
        },
        'form.export-design': () => {
            try {
                const definition = effectiveDefinition();
                downloadText(
                    `${definition.application_id}.form-application.json`,
                    canonicalizeFormApplication(definition, { connectionString }),
                );
            } catch (error) {
                handleError('設計匯出失敗', error);
            }
        },
        'form.export-bundle': () => {
            try {
                const bundle = lastBundle || generate();
                downloadText(`${bundle.definition.application_id}.bundle.json`, `${JSON.stringify(bundle, null, 2)}\n`);
            } catch (error) {
                handleError('Bundle 匯出失敗', error);
            }
        },
    };

    async function prepare() {
        const response = await fetch(SAMPLE_SCHEMA_URL);
        if (!response.ok) throw new Error(`Sample schema request failed (${response.status}).`);
        schemaText = await response.text();
        const schema = JSON.parse(schemaText);
        const tableName = schemaTableName(schema);
        currentDefinition = schemaToFormApplication(schema, {
            applicationId: toApplicationId(tableName),
            displayName: schema.display_name || tableName,
            provider: 'sqlite',
            connectionString: '',
        });
        return initialState();
    }

    function attachRenderer(nextRenderer) {
        renderer = nextRenderer;
        const upload = renderer.getComponent?.('schema-import');
        if (upload?.fileInput) upload.fileInput.id = 'form-schema-import-file';
        syncDefinition();
        return api;
    }

    function destroy() {
        connectionString = '';
        currentDefinition = null;
        lastBundle = null;
        renderer = null;
    }

    const api = {
        commands,
        prepare,
        attachRenderer,
        destroy,
        getDefinition: () => clone(currentDefinition),
        getBundle: () => clone(lastBundle),
        generate,
        parseSchema,
        get effectiveTarget() { return effectiveTarget(); },
        get selectedFieldId() { return selectedFieldId; },
    };

    return api;
}

export default createFormApplicationStudioController;
