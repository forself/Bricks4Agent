import { ComponentFactory } from '../ui_components/binding/ComponentFactory.js';
import { validateCustomComponentDefinition } from './CustomComponentDefinition.js';
import { CustomComponentRenderer } from './CustomComponentRenderer.js';

const SAFE_JSON_PATH = /^[A-Za-z0-9._/-]+\.json$/i;
const PROTOTYPE_KEYS = new Set(['__proto__', 'prototype', 'constructor']);

function isRecord(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

function cloneDefinition(value, seen = new WeakMap()) {
    if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
    if (typeof value === 'number') return Number.isFinite(value) ? value : null;
    if (typeof value !== 'object') return undefined;
    if (seen.has(value)) return seen.get(value);

    if (Array.isArray(value)) {
        const result = [];
        seen.set(value, result);
        for (const entry of value) {
            const cloned = cloneDefinition(entry, seen);
            if (cloned !== undefined) result.push(cloned);
        }
        return result;
    }

    if (!isRecord(value)) return undefined;
    const result = {};
    seen.set(value, result);
    for (const key of Reflect.ownKeys(value)) {
        if (typeof key !== 'string' || PROTOTYPE_KEYS.has(key)) continue;
        const descriptor = Object.getOwnPropertyDescriptor(value, key);
        if (!descriptor || descriptor.get || descriptor.set) continue;
        const cloned = cloneDefinition(descriptor.value, seen);
        if (cloned !== undefined) result[key] = cloned;
    }
    return result;
}

function own(object, key) {
    return Object.prototype.hasOwnProperty.call(object, key);
}

function dataProperty(object, key) {
    if (!isRecord(object)) return undefined;
    const descriptor = Object.getOwnPropertyDescriptor(object, key);
    return descriptor && !descriptor.get && !descriptor.set ? descriptor.value : undefined;
}

function factoryEntries(factory) {
    const registry = factory?.registry;
    if (registry instanceof Map) return [...registry.entries()];
    if (registry && typeof registry === 'object') return Object.entries(registry);
    return [];
}

function factoryHas(factory, name) {
    const registry = factory?.registry;
    if (registry instanceof Map) return registry.has(name);
    return Boolean(registry && typeof registry === 'object' && own(registry, name));
}

function factoryGet(factory, name) {
    const registry = factory?.registry;
    if (registry instanceof Map) return registry.get(name);
    return registry && typeof registry === 'object' ? registry[name] : undefined;
}

function factorySet(factory, name, componentClass) {
    if (typeof factory?.register === 'function') {
        factory.register(name, componentClass);
        return;
    }
    const registry = factory?.registry;
    if (registry instanceof Map) {
        registry.set(name, componentClass);
        return;
    }
    if (registry && typeof registry === 'object') {
        registry[name] = componentClass;
        return;
    }
    throw new TypeError('Component factory must expose register() or a mutable registry.');
}

function factoryRestore(factory, name, snapshot) {
    const registry = factory?.registry;
    if (registry instanceof Map) {
        if (snapshot.exists) registry.set(name, snapshot.value);
        else registry.delete(name);
        return;
    }
    if (registry && typeof registry === 'object') {
        if (snapshot.exists) registry[name] = snapshot.value;
        else delete registry[name];
        return;
    }
    if (snapshot.exists && typeof factory?.register === 'function') {
        factory.register(name, snapshot.value);
        return;
    }
    throw new Error(`Cannot roll back component factory registration for "${name}".`);
}

function factoryDeleteOwned(factory, name, ownedClass) {
    if (factoryGet(factory, name) !== ownedClass) return false;

    const registry = factory?.registry;
    if (registry instanceof Map) {
        registry.delete(name);
        return !registry.has(name);
    }
    if (registry && typeof registry === 'object') {
        delete registry[name];
        return !own(registry, name);
    }
    throw new Error(`Cannot uninstall component factory registration for "${name}".`);
}

function errorMessages(errors) {
    return (Array.isArray(errors) ? errors : []).map((error) => {
        if (typeof error === 'string') return error;
        const location = error?.path ? ` at ${error.path}` : '';
        return `${error?.code ?? 'INVALID_DEFINITION'}${location}: ${error?.message ?? 'Invalid definition'}`;
    });
}

function registryError(message, errors = []) {
    const detail = errorMessages(errors);
    const error = new Error(detail.length > 0 ? `${message}: ${detail.join('; ')}` : message);
    error.errors = errors;
    return error;
}

function assertSafeRelativeJsonPath(path, label) {
    if (typeof path !== 'string' || path.length === 0) {
        throw new TypeError(`${label} must be a non-empty relative JSON path.`);
    }
    if (
        path.includes('\\') ||
        path.includes('%') ||
        path.includes('?') ||
        path.includes('#') ||
        path.startsWith('/') ||
        path.startsWith('//') ||
        /^[A-Za-z]:/.test(path) ||
        /^[A-Za-z][A-Za-z0-9+.-]*:/.test(path) ||
        !SAFE_JSON_PATH.test(path)
    ) {
        throw new Error(`${label} must be a safe relative .json path.`);
    }

    const segments = path.split('/');
    if (segments.some((segment) => segment === '' || segment === '.' || segment === '..')) {
        throw new Error(`${label} cannot contain empty, current-directory, or parent-directory segments.`);
    }
    return path;
}

function folderUrl(baseUrl) {
    const fallback = globalThis.location?.href;
    let url;
    try {
        url = fallback ? new URL(String(baseUrl), fallback) : new URL(String(baseUrl));
    } catch {
        throw new TypeError('baseUrl must be an absolute URL when no browser location is available.');
    }
    url.search = '';
    url.hash = '';
    if (!url.pathname.endsWith('/')) url.pathname += '/';
    return url;
}

function extractManifestEntries(manifest) {
    if (Array.isArray(manifest)) return manifest;
    if (!isRecord(manifest)) {
        throw new Error('Custom component folder manifest must be an object or array.');
    }
    if (Array.isArray(manifest.components)) return manifest.components;
    if (Array.isArray(manifest.definitions)) return manifest.definitions;
    if (isRecord(manifest.by_registry_name)) return Object.values(manifest.by_registry_name);
    throw new Error('Custom component folder manifest must expose components, definitions, or by_registry_name.');
}

function rendererOptions(options, registry, definition) {
    const source = isRecord(options) ? options : {};
    const explicitNodeOptions = source.nodeOptions ?? source.node_options;
    const nodeOptions = explicitNodeOptions ?? source;
    const values = own(source, 'values') ? source.values : (own(source, 'value') ? source.value : undefined);
    return {
        definition,
        registry,
        factory: source.factory ?? registry.factory,
        nodeOptions,
        values,
    };
}

/**
 * In-memory registry for validated JSON-defined custom components.
 */
export class CustomComponentRegistry {
    constructor({
        factory = ComponentFactory,
        fetchImpl = globalThis.fetch,
        registerWithFactory = true,
    } = {}) {
        this.factory = factory;
        this.fetchImpl = fetchImpl;
        this.registerWithFactory = registerWithFactory !== false;

        this._byRegistryName = new Map();
        this._byComponentId = new Map();
        this._factoryClasses = new Map();
        this._disposed = false;
    }

    has(reference) {
        return this._byRegistryName.has(reference) || this._byComponentId.has(reference);
    }

    get(reference) {
        const definition = this._resolveDefinition(reference);
        return definition ? cloneDefinition(definition) : null;
    }

    list() {
        return [...this._byRegistryName.values()]
            .sort((left, right) => left.registry_name.localeCompare(right.registry_name))
            .map((definition) => cloneDefinition(definition));
    }

    getAll() {
        return this.list();
    }

    entries() {
        return this.list().map((definition) => [definition.registry_name, definition]);
    }

    get definitions() {
        return this.list();
    }

    register(definition) {
        return this.registerMany([definition])[0];
    }

    registerMany(definitions) {
        this._assertActive();
        if (!Array.isArray(definitions)) {
            throw new TypeError('registerMany(definitions) requires an array.');
        }
        if (definitions.length === 0) return [];

        const builtinNames = this._currentBuiltinNames();
        this._validateDefinitionGraph(definitions, { builtinNames });

        const incoming = definitions.map((definition) => cloneDefinition(definition));
        if (incoming.some((definition) => !definition)) {
            throw new TypeError('Every custom component definition must be a plain JSON object.');
        }

        const nextByName = new Map(this._byRegistryName);
        const nextById = new Map(this._byComponentId);
        const batchNames = new Set();
        const batchIds = new Set();

        for (const definition of incoming) {
            const name = definition.registry_name;
            const componentId = definition.component_id;

            if (typeof name === 'string' && builtinNames.has(name)) {
                throw registryError(`Custom component registry_name collides with built-in "${name}".`);
            }
            if (typeof componentId === 'string' && builtinNames.has(componentId)) {
                throw registryError(`Custom component component_id collides with built-in "${componentId}".`);
            }
            if (batchNames.has(name)) {
                throw registryError(`Duplicate custom component registry_name "${name}" in batch.`);
            }
            if (batchIds.has(componentId)) {
                throw registryError(`Duplicate custom component component_id "${componentId}" in batch.`);
            }

            const existingByName = nextByName.get(name);
            const existingById = nextById.get(componentId);
            if (existingByName && existingByName.component_id !== componentId) {
                throw registryError(`registry_name "${name}" is already owned by another component_id.`);
            }
            if (existingById && existingById.registry_name !== name) {
                throw registryError(`component_id "${componentId}" is already owned by another registry_name.`);
            }

            batchNames.add(name);
            batchIds.add(componentId);
            nextByName.set(name, definition);
            nextById.set(componentId, definition);
        }

        this._validateDefinitionGraph([...nextByName.values()], {
            builtinNames,
            includeExisting: false,
        });

        const nextFactoryClasses = new Map(this._factoryClasses);
        const preparedClasses = incoming.map((definition) => ({
            definition,
            name: definition.registry_name,
            componentClass: this._createFactoryClass(definition),
        }));

        if (this.registerWithFactory) {
            this._installFactoryClasses(preparedClasses);
            for (const entry of preparedClasses) {
                nextFactoryClasses.set(entry.name, entry.componentClass);
            }
        }

        this._byRegistryName = nextByName;
        this._byComponentId = nextById;
        this._factoryClasses = nextFactoryClasses;
        return incoming.map((definition) => cloneDefinition(definition));
    }

    create(reference, options = {}) {
        this._assertActive();
        const definition = this._resolveDefinition(reference);
        if (!definition) {
            throw new Error(`Custom component "${reference}" is not registered.`);
        }
        return new CustomComponentRenderer(rendererOptions(options, this, definition));
    }

    installFieldResolver(fieldResolver) {
        this._assertActive();
        if (!fieldResolver || typeof fieldResolver.registerComponent !== 'function') {
            throw new TypeError('installFieldResolver requires a resolver with registerComponent().');
        }

        for (const definition of this._byRegistryName.values()) {
            const createFromField = (fieldDefinition = {}) => {
                let values;
                if (own(fieldDefinition, 'value')) values = fieldDefinition.value;
                else if (own(fieldDefinition, 'defaultValue')) values = fieldDefinition.defaultValue;
                else values = fieldDefinition.default;

                return this.create(definition.registry_name, {
                    nodeOptions:
                        fieldDefinition.componentOptions ??
                        fieldDefinition.nodeOptions ??
                        fieldDefinition.node_options ??
                        fieldDefinition.config ??
                        {},
                    values,
                });
            };
            fieldResolver.registerComponent(definition.registry_name, createFromField);
            fieldResolver.registerComponent(definition.component_id, createFromField);
        }
        return fieldResolver;
    }

    async loadDefinition(url) {
        this._assertActive();
        const definition = await this._fetchJson(url, 'custom component definition');
        return this.register(definition);
    }

    async loadFolder(baseUrl, { manifest = 'registry.json' } = {}) {
        const definitions = await this.fetchFolderDefinitions(baseUrl, { manifest });
        return this.registerMany(definitions);
    }

    async fetchFolderDefinitions(baseUrl, {
        manifest = 'registry.json',
        additionalDefinitions = [],
    } = {}) {
        this._assertActive();
        if (!Array.isArray(additionalDefinitions)) {
            throw new TypeError('additionalDefinitions must be an array.');
        }
        const safeManifest = assertSafeRelativeJsonPath(manifest, 'manifest');
        const base = folderUrl(baseUrl);
        const manifestUrl = new URL(safeManifest, base);
        const manifestDefinition = await this._fetchJson(manifestUrl, 'custom component registry manifest');
        const entries = extractManifestEntries(manifestDefinition);

        const fetched = await Promise.all(entries.map(async (entry, index) => {
            if (isRecord(entry) && entry.root) return entry;
            if (isRecord(entry) && isRecord(entry.definition)) return entry.definition;

            const path = typeof entry === 'string' ? entry : entry?.path;
            const safePath = assertSafeRelativeJsonPath(path, `manifest entry ${index} path`);
            const definitionUrl = new URL(safePath, manifestUrl);
            const definition = await this._fetchJson(definitionUrl, `custom component definition ${index}`);

            if (isRecord(entry)) {
                if (entry.registry_name && entry.registry_name !== definition?.registry_name) {
                    throw new Error(`Manifest registry_name mismatch for "${safePath}".`);
                }
                if (entry.component_id && entry.component_id !== definition?.component_id) {
                    throw new Error(`Manifest component_id mismatch for "${safePath}".`);
                }
            }
            return definition;
        }));

        this._validateDefinitionGraph([...additionalDefinitions, ...fetched]);
        return fetched.map((definition) => cloneDefinition(definition));
    }

    uninstallFactoryClasses() {
        const errors = [];
        for (const [name, ownedClass] of this._factoryClasses) {
            try {
                const current = factoryGet(this.factory, name);
                if (current === ownedClass && !factoryDeleteOwned(this.factory, name, ownedClass)) {
                    throw new Error(`Factory retained owned custom component "${name}" during uninstall.`);
                }
                this._factoryClasses.delete(name);
            } catch (error) {
                errors.push(error);
            }
        }
        if (errors.length > 0) {
            const error = new Error(`Failed to uninstall ${errors.length} custom component factory registration(s).`);
            error.uninstallErrors = errors;
            throw error;
        }
        return this;
    }

    dispose() {
        if (this._disposed) return;
        this.uninstallFactoryClasses();
        this._byRegistryName.clear();
        this._byComponentId.clear();
        this._disposed = true;
    }

    _resolveDefinition(reference) {
        return this._byRegistryName.get(reference) ?? this._byComponentId.get(reference) ?? null;
    }

    _assertActive() {
        if (this._disposed) {
            throw new Error('CustomComponentRegistry has been disposed.');
        }
    }

    _validateDefinitionGraph(definitions, {
        builtinNames = this._currentBuiltinNames(),
        includeExisting = true,
    } = {}) {
        const byName = includeExisting ? new Map(this._byRegistryName) : new Map();
        const byId = includeExisting ? new Map(this._byComponentId) : new Map();
        for (const definition of definitions) {
            const name = dataProperty(definition, 'registry_name');
            const componentId = dataProperty(definition, 'component_id');
            if (typeof name === 'string') byName.set(name, definition);
            if (typeof componentId === 'string') byId.set(componentId, definition);
        }

        const resolveCustom = (reference) => byName.get(reference) ?? byId.get(reference) ?? null;
        const graphDefinitions = new Set([...byName.values(), ...definitions]);
        for (const definition of graphDefinitions) {
            const validation = validateCustomComponentDefinition(definition, {
                builtinNames,
                resolveCustom,
            });
            if (!validation?.valid) {
                throw registryError(
                    `Custom component "${dataProperty(definition, 'registry_name') ?? '(anonymous)'}" failed validation`,
                    validation?.errors,
                );
            }
        }
    }

    _currentBuiltinNames() {
        const names = new Set();
        for (const [name, componentClass] of factoryEntries(this.factory)) {
            if (this._factoryClasses.get(name) !== componentClass) names.add(name);
        }
        return names;
    }

    _createFactoryClass(definition) {
        const registry = this;
        const definitionSnapshot = cloneDefinition(definition);
        return class RegisteredCustomComponent extends CustomComponentRenderer {
            constructor(options = {}) {
                super(rendererOptions(options, registry, definitionSnapshot));
            }
        };
    }

    _installFactoryClasses(preparedClasses) {
        if (!this.factory || !this.factory.registry) {
            throw new TypeError('registerWithFactory requires a factory with an inspectable registry.');
        }

        const snapshots = [];
        try {
            for (const { name, componentClass } of preparedClasses) {
                const existing = factoryGet(this.factory, name);
                const owned = this._factoryClasses.get(name);
                if (factoryHas(this.factory, name) && existing !== owned) {
                    throw new Error(`Refusing to overwrite factory component "${name}".`);
                }
                snapshots.push({
                    name,
                    exists: factoryHas(this.factory, name),
                    value: existing,
                });
                factorySet(this.factory, name, componentClass);
                if (factoryGet(this.factory, name) !== componentClass) {
                    throw new Error(`Factory did not retain custom component "${name}".`);
                }
            }
        } catch (error) {
            const rollbackErrors = [];
            for (let index = snapshots.length - 1; index >= 0; index -= 1) {
                const snapshot = snapshots[index];
                try {
                    factoryRestore(this.factory, snapshot.name, snapshot);
                } catch (rollbackError) {
                    rollbackErrors.push(rollbackError);
                }
            }
            if (rollbackErrors.length > 0) error.rollbackErrors = rollbackErrors;
            throw error;
        }
    }

    async _fetchJson(url, label) {
        if (typeof this.fetchImpl !== 'function') {
            throw new Error(`Cannot load ${label}: fetch is unavailable.`);
        }

        let response;
        try {
            response = await this.fetchImpl.call(globalThis, url);
        } catch (error) {
            throw new Error(`Failed to fetch ${label}: ${error instanceof Error ? error.message : String(error)}`);
        }

        if (!response || response.ok !== true || typeof response.json !== 'function') {
            const status = response?.status == null ? 'unknown status' : `HTTP ${response.status}`;
            throw new Error(`Failed to fetch ${label}: ${status}.`);
        }

        try {
            return await response.json();
        } catch (error) {
            throw new Error(`Failed to parse ${label} JSON: ${error instanceof Error ? error.message : String(error)}`);
        }
    }
}

export default CustomComponentRegistry;
