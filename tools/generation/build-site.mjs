// build 指令：先跑與 validate 完全相同的驗證；通過才寫出
//   out_dir/site/**            定義網站（外殼 + 原始頁定義 + 執行期元件庫）
//   out_dir/report/validation.json
//   out_dir/report/manifest.json（檔案清單、sha256、生成器版本、型錄 hash）
// 只寫 out_dir 之內；out_dir 必須不存在或為空目錄，且不得是符號連結或 junction。
// 不寫時間戳、檔案依路徑排序，同一輸入兩次 build 的所有檔案逐位元組相同。
import { lstatSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import {
    GENERATOR_VERSION,
    PACKAGE_FORMAT,
    PAGE_GENERATOR_DIR,
    SHELL_DIR,
    UI_COMPONENTS_DIR
} from './paths.mjs';
import { compareCodeUnits, isPlainObject, prettyJson, sha256Hex } from './json-util.mjs';
import { loadValidationContext, validateRequest } from './validate-definition.mjs';

/** 外殼檔案（templates/definition-site → site/），逐位元組複製 */
export const SHELL_FILES = Object.freeze([
    'README.txt',
    'app.css',
    'boot.js',
    'index.html',
    'locale.js',
    'memory-store.js',
    'site-model.js'
]);

const RUNTIME_ASSET_EXTENSIONS = new Set([
    '.js', '.css', '.json', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.woff', '.woff2', '.ttf', '.otf'
]);
const EXCLUDED_TOP_LEVEL_DIRS = new Set(['data', 'refresource']);
const EXCLUDED_SEGMENTS = new Set(['__tests__', 'node_modules']);

function makeError(code, errorPath, message, hint = '') {
    return { code, path: errorPath, message, hint };
}

function toPosix(relativePath) {
    return relativePath.split(path.sep).join('/');
}

/**
 * ui_components 的執行期檔案：排除 data、refresource、demo、測試、文件與建置腳本（.mjs）。
 * SVG 也不打包（元件庫政策為 Canvas-only）。
 */
export function isRuntimeComponentFile(relativePosixPath) {
    const segments = relativePosixPath.split('/');
    if (EXCLUDED_TOP_LEVEL_DIRS.has(segments[0])) return false;
    if (segments.some(segment => EXCLUDED_SEGMENTS.has(segment))) return false;
    const fileName = segments[segments.length - 1];
    if (/demo/i.test(fileName)) return false;
    if (/(^|[._-])tests?([._-]|$)/i.test(fileName)) return false;
    return RUNTIME_ASSET_EXTENSIONS.has(path.extname(fileName).toLowerCase());
}

function walkFiles(rootDir, skipDirectory, relativeDir = '') {
    const files = [];
    const absoluteDir = path.join(rootDir, relativeDir);
    for (const entry of readdirSync(absoluteDir, { withFileTypes: true })) {
        const relativePath = relativeDir ? path.join(relativeDir, entry.name) : entry.name;
        if (entry.isDirectory() && skipDirectory(toPosix(relativePath))) continue;
        if (entry.isSymbolicLink()) {
            throw new Error(`source tree contains a symbolic link: ${toPosix(relativePath)}`);
        }
        if (entry.isDirectory()) {
            files.push(...walkFiles(rootDir, skipDirectory, relativePath));
        } else if (entry.isFile()) {
            files.push(toPosix(relativePath));
        }
    }
    return files;
}

function isExcludedComponentDirectory(relativePosixPath) {
    const segments = relativePosixPath.split('/');
    return (segments.length === 1 && EXCLUDED_TOP_LEVEL_DIRS.has(segments[0]))
        || EXCLUDED_SEGMENTS.has(segments[segments.length - 1]);
}

/** 要打包的執行期檔案清單（相對 site/ 的路徑 → 來源絕對路徑） */
export function listRuntimeSources() {
    const sources = [];
    for (const relativePath of walkFiles(UI_COMPONENTS_DIR, isExcludedComponentDirectory)) {
        if (isRuntimeComponentFile(relativePath)) {
            sources.push({ path: `runtime/ui_components/${relativePath}`, from: path.join(UI_COMPONENTS_DIR, ...relativePath.split('/')) });
        }
    }
    for (const entry of readdirSync(PAGE_GENERATOR_DIR, { withFileTypes: true })) {
        if (entry.isFile() && entry.name.endsWith('.js')) {
            sources.push({ path: `runtime/page-generator/${entry.name}`, from: path.join(PAGE_GENERATOR_DIR, entry.name) });
        }
    }
    return sources.sort((a, b) => compareCodeUnits(a.path, b.path));
}

function checkOutDir(outDir) {
    if (typeof outDir !== 'string' || outDir.length === 0 || outDir.length > 1024 || outDir.includes('\0')) {
        return makeError('OUT_DIR_INVALID', 'out_dir', 'out_dir must be an absolute directory path.');
    }
    if (!path.isAbsolute(outDir)) {
        return makeError('OUT_DIR_INVALID', 'out_dir', 'out_dir must be an absolute directory path.');
    }
    const resolved = path.resolve(outDir);
    let stats = null;
    try {
        stats = lstatSync(resolved);
    } catch (error) {
        if (error?.code !== 'ENOENT') return makeError('OUT_DIR_INVALID', 'out_dir', 'out_dir cannot be inspected.');
    }
    if (stats) {
        if (stats.isSymbolicLink()) {
            return makeError('OUT_DIR_SYMLINK', 'out_dir', 'out_dir is a symbolic link or junction.');
        }
        if (!stats.isDirectory()) {
            return makeError('OUT_DIR_INVALID', 'out_dir', 'out_dir exists and is not a directory.');
        }
        const entries = readdirSync(resolved, { withFileTypes: true });
        if (entries.some(entry => entry.isSymbolicLink())) {
            return makeError('OUT_DIR_SYMLINK', 'out_dir', 'out_dir contains a symbolic link or junction.');
        }
        if (entries.length > 0) {
            return makeError('OUT_DIR_NOT_EMPTY', 'out_dir', 'out_dir must be empty.');
        }
        return null;
    }
    let parentStats;
    try {
        parentStats = lstatSync(path.dirname(resolved));
    } catch {
        return makeError('OUT_DIR_INVALID', 'out_dir', 'The parent directory of out_dir does not exist.');
    }
    if (!parentStats.isDirectory() || parentStats.isSymbolicLink()) {
        return makeError('OUT_DIR_INVALID', 'out_dir', 'The parent of out_dir must be a real directory.');
    }
    return null;
}

function resolveInside(rootDir, relativePosixPath) {
    const target = path.resolve(rootDir, ...relativePosixPath.split('/'));
    const relative = path.relative(rootDir, target);
    if (relative === '' || relative.startsWith('..') || path.isAbsolute(relative)) {
        throw new Error(`refusing to write outside out_dir: ${relativePosixPath}`);
    }
    return target;
}

function selectedPages(template, pageIds) {
    const pages = template.definitions.pages;
    return Array.isArray(pageIds) ? pages.filter(entry => pageIds.includes(entry.id)) : pages;
}

/**
 * @param {object} request - { template, page_ids?, title?, out_dir }
 */
export async function buildSite(request) {
    const validation = await validateRequest(request, 'build');
    const outDirError = isPlainObject(request) && Object.prototype.hasOwnProperty.call(request, 'out_dir')
        ? checkOutDir(request.out_dir)
        : makeError('OUT_DIR_REQUIRED', 'out_dir', 'out_dir is required.');

    if (!validation.ok || outDirError) {
        return {
            ok: false,
            errors: [...validation.errors, ...(outDirError ? [outDirError] : [])],
            warnings: validation.warnings,
            pages: validation.pages,
            validation_digest: validation.validation_digest,
            validator_version: validation.validator_version
        };
    }

    const context = await loadValidationContext();
    const template = request.template;
    const pageIds = Array.isArray(request.page_ids) ? request.page_ids : null;
    const pages = selectedPages(template, pageIds);
    const title = typeof request.title === 'string' && request.title.trim() !== ''
        ? request.title.trim()
        : (typeof template.meta?.title === 'string' ? template.meta.title : '');

    // 檔案計畫：相對 out_dir 的 posix 路徑 → 內容
    const plan = new Map();
    const add = (relativePath, data) => {
        if (plan.has(relativePath)) throw new Error(`duplicate output path ${relativePath}`);
        plan.set(relativePath, Buffer.isBuffer(data) ? data : Buffer.from(data, 'utf8'));
    };

    for (const name of SHELL_FILES) {
        add(`site/${name}`, readFileSync(path.join(SHELL_DIR, name)));
    }
    add('site/site.json', prettyJson({
        format: PACKAGE_FORMAT,
        title,
        pages: pages.map(entry => entry.id)
    }));
    add('site/definition-template.json', prettyJson(template));
    for (const entry of pages) {
        // 檔名一律取自已驗證的 page id（^[a-z0-9][a-z0-9-]*$），從不使用定義內的其他欄位
        add(`site/definitions/${entry.id}.json`, prettyJson(entry.definition));
    }
    for (const source of listRuntimeSources()) {
        add(`site/${source.path}`, readFileSync(source.from));
    }
    add('report/validation.json', prettyJson({
        ...validation,
        catalog_sha256: context.catalogSha256,
        matrix_sha256: context.matrixSha256
    }));

    const describe = (relativePath, data) => ({ path: relativePath, sha256: sha256Hex(data), size: data.length });
    const contentFiles = [...plan.entries()]
        .map(([relativePath, data]) => describe(relativePath, data))
        .sort((a, b) => compareCodeUnits(a.path, b.path));
    const manifestData = Buffer.from(prettyJson({
        format: PACKAGE_FORMAT,
        generator_version: GENERATOR_VERSION,
        validator_version: validation.validator_version,
        catalog_sha256: context.catalogSha256,
        matrix_sha256: context.matrixSha256,
        validation_digest: validation.validation_digest,
        title,
        pages: validation.pages,
        file_count: contentFiles.length,
        total_size: contentFiles.reduce((sum, file) => sum + file.size, 0),
        files: contentFiles
    }), 'utf8');
    plan.set('report/manifest.json', manifestData);

    const outDir = path.resolve(request.out_dir);
    let createdOutDir = false;
    try {
        try {
            mkdirSync(outDir);
            createdOutDir = true;
        } catch (error) {
            if (error?.code !== 'EEXIST') throw error;
        }
        const orderedPaths = [...plan.keys()].sort(compareCodeUnits);
        for (const relativePath of orderedPaths) {
            const target = resolveInside(outDir, relativePath);
            mkdirSync(path.dirname(target), { recursive: true });
            writeFileSync(target, plan.get(relativePath), { flag: 'wx' });
        }
    } catch (error) {
        // 部分寫出的內容一律清除（只動 out_dir 之內）；out_dir 本身若是本次建立的也一併移除
        try {
            for (const entry of readdirSync(outDir)) {
                rmSync(path.join(outDir, entry), { recursive: true, force: true });
            }
            if (createdOutDir) rmSync(outDir, { recursive: true, force: true });
        } catch {
            // 清理失敗時保留原始錯誤
        }
        throw error;
    }

    const files = [...contentFiles, describe('report/manifest.json', manifestData)]
        .sort((a, b) => compareCodeUnits(a.path, b.path));
    return {
        ok: true,
        pages: validation.pages,
        files,
        generator_version: GENERATOR_VERSION,
        catalog_sha256: context.catalogSha256,
        validation_digest: validation.validation_digest,
        warnings: validation.warnings,
        validator_version: validation.validator_version,
        matrix_sha256: context.matrixSha256,
        file_count: files.length,
        total_size: files.reduce((sum, file) => sum + file.size, 0)
    };
}
