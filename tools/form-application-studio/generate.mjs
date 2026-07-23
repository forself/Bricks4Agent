#!/usr/bin/env node
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';

import {
    generateFormApplicationBundle,
    schemaToFormApplication,
} from '../../packages/javascript/browser/form-application/index.js';

function parseArgs(argv) {
    const valueOptions = new Set([
        'schema',
        'definition',
        'output',
        'application-id',
        'display-name',
        'provider',
        'connection-string-env',
    ]);
    const values = {};
    for (let index = 0; index < argv.length; index += 1) {
        const token = argv[index];
        if (!token.startsWith('--')) throw new Error(`Unexpected argument: ${token}`);
        const name = token.slice(2);
        if (name === 'help' || name === 'include-connection-string') {
            values[name === 'help' ? 'help' : 'includeConnectionString'] = true;
            continue;
        }
        if (!valueOptions.has(name)) throw new Error(`Unknown option: --${name}.`);
        const value = argv[++index];
        if (!value || value.startsWith('--')) throw new Error(`Missing value for --${name}.`);
        values[name] = value;
    }
    return values;
}

function applicationId(value) {
    const raw = String(value || '')
        .replace(/([a-z0-9])([A-Z])/g, '$1_$2')
        .replace(/[^A-Za-z0-9_]+/g, '_')
        .replace(/^_+|_+$/g, '')
        .toLowerCase();
    const safe = /^[a-z]/.test(raw) ? raw : `form_${raw || 'application'}`;
    return safe.slice(0, 63);
}

function safeOutputPath(root, relativePath) {
    const target = path.resolve(root, relativePath);
    const prefix = `${path.resolve(root)}${path.sep}`;
    if (!target.startsWith(prefix)) throw new Error(`Unsafe output path: ${relativePath}`);
    return target;
}

function writeDeterministic(root, relativePath, content) {
    const target = safeOutputPath(root, relativePath);
    if (existsSync(target)) {
        const current = readFileSync(target, 'utf8');
        if (current !== content) throw new Error(`Refusing to overwrite changed file: ${relativePath}`);
        return 'unchanged';
    }
    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, content, 'utf8');
    return 'created';
}

function usage() {
    console.log(`Form Application materializer (generate-only; never connects to a database)

Usage:
  node tools/form-application-studio/generate.mjs --schema schema.json --output ./out/customer-form [options]
  node tools/form-application-studio/generate.mjs --definition form-application.json --output ./out/customer-form [options]

Options:
  --help                            Show this help and exit
  --application-id <id>             Required with --schema unless schema provides application_id/name/table
  --display-name <name>             Optional display label
  --provider <provider>             sqlite | sqlserver | postgresql | mysql (default sqlite)
  --connection-string-env <name>    Read the secret from this environment variable
  --include-connection-string       Put the secret only in backend/appsettings.Development.json

An empty or missing environment value always selects local SQLite under data/<application_id>.db.
Existing files are never overwritten unless their content is byte-identical.`);
}

const args = parseArgs(process.argv.slice(2));
if (args.help) {
    usage();
} else if ((!args.schema && !args.definition) || (args.schema && args.definition) || !args.output) {
    usage();
    process.exitCode = 2;
} else {
    const connectionString = args['connection-string-env']
        ? String(process.env[args['connection-string-env']] || '')
        : '';
    let definition;
    if (args.definition) {
        definition = JSON.parse(readFileSync(path.resolve(args.definition), 'utf8'));
    } else {
        const schema = JSON.parse(readFileSync(path.resolve(args.schema), 'utf8'));
        const id = applicationId(args['application-id'] || schema.application_id || schema.name || schema.table);
        definition = schemaToFormApplication(schema, {
            applicationId: id,
            displayName: args['display-name'] || schema.display_name || schema.name || schema.table || id,
            provider: args.provider || 'sqlite',
            connectionString,
        });
    }

    const bundle = generateFormApplicationBundle(definition, {
        connectionString,
        includeConnectionString: args.includeConnectionString === true,
    });
    const outputRoot = path.resolve(args.output);
    mkdirSync(outputRoot, { recursive: true });
    const results = Object.entries(bundle.files).map(([relativePath, content]) => ({
        path: relativePath,
        status: writeDeterministic(outputRoot, relativePath, content),
    }));
    const manifest = {
        schema_version: 1,
        application_id: bundle.definition.application_id,
        provider: bundle.sql.provider,
        connection_string_name: bundle.definition.persistence.connection_string_name,
        sqlite_fallback: bundle.definition.persistence.sqlite_file,
        preview_only: true,
        files: results.map((entry) => entry.path),
    };
    writeDeterministic(outputRoot, 'form-application.bundle-manifest.json', `${JSON.stringify(manifest, null, 2)}\n`);
    results.forEach((entry) => console.log(`${entry.status.padEnd(9)} ${entry.path}`));
    console.log(`generated  ${path.relative(process.cwd(), outputRoot) || '.'}`);
    console.log('database   not connected; review SQL and approve the write plan before applying');
}
