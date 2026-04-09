import fs from 'node:fs';
import path from 'node:path';

import { introspectBrowserMetadata } from './introspection.js';
import { validateManifestMap } from './manifest-schema.js';

const DEFAULT_PAGE_TYPES = ['form', 'detail', 'list'];
const DASHBOARD_PAGE_TYPES = ['form', 'detail', 'list', 'dashboard'];
const STYLE_KNOB_ALLOWLIST = new Set([
    'size',
    'width',
    'height',
    'disabled',
    'readonly',
    'required',
    'error',
    'hint',
    'variant',
    'theme',
    'layout',
    'bordered',
    'striped',
    'hoverable',
    'selectable',
    'multiSelect',
    'pageSize',
    'loading',
]);

const KIND_OVERRIDES = {
    ActionButton: 'composite',
    AuthButton: 'composite',
    Breadcrumb: 'composite',
    ButtonGroup: 'composite',
    Pagination: 'composite',
    SearchForm: 'composite',
    BatchUploader: 'composite',
    TreeList: 'composite',
    DateTimeInput: 'composite',
    AddressInput: 'composite',
    AddressListInput: 'composite',
    ChainedInput: 'composite',
    ListInput: 'composite',
    OrganizationInput: 'composite',
    PersonInfoList: 'composite',
    PhoneListInput: 'composite',
    SocialMediaList: 'composite',
    StudentInput: 'composite',
    WebTextEditor: 'composite',
    DataTable: 'container',
    DocumentWall: 'container',
    FormRow: 'container',
    FunctionMenu: 'container',
    InfoPanel: 'container',
    PanelManager: 'container',
    PhotoWall: 'container',
    SideMenu: 'container',
    TabContainer: 'container',
    WorkflowPanel: 'container',
    RegionMap: 'visualizer',
    Avatar: 'atomic',
    ConnectionCard: 'composite',
    FeedCard: 'composite',
    StatCard: 'composite',
    Timeline: 'container',
    DrawingBoard: 'visualizer',
    OSMMapEditor: 'visualizer',
    WebPainter: 'visualizer',
};

const ROLE_OVERRIDES = {
    ActionButton: 'action',
    AuthButton: 'action',
    BasicButton: 'action',
    SortButton: 'action',
    UploadButton: 'action',
    DownloadButton: 'action',
    EditorButton: 'action',
    Breadcrumb: 'navigation',
    Pagination: 'navigation',
    SearchForm: 'search',
    LoadingSpinner: 'feedback',
    Notification: 'feedback',
    Progress: 'feedback',
    ColorPicker: 'input',
    ImageViewer: 'display',
    TreeList: 'display',
    FeatureCard: 'display',
    PhotoCard: 'display',
    Badge: 'display',
    Tag: 'display',
    Tooltip: 'display',
    Divider: 'display',
    DataTable: 'data_view',
    DocumentWall: 'container',
    FormRow: 'container',
    FunctionMenu: 'container',
    InfoPanel: 'container',
    PanelManager: 'container',
    PhotoWall: 'container',
    SideMenu: 'navigation',
    TabContainer: 'container',
    WorkflowPanel: 'workflow',
    Avatar: 'display',
    ConnectionCard: 'display',
    FeedCard: 'display',
    StatCard: 'display',
    Timeline: 'display',
    WebTextEditor: 'editor',
    RegionMap: 'visualizer',
    DrawingBoard: 'visualizer',
    OSMMapEditor: 'visualizer',
    WebPainter: 'visualizer',
    GeolocationService: 'service',
    WeatherService: 'service',
};

const RUNTIME_ONLY_COMPONENTS = new Set(['PanelManager']);
const MANUAL_ONLY_COMPONENTS = new Set([
    'BarChart',
    'BaseChart',
    'CanvasMap',
    'ConnectionCard',
    'DocumentWall',
    'FeatureCard',
    'FeedCard',
    'FlameChart',
    'FunctionMenu',
    'HierarchyChart',
    'InfoPanel',
    'LeafletMap',
    'LineChart',
    'MapEditor',
    'MapEditorV2',
    'Notification',
    'OrgChart',
    'OSMMapEditor',
    'PanelManager',
    'PhotoCard',
    'PhotoWall',
    'PieChart',
    'Progress',
    'RegionMap',
    'RelationChart',
    'RoseChart',
    'SankeyChart',
    'SideMenu',
    'StatCard',
    'SunburstChart',
    'TabContainer',
    'Timeline',
    'TimelineChart',
    'TreeList',
    'UploadButton',
    'WebPainter',
    'WorkflowPanel',
]);

function toComponentId(category, registryName) {
    const snake = registryName
        .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
        .replace(/([A-Z]+)([A-Z][a-z])/g, '$1_$2')
        .toLowerCase();

    return `${category}.${snake}`;
}

function inferKind(category, registryName) {
    if (KIND_OVERRIDES[registryName]) {
        return KIND_OVERRIDES[registryName];
    }

    if (category === 'layout') return 'container';
    if (category === 'viz' || category === 'data') return 'visualizer';
    if (category === 'input' || category === 'editor' || category === 'social') return 'composite';
    return 'atomic';
}

function inferRole(category, registryName) {
    if (ROLE_OVERRIDES[registryName]) {
        return ROLE_OVERRIDES[registryName];
    }

    if (category === 'form' || category === 'input') return 'input';
    if (category === 'layout') return 'container';
    if (category === 'viz' || category === 'data') return 'visualizer';
    if (category === 'editor') return 'editor';
    if (category === 'social') return 'display';
    return 'display';
}

function inferUsageMode(registryName, supportedFieldTypes, definitionExplicitNames) {
    if (supportedFieldTypes.length > 0) {
        return 'field_direct';
    }

    if (RUNTIME_ONLY_COMPONENTS.has(registryName)) {
        return 'runtime_only';
    }

    if (definitionExplicitNames.includes(registryName)) {
        return 'definition_explicit';
    }

    if (MANUAL_ONLY_COMPONENTS.has(registryName)) {
        return 'manual_only';
    }

    return 'manual_only';
}

function inferPageTypes(category, usageMode) {
    if (usageMode === 'manual_only') {
        return [];
    }

    if (usageMode === 'runtime_only') {
        return [];
    }

    if (category === 'layout' || category === 'data' || category === 'viz' || category === 'social') {
        return DASHBOARD_PAGE_TYPES;
    }

    return DEFAULT_PAGE_TYPES;
}

function inferMaturity(location) {
    return location?.docs_path ? 'stable' : 'beta';
}

function inferStyleKnobs(optionKeys, role) {
    const knobs = optionKeys.filter((key) => STYLE_KNOB_ALLOWLIST.has(key));

    if (role === 'input') {
        for (const required of ['disabled', 'readonly', 'required']) {
            if (!knobs.includes(required)) {
                knobs.push(required);
            }
        }
    }

    return [...new Set(knobs)].sort();
}

function inferBinding(sourceText, requiresWrapper, listenerEvents, role) {
    const hasGetValue = /getValue\s*\(/.test(sourceText);
    const hasSetValue = /setValue\s*\(/.test(sourceText);
    const hasClear = /clear\s*\(/.test(sourceText);
    const hasSetItems = /setItems\s*\(/.test(sourceText);
    const hasReload = /(?:reload|refresh)\s*\(/.test(sourceText);

    const targetActions = [];
    if (hasClear) targetActions.push('clear');
    if (hasSetValue) targetActions.push('setValue');
    if (hasReload) targetActions.push('reload');
    if (hasSetItems) targetActions.push('reloadOptions');
    if (requiresWrapper) {
        targetActions.push('setReadonly', 'setRequired');
    }

    return {
        value_io: hasGetValue && hasSetValue,
        listener_events: listenerEvents,
        target_actions: [...new Set(targetActions)].sort(),
        role,
    };
}

export function createManifestSkeleton(introspection, componentName) {
    const location = introspection.componentLocations[componentName];
    if (!location) {
        throw new Error(`Unable to resolve source file for component: ${componentName}`);
    }

    const supportedFieldTypes = Object.entries(introspection.fieldTypeSupport)
        .filter(([, support]) => support.default_component === componentName)
        .map(([fieldType]) => fieldType)
        .sort();

    const usageMode = inferUsageMode(componentName, supportedFieldTypes, introspection.definitionExplicitNames);
    const role = inferRole(location.category, componentName);
    const requiresFormFieldWrapper = role === 'input' || usageMode === 'field_direct';
    const binding = inferBinding(
        location.source_text,
        requiresFormFieldWrapper,
        location.listener_events,
        role,
    );

    return {
        schema_version: 1,
        component_id: toComponentId(location.category, componentName),
        registry_name: componentName,
        display_name: componentName,
        category: location.category,
        kind: inferKind(location.category, componentName),
        source_path: location.source_path,
        docs_path: location.docs_path,
        maturity: inferMaturity(location),
        generator: {
            usable: usageMode === 'field_direct' || usageMode === 'definition_explicit',
            usage_mode: usageMode,
            supported_field_types: supportedFieldTypes,
            supported_page_types: inferPageTypes(location.category, usageMode),
            definition_runtime: usageMode !== 'manual_only',
        },
        composition: {
            role,
            requires_form_field_wrapper: requiresFormFieldWrapper,
            manual_only: usageMode === 'manual_only',
        },
        binding: {
            value_io: binding.value_io,
            listener_events: binding.listener_events,
            target_actions: binding.target_actions,
        },
        styling: {
            theme_token_only: true,
            style_knobs: inferStyleKnobs(location.option_keys, role),
        },
    };
}

export function resolveManifestRelativePath(location, componentName) {
    const sourceDir = path.dirname(location.source_path).replaceAll('\\', '/');
    const sourceFileName = path.basename(location.source_path, '.js');
    const sourceDirName = path.basename(sourceDir);

    if (sourceFileName === componentName && sourceDirName === componentName) {
        return `${sourceDir}/component.manifest.json`;
    }

    return `${sourceDir}/${componentName}.manifest.json`;
}

function readManifest(absolutePath) {
    return JSON.parse(fs.readFileSync(absolutePath, 'utf8'));
}

export function loadManifestMap(browserRoot, introspection) {
    const manifestMap = {};

    for (const componentName of Object.keys(introspection.componentLocations).sort()) {
        const location = introspection.componentLocations[componentName];
        if (!location) {
            continue;
        }

        const manifestPath = path.join(
            browserRoot,
            resolveManifestRelativePath(location, componentName),
        );
        if (!fs.existsSync(manifestPath)) {
            continue;
        }

        manifestMap[componentName] = readManifest(manifestPath);
    }

    return manifestMap;
}

export function renderComponentCatalog(manifestMap) {
    const components = Object.values(manifestMap)
        .sort((left, right) => left.component_id.localeCompare(right.component_id));

    const byCategory = {};
    const byKind = {};
    const byMaturity = {};
    const byUsageMode = {};
    const byRegistryName = {};

    for (const manifest of components) {
        byRegistryName[manifest.registry_name] = manifest;
        byCategory[manifest.category] ??= [];
        byKind[manifest.kind] ??= [];
        byMaturity[manifest.maturity] ??= [];
        byUsageMode[manifest.generator.usage_mode] ??= [];

        byCategory[manifest.category].push(manifest.registry_name);
        byKind[manifest.kind].push(manifest.registry_name);
        byMaturity[manifest.maturity].push(manifest.registry_name);
        byUsageMode[manifest.generator.usage_mode].push(manifest.registry_name);
    }

    for (const group of [byCategory, byKind, byMaturity, byUsageMode]) {
        for (const key of Object.keys(group)) {
            group[key].sort();
        }
    }

    return {
        schema_version: 1,
        component_count: components.length,
        components,
        by_registry_name: byRegistryName,
        by_category: byCategory,
        by_kind: byKind,
        by_maturity: byMaturity,
        by_usage_mode: byUsageMode,
    };
}

function resolveFieldTypeStatus(fieldType, support) {
    if (support.default_component && support.in_catalog) {
        return 'supported';
    }

    if (fieldType === 'hidden' || fieldType === 'textarea') {
        return 'wrapped';
    }

    return 'out_of_catalog';
}

export function renderGeneratorSupportMatrix(manifestMap, introspection) {
    const byRegistry = Object.fromEntries(
        Object.values(manifestMap).map((manifest) => [manifest.registry_name, manifest]),
    );

    const fieldTypeSupport = {};
    for (const [fieldType, support] of Object.entries(introspection.fieldTypeSupport)) {
        const defaultComponentInCatalog = Boolean(
            support.default_component && byRegistry[support.default_component],
        );
        const alternatives = Object.values(manifestMap)
            .filter((manifest) => manifest.generator.usage_mode === 'field_direct')
            .filter((manifest) => manifest.generator.supported_field_types.includes(fieldType))
            .map((manifest) => manifest.registry_name)
            .filter((registryName) => registryName !== support.default_component)
            .sort();

        fieldTypeSupport[fieldType] = {
            default_component: support.default_component,
            alternative_components: alternatives,
            status: resolveFieldTypeStatus(fieldType, {
                ...support,
                in_catalog: defaultComponentInCatalog,
            }),
        };
    }

    const triggerSupport = {};
    for (const action of introspection.triggerActions) {
        const matchingManifests = Object.values(manifestMap)
            .filter((manifest) => manifest.binding.target_actions.includes(action));

        triggerSupport[action] = {
            component_roles: [...new Set(matchingManifests.map((manifest) => manifest.composition.role))].sort(),
            usage_modes: [...new Set(matchingManifests.map((manifest) => manifest.generator.usage_mode))].sort(),
        };
    }

    return {
        schema_version: 1,
        field_type_support: fieldTypeSupport,
        page_type_support: {
            form: { status: 'supported' },
            detail: { status: 'supported' },
            list: { status: 'supported' },
            dashboard: { status: 'partial' },
        },
        trigger_support: triggerSupport,
        manual_only_components: Object.values(byRegistry)
            .filter((manifest) => manifest.generator.usage_mode === 'manual_only')
            .map((manifest) => manifest.registry_name)
            .sort(),
        runtime_only_components: Object.values(byRegistry)
            .filter((manifest) => manifest.generator.usage_mode === 'runtime_only')
            .map((manifest) => manifest.registry_name)
            .sort(),
    };
}

export function buildMetadataArtifacts(browserRoot) {
    const introspection = introspectBrowserMetadata(browserRoot);
    const manifestMap = loadManifestMap(browserRoot, introspection);
    const validation = validateManifestMap(manifestMap, introspection, browserRoot);
    const componentCatalog = renderComponentCatalog(manifestMap);
    const generatorSupportMatrix = renderGeneratorSupportMatrix(manifestMap, introspection);

    return {
        introspection,
        manifestMap,
        validation,
        componentCatalog,
        generatorSupportMatrix,
    };
}

export function writeJsonFile(targetPath, value) {
    fs.writeFileSync(targetPath, `${JSON.stringify(value, null, 2)}\n`, 'utf8');
}
