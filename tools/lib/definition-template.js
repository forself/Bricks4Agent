'use strict';

function isObject(value) {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function isDefinitionTemplate(value) {
    return isObject(value) && value.kind === 'definition-template' && isObject(value.definitions);
}

function findDuplicateIds(items, label) {
    const seen = new Set();
    const duplicates = new Set();

    for (const item of items) {
        const id = item?.id;
        if (typeof id !== 'string' || id.trim() === '') {
            throw new Error(`${label} 缺少有效 id`);
        }

        if (seen.has(id)) {
            duplicates.add(id);
        }

        seen.add(id);
    }

    if (duplicates.size > 0) {
        throw new Error(`${label} 出現重複 id: ${Array.from(duplicates).join(', ')}`);
    }
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

    findDuplicateIds(pages, 'definitions.pages');

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
    isDefinitionTemplate,
    resolveTemplateEnvelope,
    extractPageEntry
};
