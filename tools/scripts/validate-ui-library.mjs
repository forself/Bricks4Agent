#!/usr/bin/env node

import { spawnSync } from 'node:child_process';
import { createReadStream, existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const uiRoot = path.join(repoRoot, 'packages', 'javascript', 'browser', 'ui_components');
const auditScript = path.join(__dirname, 'audit-ui-style-rules.mjs');

const shouldRunBrowserSmoke = process.argv.includes('--browser');
const requireBrowserSmoke = process.argv.includes('--require-browser');

const mimeTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.gif', 'image/gif'],
    ['.html', 'text/html; charset=utf-8'],
    ['.jpg', 'image/jpeg'],
    ['.jpeg', 'image/jpeg'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.mjs', 'text/javascript; charset=utf-8'],
    ['.png', 'image/png'],
    ['.svg', 'image/svg+xml; charset=utf-8'],
    ['.txt', 'text/plain; charset=utf-8'],
    ['.webp', 'image/webp']
]);

const smokeDemos = [
    '/packages/javascript/browser/ui_components/form/TextInput/demo.html',
    '/packages/javascript/browser/ui_components/form/NumberInput/demo.html',
    '/packages/javascript/browser/ui_components/common/ColorPicker/demo.html',
    '/packages/javascript/browser/ui_components/layout/InfoPanel/demo.html',
    '/packages/javascript/browser/ui_components/layout/TabContainer/demo.html',
    '/packages/javascript/browser/ui_components/social/FeedCard/demo.html',
    '/packages/javascript/browser/ui_components/social/Timeline/demo.html',
    '/packages/javascript/browser/ui_components/data/RegionMap/demo.html'
];

function walkFiles(dir, predicate, out = []) {
    for (const entry of readdirSync(dir)) {
        const fullPath = path.join(dir, entry);
        const stats = statSync(fullPath);
        if (stats.isDirectory()) {
            walkFiles(fullPath, predicate, out);
        } else if (predicate(fullPath)) {
            out.push(fullPath);
        }
    }
    return out;
}

function runAudit() {
    const result = spawnSync(process.execPath, [auditScript, '--fail-on-violations'], {
        cwd: repoRoot,
        stdio: 'inherit'
    });

    if (result.status !== 0) {
        throw new Error('UI style audit failed.');
    }
}

async function validateImports() {
    const files = walkFiles(
        uiRoot,
        (filePath) => filePath.endsWith('.js') && !filePath.endsWith('.bak')
    );

    for (const filePath of files) {
        await import(pathToFileURL(filePath).href);
    }

    return files.length;
}

function normalizeReference(reference) {
    const normalized = reference.trim();
    if (!normalized) return null;
    if (normalized.startsWith('data:')) return null;
    if (normalized.startsWith('http://') || normalized.startsWith('https://')) return null;
    if (normalized.startsWith('mailto:') || normalized.startsWith('tel:')) return null;
    if (normalized.startsWith('#')) return null;
    return normalized.split('#')[0].split('?')[0];
}

function resolveReference(demoFile, reference) {
    if (reference.startsWith('/')) {
        return path.join(repoRoot, reference.slice(1).replaceAll('/', path.sep));
    }
    return path.resolve(path.dirname(demoFile), reference);
}

function validateDemoReferences() {
    const demoFiles = walkFiles(uiRoot, (filePath) => path.basename(filePath) === 'demo.html');
    const failures = [];
    let checkedRefs = 0;

    for (const demoFile of demoFiles) {
        const source = readFileSync(demoFile, 'utf8');
        const refs = new Set();

        for (const match of source.matchAll(/\b(?:href|src)=["']([^"']+)["']/g)) {
            const ref = normalizeReference(match[1]);
            if (ref) refs.add(ref);
        }

        for (const match of source.matchAll(/\bimport\s+(?:[^'"]+?\s+from\s+)?["']([^"']+)["']/g)) {
            const ref = normalizeReference(match[1]);
            if (ref) refs.add(ref);
        }

        for (const ref of refs) {
            const targetPath = resolveReference(demoFile, ref);
            checkedRefs += 1;
            if (!existsSync(targetPath)) {
                failures.push({
                    demoFile,
                    ref,
                    targetPath
                });
            }
        }
    }

    if (failures.length > 0) {
        const errorLines = failures
            .slice(0, 20)
            .map(({ demoFile, ref, targetPath }) => `- ${path.relative(repoRoot, demoFile)} -> ${ref} (${targetPath})`);
        const suffix = failures.length > 20 ? `\n...and ${failures.length - 20} more.` : '';
        throw new Error(`Demo reference validation failed:\n${errorLines.join('\n')}${suffix}`);
    }

    return {
        demosChecked: demoFiles.length,
        refsChecked: checkedRefs
    };
}

function createStaticServer(rootDir) {
    const server = createServer((req, res) => {
        try {
            const requestUrl = new URL(req.url ?? '/', 'http://127.0.0.1');
            const relativePath = decodeURIComponent(requestUrl.pathname).replace(/^\/+/, '');
            const resolvedPath = path.resolve(rootDir, relativePath);

            if (!resolvedPath.startsWith(rootDir)) {
                res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
                res.end('Forbidden');
                return;
            }

            if (!existsSync(resolvedPath) || statSync(resolvedPath).isDirectory()) {
                res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
                res.end('Not Found');
                return;
            }

            const contentType = mimeTypes.get(path.extname(resolvedPath).toLowerCase()) || 'application/octet-stream';
            res.writeHead(200, { 'Content-Type': contentType });
            createReadStream(resolvedPath).pipe(res);
        } catch {
            res.writeHead(500, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end('Server Error');
        }
    });

    return new Promise((resolve) => {
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            resolve({
                server,
                baseUrl: `http://127.0.0.1:${address.port}`
            });
        });
    });
}

async function loadChromium() {
    const moduleCandidates = ['@playwright/test', 'playwright'];
    for (const moduleName of moduleCandidates) {
        try {
            const module = await import(moduleName);
            if (module.chromium) {
                return module.chromium;
            }
        } catch {
            // Try next candidate.
        }
    }
    return null;
}

async function runBrowserSmoke() {
    const chromium = await loadChromium();
    if (!chromium) {
        if (requireBrowserSmoke) {
            throw new Error('Browser smoke validation requires Playwright. Run npm install first.');
        }
        return {
            skipped: true,
            reason: 'Playwright is not installed.'
        };
    }

    const { server, baseUrl } = await createStaticServer(repoRoot);
    const browser = await chromium.launch({ headless: true });
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });

    try {
        for (const demoPath of smokeDemos) {
            const page = await context.newPage();
            const errors = [];
            const sameOriginRequestFailures = [];

            page.on('pageerror', (error) => {
                errors.push(`pageerror: ${error.message}`);
            });
            page.on('console', (msg) => {
                if (msg.type() === 'error') {
                    errors.push(`console: ${msg.text()}`);
                }
            });
            page.on('requestfailed', (request) => {
                if (request.url().startsWith(baseUrl)) {
                    sameOriginRequestFailures.push(`${request.method()} ${request.url()} :: ${request.failure()?.errorText ?? 'request failed'}`);
                }
            });

            try {
                await page.goto(`${baseUrl}${demoPath}`, {
                    waitUntil: 'load',
                    timeout: 15000
                });
                await page.waitForTimeout(600);
            } finally {
                await page.close();
            }

            if (errors.length > 0 || sameOriginRequestFailures.length > 0) {
                const details = [...errors, ...sameOriginRequestFailures].join('\n');
                throw new Error(`Browser smoke failed for ${demoPath}\n${details}`);
            }
        }

        return {
            skipped: false,
            demosChecked: smokeDemos.length
        };
    } finally {
        await context.close();
        await browser.close();
        await new Promise((resolve, reject) => {
            server.close((error) => {
                if (error) reject(error);
                else resolve();
            });
        });
    }
}

async function main() {
    console.log('Running UI library validation...\n');

    runAudit();
    const importedFiles = await validateImports();
    const demoReferenceSummary = validateDemoReferences();
    const browserSummary = shouldRunBrowserSmoke || requireBrowserSmoke
        ? await runBrowserSmoke()
        : { skipped: true, reason: 'Browser smoke not requested.' };

    console.log('\nValidation summary:');
    console.log(`- Style audit: passed`);
    console.log(`- JS import smoke: passed (${importedFiles} files)`);
    console.log(`- Demo reference check: passed (${demoReferenceSummary.demosChecked} demos, ${demoReferenceSummary.refsChecked} references)`);
    if (browserSummary.skipped) {
        console.log(`- Browser smoke: skipped (${browserSummary.reason})`);
    } else {
        console.log(`- Browser smoke: passed (${browserSummary.demosChecked} demos)`);
    }
}

main().catch((error) => {
    console.error(`\nValidation failed: ${error.message}`);
    process.exitCode = 1;
});
