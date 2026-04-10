import { setDefinitionBundle } from './definition-store.js';

async function fetchJson(url) {
    const response = await fetch(url, { cache: 'no-store' });
    if (!response.ok) {
        throw new Error(`Failed to load definition file ${url}: ${response.status} ${response.statusText}`);
    }

    return response.json();
}

function validateArchitecture(definition) {
    if (!definition || typeof definition !== 'object') {
        throw new Error('architecture.json must be an object');
    }

    if (definition.schema_version !== 1) {
        throw new Error('architecture.schema_version must be 1');
    }

    if (!definition.frontend?.definition_file) {
        throw new Error('architecture.frontend.definition_file is required');
    }

    return definition;
}

function validateFrontendDefinition(definition) {
    if (!definition || typeof definition !== 'object') {
        throw new Error('frontend-definition.json must be an object');
    }

    if (definition.schema_version !== 1) {
        throw new Error('frontend.schema_version must be 1');
    }

    if (!Array.isArray(definition.routes)) {
        throw new Error('frontend.routes must be an array');
    }

    return definition;
}

export async function loadFrontendDefinitionBootstrap() {
    const architectureUrl = new URL('./architecture.json', import.meta.url);
    const architecture = validateArchitecture(await fetchJson(architectureUrl));
    const frontendDefinitionUrl = new URL(architecture.frontend.definition_file, architectureUrl);
    const frontendDefinition = validateFrontendDefinition(await fetchJson(frontendDefinitionUrl));

    setDefinitionBundle({
        architecture,
        frontendDefinition,
    });

    return {
        architecture,
        frontendDefinition,
    };
}

export default loadFrontendDefinitionBootstrap;
