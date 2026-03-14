'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { extractAppEntry } = require('./definition-template.js');

const TEMPLATE_BACKEND_DIR = path.resolve(__dirname, '../../templates/spa/backend');
const SKIP_DIRECTORIES = new Set(['bin', 'obj']);
const SUPPORTED_SERVICE_PAIRS = new Map([
    ['SpaApi.Services.IUserService', 'SpaApi.Services.UserService'],
    ['SpaApi.Services.IAuthService', 'SpaApi.Services.AuthService']
]);

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

function ensureLeadingSlash(value) {
    if (!value) {
        return '/';
    }

    return value.startsWith('/') ? value : `/${value}`;
}

function escapeCSharpString(value) {
    return String(value)
        .replace(/\\/g, '\\\\')
        .replace(/"/g, '\\"');
}

function serializeStringArray(values) {
    if (!values || values.length === 0) {
        return 'Array.Empty<string>()';
    }

    return `new[] { ${values.map(value => `"${escapeCSharpString(value)}"`).join(', ')} }`;
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

        fs.mkdirSync(path.dirname(targetPath), { recursive: true });
        fs.copyFileSync(sourcePath, targetPath);
    }
}

function validateAppGenerationSupport(appEntry) {
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

function materializeAppBackendProject(template, appId, outputRoot) {
    const appEntry = extractAppEntry(template, appId);
    const support = validateAppGenerationSupport(appEntry);
    if (!support.valid) {
        const error = new Error(support.errors.join('; '));
        error.errors = support.errors;
        throw error;
    }

    const projectRoot = path.resolve(outputRoot, appEntry.id);
    const backendDir = path.join(projectRoot, 'backend');

    if (fs.existsSync(projectRoot)) {
        throw new Error(`Output path already exists: ${projectRoot}`);
    }

    copyDirectory(TEMPLATE_BACKEND_DIR, backendDir);

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

module.exports = {
    validateAppGenerationSupport,
    buildGeneratedCompositionSource,
    materializeAppBackendProject
};
