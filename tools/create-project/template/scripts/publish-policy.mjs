import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, realpathSync, statSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import * as vm from 'node:vm';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { analyzeJavaScriptModule } from './module-analysis.mjs';

const TEXT_EXTENSIONS = new Set(['.js', '.mjs', '.html', '.css']);
const moduleAnalysisPath = fileURLToPath(new URL('./module-analysis.mjs', import.meta.url));
const moduleAnalysisCache = new Map();

function analyzeModuleFile(file, content) {
    if (typeof vm.SourceTextModule === 'function') return analyzeJavaScriptModule(content, file);
    const cached = moduleAnalysisCache.get(file);
    if (cached?.source === content) return cached.result;
    const parsed = spawnSync(process.execPath, ['--experimental-vm-modules', moduleAnalysisPath, file], {
        encoding: 'utf8', maxBuffer: 4 * 1024 * 1024,
    });
    if (parsed.status !== 0) throw new Error(`Cannot parse ${file} as an ES module: ${parsed.stderr.trim()}`);
    const result = JSON.parse(parsed.stdout);
    moduleAnalysisCache.set(file, { source: content, result });
    return result;
}

function references(file, content) {
    const refs = [];
    const imports = [];
    const add = (re) => { for (const match of content.matchAll(re)) refs.push(match[1]); };
    if (/\.(js|mjs)$/.test(file)) {
        const analysis = analyzeModuleFile(file, content);
        imports.push(...analysis.imports, ...analysis.dynamicImports);
        refs.push(...imports);
        add(/new URL\(\s*['"]([^'"]+)['"]\s*,\s*import\.meta\.url/g);
        add(/(?:fetch|Worker|SharedWorker|Audio)\(\s*['"]([^'"]+)['"]/g);
        add(/new Worker\(\s*['"]([^'"]+)['"]/g);
        return { refs, imports, expressions: analysis.unresolvedDynamic };
    } else if (/\.html$/.test(file)) {
        add(/(?:src|href)\s*=\s*["']([^"']+)["']/g);
    } else if (/\.css$/.test(file)) {
        add(/@import\s+(?:url\()?\s*["']([^"']+)["']/g);
        add(/url\(\s*["']?([^"')]+)["']?\s*\)/g);
    }
    return { refs, imports, expressions: [] };
}

function ignoredRef(ref) {
    return /^(?:https?:|data:|blob:|#|mailto:|javascript:|node:|\/\/)/i.test(ref) || ref.includes('${');
}

function hasRootPrefix(target, root) { return target === root || target.startsWith(root + path.sep); }
function isRelativeRef(ref) { return ref.startsWith('./') || ref.startsWith('../'); }
function referenceTarget(base, ref) { return path.resolve(base, ref.split(/[?#]/, 1)[0]); }

function isInsideLibraryRealPath(target, root) {
    const realTarget = realpathSync(target);
    const realRoots = ['ui_components', 'page-generator', 'custom_components']
        .map((name) => path.join(root, name)).filter(existsSync).map((entry) => realpathSync(entry));
    return realRoots.some((realRoot) => realTarget === realRoot || realTarget.startsWith(realRoot + path.sep));
}

function listFiles(target, root = null, visited = new Set()) {
    if (!existsSync(target)) return [];
    if (root && !isInsideLibraryRealPath(target, root)) throw new Error(`Library path resolves outside published roots: ${target}`);
    const st = statSync(target);
    if (st.isFile()) return [target];
    if (root) {
        const realDirectory = realpathSync(target);
        if (visited.has(realDirectory)) return [];
        visited.add(realDirectory);
    }
    const result = [];
    for (const name of readdirSync(target)) result.push(...listFiles(path.join(target, name), root, visited));
    return result;
}

function eligible(file, root) {
    const relative = path.relative(root, file).split(path.sep).join('/');
    return !relative.split('/').includes('node_modules') && !relative.endsWith('.test.mjs') &&
        !(relative.startsWith('page-generator/') && relative.split('/').includes('examples'));
}

function normalizePolicyPath(value, root) {
    if (typeof value !== 'string' || !value || path.isAbsolute(value)) throw new Error(`Invalid policy path: ${String(value)}`);
    const full = path.resolve(root, value);
    if (full !== root && !full.startsWith(root + path.sep)) throw new Error(`Policy path escapes lib/: ${value}`);
    if (!existsSync(full)) throw new Error(`Policy path does not exist: ${value}`);
    if (!isInsideLibraryRealPath(full, root)) throw new Error(`Policy path resolves outside the published libraries: ${value}`);
    return full;
}

export function readPublishPolicy(policyPath) {
    const policy = JSON.parse(readFileSync(policyPath, 'utf8'));
    const allowed = new Set(['version', 'entrypoints', 'keep', 'dynamicImportKeep']);
    if (!policy || typeof policy !== 'object' || Array.isArray(policy) ||
        Object.keys(policy).some(key => !allowed.has(key)) || policy.version !== 1 ||
        (Object.hasOwn(policy, 'entrypoints') && !Array.isArray(policy.entrypoints)) ||
        (Object.hasOwn(policy, 'keep') && !Array.isArray(policy.keep)) ||
        (Object.hasOwn(policy, 'dynamicImportKeep') && (!policy.dynamicImportKeep ||
            typeof policy.dynamicImportKeep !== 'object' || Array.isArray(policy.dynamicImportKeep)))) {
        throw new Error('Publish policy must use version 1 and arrays for entrypoints/keep plus a dynamicImportKeep object.');
    }
    const entrypoints = policy.entrypoints ?? [];
    const keep = policy.keep ?? [];
    for (const value of [...entrypoints, ...keep]) validatePortablePath(value, 'policy path');
    const dynamicImportKeep = policy.dynamicImportKeep ?? {};
    for (const [source, candidates] of Object.entries(dynamicImportKeep)) {
        validatePortablePath(source, 'dynamic import source');
        if (!Array.isArray(candidates) || candidates.length === 0)
            throw new Error(`dynamicImportKeep[${source}] must list one or more files/directories.`);
        for (const candidate of candidates) validatePortablePath(candidate, 'dynamic import keep path');
    }
    return { version: 1, entrypoints, keep, dynamicImportKeep };
}

function validatePortablePath(value, label) {
    if (typeof value !== 'string' || !value || value.includes('\\') || value.includes(':') ||
        value.startsWith('/') || value.split('/').some(segment => !segment || segment === '.' || segment === '..'))
        throw new Error(`Invalid ${label}: ${String(value)}`);
}

export function planPublishedFiles({ libRoot, frontendRoot, policy }) {
    const root = path.resolve(libRoot);
    const selected = new Set();
    const queue = [];
    const dynamicRules = policy.dynamicImportKeep ?? {};
    const ruleKeys = new Set(Object.keys(dynamicRules));
    const addPath = (value, { traverse = true } = {}) => {
        const full = normalizePolicyPath(value, root);
        const files = listFiles(full, root);
        for (const file of files) {
            if (!eligible(file, root)) continue;
            selected.add(file);
            if (traverse && TEXT_EXTENSIONS.has(path.extname(file))) queue.push(file);
        }
    };

    for (const entry of policy.entrypoints ?? []) addPath(entry);
    for (const keep of policy.keep ?? []) addPath(keep);
    for (const [source, candidates] of Object.entries(dynamicRules)) {
        if (source.startsWith('src/frontend/')) {
            const frontendSource = path.resolve(frontendRoot, source.slice('src/frontend/'.length));
            if (!frontendSource.startsWith(path.resolve(frontendRoot) + path.sep) || !existsSync(frontendSource) ||
                !['.js', '.mjs'].includes(path.extname(frontendSource))) throw new Error(`Unknown dynamic import source: ${source}`);
        } else {
            const librarySource = normalizePolicyPath(source, root);
            if (!['.js', '.mjs'].includes(path.extname(librarySource)) || !statSync(librarySource).isFile())
                throw new Error(`Dynamic import source must name a JavaScript file: ${source}`);
        }
        for (const candidate of candidates) addPath(candidate);
    }

    for (const file of listFiles(frontendRoot)) {
        if (!TEXT_EXTENSIONS.has(path.extname(file))) continue;
        const { refs, imports, expressions } = references(file, readFileSync(file, 'utf8'));
        const frontendRelative = `src/frontend/${path.relative(frontendRoot, file).split(path.sep).join('/')}`;
        if (expressions.length && !ruleKeys.has(frontendRelative)) {
            throw new Error(`Unresolved dynamic import in ${frontendRelative}; add dynamicImportKeep["${frontendRelative}"] with every possible lib file/directory.`);
        }
        for (const ref of refs) {
            if (ignoredRef(ref) || ref.startsWith('/')) continue;
            if (imports.includes(ref) && !isRelativeRef(ref)) continue;
            const lexicalTarget = referenceTarget(path.dirname(file), ref);
            if (!hasRootPrefix(lexicalTarget, root)) continue;
            const target = lexicalTarget;
            if (!existsSync(target)) throw new Error(`Unresolved relative reference in ${frontendRelative}: ${ref}`);
            if (!isInsideLibraryRealPath(target, root)) throw new Error(`Library reference escapes published roots in ${frontendRelative}: ${ref}`);
            if (imports.includes(ref) && !statSync(target).isFile())
                throw new Error(`Relative module import is not a file in ${frontendRelative}: ${ref}`);
            addPath(path.relative(root, target).split(path.sep).join('/'));
        }
    }

    const processed = new Set();
    while (queue.length) {
        const file = queue.pop();
        if (processed.has(file)) continue;
        processed.add(file);
        const relativeSource = path.relative(root, file).split(path.sep).join('/');
        const librarySource = hasRootPrefix(file, root);
        const { refs, imports, expressions } = references(file, readFileSync(file, 'utf8'));
        if (expressions.length && !ruleKeys.has(relativeSource)) {
            throw new Error(`Unresolved dynamic import in lib/${relativeSource}; add dynamicImportKeep["${relativeSource}"] with every possible file/directory.`);
        }
        for (const ref of refs) {
            if (ignoredRef(ref)) continue;
            if (ref.startsWith('/')) {
                if (librarySource) throw new Error(`Root-absolute reference in lib/${relativeSource}: ${ref}`);
                continue;
            }
            if (imports.includes(ref) && !isRelativeRef(ref)) continue;
            const target = referenceTarget(path.dirname(file), ref);
            if (!hasRootPrefix(target, root)) {
                if (librarySource && (imports.includes(ref) || isRelativeRef(ref)))
                    throw new Error(`Relative reference escapes lib/: lib/${relativeSource} → ${ref}`);
                continue;
            }
            if (!existsSync(target)) throw new Error(`Unresolved relative reference in lib/${relativeSource}: ${ref}`);
            if (!isInsideLibraryRealPath(target, root))
                throw new Error(`Relative reference escapes published libraries in lib/${relativeSource}: ${ref}`);
            if (imports.includes(ref) && !statSync(target).isFile())
                throw new Error(`Relative module import is not a file in lib/${relativeSource}: ${ref}`);
            for (const found of listFiles(target, root)) {
                if (selected.has(found)) continue;
                if (!eligible(found, root)) continue;
                selected.add(found);
                if (TEXT_EXTENSIONS.has(path.extname(found))) queue.push(found);
            }
        }
    }
    return selected;
}

// 不列入「可能漏帶」提示的檔案:程式碼(是否發布由 import 可達性決定)、說明文件、demo 頁與元件 metadata。
const NON_ASSET_EXTENSIONS = new Set(['.js', '.mjs', '.md', '.html']);
const isComponentMetadata = (file) => path.basename(file).endsWith('.manifest.json');

// 裁剪只看得到靜態引用:執行期才組出路徑的資產(地圖資料、圖示…)不會被選入。
// 這裡列出「位於已納入目錄(含其下不含 JS 的純資產子目錄)卻未被選入」的非程式檔,
// 供 publish 印出警告;只回報、不改變選取結果。回傳相對 lib/ 的路徑(已排序)。
export function findUnselectedAssets({ libRoot, selected }) {
    const root = path.resolve(libRoot);
    const includedDirs = new Set([...selected].map((file) => path.dirname(file)));
    const isCode = (file) => ['.js', '.mjs'].includes(path.extname(file).toLowerCase());
    const omitted = new Set();
    for (const directory of includedDirs) {
        let names;
        try { names = readdirSync(directory); } catch { continue; }
        for (const name of names) {
            const entry = path.join(directory, name);
            if (includedDirs.has(entry)) continue;
            let files;
            try { files = listFiles(entry, root); } catch { continue; }
            // 含 JS 的子目錄是另一個元件/模組,不屬於這個目錄的資產
            if (!(files.length === 1 && files[0] === entry) && files.some(isCode)) continue;
            for (const file of files) {
                if (selected.has(file) || !eligible(file, root)) continue;
                if (NON_ASSET_EXTENSIONS.has(path.extname(file).toLowerCase()) || isComponentMetadata(file)) continue;
                omitted.add(path.relative(root, file).split(path.sep).join('/'));
            }
        }
    }
    return [...omitted].sort();
}

export function copySelectedLibrary({ sourceRoot, targetRoot, selected }) {
    for (const source of selected) {
        const relative = path.relative(sourceRoot, source);
        const target = path.join(targetRoot, relative);
        mkdirSync(path.dirname(target), { recursive: true });
        copyFileSync(source, target);
    }
}
