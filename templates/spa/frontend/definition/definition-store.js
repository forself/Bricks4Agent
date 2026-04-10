let currentArchitecture = null;
let currentFrontendDefinition = null;

function clone(value) {
    return value == null ? value : JSON.parse(JSON.stringify(value));
}

function toRouteRegex(routePath) {
    const pattern = String(routePath || '')
        .replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
        .replace(/:([^/]+)/g, '[^/]+');

    return new RegExp(`^${pattern}$`);
}

export function setDefinitionBundle({ architecture = null, frontendDefinition = null } = {}) {
    currentArchitecture = clone(architecture);
    currentFrontendDefinition = clone(frontendDefinition);
}

export function getArchitectureDefinition() {
    return clone(currentArchitecture);
}

export function getFrontendDefinition() {
    return clone(currentFrontendDefinition);
}

export function getAppDefinition() {
    return currentFrontendDefinition?.app || {};
}

export function getNavigationItems() {
    return Array.isArray(currentFrontendDefinition?.navigation?.items)
        ? currentFrontendDefinition.navigation.items.slice()
        : [];
}

export function getSharedResources() {
    return currentFrontendDefinition?.shared_resources || { enums: {}, messages: {} };
}

export function getSharedEnumOptions(name, fallback = []) {
    const options = currentFrontendDefinition?.shared_resources?.enums?.[name];
    return Array.isArray(options) ? options.map((item) => ({ ...item })) : fallback.slice();
}

export function getSharedEnumLabel(name, value, fallbackLabel = '') {
    const options = getSharedEnumOptions(name);
    return options.find((item) => String(item.value) === String(value))?.label || fallbackLabel;
}

export function getRouteDefinitionForPath(path) {
    const routes = Array.isArray(currentFrontendDefinition?.routes) ? currentFrontendDefinition.routes : [];
    for (const route of routes) {
        if (route.path === path) {
            return route;
        }

        if (route.path?.includes(':') && toRouteRegex(route.path).test(path)) {
            return route;
        }
    }

    return null;
}

export function getRouteTitleForPath(path) {
    return getRouteDefinitionForPath(path)?.title || null;
}

export default {
    setDefinitionBundle,
    getArchitectureDefinition,
    getFrontendDefinition,
    getAppDefinition,
    getNavigationItems,
    getSharedResources,
    getSharedEnumOptions,
    getSharedEnumLabel,
    getRouteDefinitionForPath,
    getRouteTitleForPath,
};
