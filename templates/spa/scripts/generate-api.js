#!/usr/bin/env node
/**
 * SPA API 端點生成器
 *
 * 用法:
 *   node generate-api.js Product
 *   node generate-api.js Order --fields "CustomerId:int,Total:decimal,Status:string"
 *
 * @module generate-api
 */

const fs = require('fs');
const path = require('path');

// ===== 配置 =====
const BACKEND_DIR = path.join(__dirname, '..', 'backend');
const MODELS_DIR = path.join(BACKEND_DIR, 'Models');
const SERVICES_DIR = path.join(BACKEND_DIR, 'Services');

// ===== 模板 =====

const MODEL_TEMPLATE = `namespace {{namespace}}.Models;

/// <summary>
/// {{displayName}} 資料模型
/// </summary>
public class {{className}}
{
    public int Id { get; set; }
{{properties}}
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// {{className}} 建立請求
/// </summary>
public record Create{{className}}Request(
{{requestProperties}});

/// <summary>
/// {{className}} 更新請求
/// </summary>
public record Update{{className}}Request(
{{updateProperties}});

/// <summary>
/// {{className}} 回應
/// </summary>
public record {{className}}Response(
    int Id,
{{responseProperties}}
    DateTime CreatedAt);
`;

const SERVICE_TEMPLATE = `using {{namespace}}.Data;
using {{namespace}}.Models;

namespace {{namespace}}.Services;

/// <summary>
/// {{displayName}} 服務介面
/// </summary>
public interface I{{className}}Service
{
    Task<List<{{className}}Response>> GetAllAsync();
    Task<{{className}}Response?> GetByIdAsync(int id);
    Task<{{className}}Response> CreateAsync(Create{{className}}Request request);
    Task<{{className}}Response?> UpdateAsync(int id, Update{{className}}Request request);
    Task<bool> DeleteAsync(int id);
}

/// <summary>
/// {{displayName}} 服務實作
/// </summary>
public class {{className}}Service : I{{className}}Service
{
    private readonly AppDb _db;

    public {{className}}Service(AppDb db)
    {
        _db = db;
    }

    public Task<List<{{className}}Response>> GetAllAsync()
    {
        var items = _db.Query<{{className}}>("SELECT * FROM {{pluralName}} ORDER BY CreatedAt DESC");
        return Task.FromResult(items.Select(ToResponse).ToList());
    }

    public Task<{{className}}Response?> GetByIdAsync(int id)
    {
        var entity = _db.Get<{{className}}>(id);
        return Task.FromResult(entity != null ? ToResponse(entity) : null);
    }

    public Task<{{className}}Response> CreateAsync(Create{{className}}Request request)
    {
        var entity = new {{className}}
        {
{{createMapping}}
            CreatedAt = DateTime.UtcNow
        };

        var newId = _db.Insert(entity);
        entity.Id = (int)newId;

        return Task.FromResult(ToResponse(entity));
    }

    public Task<{{className}}Response?> UpdateAsync(int id, Update{{className}}Request request)
    {
        var entity = _db.Get<{{className}}>(id);
        if (entity == null) return Task.FromResult<{{className}}Response?>(null);

{{updateMapping}}
        entity.UpdatedAt = DateTime.UtcNow;

        _db.Update(entity);
        return Task.FromResult<{{className}}Response?>(ToResponse(entity));
    }

    public Task<bool> DeleteAsync(int id)
    {
        var affected = _db.Delete<{{className}}>(id);
        return Task.FromResult(affected > 0);
    }

    private static {{className}}Response ToResponse({{className}} entity) => new(
        entity.Id,
{{responseMapping}}
        entity.CreatedAt
    );
}
`;

const ENDPOINTS_TEMPLATE = `
// ===== {{displayName}} API =====

app.MapGet("/api/{{routePath}}", async (I{{className}}Service service) =>
{
    var items = await service.GetAllAsync();
    return Results.Ok(items);
}).WithName("Get{{pluralName}}").RequireAuthorization().RequireRateLimiting("api");

app.MapGet("/api/{{routePath}}/{id:int}", async (int id, I{{className}}Service service) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Invalid ID" });

    var item = await service.GetByIdAsync(id);
    if (item == null) return Results.NotFound(new { error = "Not found" });
    return Results.Ok(item);
}).WithName("Get{{className}}ById").RequireAuthorization().RequireRateLimiting("api");

app.MapPost("/api/{{routePath}}", async (Create{{className}}Request request, I{{className}}Service service) =>
{
    // TODO: 加入驗證邏輯
    var item = await service.CreateAsync(request);
    return Results.Created($"/api/{{routePath}}/{item.Id}", item);
}).WithName("Create{{className}}").RequireAuthorization().RequireRateLimiting("api");

app.MapPut("/api/{{routePath}}/{id:int}", async (int id, Update{{className}}Request request, I{{className}}Service service) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Invalid ID" });

    var item = await service.UpdateAsync(id, request);
    if (item == null) return Results.NotFound(new { error = "Not found" });
    return Results.Ok(item);
}).WithName("Update{{className}}").RequireAuthorization().RequireRateLimiting("api");

app.MapDelete("/api/{{routePath}}/{id:int}", async (int id, I{{className}}Service service) =>
{
    if (id <= 0) return Results.BadRequest(new { error = "Invalid ID" });

    var success = await service.DeleteAsync(id);
    if (!success) return Results.NotFound(new { error = "Not found" });
    return Results.Ok(new { message = "Deleted", id });
}).WithName("Delete{{className}}").RequireAuthorization().RequireRateLimiting("api");
`;

// ===== 工具函數 =====

function parseArgs() {
    const args = { flags: {}, fields: [] };
    const argv = process.argv.slice(2);

    for (let i = 0; i < argv.length; i++) {
        if (argv[i] === '--fields' && argv[i + 1]) {
            args.fields = parseFields(argv[i + 1]);
            i++;
        } else if (argv[i].startsWith('--')) {
            const key = argv[i].slice(2);
            args.flags[key] = true;
        } else if (!args.entityName) {
            args.entityName = argv[i];
        }
    }

    return args;
}

function parseFields(fieldsStr) {
    return fieldsStr.split(',').map(field => {
        const [name, type] = field.split(':');
        return { name: name.trim(), type: type?.trim() || 'string' };
    });
}

function toPascalCase(str) {
    return str.charAt(0).toUpperCase() + str.slice(1);
}

function toCamelCase(str) {
    return str.charAt(0).toLowerCase() + str.slice(1);
}

function toKebabCase(str) {
    return str
        .replace(/([a-z])([A-Z])/g, '$1-$2')
        .toLowerCase();
}

function pluralize(word) {
    if (word.endsWith('y')) {
        return word.slice(0, -1) + 'ies';
    }
    if (word.endsWith('s') || word.endsWith('x') || word.endsWith('ch') || word.endsWith('sh')) {
        return word + 'es';
    }
    return word + 's';
}

function getCSharpType(type) {
    const typeMap = {
        'int': 'int',
        'integer': 'int',
        'long': 'long',
        'decimal': 'decimal',
        'float': 'float',
        'double': 'double',
        'bool': 'bool',
        'boolean': 'bool',
        'string': 'string',
        'datetime': 'DateTime',
        'date': 'DateTime',
        'guid': 'Guid'
    };
    return typeMap[type.toLowerCase()] || 'string';
}

function getNullableType(type) {
    const csharpType = getCSharpType(type);
    if (csharpType === 'string') return 'string?';
    return csharpType + '?';
}

function getNamespace() {
    // 嘗試從 .csproj 讀取
    const files = fs.readdirSync(BACKEND_DIR);
    const csproj = files.find(f => f.endsWith('.csproj'));
    if (csproj) {
        return csproj.replace('.csproj', '');
    }
    return 'SpaApi';
}

// ===== 生成函數 =====

function generateModel(entityName, fields, namespace) {
    const className = toPascalCase(entityName);

    // 生成屬性
    let properties = '';
    let requestProperties = '';
    let updateProperties = '';
    let responseProperties = '';

    const defaultFields = [
        { name: 'Name', type: 'string' }
    ];

    const allFields = fields.length > 0 ? fields : defaultFields;

    allFields.forEach((field, index) => {
        const propName = toPascalCase(field.name);
        const csharpType = getCSharpType(field.type);
        const nullableType = getNullableType(field.type);
        const comma = index < allFields.length - 1 ? ',' : '';

        properties += `    public ${csharpType === 'string' ? 'string' : csharpType} ${propName} { get; set; }${csharpType === 'string' ? ' = "";' : ''}\n`;
        requestProperties += `    ${csharpType} ${propName}${comma}\n`;
        updateProperties += `    ${nullableType} ${propName}${comma}\n`;
        responseProperties += `    ${csharpType} ${propName},\n`;
    });

    let content = MODEL_TEMPLATE
        .replace(/\{\{namespace\}\}/g, namespace)
        .replace(/\{\{className\}\}/g, className)
        .replace(/\{\{displayName\}\}/g, entityName)
        .replace(/\{\{properties\}\}/g, properties)
        .replace(/\{\{requestProperties\}\}/g, requestProperties.trimEnd())
        .replace(/\{\{updateProperties\}\}/g, updateProperties.trimEnd())
        .replace(/\{\{responseProperties\}\}/g, responseProperties);

    const outputPath = path.join(MODELS_DIR, `${className}.cs`);

    if (!fs.existsSync(MODELS_DIR)) {
        fs.mkdirSync(MODELS_DIR, { recursive: true });
    }

    fs.writeFileSync(outputPath, content, 'utf8');
    return outputPath;
}

function generateService(entityName, fields, namespace) {
    const className = toPascalCase(entityName);
    const pluralName = pluralize(className);

    const allFields = fields.length > 0 ? fields : [{ name: 'Name', type: 'string' }];

    // 生成映射
    let createMapping = '';
    let updateMapping = '';
    let responseMapping = '';

    allFields.forEach(field => {
        const propName = toPascalCase(field.name);
        createMapping += `            ${propName} = request.${propName},\n`;
        updateMapping += `        if (request.${propName} != null) entity.${propName} = request.${propName}${getCSharpType(field.type) === 'string' ? '' : '.Value'};\n`;
        responseMapping += `        entity.${propName},\n`;
    });

    let content = SERVICE_TEMPLATE
        .replace(/\{\{namespace\}\}/g, namespace)
        .replace(/\{\{className\}\}/g, className)
        .replace(/\{\{displayName\}\}/g, entityName)
        .replace(/\{\{pluralName\}\}/g, pluralName)
        .replace(/\{\{createMapping\}\}/g, createMapping.trimEnd())
        .replace(/\{\{updateMapping\}\}/g, updateMapping.trimEnd())
        .replace(/\{\{responseMapping\}\}/g, responseMapping);

    const outputPath = path.join(SERVICES_DIR, `${className}Service.cs`);

    if (!fs.existsSync(SERVICES_DIR)) {
        fs.mkdirSync(SERVICES_DIR, { recursive: true });
    }

    fs.writeFileSync(outputPath, content, 'utf8');
    return outputPath;
}

function generateEndpoints(entityName) {
    const className = toPascalCase(entityName);
    const pluralName = pluralize(className);
    const routePath = toKebabCase(pluralName);

    let content = ENDPOINTS_TEMPLATE
        .replace(/\{\{className\}\}/g, className)
        .replace(/\{\{displayName\}\}/g, entityName)
        .replace(/\{\{pluralName\}\}/g, pluralName)
        .replace(/\{\{routePath\}\}/g, routePath);

    return {
        content,
        routePath,
        className,
        pluralName
    };
}

// ===== 主程式 =====

async function main() {
    const args = parseArgs();

    if (!args.entityName) {
        console.log('');
        console.log('SPA API 端點生成器');
        console.log('');
        console.log('用法:');
        console.log('  node generate-api.js <實體名稱> [選項]');
        console.log('');
        console.log('範例:');
        console.log('  node generate-api.js Product');
        console.log('  node generate-api.js Order --fields "CustomerId:int,Total:decimal,Status:string"');
        console.log('');
        console.log('支援的欄位類型:');
        console.log('  string, int, long, decimal, float, double, bool, datetime, guid');
        console.log('');
        process.exit(0);
    }

    const namespace = getNamespace();

    console.log('');
    console.log('正在生成 API...');
    console.log('');

    // 生成 Model
    const modelPath = generateModel(args.entityName, args.fields, namespace);
    console.log(`✓ Model: ${modelPath}`);

    // 生成 Service
    const servicePath = generateService(args.entityName, args.fields, namespace);
    console.log(`✓ Service: ${servicePath}`);

    // 生成 Endpoints
    const endpoints = generateEndpoints(args.entityName);
    console.log(`✓ Endpoints: /api/${endpoints.routePath}`);

    console.log('');
    console.log('下一步:');
    console.log('');
    console.log('1. 在 AppDbContext.cs 的 EnsureCreated() 中加入建表 SQL');
    console.log('');
    console.log('2. 在 Program.cs 中註冊服務:');
    console.log('');
    console.log(`   builder.Services.AddSingleton<I${endpoints.className}Service, ${endpoints.className}Service>();`);
    console.log('');
    console.log('3. 在 Program.cs 中加入以下端點 (在 app.Run() 之前):');
    console.log('');
    console.log(endpoints.content);
    console.log('');
}

main().catch(console.error);
