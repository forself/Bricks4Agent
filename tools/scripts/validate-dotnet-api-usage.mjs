#!/usr/bin/env node

import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const policyPath = path.join(__dirname, 'dotnet-api-usage-policy.json');

function normalizePath(filePath) {
    return filePath.replaceAll('\\', '/');
}

function loadPolicy() {
    const policy = JSON.parse(readFileSync(policyPath, 'utf8'));
    if (!Array.isArray(policy.rules)) {
        throw new Error('dotnet API usage policy is missing a rules array.');
    }

    return policy;
}

function walkFiles(rootPath, extensions, files = []) {
    if (!statSync(rootPath).isDirectory()) {
        return files;
    }

    for (const entry of readdirSync(rootPath, { withFileTypes: true })) {
        const entryPath = path.join(rootPath, entry.name);
        if (entry.isDirectory()) {
            walkFiles(entryPath, extensions, files);
            continue;
        }

        if (extensions.includes(path.extname(entry.name))) {
            files.push(entryPath);
        }
    }

    return files;
}

function createMatcher(forbiddenRule) {
    if (forbiddenRule.type === 'literal') {
        return {
            test: (line) => line.includes(forbiddenRule.value)
        };
    }

    if (forbiddenRule.type === 'regex') {
        const regex = new RegExp(forbiddenRule.value);
        return {
            test: (line) => regex.test(line)
        };
    }

    throw new Error(`Unsupported forbidden rule type '${forbiddenRule.type}'.`);
}

function validateRule(rule) {
    const findings = [];
    const extensions = rule.extensions ?? ['.cs'];

    for (const relativePath of rule.paths ?? []) {
        const absolutePath = path.join(repoRoot, relativePath);
        const files = walkFiles(absolutePath, extensions);

        for (const filePath of files) {
            const source = readFileSync(filePath, 'utf8');
            const lines = source.split(/\r?\n/);
            const normalizedFilePath = normalizePath(path.relative(repoRoot, filePath));

            for (const forbiddenRule of rule.forbidden ?? []) {
                const matcher = createMatcher(forbiddenRule);

                for (let index = 0; index < lines.length; index++) {
                    if (!matcher.test(lines[index])) {
                        continue;
                    }

                    findings.push({
                        file: normalizedFilePath,
                        line: index + 1,
                        message: forbiddenRule.message
                    });
                }
            }
        }
    }

    return findings;
}

function main() {
    const policy = loadPolicy();
    const results = policy.rules.map((rule) => ({
        name: rule.name,
        findings: validateRule(rule)
    }));

    const failures = results.filter((result) => result.findings.length > 0);

    console.log(`Checked ${results.length} .NET API usage policy scopes.`);

    if (failures.length === 0) {
        console.log('All configured scopes satisfy the API usage policy.');
        return;
    }

    console.log('');
    console.log('API usage policy violations:');

    for (const result of failures) {
        console.log(`- ${result.name}`);
        for (const finding of result.findings) {
            console.log(`  ${finding.file}:${finding.line} ${finding.message}`);
        }
    }

    process.exitCode = 1;
}

main();
