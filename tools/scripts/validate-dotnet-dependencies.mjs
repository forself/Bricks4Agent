#!/usr/bin/env node

import { readFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..', '..');
const policyPath = path.join(__dirname, 'dotnet-dependency-policy.json');

function normalizeProjectPath(projectPath) {
    return projectPath.replaceAll('\\', '/');
}

function parseReferences(projectFileContent, tagName) {
    const pattern = new RegExp(`<${tagName}\\s+Include="([^"]+)"`, 'g');
    return Array.from(projectFileContent.matchAll(pattern)).map((match) => match[1]);
}

function loadPolicy() {
    const policy = JSON.parse(readFileSync(policyPath, 'utf8'));
    if (!Array.isArray(policy.rules)) {
        throw new Error('dotnet dependency policy is missing a rules array.');
    }

    return policy;
}

function resolveProjectReferences(projectPath, references) {
    const projectDir = path.dirname(projectPath);
    return references.map((reference) =>
        normalizeProjectPath(path.relative(repoRoot, path.resolve(projectDir, reference))));
}

function compareAllowed(actualValues, allowedValues, label) {
    const unexpected = actualValues.filter((value) => !allowedValues.has(value));
    return unexpected.map((value) => `${label}: unexpected dependency '${value}'`);
}

function validateRule(rule) {
    const projectPath = path.join(repoRoot, rule.project);
    const source = readFileSync(projectPath, 'utf8');

    const actualProjectReferences = resolveProjectReferences(
        projectPath,
        parseReferences(source, 'ProjectReference')
    );
    const actualPackageReferences = parseReferences(source, 'PackageReference');
    const actualFrameworkReferences = parseReferences(source, 'FrameworkReference');

    const allowedProjectReferences = new Set(rule.allowedProjectReferences ?? []);
    const allowedPackageReferences = new Set(rule.allowedPackageReferences ?? []);
    const allowedFrameworkReferences = new Set(rule.allowedFrameworkReferences ?? []);

    return [
        ...compareAllowed(actualProjectReferences, allowedProjectReferences, 'ProjectReference'),
        ...compareAllowed(actualPackageReferences, allowedPackageReferences, 'PackageReference'),
        ...compareAllowed(actualFrameworkReferences, allowedFrameworkReferences, 'FrameworkReference')
    ];
}

function main() {
    const policy = loadPolicy();
    const findings = [];

    for (const rule of policy.rules) {
        const projectFindings = validateRule(rule);
        findings.push({
            project: rule.project,
            errors: projectFindings
        });
    }

    const failed = findings.filter((entry) => entry.errors.length > 0);

    console.log(`Checked ${findings.length} .csproj dependency policies.`);

    if (failed.length === 0) {
        console.log('All configured projects satisfy the dependency policy.');
        return;
    }

    console.log('');
    console.log('Dependency policy violations:');
    for (const entry of failed) {
        console.log(`- ${entry.project}`);
        for (const error of entry.errors) {
            console.log(`  ${error}`);
        }
    }

    process.exitCode = 1;
}

main();
