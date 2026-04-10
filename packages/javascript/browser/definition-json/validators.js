import { backendSchema } from './backend-schema.js';
import { frontendSchema } from './frontend-schema.js';
import { architectureSchema } from './architecture-schema.js';

function result(errors) {
    return {
        valid: errors.length === 0,
        errors,
    };
}

function requireObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function validateArchitecture(input) {
    const errors = [];

    if (!requireObject(input)) {
        errors.push('architecture must be an object');
        return result(errors);
    }

    if (input.schema_version !== architectureSchema.schema_version) {
        errors.push('architecture.schema_version must be 1');
    }
    if (!input.project?.id) {
        errors.push('architecture.project.id is required');
    }
    if (!input.frontend?.definition_file) {
        errors.push('architecture.frontend.definition_file is required');
    }
    if (!input.backend?.definition_file) {
        errors.push('architecture.backend.definition_file is required');
    }

    return result(errors);
}

export function validateFrontendDefinition(input) {
    const errors = [];

    if (!requireObject(input)) {
        errors.push('frontend must be an object');
        return result(errors);
    }

    if (input.schema_version !== frontendSchema.schema_version) {
        errors.push('frontend.schema_version must be 1');
    }

    for (const route of input.routes || []) {
        if (!frontendSchema.routeKinds.includes(route?.page_kind)) {
            errors.push(`Unsupported page_kind: ${route?.page_kind}`);
        }
        if (!Array.isArray(route?.surfaces) || route.surfaces.length === 0) {
            errors.push(`Route ${route?.path ?? 'unknown'} must declare surfaces`);
        }
    }

    return result(errors);
}

export function validateBackendDefinition(input) {
    const errors = [];

    if (!requireObject(input)) {
        errors.push('backend must be an object');
        return result(errors);
    }

    if (input.schema_version !== backendSchema.schema_version) {
        errors.push('backend.schema_version must be 1');
    }
    if (!backendSchema.tiers.includes(input.tier)) {
        errors.push(`Unsupported backend tier: ${input?.tier}`);
    }
    if (!input.template) {
        errors.push('backend.template is required');
    }
    if (!input.persistence?.orm) {
        errors.push('backend.persistence.orm is required');
    }

    return result(errors);
}
