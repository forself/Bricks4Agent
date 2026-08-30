'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { extractAppEntry } = require('./definition-template.js');

const TEMPLATE_BACKEND_DIR = path.resolve(__dirname, '../../templates/spa/backend');
const TEMPLATE_FRONTEND_DIR = path.resolve(__dirname, '../../templates/spa/frontend');
const PAGE_GENERATOR_RUNTIME_DIR = path.resolve(__dirname, '../../packages/javascript/browser/page-generator');
const UI_COMPONENTS_RUNTIME_DIR = path.resolve(__dirname, '../../packages/javascript/browser/ui_components');
const SKIP_DIRECTORIES = new Set(['bin', 'obj']);
const JWT_KEY_PLACEHOLDER = '"Key": ""';
const SUPPORTED_SERVICE_PAIRS = new Map([
    ['SpaApi.Services.IUserService', 'SpaApi.Services.UserService'],
    ['SpaApi.Services.IAuthService', 'SpaApi.Services.AuthService']
]);
// 與 PageGenerator 相同的 JS IdentifierName 規則（ID_Start／ID_Continue 加上 $ _ 與 ZWNJ／ZWJ）
const JS_IDENTIFIER_PATTERN = /^[\p{ID_Start}$_][\p{ID_Continue}$\u200C\u200D]*$/u;

function ensureArray(value) {
    return Array.isArray(value) ? value : [];
}

function toPascalCase(value) {
    return String(value || '')
        .split(/[^A-Za-z0-9]+/)
        .filter(Boolean)
        .map(part => part.charAt(0).toUpperCase() + part.slice(1))
        .join('');
}

function toIdentifier(value, fallback = 'GeneratedNode') {
    const normalized = String(value || fallback)
        .replace(/[^A-Za-z0-9_]+/g, '_')
        .replace(/_{2,}/g, '_')
        .replace(/^_+|_+$/g, '');

    if (!normalized) {
        return fallback;
    }

    return /^[0-9]/.test(normalized) ? `N_${normalized}` : normalized;
}

function toRouteSegment(value, fallback = 'module') {
    const normalized = String(value || fallback)
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, '-')
        .replace(/-{2,}/g, '-')
        .replace(/^-+|-+$/g, '');

    return normalized || fallback;
}

function assertEmittableIdentifier(value, what) {
    if (typeof value !== 'string' || !JS_IDENTIFIER_PATTERN.test(value)) {
        throw new Error(
            `${what} is emitted as a JavaScript identifier and a file name, and must match ${JS_IDENTIFIER_PATTERN}, got ${JSON.stringify(value)}`
        );
    }

    return value;
}

/**
 * 組合出來的檔案路徑必須確認落點仍在預期目錄內：
 * Windows 要一併涵蓋磁碟機代號、反斜線與大小寫不敏感的前綴比較。
 */
function resolveInsideDirectory(baseDir, relativePath, what) {
    const base = path.resolve(baseDir);
    const resolved = path.resolve(base, relativePath);
    const prefix = base.endsWith(path.sep) ? base : `${base}${path.sep}`;
    const normalize = value => (process.platform === 'win32' ? value.toLowerCase() : value);

    if (!normalize(resolved).startsWith(normalize(prefix))) {
        throw new Error(
            `${what} must stay inside ${base}, got ${JSON.stringify(relativePath)} (resolved to ${resolved})`
        );
    }

    return resolved;
}

function resolvePageClassName(pageEntry) {
    const pageId = pageEntry?.id || '(missing-id)';
    const declaredName = pageEntry?.definition?.name;
    const className = declaredName || `${toPascalCase(pageId)}Page`;

    return assertEmittableIdentifier(
        className,
        `page ${pageId}: ${declaredName ? 'definition.name' : 'derived page class name'}`
    );
}

function deriveEntityKey(pageEntry) {
    const definition = pageEntry.definition || {};
    const api = definition.api || {};
    return api.list || api.get || api.create || api.update || api.delete || definition.name || pageEntry.id;
}

function buildGeneratedRoutePath(pageEntry) {
    const basePath = `/${toRouteSegment(pageEntry.id, 'page')}`;
    return pageEntry.definition?.type === 'detail' ? `${basePath}/:id` : basePath;
}

function ensureLeadingSlash(value) {
    if (!value) {
        return '/';
    }

    return value.startsWith('/') ? value : `/${value}`;
}

function escapeCSharpString(value) {
    return String(value)
        .replace(/\\/g, '\\\\')
        .replace(/"/g, '\\"')
        .replace(/\r/g, '\\r')
        .replace(/\n/g, '\\n')
        .replace(/\u2028/g, '\\u2028')
        .replace(/\u2029/g, '\\u2029');
}

function serializeStringArray(values) {
    if (!values || values.length === 0) {
        return 'Array.Empty<string>()';
    }

    return `new[] { ${values.map(value => `"${escapeCSharpString(value)}"`).join(', ')} }`;
}

function serializeJs(value) {
    return JSON.stringify(value, null, 4);
}

function isSupportedRuntimePageType(type) {
    return type === 'form' || type === 'list' || type === 'detail';
}

function readNumericOrStringFeature(featureValues, key, fallback) {
    const value = featureValues?.[key];
    if (typeof value === 'number' || typeof value === 'string') {
        return String(value);
    }

    return fallback;
}

function copyDirectory(sourceDir, targetDir) {
    fs.mkdirSync(targetDir, { recursive: true });

    for (const entry of fs.readdirSync(sourceDir, { withFileTypes: true })) {
        if (SKIP_DIRECTORIES.has(entry.name)) {
            continue;
        }

        const sourcePath = path.join(sourceDir, entry.name);
        const targetPath = path.join(targetDir, entry.name);

        if (entry.isDirectory()) {
            copyDirectory(sourcePath, targetPath);
            continue;
        }

        fs.copyFileSync(sourcePath, targetPath);
    }
}

/**
 * 與 templates/spa/scripts/create-project.js 相同的簽章金鑰置換：
 * 產生的專案必須帶自己的 Jwt:Key，不能只靠後端的環境判斷把關。
 */
function injectBackendJwtKey(backendDir) {
    const appSettingsPath = path.join(backendDir, 'appsettings.json');
    if (!fs.existsSync(appSettingsPath)) {
        return null;
    }

    const content = fs.readFileSync(appSettingsPath, 'utf8');
    if (!content.includes(JWT_KEY_PLACEHOLDER)) {
        return null;
    }

    const jwtKey = crypto.randomBytes(48).toString('base64');
    fs.writeFileSync(
        appSettingsPath,
        content.replace(JWT_KEY_PLACEHOLDER, `"Key": ${JSON.stringify(jwtKey)}`),
        'utf8'
    );

    return appSettingsPath;
}

function copyPageGeneratorRuntime(targetDir) {
    fs.mkdirSync(targetDir, { recursive: true });

    for (const entry of fs.readdirSync(PAGE_GENERATOR_RUNTIME_DIR, { withFileTypes: true })) {
        if (!entry.isFile() || path.extname(entry.name) !== '.js') {
            continue;
        }

        const sourcePath = path.join(PAGE_GENERATOR_RUNTIME_DIR, entry.name);
        const targetPath = path.join(targetDir, entry.name);
        fs.copyFileSync(sourcePath, targetPath);
    }
}

function materializeFrontendRuntime(frontendDir) {
    const runtimeDir = path.join(frontendDir, 'runtime');
    const pageGeneratorDir = path.join(runtimeDir, 'page-generator');
    const uiComponentsDir = path.join(runtimeDir, 'ui_components');

    copyPageGeneratorRuntime(pageGeneratorDir);
    copyDirectory(UI_COMPONENTS_RUNTIME_DIR, uiComponentsDir);
}

function getSelectedPageEntries(template, appEntry) {
    const pagesById = new Map(ensureArray(template?.definitions?.pages).map(page => [page.id, page]));
    return ensureArray(appEntry?.app?.frontend?.pageRefs)
        .map(pageRef => pagesById.get(pageRef))
        .filter(Boolean);
}

function validateAppGenerationSupport(appEntry, template = null) {
    const errors = [];

    if (!appEntry || typeof appEntry !== 'object' || typeof appEntry.app !== 'object') {
        return {
            valid: false,
            errors: ['Invalid app entry']
        };
    }

    const appId = appEntry.id || '(missing-id)';
    const backend = appEntry.app.backend || {};
    const hosting = backend.hosting || {};

    if (hosting.mode !== 'api') {
        errors.push(`app ${appId}: backend.hosting.mode currently only supports "api"`);
    }

    for (const middleware of ensureArray(backend.middleware)) {
        errors.push(`app ${appId}: backend.middleware is not implemented yet (${middleware.id || 'missing-id'})`);
    }

    for (const service of ensureArray(backend.services)) {
        if (service.condition) {
            errors.push(`app ${appId}: service ${service.id} condition is not implemented yet`);
        }

        if (service.implementation?.kind !== 'type') {
            errors.push(`app ${appId}: service ${service.id} only supports implementation.kind = "type"`);
            continue;
        }

        const expectedImplementation = SUPPORTED_SERVICE_PAIRS.get(service.contract);
        if (!expectedImplementation || expectedImplementation !== service.implementation.reference) {
            errors.push(
                `app ${appId}: service ${service.id} must use one of the supported template service pairs`
            );
        }
    }

    for (const policy of ensureArray(backend.policies)) {
        if (policy.kind !== 'role') {
            errors.push(`app ${appId}: policy ${policy.id} only supports kind = "role"`);
            continue;
        }

        if (!Array.isArray(policy.roles) || policy.roles.length === 0) {
            errors.push(`app ${appId}: policy ${policy.id} must declare at least one role`);
        }
    }

    if (template) {
        for (const pageEntry of getSelectedPageEntries(template, appEntry)) {
            const pageType = pageEntry.definition?.type;
            if (!isSupportedRuntimePageType(pageType)) {
                errors.push(
                    `app ${appId}: frontend page ${pageEntry.id} uses unsupported runtime page type "${pageType}"`
                );
            }
        }
    }

    return {
        valid: errors.length === 0,
        errors
    };
}

function buildGeneratedCompositionSource(appEntry) {
    const appId = appEntry.id;
    const app = appEntry.app;
    const backend = app.backend;
    const routeGroups = ensureArray(backend.routeGroups);
    const policies = ensureArray(backend.policies);
    const policyById = new Map(policies.map(policy => [policy.id, policy]));
    const modulesById = new Map(ensureArray(backend.endpointModules).map(module => [module.id, module]));
    const startupHooks = ensureArray(backend.startupHooks);

    const lines = [
        'using System;',
        'using Microsoft.AspNetCore.Authorization;',
        'using Microsoft.AspNetCore.Builder;',
        'using Microsoft.AspNetCore.Http;',
        'using Microsoft.Extensions.Configuration;',
        'using Microsoft.Extensions.DependencyInjection;',
        'using Microsoft.Extensions.Logging;',
        '',
        'namespace SpaApi.Generated;',
        '',
        'public static class DefinitionTemplateGeneratedComposition',
        '{',
        '    public static void BeforeBuild(WebApplicationBuilder builder)',
        '    {'
    ];

    const beforeBuildHooks = startupHooks.filter(hook => hook.phase === 'beforeBuild');
    if (beforeBuildHooks.length === 0) {
        lines.push('    }');
    } else {
        for (const hook of beforeBuildHooks) {
            lines.push(`        Console.WriteLine("DefinitionTemplate beforeBuild hook: ${escapeCSharpString(hook.id)} (${escapeCSharpString(hook.reference)})");`);
        }
        lines.push('    }');
    }

    lines.push('');
    lines.push('    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)');
    lines.push('    {');

    if (policies.length > 0) {
        lines.push('        services.AddAuthorization(options =>');
        lines.push('        {');
        for (const policy of policies) {
            lines.push(`            options.AddPolicy("${escapeCSharpString(policy.id)}", policy => policy.RequireRole(${serializeStringArray(policy.roles)}));`);
        }
        lines.push('        });');
    }

    for (const service of ensureArray(backend.services)) {
        const lifetimeMethod = {
            singleton: 'AddSingleton',
            scoped: 'AddScoped',
            transient: 'AddTransient'
        }[service.lifetime];
        lines.push(
            `        services.${lifetimeMethod}<global::${service.contract}, global::${service.implementation.reference}>();`
        );
    }

    lines.push('    }');
    lines.push('');
    lines.push('    public static void ConfigureMiddleware(WebApplication app)');
    lines.push('    {');
    lines.push('    }');
    lines.push('');
    lines.push('    public static void MapEndpoints(WebApplication app)');
    lines.push('    {');
    lines.push('        app.MapGet("/api/__definition-template/app", () => Results.Ok(new');
    lines.push('        {');
    lines.push(`            appId = "${escapeCSharpString(appId)}",`);
    lines.push(`            name = "${escapeCSharpString(app.identity?.name || appId)}",`);
    lines.push(`            hostingMode = "${escapeCSharpString(backend.hosting?.mode || 'api')}",`);
    lines.push(`            profiles = ${serializeStringArray(ensureArray(app.profiles))},`);
    lines.push(`            routeGroups = ${serializeStringArray(routeGroups.map(group => group.id))}`);
    lines.push('        })).WithName("GeneratedDefinitionTemplateApp");');
    lines.push('');

    for (const routeGroup of routeGroups) {
        const groupIdentifier = toIdentifier(routeGroup.id, 'RouteGroup');
        const prefix = ensureLeadingSlash(routeGroup.prefix);
        const moduleRefs = ensureArray(routeGroup.moduleRefs);
        lines.push(`        var ${groupIdentifier} = app.MapGroup("${escapeCSharpString(prefix)}");`);
        for (const policyId of ensureArray(routeGroup.policies)) {
            if (policyById.has(policyId)) {
                lines.push(`        ${groupIdentifier}.RequireAuthorization("${escapeCSharpString(policyId)}");`);
            }
        }

        lines.push(
            `        ${groupIdentifier}.MapGet("/", () => Results.Ok(new { appId = "${escapeCSharpString(appId)}", routeGroup = "${escapeCSharpString(routeGroup.id)}", prefix = "${escapeCSharpString(prefix)}", modules = ${serializeStringArray(moduleRefs)} })).WithName("Generated${groupIdentifier}Index");`
        );

        for (const moduleRef of moduleRefs) {
            const module = modulesById.get(moduleRef);
            const moduleSegment = toRouteSegment(module?.reference || moduleRef, toRouteSegment(moduleRef, 'module'));
            lines.push(
                `        ${groupIdentifier}.MapGet("/${escapeCSharpString(moduleSegment)}", () => Results.Ok(new { appId = "${escapeCSharpString(appId)}", routeGroup = "${escapeCSharpString(routeGroup.id)}", moduleId = "${escapeCSharpString(moduleRef)}", source = "${escapeCSharpString(module?.source || 'module')}", reference = "${escapeCSharpString(module?.reference || moduleRef)}" })).WithName("Generated${groupIdentifier}${toIdentifier(moduleRef, 'Module')}");`
            );
        }

        lines.push('');
    }

    lines.push('    }');
    lines.push('');
    lines.push('    public static void BeforeRun(WebApplication app)');
    lines.push('    {');

    const beforeRunHooks = startupHooks.filter(hook => hook.phase === 'beforeRun');
    for (const hook of beforeRunHooks) {
        lines.push(`        app.Logger.LogInformation("DefinitionTemplate beforeRun hook: {HookId} ({Reference})", "${escapeCSharpString(hook.id)}", "${escapeCSharpString(hook.reference)}");`);
    }

    lines.push('    }');
    lines.push('');
    lines.push('    public static void RegisterLifetimeHooks(WebApplication app)');
    lines.push('    {');

    const afterStartHooks = startupHooks.filter(hook => hook.phase === 'afterStart');
    if (afterStartHooks.length > 0) {
        lines.push('        app.Lifetime.ApplicationStarted.Register(() =>');
        lines.push('        {');
        for (const hook of afterStartHooks) {
            lines.push(`            app.Logger.LogInformation("DefinitionTemplate afterStart hook: {HookId} ({Reference})", "${escapeCSharpString(hook.id)}", "${escapeCSharpString(hook.reference)}");`);
        }
        lines.push('        });');
    }

    const onStoppingHooks = startupHooks.filter(hook => hook.phase === 'onStopping');
    if (onStoppingHooks.length > 0) {
        lines.push('        app.Lifetime.ApplicationStopping.Register(() =>');
        lines.push('        {');
        for (const hook of onStoppingHooks) {
            lines.push(`            app.Logger.LogInformation("DefinitionTemplate onStopping hook: {HookId} ({Reference})", "${escapeCSharpString(hook.id)}", "${escapeCSharpString(hook.reference)}");`);
        }
        lines.push('        });');
    }

    lines.push('    }');
    lines.push('}');

    return `${lines.join('\n')}\n`;
}

function buildGeneratedFrontendPageSource(pageEntry) {
    const pageDefinition = pageEntry.definition || {};
    const className = resolvePageClassName(pageEntry);
    const runtimeMode = isSupportedRuntimePageType(pageDefinition.type) ? pageDefinition.type : null;

    return `import { DefinitionRuntimePage } from '../../runtime/DefinitionRuntimePage.js';

const definition = ${serializeJs(pageDefinition)};
const pageId = ${serializeJs(pageEntry.id)};
const runtimeMode = ${serializeJs(runtimeMode)};

export class ${className} extends DefinitionRuntimePage {}

${className}.definition = definition;
${className}.pageId = pageId;
${className}.mode = runtimeMode;

export default ${className};
`;
}

function buildGeneratedRoutesSource(pageEntries) {
    const imports = [];
    const routeSpecs = pageEntries.map(pageEntry => {
        const className = resolvePageClassName(pageEntry);
        return {
            pageEntry,
            className,
            pageType: pageEntry.definition?.type || 'form',
            path: buildGeneratedRoutePath(pageEntry),
            entityKey: deriveEntityKey(pageEntry)
        };
    });
    const entityRoutes = new Map();
    const routes = [];

    for (const routeSpec of routeSpecs) {
        const { pageEntry, className } = routeSpec;
        imports.push(`import { ${className} } from './${className}.js';`);

        if (!entityRoutes.has(routeSpec.entityKey)) {
            entityRoutes.set(routeSpec.entityKey, []);
        }
        entityRoutes.get(routeSpec.entityKey).push(routeSpec);
    }

    for (const routeSpec of routeSpecs) {
        const siblings = entityRoutes.get(routeSpec.entityKey) || [];
        const detailRoute = siblings.find(item => item.pageType === 'detail');
        const formRoute = siblings.find(item => item.pageType === 'form');
        const actionRoutes = {};

        if (routeSpec.pageType === 'list') {
            if (detailRoute) {
                actionRoutes.view = detailRoute.path;
            } else if (formRoute) {
                actionRoutes.view = `${formRoute.path}?id=:id`;
            }

            if (formRoute) {
                actionRoutes.edit = `${formRoute.path}?id=:id`;
            }
        }

        if (routeSpec.pageType === 'detail' && formRoute) {
            actionRoutes.edit = `${formRoute.path}?id=:id`;
        }

        routes.push(`    {
        path: ${serializeJs(routeSpec.path)},
        component: ${routeSpec.className},
        meta: {
            title: ${serializeJs(routeSpec.pageEntry.definition?.description || routeSpec.className.replace(/Page$/, ''))},
            requiresAuth: false,
            generated: true,
            definitionId: ${serializeJs(routeSpec.pageEntry.id)},
            actionRoutes: ${serializeJs(actionRoutes)}
        }
    }`);
    }

    return `${imports.join('\n')}

export const generatedRoutes = [
${routes.join(',\n')}
];

export default generatedRoutes;
`;
}

function buildProjectManifest(appEntry, outputRoot, hasFrontend) {
    const app = appEntry.app;
    const featureValues = app.configuration?.featureValues || {};
    const apiPort = readNumericOrStringFeature(featureValues, 'apiPort', '5001');
    const frontendPort = readNumericOrStringFeature(featureValues, 'frontendPort', '3000');

    const manifest = {
        project: {
            name: appEntry.id,
            displayName: app.identity?.name || appEntry.id,
            description: `Generated from DefinitionTemplate app ${appEntry.id}`,
            outputDir: path.resolve(outputRoot)
        },
        backend: {
            dbName: `${appEntry.id}.db`,
            apiPort
        },
        definitionTemplate: {
            appId: appEntry.id,
            pageRefs: ensureArray(app.frontend?.pageRefs)
        }
    };

    if (hasFrontend) {
        manifest.frontend = {
            devPort: frontendPort,
            apiBaseUrl: `https://localhost:${apiPort}/api`,
            runtime: {
                mode: 'definition-runtime',
                entry: 'frontend/runtime/DefinitionRuntimePage.js',
                pageGeneratorDir: 'frontend/runtime/page-generator',
                uiComponentsDir: 'frontend/runtime/ui_components',
                generatedRoutes: 'frontend/pages/generated/routes.generated.js'
            }
        };
    }

    return manifest;
}

function buildProjectReadme(appEntry, result) {
    const lines = [
        `# ${appEntry.app.identity?.name || appEntry.id}`,
        '',
        'Generated from DefinitionTemplate.',
        '',
        '## Paths',
        '',
        `- App Id: \`${appEntry.id}\``,
        `- Backend: \`${result.backendDir}\``
    ];

    if (result.frontendDir) {
        lines.push(`- Frontend: \`${result.frontendDir}\``);
        lines.push(`- Frontend runtime: \`${path.join(result.frontendDir, 'runtime')}\``);
    }

    lines.push(`- Definition Snapshot: \`${path.join(result.projectRoot, 'definition-template.json')}\``);
    lines.push('');
    lines.push('## Generated Assets');
    lines.push('');
    lines.push(`- Backend composition: \`${result.generatedFilePath}\``);

    if (result.routesFilePath) {
        lines.push(`- Frontend generated routes: \`${result.routesFilePath}\``);
    }

    if (result.generatedPagePaths.length > 0) {
        for (const pagePath of result.generatedPagePaths) {
            lines.push(`- Frontend page: \`${pagePath}\``);
        }
    }

    if (result.frontendDir) {
        lines.push('');
        lines.push('## Frontend Runtime');
        lines.push('');
        lines.push('- Generated frontend pages are thin DefinitionRuntimePage wrappers.');
        lines.push('- Runtime renderer lives under `frontend/runtime/page-generator`.');
        lines.push('- Shared UI components live under `frontend/runtime/ui_components`.');
    }

    return `${lines.join('\n')}\n`;
}

function materializeAppBackendProject(template, appId, outputRoot) {
    const appEntry = extractAppEntry(template, appId);
    const support = validateAppGenerationSupport(appEntry, template);
    if (!support.valid) {
        const error = new Error(support.errors.join('; '));
        error.errors = support.errors;
        throw error;
    }

    const projectRoot = resolveInsideDirectory(
        outputRoot,
        appEntry.id,
        `app ${appEntry.id}: generated project directory`
    );
    const backendDir = path.join(projectRoot, 'backend');

    if (fs.existsSync(projectRoot)) {
        throw new Error(`Output path already exists: ${projectRoot}`);
    }

    copyDirectory(TEMPLATE_BACKEND_DIR, backendDir);
    injectBackendJwtKey(backendDir);

    const projectName = toPascalCase(appEntry.id || appEntry.app.identity?.name || 'GeneratedApp') || 'GeneratedApp';
    const oldCsprojPath = path.join(backendDir, 'SpaApi.csproj');
    const csprojPath = path.join(backendDir, `${projectName}Backend.csproj`);
    fs.renameSync(oldCsprojPath, csprojPath);

    const generatedDir = path.join(backendDir, 'Generated');
    const generatedFilePath = path.join(generatedDir, 'DefinitionTemplateGeneratedComposition.cs');
    fs.mkdirSync(generatedDir, { recursive: true });
    fs.writeFileSync(generatedFilePath, buildGeneratedCompositionSource(appEntry), 'utf8');

    fs.writeFileSync(
        path.join(projectRoot, 'definition-template.json'),
        JSON.stringify(template, null, 2),
        'utf8'
    );
    fs.writeFileSync(
        path.join(projectRoot, 'app-selection.json'),
        JSON.stringify({ appId: appEntry.id }, null, 2),
        'utf8'
    );

    return {
        appId: appEntry.id,
        projectRoot,
        backendDir,
        csprojPath,
        generatedFilePath
    };
}

function materializeAppProject(template, appId, outputRoot) {
    const appEntry = extractAppEntry(template, appId);
    const pageRefs = ensureArray(appEntry.app.frontend?.pageRefs);
    const selectedPages = getSelectedPageEntries(template, appEntry);
    const pageClassNames = selectedPages.map(pageEntry => resolvePageClassName(pageEntry));

    const backendResult = materializeAppBackendProject(template, appId, outputRoot);

    let frontendDir = null;
    let runtimeDir = null;
    let routesFilePath = null;
    const generatedPagePaths = [];

    if (pageRefs.length > 0) {
        frontendDir = path.join(backendResult.projectRoot, 'frontend');
        copyDirectory(TEMPLATE_FRONTEND_DIR, frontendDir);
        materializeFrontendRuntime(frontendDir);
        runtimeDir = path.join(frontendDir, 'runtime');

        const generatedPagesDir = path.join(frontendDir, 'pages', 'generated');
        fs.mkdirSync(generatedPagesDir, { recursive: true });

        for (const [index, pageEntry] of selectedPages.entries()) {
            const className = pageClassNames[index];
            const pageFilePath = resolveInsideDirectory(
                generatedPagesDir,
                `${className}.js`,
                `page ${pageEntry.id}: generated page file`
            );
            fs.writeFileSync(pageFilePath, buildGeneratedFrontendPageSource(pageEntry), 'utf8');
            generatedPagePaths.push(pageFilePath);
        }

        routesFilePath = path.join(generatedPagesDir, 'routes.generated.js');
        fs.writeFileSync(routesFilePath, buildGeneratedRoutesSource(selectedPages), 'utf8');
    }

    const projectJsonPath = path.join(backendResult.projectRoot, 'project.json');
    const readmePath = path.join(backendResult.projectRoot, 'README.md');
    const manifest = buildProjectManifest(appEntry, outputRoot, Boolean(frontendDir));

    fs.writeFileSync(projectJsonPath, JSON.stringify(manifest, null, 2), 'utf8');

    const result = {
        ...backendResult,
        frontendDir,
        runtimeDir,
        routesFilePath,
        generatedPagePaths
    };

    fs.writeFileSync(readmePath, buildProjectReadme(appEntry, result), 'utf8');

    return {
        ...result,
        projectJsonPath,
        readmePath
    };
}

module.exports = {
    validateAppGenerationSupport,
    buildGeneratedCompositionSource,
    materializeAppBackendProject,
    materializeAppProject
};
