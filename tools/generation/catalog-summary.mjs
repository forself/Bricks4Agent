#!/usr/bin/env node
// 給代理撰寫定義用的精簡型錄。由 component-catalog.json、generator-support-matrix.json 與 golden 範例
// 決定性產生（同輸入同輸出），分 overview／field_types／example／component 四節。
//
// 映像建置時預先產生：node tools/generation/catalog-summary.mjs --out tools/generation/catalog-summary.json
// cli 的 catalog 指令會檢查預產生檔的來源 hash；與目前檔案不符就即時重建，兩者內容相同。
import { existsSync, lstatSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import {
    CATALOG_PATH,
    GOLDEN_EXAMPLE_PATH,
    MATRIX_PATH,
    PAGE_GEN_PATH,
    PREGENERATED_SUMMARY_PATH,
    SHELL_DIR,
    SUMMARY_VERSION
} from './paths.mjs';
import { COMMON_TYPE_ALIASES, computeSliceFieldTypes, describeFieldTypes, FIELD_TYPE_SUBSTITUTES } from './field-types.mjs';
import { hasOwn, isPlainObject, prettyJson, sha256Hex } from './json-util.mjs';
import { LIMITS } from './validate-definition.mjs';

export const SECTIONS = Object.freeze(['overview', 'field_types', 'example', 'component']);
export const MAX_RESPONSE_BYTES = 32 * 1024;

// 摘要內容也取決於這些程式（型別交集、說明表、上限值）；預產生檔須與它們同版才可使用
const CODE_INPUTS = [
    fileURLToPath(import.meta.url),
    fileURLToPath(new URL('./field-types.mjs', import.meta.url)),
    fileURLToPath(new URL('./validate-definition.mjs', import.meta.url)),
    PAGE_GEN_PATH,
    path.join(SHELL_DIR, 'site-model.js')
];

function readSources() {
    return {
        catalogBytes: readFileSync(CATALOG_PATH),
        matrixBytes: readFileSync(MATRIX_PATH),
        goldenBytes: readFileSync(GOLDEN_EXAMPLE_PATH),
        codeSha256: sha256Hex(CODE_INPUTS.map(file => sha256Hex(readFileSync(file))).join('\n'))
    };
}

function sourceHashes({ catalogBytes, matrixBytes, goldenBytes, codeSha256 }) {
    return {
        catalog_sha256: sha256Hex(catalogBytes),
        matrix_sha256: sha256Hex(matrixBytes),
        example_sha256: sha256Hex(goldenBytes),
        code_sha256: codeSha256
    };
}

function buildOverview() {
    return {
        purpose: 'Write one DefinitionTemplate (JSON) that describes a multi-page front-end prototype. Pages are rendered with the Bricks4Agent component library; records live in an in-browser store (no backend).',
        workflow: [
            'query field_types and example',
            'write the template',
            'call validate_definition; fix every error using its path and hint; repeat until ok',
            'call generate_scaffold with the same template'
        ],
        root: {
            kind: '"definition-template"',
            version: '"0.1.0"',
            meta: 'optional {title, description}',
            definitions: '{pages:[...]} (apps not supported)'
        },
        page_entry: {
            id: '^[a-z0-9][a-z0-9-]*$, max 64, unique; also the output file name',
            definition: 'page definition'
        },
        page_definition: {
            name: '^[A-Z][a-zA-Z0-9]*Page$',
            type: 'list | detail | form',
            description: 'optional page title, max 200',
            fields: '1-60 fields',
            api: '{list,get,create,update,delete}: base paths only, ^/api/[a-z0-9/_-]+$; never {id} or :id'
        },
        field: {
            name: 'JavaScript IdentifierName (letters incl. CJK, digits, _ or $; no hyphen or space); unique per page; not "id"',
            type: 'one of field_types',
            label: 'required plain text, max 100',
            required: 'optional boolean',
            default: 'optional string, number or boolean',
            options: 'select, radio, multiselect only: [{value,label}]',
            validation: 'optional; keys per field_types',
            component: 'optional; normally omit (the type selects the component)'
        },
        page_types: {
            list: 'table of the fields; needs api.list; api.delete enables delete',
            detail: 'read-only view of one record; needs api.get',
            form: 'create or edit one record; needs api.create; api.update enables editing; api.get loads the record'
        },
        linking: 'Pages that share one api base path form one resource: list rows open its detail and form pages.',
        rules: [
            'declarative data only: no code, HTML, expressions or lambdas',
            'unknown keys are rejected at every level',
            'not available: components, behaviors, styles, services, tool and dashboard pages',
            'text must not contain < or >',
            'record ids are assigned by the store'
        ],
        limits: {
            pages: LIMITS.maxPages,
            fields_per_page: LIMITS.maxFieldsPerPage,
            options_per_field: LIMITS.maxOptionsPerField,
            template_bytes: LIMITS.maxTemplateBytes
        },
        errors: 'validate returns errors as {code, path, message, hint}; path points into the template, e.g. definitions.pages[1].definition.fields[3].type'
    };
}

function buildFieldTypes(matrix) {
    const slice = computeSliceFieldTypes(matrix);
    const excluded = [
        ...slice.intersection.excluded.notInMatrix,
        ...slice.intersection.excluded.outOfCatalog,
        ...slice.intersection.excluded.notInPageGen,
        ...slice.blocked
    ];
    const substitutes = {};
    for (const type of excluded) {
        if (hasOwn(FIELD_TYPE_SUBSTITUTES, type)) substitutes[type] = FIELD_TYPE_SUBSTITUTES[type];
    }
    return {
        types: describeFieldTypes(matrix, slice.allowed),
        not_supported: excluded,
        use_instead: substitutes,
        not_field_types: { ...COMMON_TYPE_ALIASES }
    };
}

function buildComponentEntries(catalog) {
    const entries = {};
    for (const component of catalog.components || []) {
        if (!component || typeof component.registry_name !== 'string') continue;
        entries[component.registry_name] = {
            registry_name: component.registry_name,
            category: component.category ?? null,
            kind: component.kind ?? null,
            maturity: component.maturity ?? null,
            generator: component.generator ?? null,
            binding: component.binding ?? null
        };
    }
    return entries;
}

/**
 * 由來源檔決定性產生完整摘要（四節的內容都在裡面）。
 */
export function buildCatalogSummary(sources = readSources()) {
    const catalog = JSON.parse(sources.catalogBytes.toString('utf8'));
    const matrix = JSON.parse(sources.matrixBytes.toString('utf8'));
    const example = JSON.parse(sources.goldenBytes.toString('utf8'));
    return {
        summary_version: SUMMARY_VERSION,
        ...sourceHashes(sources),
        sections: {
            overview: buildOverview(),
            field_types: buildFieldTypes(matrix),
            example
        },
        components: buildComponentEntries(catalog)
    };
}

function isUsablePregenerated(candidate, hashes) {
    return isPlainObject(candidate)
        && candidate.summary_version === SUMMARY_VERSION
        && candidate.catalog_sha256 === hashes.catalog_sha256
        && candidate.matrix_sha256 === hashes.matrix_sha256
        && candidate.example_sha256 === hashes.example_sha256
        && candidate.code_sha256 === hashes.code_sha256
        && isPlainObject(candidate.sections)
        && isPlainObject(candidate.components);
}

/**
 * 預產生檔存在且來源 hash 相符時使用它，否則即時產生。
 * @returns {{ summary: object, source: 'pregenerated'|'live' }}
 */
export function loadCatalogSummary({ pregeneratedPath = PREGENERATED_SUMMARY_PATH } = {}) {
    const sources = readSources();
    const hashes = sourceHashes(sources);
    if (pregeneratedPath && existsSync(pregeneratedPath)) {
        try {
            if (lstatSync(pregeneratedPath).isFile()) {
                const candidate = JSON.parse(readFileSync(pregeneratedPath, 'utf8'));
                if (isUsablePregenerated(candidate, hashes)) {
                    return { summary: candidate, source: 'pregenerated' };
                }
            }
        } catch {
            // 損毀或不可讀的預產生檔一律忽略，改為即時產生
        }
    }
    return { summary: buildCatalogSummary(sources), source: 'live' };
}

function queryError(section, code, message, hint = '') {
    return { ok: false, section, errors: [{ code, path: code === 'SECTION_UNKNOWN' ? 'section' : 'name', message, hint }] };
}

/**
 * catalog 指令：{ section?, name? } → { ok, section, content, catalog_sha256, matrix_sha256, summary_version }
 * 單次回應超過 32KB 時改回 RESPONSE_TOO_LARGE（硬上限，正常內容遠低於此）。
 */
export function queryCatalog(request, summary) {
    const response = answerCatalogQuery(request, summary);
    if (Buffer.byteLength(JSON.stringify(response), 'utf8') > MAX_RESPONSE_BYTES) {
        return {
            ok: false,
            section: response.section ?? null,
            errors: [{ code: 'RESPONSE_TOO_LARGE', path: 'section', message: 'The catalog section exceeds the response size limit.', hint: '' }],
            catalog_sha256: summary.catalog_sha256,
            matrix_sha256: summary.matrix_sha256,
            summary_version: summary.summary_version
        };
    }
    return response;
}

function answerCatalogQuery(request, summary) {
    const base = {
        catalog_sha256: summary.catalog_sha256,
        matrix_sha256: summary.matrix_sha256,
        summary_version: summary.summary_version
    };
    if (!isPlainObject(request)) {
        return { ...queryError(null, 'INPUT_INVALID', 'The input must be a JSON object.'), ...base };
    }
    const unknown = Object.keys(request).filter(key => key !== 'section' && key !== 'name');
    if (unknown.length > 0) {
        return {
            ok: false,
            section: null,
            errors: unknown.map(key => ({ code: 'INPUT_UNKNOWN_KEY', path: key, message: `Unknown input key "${key}".`, hint: 'Allowed keys: section, name.' })),
            ...base
        };
    }
    const section = request.section === undefined || request.section === null ? 'overview' : request.section;
    if (typeof section !== 'string' || !SECTIONS.includes(section)) {
        return { ...queryError(null, 'SECTION_UNKNOWN', 'Unknown catalog section.', `Use one of: ${SECTIONS.join(', ')}.`), ...base };
    }
    if (section !== 'component') {
        return { ok: true, section, content: summary.sections[section], ...base };
    }
    const name = request.name;
    if (typeof name !== 'string' || name.length === 0 || name.length > 64 || !hasOwn(summary.components, name)) {
        return {
            ...queryError(section, 'COMPONENT_NOT_FOUND', 'No component with that registry name.', 'Component names are case-sensitive, e.g. TextInput; most pages need no explicit component.'),
            ...base
        };
    }
    return { ok: true, section, content: summary.components[name], ...base };
}

// ------------------------------------------------------------
// CLI：--out <file>
// ------------------------------------------------------------

function runCli(argv) {
    const index = argv.indexOf('--out');
    const out = index >= 0 ? argv[index + 1] : null;
    if (!out || out.startsWith('--') || argv.length !== 2) {
        process.stderr.write('usage: node tools/generation/catalog-summary.mjs --out <file>\n');
        process.exitCode = 64;
        return;
    }
    const target = path.resolve(out);
    if (existsSync(target) && lstatSync(target).isSymbolicLink()) {
        process.stderr.write('refusing to write through a symbolic link\n');
        process.exitCode = 1;
        return;
    }
    const summary = buildCatalogSummary();
    writeFileSync(target, prettyJson(summary), 'utf8');
    process.stderr.write(`catalog summary written (${Object.keys(summary.components).length} components)\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    runCli(process.argv.slice(2));
}
