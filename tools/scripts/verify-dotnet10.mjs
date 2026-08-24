import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const build = process.argv.includes('--build');
const configurationArg = process.argv.indexOf('--configuration');
const configuration = configurationArg >= 0
    ? process.argv[configurationArg + 1]
    : 'Release';

if (configurationArg >= 0 && !configuration) {
    console.error('--configuration requires a value.');
    process.exit(2);
}

const excludedDirectories = new Set([
    '.git',
    '.test-output',
    'bin',
    'node_modules',
    'obj',
    'out',
]);
const allowedLegacyProjects = new Set([
    'packages/csharp/database/BaseOrm/netfx48/BaseOrm.csproj',
]);

function normalize(relativePath) {
    return relativePath.replaceAll('\\', '/');
}

function discoverProjects(directory) {
    const projects = [];
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        if (entry.isDirectory() && excludedDirectories.has(entry.name)) {
            continue;
        }
        const entryPath = path.join(directory, entry.name);
        if (entry.isDirectory()) {
            projects.push(...discoverProjects(entryPath));
        } else if (entry.isFile() && entry.name.endsWith('.csproj')) {
            projects.push(entryPath);
        }
    }
    return projects;
}

const projects = discoverProjects(repoRoot)
    .map(project => ({
        absolute: project,
        relative: normalize(path.relative(repoRoot, project)),
    }))
    .sort((left, right) => left.relative.localeCompare(right.relative));

const errors = [];
const net10Projects = [];

for (const project of projects) {
    const source = fs.readFileSync(project.absolute, 'utf8');
    if (allowedLegacyProjects.has(project.relative)) {
        if (!source.includes('<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>')) {
            errors.push(`${project.relative}: allowlisted legacy project is no longer targeting .NET Framework 4.8`);
        }
        continue;
    }

    const target = source.match(/<TargetFramework>([^<]+)<\/TargetFramework>/)?.[1]?.trim();
    if (target !== 'net10.0') {
        errors.push(`${project.relative}: expected <TargetFramework>net10.0</TargetFramework>, found ${target ?? 'none'}`);
        continue;
    }
    net10Projects.push(project);
}

for (const legacyProject of allowedLegacyProjects) {
    if (!projects.some(project => project.relative === legacyProject)) {
        errors.push(`${legacyProject}: allowlisted legacy project is missing`);
    }
}

if (errors.length > 0) {
    console.error('The .NET 10 project policy failed:');
    for (const error of errors) {
        console.error(`- ${error}`);
    }
    process.exit(1);
}

console.log(`.NET 10 project policy passed: ${net10Projects.length} net10.0 projects, ${allowedLegacyProjects.size} explicit legacy project.`);

if (!build) {
    process.exit(0);
}

const failedProjects = [];
for (const project of net10Projects) {
    console.log(`Building ${project.relative}`);
    const result = spawnSync(
        'dotnet',
        [
            'build',
            project.absolute,
            '--configuration',
            configuration,
            '--nologo',
            '--verbosity',
            'quiet',
            '--warnaserror',
        ],
        {
            cwd: repoRoot,
            stdio: 'inherit',
        },
    );
    if (result.error) {
        console.error(`Unable to start dotnet for ${project.relative}: ${result.error.message}`);
        failedProjects.push(project.relative);
        continue;
    }
    if (result.status !== 0) {
        console.error(`Build failed: ${project.relative}`);
        failedProjects.push(project.relative);
    }
}

if (failedProjects.length > 0) {
    console.error(`.NET 10 build gate failed: ${failedProjects.length}/${net10Projects.length} projects.`);
    for (const project of failedProjects) {
        console.error(`- ${project}`);
    }
    process.exit(1);
}

console.log(`.NET 10 build gate passed: ${net10Projects.length}/${net10Projects.length} projects.`);
