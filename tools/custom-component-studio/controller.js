import {
    CustomComponentRegistry,
    CustomComponentRenderer,
    analyzeCustomComponentDefinition,
    validateCustomComponentDefinition,
} from '../../packages/javascript/browser/custom_components/index.js';
import { ComponentFactory } from '../../packages/javascript/browser/ui_components/binding/index.js';
import Locale from '../../packages/javascript/browser/ui_components/i18n/index.js';

const MAX_IMPORT_BYTES = 1024 * 1024;
const CATALOG_URL = new URL('../../packages/javascript/browser/ui_components/metadata/component-catalog.json', import.meta.url);
const DEFINITIONS_URL = new URL('../../packages/javascript/browser/custom_components/', import.meta.url);

function clone(value) {
    return typeof structuredClone === 'function'
        ? structuredClone(value)
        : JSON.parse(JSON.stringify(value));
}

function createDefaultDefinition() {
    return {
        schema_version: 1,
        component_id: 'custom.text_input',
        registry_name: 'CustomTextInput',
        display_name: 'Custom Text Input',
        version: '1.0.0',
        description: 'Custom TextInput component.',
        kind: 'atomic',
        root: {
            id: 'node-1',
            type: 'component',
            component: 'TextInput',
            options: {},
        },
    };
}

function visitNodes(node, callback, parent = null, index = -1) {
    if (!node || typeof node !== 'object') return;
    callback(node, parent, index);
    if (node.type === 'group' && Array.isArray(node.children)) {
        node.children.forEach((child, childIndex) => visitNodes(child, callback, node, childIndex));
    }
}

function containsUnsafeKeys(value, seen = new Set()) {
    if (!value || typeof value !== 'object') return false;
    if (seen.has(value)) return false;
    seen.add(value);
    for (const key of Object.keys(value)) {
        const normalized = key.toLowerCase();
        if (['__proto__', 'prototype', 'constructor'].includes(normalized)) return true;
        if (containsUnsafeKeys(value[key], seen)) return true;
    }
    return false;
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

function diagnosticText(entry) {
    if (typeof entry === 'string') return entry;
    if (!entry || typeof entry !== 'object') return String(entry);
    const path = entry.path || entry.instancePath || entry.field || '';
    const message = entry.message || entry.error || JSON.stringify(entry);
    return path ? `${path}: ${message}` : String(message);
}

function normalizedValidation(result) {
    const errors = Array.isArray(result?.errors) ? result.errors.map(diagnosticText) : [];
    const warnings = Array.isArray(result?.warnings) ? result.warnings.map(diagnosticText) : [];
    return {
        valid: typeof result?.valid === 'boolean' ? result.valid : errors.length === 0,
        errors,
        warnings,
    };
}

export function createCustomComponentStudioController() {
    const state = {
        definition: createDefaultDefinition(),
        selectedId: 'node-1',
        paletteQuery: '',
        paletteCategory: 'all',
        optionError: '',
        diagnostics: [],
        warnings: [],
        previewError: '',
        status: 'Studio 準備完成。',
        theme: 'light',
        locale: 'zh-TW',
    };

    let renderer = null;
    let registry = null;
    let catalog = [];
    let customDefinitions = [];
    let preview = null;
    let previewBuildId = 0;
    let idCounter = 1;

    const catalogByName = new Map();

    function definitionContext() {
        return {
            builtinNames: Object.keys(ComponentFactory.registry),
            resolveCustom: (reference) => registry?.get?.(reference) || null,
        };
    }

    function findNode(id) {
        let result = null;
        visitNodes(state.definition?.root, (node) => {
            if (!result && node.id === id) result = node;
        });
        return result;
    }

    function findLocation(id) {
        let result = null;
        visitNodes(state.definition?.root, (node, parent, index) => {
            if (!result && node.id === id) result = { node, parent, index };
        });
        return result;
    }

    function resetIdCounter() {
        idCounter = 1;
        visitNodes(state.definition?.root, (node) => {
            const match = /^node-(\d+)$/.exec(node.id || '');
            if (match) idCounter = Math.max(idCounter, Number(match[1]));
        });
    }

    function nextNodeId() {
        const ids = new Set();
        visitNodes(state.definition?.root, (node) => ids.add(node.id));
        let candidate;
        do {
            idCounter += 1;
            candidate = `node-${idCounter}`;
        } while (ids.has(candidate));
        return candidate;
    }

    function createGroup(children = []) {
        return {
            id: nextNodeId(),
            type: 'group',
            layout: { mode: 'stack', gap: 'md', columns: 1, align: 'stretch' },
            children,
        };
    }

    function analyze() {
        return analyzeCustomComponentDefinition(state.definition, definitionContext());
    }

    function validate() {
        const result = normalizedValidation(validateCustomComponentDefinition(state.definition, definitionContext()));
        state.diagnostics = [...result.errors];
        state.warnings = [...result.warnings];
        if (state.optionError) state.diagnostics.push(state.optionError);
        return { ...result, valid: result.valid && !state.optionError };
    }

    function syncKind() {
        const result = analyze();
        if (result?.derived_kind) state.definition.kind = result.derived_kind;
        return result;
    }

    function setStatus(message) {
        state.status = message;
        renderer?.setState?.('custom.status', message);
    }

    function definitionName() {
        const raw = state.definition?.registry_name || state.definition?.component_id || 'custom-component';
        return String(raw).replace(/[^A-Za-z0-9._-]/g, '-') || 'custom-component';
    }

    function serializedDefinition() {
        syncKind();
        return JSON.stringify(state.definition, null, 2);
    }

    function setDefinition(definition) {
        state.definition = clone(definition);
        resetIdCounter();
        state.selectedId = state.definition?.root?.id || null;
        state.optionError = '';
        syncKind();
        syncUi();
    }

    function addNode(node, displayName) {
        if (!state.definition.root) {
            state.definition.root = node;
        } else if (state.definition.root.type === 'group') {
            state.definition.root.children.push(node);
        } else {
            state.definition.root = createGroup([state.definition.root, node]);
        }
        state.selectedId = node.id;
        state.optionError = '';
        syncKind();
        setStatus(`已加入 ${displayName}。`);
        syncUi();
    }

    function addBuiltIn(name) {
        addNode({ id: nextNodeId(), type: 'component', component: name, options: {} }, name);
    }

    function addCustom(name) {
        addNode({ id: nextNodeId(), type: 'custom', component: name, options: {} }, name);
    }

    function addGroup() {
        addNode(createGroup([]), 'Group');
    }

    function moveSelected(delta) {
        const location = findLocation(state.selectedId);
        if (!location?.parent || location.parent.type !== 'group') return;
        const next = location.index + delta;
        if (next < 0 || next >= location.parent.children.length) return;
        const [node] = location.parent.children.splice(location.index, 1);
        location.parent.children.splice(next, 0, node);
        setStatus('節點已移動。');
        syncUi();
    }

    function wrapSelected() {
        const location = findLocation(state.selectedId);
        if (!location) return;
        const group = createGroup([location.node]);
        if (location.parent?.type === 'group') location.parent.children.splice(location.index, 1, group);
        else state.definition.root = group;
        state.selectedId = group.id;
        syncKind();
        setStatus('節點已包成 Group。');
        syncUi();
    }

    function removeSelected() {
        const location = findLocation(state.selectedId);
        if (!location) return;
        if (location.parent?.type === 'group') {
            location.parent.children.splice(location.index, 1);
            state.selectedId = location.parent.id;
        } else {
            state.definition.root = null;
            state.selectedId = null;
        }
        syncKind();
        setStatus('節點已刪除。');
        syncUi();
    }

    function updateMetadata(nodeId, value) {
        const mapping = {
            'metadata-component-id': 'component_id',
            'metadata-registry-name': 'registry_name',
            'metadata-display-name': 'display_name',
            'metadata-version': 'version',
            'metadata-description': 'description',
        };
        const key = mapping[nodeId];
        if (!key) return;
        state.definition[key] = value;
        syncUi({ preview: false });
    }

    function filteredPaletteItems() {
        const query = state.paletteQuery.trim().toLowerCase();
        const builtins = catalog.filter((component) => {
            if (state.paletteCategory !== 'all' && component.category !== state.paletteCategory) return false;
            const haystack = `${component.registry_name} ${component.display_name || ''} ${component.category || ''}`.toLowerCase();
            return !query || haystack.includes(query);
        }).map((component) => ({
            id: `builtin:${component.registry_name}`,
            primary: component.registry_name,
            secondary: `${component.category || 'other'} · ${component.kind || 'atomic'}`,
            source: 'builtin',
            component: component.registry_name,
        }));
        const customs = customDefinitions.filter((definition) => {
            const name = definition.registry_name || definition.component_id || '';
            return !query || `${name} ${definition.display_name || ''}`.toLowerCase().includes(query);
        }).map((definition) => ({
            id: `custom:${definition.registry_name || definition.component_id}`,
            primary: definition.registry_name || definition.component_id,
            secondary: `custom · ${definition.kind || 'atomic'}`,
            source: 'custom',
            component: definition.registry_name || definition.component_id,
        }));
        return [...builtins, ...customs];
    }

    function outlineNode(node) {
        if (!node) return null;
        const label = node.type === 'group'
            ? `Group · ${node.layout?.mode || 'stack'}`
            : `${node.component} · ${node.type}`;
        const result = { id: node.id, label };
        if (node.type === 'group') result.children = node.children.map(outlineNode).filter(Boolean);
        return result;
    }

    function stateSnapshot() {
        const selected = findNode(state.selectedId);
        const validation = validate();
        const categories = [...new Set(catalog.map((entry) => entry.category || 'other'))].sort();
        return {
            custom: {
                theme: state.theme,
                locale: state.locale,
                metadata: {
                    component_id: state.definition.component_id || '',
                    registry_name: state.definition.registry_name || '',
                    display_name: state.definition.display_name || '',
                    version: state.definition.version || '',
                    kind: state.definition.kind || '',
                    description: state.definition.description || '',
                },
                palette: {
                    query: state.paletteQuery,
                    category: state.paletteCategory,
                    categories: [
                        { value: 'all', label: '全部分類' },
                        ...categories.map((value) => ({ value, label: value })),
                    ],
                    items: filteredPaletteItems(),
                    activeId: null,
                },
                outline: {
                    data: state.definition.root ? [outlineNode(state.definition.root)] : [],
                    activeId: state.selectedId,
                },
                inspector: {
                    type: selected?.type || '',
                    component: selected?.component || '',
                    options: selected && selected.type !== 'group' ? JSON.stringify(selected.options || {}, null, 2) : '',
                    mode: selected?.layout?.mode || 'stack',
                    gap: selected?.layout?.gap || 'md',
                    columns: selected?.layout?.columns || 1,
                    align: selected?.layout?.align || 'stretch',
                    ariaLabel: selected?.aria_label || '',
                },
                diagnostics: validation.valid
                    ? [{ id: 'valid', primary: '沒有驗證問題', secondary: state.definition.kind }]
                    : state.diagnostics.map((message, index) => ({ id: `error-${index}`, primary: message, secondary: 'error' })),
                status: state.status,
            },
        };
    }

    function setVisible(id, visible) {
        const component = renderer?.getComponent?.(id);
        if (!component) return;
        if (visible) component.show?.();
        else component.hide?.();
    }

    function setDisabled(id, disabled) {
        renderer?.getComponent?.(id)?.setDisabled?.(disabled);
    }

    function syncUi({ preview: shouldPreview = true } = {}) {
        if (!renderer) return;
        syncKind();
        const snapshot = stateSnapshot();
        const paths = [];
        const flatten = (value, prefix) => {
            if (value && typeof value === 'object' && !Array.isArray(value)) {
                for (const [key, child] of Object.entries(value)) flatten(child, prefix ? `${prefix}.${key}` : key);
            } else {
                paths.push([prefix, value]);
            }
        };
        flatten(snapshot, '');
        paths.forEach(([path, value]) => renderer.setState?.(path, value));

        const selected = findNode(state.selectedId);
        const isGroup = selected?.type === 'group';
        setVisible('inspector-component-name', Boolean(selected && !isGroup));
        setVisible('inspector-options', Boolean(selected && !isGroup));
        ['inspector-mode', 'inspector-gap', 'inspector-columns', 'inspector-align', 'inspector-aria-label']
            .forEach((id) => setVisible(id, Boolean(isGroup)));

        const location = findLocation(state.selectedId);
        setDisabled('outline-up', !location?.parent || location.index <= 0);
        setDisabled('outline-down', !location?.parent || location.index >= (location.parent.children?.length || 0) - 1);
        setDisabled('outline-wrap', !selected);
        setDisabled('outline-remove', !selected);
        if (shouldPreview) rebuildPreview();
    }

    function destroyPreview() {
        previewBuildId += 1;
        preview?.destroy?.();
        preview = null;
        const host = renderer?.getHost?.('component-preview');
        host?.replaceChildren();
    }

    async function rebuildPreview() {
        const buildId = ++previewBuildId;
        preview?.destroy?.();
        preview = null;
        const host = renderer?.getHost?.('component-preview');
        if (!host) return;
        host.replaceChildren();
        state.previewError = '';
        if (!state.definition.root) {
            host.textContent = '加入元件後會在此顯示預覽。';
            return;
        }
        const validation = validate();
        if (!validation.valid) {
            host.textContent = '定義尚未通過驗證，預覽已暫停。';
            return;
        }
        try {
            const instance = new CustomComponentRenderer({
                definition: clone(state.definition),
                registry,
                factory: ComponentFactory,
            });
            instance.mount(host);
            if (buildId !== previewBuildId) {
                instance.destroy();
                return;
            }
            preview = instance;
            if (window.__customComponentStudio) window.__customComponentStudio.preview = instance;
        } catch (error) {
            state.previewError = String(error?.message || error);
            host.textContent = `預覽失敗：${state.previewError}`;
        }
    }

    async function importFile(file, fileCount = 1) {
        if (fileCount !== 1 || !file || !String(file.name || '').toLowerCase().endsWith('.json')) {
            state.diagnostics = ['只能匯入一個 JSON 檔案。'];
            setStatus('只能匯入一個 JSON 檔案。');
            return false;
        }
        if (file.size > MAX_IMPORT_BYTES) {
            state.diagnostics = ['檔案超過 1 MB，已拒絕匯入。'];
            setStatus('檔案超過 1 MB，已拒絕匯入。');
            return false;
        }
        try {
            const parsed = JSON.parse(await file.text());
            if (containsUnsafeKeys(parsed)) throw new Error('JSON 含有不安全的物件鍵。');
            const result = normalizedValidation(validateCustomComponentDefinition(parsed, definitionContext()));
            if (!result.valid) throw new Error(result.errors.join('; '));
            setDefinition(parsed);
            setStatus(`已匯入 ${definitionName()}。`);
            return true;
        } catch (error) {
            const message = String(error?.message || error);
            setStatus(`JSON 匯入失敗：${message}`);
            syncUi({ preview: false });
            state.diagnostics = [message];
            renderer?.setState?.('custom.diagnostics', [{ id: 'import-error', primary: message, secondary: 'error' }]);
            return false;
        }
    }

    function exportJson() {
        const result = validate();
        if (!result.valid) {
            setStatus('定義必須通過驗證才能匯出。');
            syncUi({ preview: false });
            return false;
        }
        const filename = `${definitionName()}.json`;
        downloadText(filename, serializedDefinition());
        setStatus(`已匯出 ${filename}。`);
        return true;
    }

    async function copyJson() {
        try {
            await navigator.clipboard.writeText(serializedDefinition());
            setStatus('已複製 JSON。');
            return true;
        } catch (error) {
            setStatus(`無法寫入剪貼簿：${error?.message || error}`);
            return false;
        }
    }

    function updateSelectedOptions(text) {
        const node = findNode(state.selectedId);
        if (!node || node.type === 'group') return;
        try {
            const parsed = JSON.parse(text || '{}');
            if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('根值必須是物件');
            if (containsUnsafeKeys(parsed)) throw new Error('含有不安全的物件鍵');
            node.options = parsed;
            state.optionError = '';
            syncUi();
        } catch (error) {
            state.optionError = `Options 必須是合法 JSON 物件：${error?.message || error}`;
            syncUi({ preview: false });
        }
    }

    function updateSelectedLayout(nodeId, value) {
        const node = findNode(state.selectedId);
        if (!node || node.type !== 'group') return;
        node.layout ||= { mode: 'stack', gap: 'md', columns: 1, align: 'stretch' };
        const field = {
            'inspector-mode': 'mode',
            'inspector-gap': 'gap',
            'inspector-columns': 'columns',
            'inspector-align': 'align',
        }[nodeId];
        if (field) node.layout[field] = field === 'columns' ? Math.max(1, Math.min(12, Number(value) || 1)) : value;
        syncKind();
        syncUi();
    }

    const commands = {
        'custom.theme': (_context, value) => {
            state.theme = value === 'dark' ? 'dark' : 'light';
            document.documentElement.setAttribute('data-theme', state.theme === 'dark' ? 'dark' : '');
            syncUi({ preview: false });
        },
        'custom.locale': (_context, value) => {
            state.locale = value === 'en' ? 'en' : 'zh-TW';
            Locale.setLang(state.locale);
            syncUi({ preview: false });
        },
        'custom.validate': () => {
            const result = validate();
            setStatus(result.valid ? '定義驗證通過。' : `定義有 ${state.diagnostics.length} 個錯誤。`);
            syncUi({ preview: false });
        },
        'custom.import-json': (_context, files) => importFile(files?.[0], files?.length || 0),
        'custom.export-json': () => exportJson(),
        'custom.copy-json': () => copyJson(),
        'custom.metadata-change': (context, value) => updateMetadata(context.nodeId, value),
        'custom.palette-search': (_context, value) => {
            state.paletteQuery = value || '';
            syncUi({ preview: false });
        },
        'custom.palette-category': (_context, value) => {
            state.paletteCategory = value || 'all';
            syncUi({ preview: false });
        },
        'custom.palette-add': (_context, item) => {
            if (!item) return;
            if (item.source === 'custom') addCustom(item.component);
            else addBuiltIn(item.component || item.primary);
        },
        'custom.outline-select': (_context, node) => {
            state.selectedId = node?.id || null;
            state.optionError = '';
            syncUi({ preview: false });
        },
        'custom.move-up': () => moveSelected(-1),
        'custom.move-down': () => moveSelected(1),
        'custom.wrap': () => wrapSelected(),
        'custom.add-group': () => addGroup(),
        'custom.remove': () => removeSelected(),
        'custom.inspector-options': (_context, value) => updateSelectedOptions(value),
        'custom.inspector-layout': (context, value) => updateSelectedLayout(context.nodeId, value),
        'custom.inspector-aria': (_context, value) => {
            const node = findNode(state.selectedId);
            if (!node || node.type !== 'group') return;
            if (value) node.aria_label = value;
            else delete node.aria_label;
            syncUi();
        },
    };

    async function loadDefinitions() {
        registry = new CustomComponentRegistry({ factory: ComponentFactory, registerWithFactory: false });
        try {
            customDefinitions = await registry.fetchFolderDefinitions(DEFINITIONS_URL.href);
            if (customDefinitions.length) registry.registerMany(customDefinitions);
        } catch (error) {
            console.warn('[CustomComponentStudio] Custom definitions were not loaded:', error);
            customDefinitions = [];
        }
    }

    async function prepare() {
        const response = await fetch(CATALOG_URL);
        if (!response.ok) throw new Error(`Catalog request failed (${response.status}).`);
        const payload = await response.json();
        catalog = Array.isArray(payload?.components) ? payload.components : [];
        catalogByName.clear();
        catalog.forEach((component) => catalogByName.set(component.registry_name, component));
        await loadDefinitions();
        resetIdCounter();
        syncKind();
        return stateSnapshot();
    }

    async function init() {
        await prepare();
        syncUi();
        return api;
    }

    function attachRenderer(nextRenderer) {
        renderer = nextRenderer;
        const upload = renderer?.getComponent?.('component-import-json');
        if (upload?.fileInput) upload.fileInput.id = 'ccs-import-file';
        syncUi();
        api.ready = true;
        return api;
    }

    function destroy() {
        destroyPreview();
        registry?.dispose?.();
        registry = null;
        renderer = null;
    }

    const actions = {
        addBuiltIn,
        addCustom,
        addGroup,
        moveSelected,
        wrapSelected,
        removeSelected,
        selectNode: (id) => {
            state.selectedId = id;
            syncUi({ preview: false });
        },
        setDefinition,
        validate: () => validate(),
        exportJson,
        importFile,
        copyJson,
        rebuildPreview,
    };

    const api = {
        ready: false,
        commands,
        actions,
        state,
        prepare,
        init,
        attachRenderer,
        destroy,
        getDefinition: () => clone(state.definition),
        analyze,
        validate,
        getRegistry: () => registry,
        getCatalog: () => catalog.slice(),
        get catalogCount() { return catalog.length; },
        get registry() { return registry; },
        get renderer() { return renderer; },
        get preview() { return preview; },
        set preview(value) { preview = value; },
    };

    return api;
}

export default createCustomComponentStudioController;
