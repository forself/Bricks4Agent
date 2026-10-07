// 生成器使用的固定路徑。一律從本檔位置推算 repo 根目錄（或映像中的 repo 子集根目錄），
// 不讀環境變數，避免呼叫端以環境改變讀寫位置。
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const GENERATION_DIR = path.dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = path.resolve(GENERATION_DIR, '..', '..');

export const BROWSER_ROOT = path.join(REPO_ROOT, 'packages', 'javascript', 'browser');
export const UI_COMPONENTS_DIR = path.join(BROWSER_ROOT, 'ui_components');
export const PAGE_GENERATOR_DIR = path.join(BROWSER_ROOT, 'page-generator');
export const CATALOG_PATH = path.join(UI_COMPONENTS_DIR, 'metadata', 'component-catalog.json');
export const MATRIX_PATH = path.join(UI_COMPONENTS_DIR, 'metadata', 'generator-support-matrix.json');

export const PAGE_GEN_PATH = path.join(REPO_ROOT, 'tools', 'page-gen.js');
export const DEFINITION_TEMPLATE_LIB_PATH = path.join(REPO_ROOT, 'tools', 'lib', 'definition-template.js');

export const SHELL_DIR = path.join(REPO_ROOT, 'templates', 'definition-site');
export const GOLDEN_EXAMPLE_PATH = path.join(GENERATION_DIR, 'examples', 'golden.definition-template.json');

// 映像建置時由 catalog-summary.mjs --out 預先產生；不納入版本控制
export const PREGENERATED_SUMMARY_PATH = path.join(GENERATION_DIR, 'catalog-summary.json');

/**
 * 生成器執行所需的 repo 子集（相對 repo 根目錄，以 / 分隔）。worker 映像只需複製這些路徑；
 * ui_components 可省略 data/ 與 refresource/。build-site.test.mjs 以此清單實際跑一次 CLI。
 */
export const REPO_SUBSET = Object.freeze([
    'tools/generation',
    'tools/page-gen.js',
    'tools/lib/definition-template.js',
    'tools/package.json',
    'packages/javascript/browser/package.json',
    'packages/javascript/browser/page-generator',
    'packages/javascript/browser/ui_components',
    'templates/definition-site'
]);

export const GENERATOR_VERSION = 'definition-site/1.1.0';
export const VALIDATOR_VERSION = 'definition-validator/1.3.0';
export const SUMMARY_VERSION = '2';
export const PACKAGE_FORMAT = 'definition-site-v1';
