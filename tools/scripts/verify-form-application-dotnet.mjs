import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import {
    existsSync,
    mkdirSync,
    readFileSync,
    readdirSync,
    rmSync,
    rmdirSync,
    writeFileSync,
} from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
    generateFormApplicationBundle,
    schemaToFormApplication,
} from '../../packages/javascript/browser/form-application/index.js';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const testRoot = path.join(repoRoot, '.test-output');
const outputRoot = path.join(testRoot, 'form-application-dotnet-verify');
const baseOrmProject = path.join(
    repoRoot,
    'packages',
    'csharp',
    'database',
    'BaseOrm',
    'net10',
    'BaseOrm.csproj',
);
const sampleSchema = JSON.parse(readFileSync(
    path.join(repoRoot, 'tools', 'form-application-studio', 'sample-schema.json'),
    'utf8',
));

const providers = new Map([
    ['sqlite', ''],
    ['sqlserver', 'Server=ci.invalid;Database=forms;User Id=ci;Password=CI_ONLY_SECRET'],
    ['postgresql', 'Host=ci.invalid;Database=forms;Username=ci;Password=CI_ONLY_SECRET'],
    ['mysql', 'Server=ci.invalid;Database=forms;User ID=ci;Password=CI_ONLY_SECRET'],
]);

function writeText(target, content) {
    mkdirSync(path.dirname(target), { recursive: true });
    writeFileSync(target, content, 'utf8');
}

function projectSource(backendDirectory) {
    const reference = path.relative(backendDirectory, baseOrmProject).replaceAll('\\', '/');
    return `<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="${reference}" />
  </ItemGroup>
</Project>
`;
}

const programSource = `using GeneratedFormApplication.Api;
using GeneratedFormApplication.Generated;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthorization();
builder.Services.AddGeneratedFormApplication(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseAuthorization();
app.MapGeneratedFormApplication();
app.Run();
`;

function buildProvider(provider, connectionString) {
    const applicationId = `ci_${provider}`;
    const definition = schemaToFormApplication(sampleSchema, {
        applicationId,
        displayName: `CI ${provider}`,
        provider,
        connectionString,
    });
    const bundle = generateFormApplicationBundle(definition, {
        connectionString,
        includeConnectionString: false,
    });
    const exportedDefinition = JSON.parse(bundle.files['definition/form-application.json']);
    const artifactText = Object.values(bundle.files).join('\n');

    assert.equal(bundle.definition.persistence.provider, provider);
    assert.equal(exportedDefinition.persistence.provider, provider);
    assert.equal(exportedDefinition.persistence.connection_string, null);
    assert.equal(artifactText.includes('CI_ONLY_SECRET'), false);
    assert.ok(bundle.files[`database/${provider}/001_create_${bundle.definition.table.name}.sql`]);

    const providerRoot = path.join(outputRoot, provider);
    for (const [relativePath, content] of Object.entries(bundle.files)) {
        writeText(path.join(providerRoot, ...relativePath.split('/')), content);
    }

    const backendDirectory = path.join(providerRoot, 'backend');
    const projectPath = path.join(backendDirectory, 'GeneratedFormApplication.csproj');
    writeText(projectPath, projectSource(backendDirectory));
    writeText(path.join(backendDirectory, 'Program.cs'), programSource);

    const command = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
    const result = spawnSync(command, [
        'build',
        projectPath,
        '--configuration',
        'Release',
        '--nologo',
    ], {
        cwd: repoRoot,
        encoding: 'utf8',
    });
    if (result.stdout) process.stdout.write(result.stdout);
    if (result.stderr) process.stderr.write(result.stderr);
    assert.equal(result.status, 0, `${provider} generated backend failed to compile.`);
    console.log(`ok  ${provider}: provider-consistent, secret-free, .NET 10 build passed`);
}

if (existsSync(outputRoot)) {
    throw new Error(`Refusing to replace existing test output: ${outputRoot}`);
}

let outputCreated = false;
try {
    mkdirSync(outputRoot, { recursive: true });
    outputCreated = true;
    for (const [provider, connectionString] of providers) {
        buildProvider(provider, connectionString);
    }
    console.log(`\nGenerated backend verification: ${providers.size}/${providers.size} providers passed.`);
} finally {
    if (outputCreated) rmSync(outputRoot, { recursive: true, force: true });
    if (existsSync(testRoot) && readdirSync(testRoot).length === 0) {
        rmdirSync(testRoot);
    }
}
