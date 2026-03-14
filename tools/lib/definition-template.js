'use strict';

const path = require('node:path');
const { pathToFileURL } = require('node:url');

function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

class DefinitionTemplateValidationError extends Error {
    constructor(errors) {
        super(errors.join('; '));
        this.name = 'DefinitionTemplateValidationError';
        this.errors = errors;
    }
}

function isDefinitionTemplate(value) {
    return isObject(value) && value.kind === 'definition-template' && isObject(value.definitions);
}

function ensureArray(value) {
    return Array.isArray(value) ? value : [];
}

function validateIdList(items, label, errors, options = {}) {
    const {
        pattern = null,
        allowMissing = false
    } = options;

    const seen = new Set();

    for (const item of items) {
        const id = item?.id;
        if (typeof id !== 'string' || id.trim() === '') {
            if (!allowMissing) {
                errors.push(`${label} 缺少有效 id`);
            }
            continue;
        }

        if (pattern && !pattern.test(id)) {
            errors.push(`${label} 的 id 格式無效: ${id}`);
        }

        if (seen.has(id)) {
            errors.push(`${label} 出現重複 id: ${id}`);
        } else {
            seen.add(id);
        }
    }
}

function validateReferenceList(refs, validIds, label, errors) {
    for (const ref of ensureArray(refs)) {
        if (typeof ref !== 'string' || ref.trim() === '') {
            errors.push(`${label} 包含無效引用`);
            continue;
        }

        if (!validIds.has(ref)) {
            errors.push(`${label} 找不到對應項目: ${ref}`);
        }
    }
}

let pageDefinitionValidatorPromise = null;

async function loadPageDefinitionValidator() {
    if (!pageDefinitionValidatorPromise) {
        const definitionPath = pathToFileURL(
            path.resolve(__dirname, '../../packages/javascript/browser/page-generator/PageDefinition.js')
        ).href;

        pageDefinitionValidatorPromise = import(definitionPath)
            .then(module => module.validateDefinition);
    }

    return pageDefinitionValidatorPromise;
}

async function validateDefinitionTemplate(template) {
    const errors = [];

    if (!isDefinitionTemplate(template)) {
        return {
            valid: false,
            errors: ['輸入不是 DefinitionTemplate'],
            stats: null
        };
    }

    if (template.version !== '0.1.0') {
        errors.push(`目前只支援 DefinitionTemplate 版本 0.1.0，收到 ${template.version || '(empty)'}`);
    }

    const pages = ensureArray(template.definitions.pages);
    const apps = ensureArray(template.definitions.apps);

    if (pages.length === 0 && apps.length === 0) {
        errors.push('DefinitionTemplate 至少要包含 definitions.pages 或 definitions.apps');
    }

    validateIdList(pages, 'definitions.pages', errors, {
        pattern: /^[a-z0-9][a-z0-9-]*$/
    });
    validateIdList(apps, 'definitions.apps', errors, {
        pattern: /^[a-z0-9][a-z0-9-]*$/
    });

    const validatePageDefinition = await loadPageDefinitionValidator();
    for (const pageEntry of pages) {
        if (!isObject(pageEntry.definition)) {
            errors.push(`page ${pageEntry.id || '(missing-id)'} 缺少 definition 物件`);
            continue;
        }

        const pageValidation = validatePageDefinition(pageEntry.definition);
        if (!pageValidation.valid) {
            for (const error of pageValidation.errors) {
                errors.push(`page ${pageEntry.id}: ${error}`);
            }
        }
    }

    const pageIds = new Set(pages.map(page => page.id).filter(Boolean));
    const appIds = new Set();

    for (const appEntry of apps) {
        const appId = appEntry.id;
        if (appIds.has(appId)) {
            errors.push(`definitions.apps 出現重複 id: ${appId}`);
        } else if (appId) {
            appIds.add(appId);
        }

        if (!isObject(appEntry.app)) {
            errors.push(`app ${appId || '(missing-id)'} 缺少 app 物件`);
            continue;
        }

        const app = appEntry.app;

        if (!isObject(app.identity) || typeof app.identity.name !== 'string' || app.identity.name.trim() === '') {
            errors.push(`app ${appId}: identity.name 為必填`);
        }

        if (!Array.isArray(app.profiles) || app.profiles.length === 0) {
            errors.push(`app ${appId}: profiles 至少需要一個值`);
        }

        const frontend = isObject(app.frontend) ? app.frontend : {};
        if (frontend.pageRefs !== undefined) {
            if (!Array.isArray(frontend.pageRefs)) {
                errors.push(`app ${appId}: frontend.pageRefs 必須是陣列`);
            } else {
                validateReferenceList(frontend.pageRefs, pageIds, `app ${appId} 的 frontend.pageRefs`, errors);
            }
        }

        const backend = app.backend;
        if (!isObject(backend)) {
            errors.push(`app ${appId}: backend 為必填`);
            continue;
        }

        const features = ensureArray(backend.features);
        const services = ensureArray(backend.services);
        const policies = ensureArray(backend.policies);
        const middleware = ensureArray(backend.middleware);
        const endpointModules = ensureArray(backend.endpointModules);
        const routeGroups = ensureArray(backend.routeGroups);
        const startupHooks = ensureArray(backend.startupHooks);

        if (!Array.isArray(backend.features)) errors.push(`app ${appId}: backend.features 必須是陣列`);
        if (!Array.isArray(backend.services)) errors.push(`app ${appId}: backend.services 必須是陣列`);
        if (!Array.isArray(backend.middleware)) errors.push(`app ${appId}: backend.middleware 必須是陣列`);
        if (!Array.isArray(backend.routeGroups)) errors.push(`app ${appId}: backend.routeGroups 必須是陣列`);
        if (!isObject(backend.hosting)) errors.push(`app ${appId}: backend.hosting 為必填`);

        validateIdList(features, `app ${appId} 的 backend.features`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });
        validateIdList(services, `app ${appId} 的 backend.services`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });
        validateIdList(policies, `app ${appId} 的 backend.policies`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });
        validateIdList(middleware, `app ${appId} 的 backend.middleware`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });
        validateIdList(endpointModules, `app ${appId} 的 backend.endpointModules`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });
        validateIdList(routeGroups, `app ${appId} 的 backend.routeGroups`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });
        validateIdList(startupHooks, `app ${appId} 的 backend.startupHooks`, errors, {
            pattern: /^[A-Za-z0-9][A-Za-z0-9_.-]*$/
        });

        const policyIds = new Set(policies.map(item => item.id).filter(Boolean));
        const moduleIds = new Set(endpointModules.map(item => item.id).filter(Boolean));

        for (const routeGroup of routeGroups) {
            if (typeof routeGroup.prefix !== 'string' || routeGroup.prefix.trim() === '') {
                errors.push(`app ${appId} 的 routeGroup ${routeGroup.id || '(missing-id)'} 缺少 prefix`);
            }

            if (!Array.isArray(routeGroup.moduleRefs) || routeGroup.moduleRefs.length === 0) {
                errors.push(`app ${appId} 的 routeGroup ${routeGroup.id || '(missing-id)'} 至少需要一個 moduleRefs`);
            } else {
                validateReferenceList(routeGroup.moduleRefs, moduleIds, `app ${appId} 的 routeGroup ${routeGroup.id} moduleRefs`, errors);
            }

            if (routeGroup.policies !== undefined) {
                if (!Array.isArray(routeGroup.policies)) {
                    errors.push(`app ${appId} 的 routeGroup ${routeGroup.id || '(missing-id)'} policies 必須是陣列`);
                } else {
                    validateReferenceList(routeGroup.policies, policyIds, `app ${appId} 的 routeGroup ${routeGroup.id} policies`, errors);
                }
            }
        }
    }

    return {
        valid: errors.length === 0,
        errors,
        stats: {
            pages: pages.length,
            apps: apps.length
        }
    };
}

async function assertValidDefinitionTemplate(template) {
    const validation = await validateDefinitionTemplate(template);
    if (!validation.valid) {
        throw new DefinitionTemplateValidationError(validation.errors);
    }
    return validation;
}

function resolveTemplateEnvelope(payload, pageIdOverride = null) {
    if (isDefinitionTemplate(payload)) {
        return {
            template: payload,
            pageId: pageIdOverride || null
        };
    }

    if (!isObject(payload)) {
        return null;
    }

    const candidates = ['definitionTemplate', 'template', 'definition'];
    for (const key of candidates) {
        if (isDefinitionTemplate(payload[key])) {
            return {
                template: payload[key],
                pageId: pageIdOverride || payload.pageId || null
            };
        }
    }

    return null;
}

function extractPageEntry(template, pageId = null) {
    if (!isDefinitionTemplate(template)) {
        throw new Error('輸入不是 DefinitionTemplate');
    }

    const pages = template.definitions?.pages;
    if (!Array.isArray(pages) || pages.length === 0) {
        throw new Error('DefinitionTemplate 缺少 definitions.pages');
    }

    let selected = null;
    if (pageId) {
        selected = pages.find(page => page.id === pageId) || null;
        if (!selected) {
            throw new Error(`找不到指定的 page id: ${pageId}`);
        }
    } else if (pages.length === 1) {
        selected = pages[0];
    } else {
        throw new Error(`DefinitionTemplate 包含 ${pages.length} 個 pages，請指定 page id`);
    }

    if (!isObject(selected.definition)) {
        throw new Error(`page ${selected.id} 缺少 definition 物件`);
    }

    return {
        pageId: selected.id,
        pageDefinition: selected.definition
    };
}

module.exports = {
    DefinitionTemplateValidationError,
    isDefinitionTemplate,
    validateDefinitionTemplate,
    assertValidDefinitionTemplate,
    resolveTemplateEnvelope,
    extractPageEntry
};
