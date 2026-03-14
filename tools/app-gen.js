#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const {
    assertValidDefinitionTemplate,
    isDefinitionTemplate,
    resolveTemplateEnvelope,
    extractAppEntry
} = require('./lib/definition-template.js');
const {
    validateAppGenerationSupport,
    materializeAppProject
} = require('./lib/app-generator.js');

function parseArgs(argv) {
    const args = {
        def: null,
        app: null,
        output: null,
        validate: false,
        help: false
    };

    const rawArgs = argv.slice(2);
    for (let i = 0; i < rawArgs.length; i++) {
        const arg = rawArgs[i];
        switch (arg) {
            case '--def':
                args.def = rawArgs[++i];
                break;
            case '--app':
                args.app = rawArgs[++i];
                break;
            case '--output':
                args.output = rawArgs[++i];
                break;
            case '--validate':
                args.validate = true;
                break;
            case '--help':
            case '-h':
                args.help = true;
                break;
            default:
                break;
        }
    }

    return args;
}

function printHelp() {
    const text = `
app-gen.js - DefinitionTemplate app backend generator

Usage:
  node tools/app-gen.js --def <path> --app <id> --output <dir>
  cat site-definition.json | node tools/app-gen.js --app <id> --output <dir>

Options:
  --def <path>       DefinitionTemplate JSON path
  --app <id>         App id inside definitions.apps
  --output <dir>     Output root directory
  --validate         Validate only
  --help, -h         Show help

Notes:
  - This phase always generates a backend skeleton.
  - If app.frontend.pageRefs is present, frontend placeholder pages and routes are generated too.
  - The current implementation supports backend.hosting.mode = "api".
  - Service registrations are limited to the built-in SPA template service pairs.
`;
    process.stderr.write(`${text.trim()}\n`);
}

function hasStdinPipe() {
    return !process.stdin.isTTY;
}

function readJsonFromFile(filePath) {
    const resolved = path.resolve(filePath);
    if (!fs.existsSync(resolved)) {
        throw new Error(`File not found: ${resolved}`);
    }

    return JSON.parse(fs.readFileSync(resolved, 'utf8'));
}

function readJsonFromStdin() {
    return new Promise((resolve, reject) => {
        let data = '';
        process.stdin.setEncoding('utf8');
        process.stdin.on('data', chunk => { data += chunk; });
        process.stdin.on('end', () => {
            try {
                resolve(JSON.parse(data));
            } catch (error) {
                reject(new Error(`stdin JSON parse failed: ${error.message}`));
            }
        });
        process.stdin.on('error', reject);
    });
}

function outputJson(data) {
    process.stdout.write(`${JSON.stringify(data, null, 2)}\n`);
}

function outputError(errors) {
    outputJson({ success: false, errors });
}

async function normalizeInput(payload, appIdOverride) {
    const envelope = resolveTemplateEnvelope(payload);
    const template = envelope?.template || payload?.definitionTemplate || payload?.template || payload;
    const selectedAppId = appIdOverride || payload?.appId || null;

    if (!isDefinitionTemplate(template)) {
        throw new Error('Input must be a DefinitionTemplate document');
    }

    const templateValidation = await assertValidDefinitionTemplate(template);
    const appEntry = extractAppEntry(template, selectedAppId);
    const support = validateAppGenerationSupport(appEntry);

    return {
        template,
        appEntry,
        templateStats: templateValidation.stats,
        support
    };
}

async function main() {
    const args = parseArgs(process.argv);

    if (args.help) {
        printHelp();
        process.exit(0);
    }

    let payload;
    try {
        if (args.def) {
            payload = readJsonFromFile(args.def);
        } else if (hasStdinPipe()) {
            payload = await readJsonFromStdin();
        } else {
            printHelp();
            process.exit(1);
        }
    } catch (error) {
        outputError([error.message]);
        process.exit(1);
    }

    let normalized;
    try {
        normalized = await normalizeInput(payload, args.app);
    } catch (error) {
        outputError(Array.isArray(error.errors) ? error.errors : [error.message]);
        process.exit(1);
    }

    if (args.validate) {
        if (!normalized.support.valid) {
            outputError(normalized.support.errors);
            process.exit(1);
        }

        outputJson({
            success: true,
            message: 'DefinitionTemplate app is valid for minimal backend generation',
            appId: normalized.appEntry.id,
            templateStats: normalized.templateStats
        });
        process.exit(0);
    }

    if (!args.output) {
        outputError(['Missing --output <dir>']);
        process.exit(1);
    }

    if (!normalized.support.valid) {
        outputError(normalized.support.errors);
        process.exit(1);
    }

    try {
        const result = materializeAppProject(normalized.template, normalized.appEntry.id, args.output);
        outputJson({
            success: true,
            appId: result.appId,
            templateStats: normalized.templateStats,
            files: [
                result.csprojPath,
                result.generatedFilePath,
                ...(result.routesFilePath ? [result.routesFilePath] : []),
                ...result.generatedPagePaths,
                path.join(result.projectRoot, 'definition-template.json'),
                path.join(result.projectRoot, 'app-selection.json')
            ]
        });
    } catch (error) {
        outputError(Array.isArray(error.errors) ? error.errors : [error.message]);
        process.exit(1);
    }
}

main().catch(error => {
    outputError([error.message || 'Unknown error']);
    process.exit(1);
});
