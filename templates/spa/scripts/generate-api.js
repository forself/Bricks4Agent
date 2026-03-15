#!/usr/bin/env node
/**
 * SPA API 端點生成器
 *
 * 用法:
 *   node generate-api.js Product
 *   node generate-api.js Order --fields "CustomerId:int,Total:decimal,Status:string"
 *   node generate-api.js Product --fields "Name:string,Price:decimal"
 *   node generate-api.js Product --fields "Name:string,Price:decimal" --no-patch
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

function getSqlType(type) {
    const typeMap = {
        'string': "TEXT NOT NULL DEFAULT ''",
        'int': 'INTEGER NOT NULL DEFAULT 0',
        'integer': 'INTEGER NOT NULL DEFAULT 0',
        'long': 'INTEGER NOT NULL DEFAULT 0',
        'decimal': 'REAL NOT NULL DEFAULT 0',
        'float': 'REAL NOT NULL DEFAULT 0',
        'double': 'REAL NOT NULL DEFAULT 0',
        'bool': 'INTEGER NOT NULL DEFAULT 0',
        'boolean': 'INTEGER NOT NULL DEFAULT 0',
        'datetime': "TEXT NOT NULL DEFAULT (datetime('now'))",
        'date': "TEXT NOT NULL DEFAULT (datetime('now'))",
        'guid': "TEXT NOT NULL DEFAULT ''"
    };
    return typeMap[type.toLowerCase()] || "TEXT NOT NULL DEFAULT ''";
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

    const filePath = path.join(MODELS_DIR, `${className}.cs`);

    return { content, filePath };
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

    const filePath = path.join(SERVICES_DIR, `${className}Service.cs`);

    return { content, filePath };
}

function generateEndpoints(entityName, fields) {
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

function generateDbMethods(entityName, fields) {
    const className = toPascalCase(entityName);
    const pluralName = pluralize(className);
    const allFields = fields.length > 0 ? fields : [{ name: 'Name', type: 'string' }];

    // Generate CREATE TABLE SQL
    let columnDefs = [];
    columnDefs.push('            Id INTEGER PRIMARY KEY AUTOINCREMENT');
    allFields.forEach(field => {
        const propName = toPascalCase(field.name);
        const sqlType = getSqlType(field.type);
        columnDefs.push(`            ${propName} ${sqlType}`);
    });
    columnDefs.push("            CreatedAt TEXT NOT NULL DEFAULT (datetime('now'))");
    columnDefs.push('            UpdatedAt TEXT');

    const tableSql = `        Execute(@"
            CREATE TABLE IF NOT EXISTS ${pluralName} (
${columnDefs.join(',\n')}
            )
        ");`;

    // Generate CRUD methods
    const methods = `    // #region ${className} Operations

    public IEnumerable<${className}> GetAll${pluralName}()
        => Query<${className}>("SELECT * FROM ${pluralName} ORDER BY Id DESC");

    public ${className}? Get${className}ById(int id)
        => QueryFirst<${className}>("SELECT * FROM ${pluralName} WHERE Id = @Id", new { Id = id });

    public long Create${className}(${className} entity)
        => Insert(entity);

    public void Update${className}(${className} entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        Update(entity);
    }

    public int Delete${className}(int id)
        => Delete<${className}>(id);

    // #endregion`;

    return { tableSql, methods };
}

// ===== Patch 模式 =====

function patchFile(filePath, marker, insertion, entityName, dupCheckString) {
    if (!fs.existsSync(filePath)) {
        console.error(`  ✗ File not found: ${filePath}`);
        return false;
    }

    let content = fs.readFileSync(filePath, 'utf8');
    const className = toPascalCase(entityName);
    const markerIndex = content.indexOf(marker);
    if (markerIndex === -1) {
        console.error(`  ✗ Marker not found: ${marker} in ${filePath}`);
        return false;
    }

    // Idempotency: check for a marker-specific duplicate string
    if (dupCheckString && content.includes(dupCheckString)) {
        console.warn(`  ⚠ ${className} already exists in ${path.basename(filePath)}, skipping.`);
        return true;
    }

    const newContent = content.replace(marker, insertion + '\n\n' + marker);
    fs.writeFileSync(filePath, newContent, 'utf8');
    return true;
}

function ensureUsings(filePath, namespace) {
    let content = fs.readFileSync(filePath, 'utf8');
    const requiredUsings = [
        `using ${namespace}.Models;`,
        `using ${namespace}.Services;`
    ];
    let changed = false;
    for (const u of requiredUsings) {
        if (!content.includes(u)) {
            // Insert after last existing using statement
            const lastUsing = content.lastIndexOf('using ');
            const lineEnd = content.indexOf('\n', lastUsing);
            content = content.substring(0, lineEnd + 1) + u + '\n' + content.substring(lineEnd + 1);
            changed = true;
        }
    }
    if (changed) fs.writeFileSync(filePath, content, 'utf8');
    return changed;
}

function buildServiceRegistration(className) {
    return `builder.Services.AddScoped<I${className}Service, ${className}Service>();`;
}

function runPatch(entityName, fields) {
    const className = toPascalCase(entityName);
    const dbContextPath = path.join(BACKEND_DIR, 'Data', 'AppDbContext.cs');
    const programPath = path.join(BACKEND_DIR, 'Program.cs');
    const ns = getNamespace();

    console.log('');
    console.log('Patching files...');

    // Ensure Program.cs has required using statements
    ensureUsings(programPath, ns);

    // Generate DB methods
    const dbResult = generateDbMethods(entityName, fields);

    const pluralName = pluralize(className);

    // Patch AppDbContext.cs - TABLE_SQL
    const tableSqlOk = patchFile(
        dbContextPath,
        '// --- BRICKS:TABLE_SQL ---',
        '\n' + dbResult.tableSql,
        entityName,
        `CREATE TABLE IF NOT EXISTS ${pluralName}`
    );
    if (tableSqlOk) {
        console.log(`  ✓ Patched AppDbContext.cs with CREATE TABLE for ${className}`);
    }

    // Patch AppDbContext.cs - DB_METHODS
    const dbMethodsOk = patchFile(
        dbContextPath,
        '// --- BRICKS:DB_METHODS ---',
        '\n' + dbResult.methods,
        entityName,
        `#region ${className} Operations`
    );
    if (dbMethodsOk) {
        console.log(`  ✓ Patched AppDbContext.cs with CRUD methods for ${className}`);
    }

    // Patch Program.cs - SERVICES
    const serviceRegistration = buildServiceRegistration(className);
    const servicesOk = patchFile(
        programPath,
        '// --- BRICKS:SERVICES ---',
        serviceRegistration,
        entityName,
        `I${className}Service, ${className}Service`
    );
    if (servicesOk) {
        console.log(`  ✓ Patched Program.cs with service registration for ${className}`);
    }

    // Patch Program.cs - ENDPOINTS
    const endpoints = generateEndpoints(entityName, fields);
    const endpointsOk = patchFile(
        programPath,
        '// --- BRICKS:ENDPOINTS ---',
        endpoints.content,
        entityName,
        `"Get${className}ById"`
    );
    if (endpointsOk) {
        console.log(`  ✓ Patched Program.cs with endpoints for ${className}`);
    }
}

// ===== 統合生成函數 =====

function generateAll(entityName, fields, options = {}) {
    const namespace = options.namespace || getNamespace();
    const patch = options.patch || false;

    const model = generateModel(entityName, fields, namespace);
    const service = generateService(entityName, fields, namespace);
    const endpoints = generateEndpoints(entityName, fields);
    const dbMethods = generateDbMethods(entityName, fields);

    // Write Model
    if (!fs.existsSync(path.dirname(model.filePath))) {
        fs.mkdirSync(path.dirname(model.filePath), { recursive: true });
    }
    fs.writeFileSync(model.filePath, model.content, 'utf8');

    // Write Service
    if (!fs.existsSync(path.dirname(service.filePath))) {
        fs.mkdirSync(path.dirname(service.filePath), { recursive: true });
    }
    fs.writeFileSync(service.filePath, service.content, 'utf8');

    const result = {
        model,
        service,
        endpoints,
        dbMethods
    };

    if (patch) {
        runPatch(entityName, fields);
    }

    return result;
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
        console.log('  node generate-api.js Product --fields "Name:string,Price:decimal"');
        console.log('  node generate-api.js Product --fields "Name:string,Price:decimal" --no-patch');
        console.log('');
        console.log('選項:');
        console.log('  --fields      欄位定義 (格式: Name:type,Name2:type2)');
        console.log('  --patch       保留的相容旗標，等同預設自動整合');
        console.log('  --no-patch    只產生檔案，不修改 AppDbContext.cs 和 Program.cs');
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
    const model = generateModel(args.entityName, args.fields, namespace);
    if (!fs.existsSync(path.dirname(model.filePath))) {
        fs.mkdirSync(path.dirname(model.filePath), { recursive: true });
    }
    fs.writeFileSync(model.filePath, model.content, 'utf8');
    console.log(`✓ Model: ${model.filePath}`);

    // 生成 Service
    const service = generateService(args.entityName, args.fields, namespace);
    if (!fs.existsSync(path.dirname(service.filePath))) {
        fs.mkdirSync(path.dirname(service.filePath), { recursive: true });
    }
    fs.writeFileSync(service.filePath, service.content, 'utf8');
    console.log(`✓ Service: ${service.filePath}`);

    // 生成 Endpoints
    const endpoints = generateEndpoints(args.entityName, args.fields);
    console.log(`✓ Endpoints: /api/${endpoints.routePath}`);

    const shouldPatch = args.flags.patch || !args.flags['no-patch'];

    if (shouldPatch) {
        runPatch(args.entityName, args.fields);
    } else {
        console.log('');
        console.log('下一步:');
        console.log('');
        console.log('1. 在 AppDbContext.cs 的 EnsureCreated() 中加入建表 SQL');
        console.log('');
        console.log('2. 在 Program.cs 中註冊服務:');
        console.log('');
        console.log(`   ${buildServiceRegistration(endpoints.className)}`);
        console.log('');
        console.log('3. 在 Program.cs 中加入以下端點 (在 app.Run() 之前):');
        console.log('');
        console.log(endpoints.content);
        console.log('');
        console.log('提示: 預設會自動整合；使用 --no-patch 可停用自動整合。');
        console.log('');
    }
}

if (require.main === module) {
    main().catch(console.error);
}

module.exports = {
    buildServiceRegistration,
    generateModel,
    generateService,
    generateEndpoints,
    generateDbMethods,
    generateAll
};
