import { readFileSync } from 'node:fs';
import path from 'node:path';

import {
    validateArchitecture,
    validateBackendDefinition,
    validateFrontendDefinition,
} from './validators.js';

function loadJsonFile(rootDir, relativePath) {
    const fullPath = path.resolve(rootDir, relativePath);
    return JSON.parse(readFileSync(fullPath, 'utf8'));
}

export function loadDefinitionFile(rootDir, relativePath, validator) {
    const parsed = loadJsonFile(rootDir, relativePath);
    const result = validator(parsed);

    if (!result.valid) {
        throw new Error(`Invalid definition file ${relativePath}: ${result.errors.join('; ')}`);
    }

    return parsed;
}

export function loadDefinitionBundle(rootDir, architecturePath) {
    const architecture = loadDefinitionFile(rootDir, architecturePath, validateArchitecture);
    const frontend = architecture.frontend?.enabled
        ? loadDefinitionFile(rootDir, architecture.frontend.definition_file, validateFrontendDefinition)
        : null;
    const backend = architecture.backend?.enabled
        ? loadDefinitionFile(rootDir, architecture.backend.definition_file, validateBackendDefinition)
        : null;

    return {
        architecture,
        frontend,
        backend,
    };
}
