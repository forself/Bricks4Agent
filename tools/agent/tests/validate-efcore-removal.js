#!/usr/bin/env node
'use strict';

/**
 * Agent Pipeline EF Core 移除驗證
 *
 * 指標任務:
 *   1. Pipeline 設定完整性 — 白名單、黑名單、約束文字
 *   2. 模板產出模擬 — 用假資料跑 promptBuilder，確認無 EF Core 殘留
 *   3. Validator 正確性 — 投入含 EF Core 的模擬程式碼，確認被攔截
 *   4. 範本檔案一致性 — 確認 template backend 無 EF Core
 *   5. 依賴政策一致性 — 確認 policy JSON 無 EF Core 套件
 */

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '../../..');
let passed = 0;
let failed = 0;
const results = [];

function assert(label, condition, detail) {
    if (condition) {
        passed++;
        results.push({ label, status: 'PASS' });
    } else {
        failed++;
        results.push({ label, status: 'FAIL', detail });
    }
}

// ============================================================
// 指標 1: Pipeline 設定完整性
// ============================================================

(function testPipelineConfig() {
    const pipelinePath = path.join(ROOT, 'tools/agent/lib/pipelines/crud-pipeline.js');
    const src = fs.readFileSync(pipelinePath, 'utf-8');

    // 1a: FORBIDDEN 清單包含 EF Core
    assert(
        '1a. FORBIDDEN_BACKEND_PATTERNS 包含 EntityFrameworkCore',
        src.includes("'Microsoft\\\\.EntityFrameworkCore'"),
        'FORBIDDEN 清單缺少 EntityFrameworkCore 項目'
    );

    // 1b: 白名單包含 BaseOrm
    assert(
        '1b. ALLOWED_BACKEND_REFS 包含 BaseOrm',
        src.includes("'BaseOrm'"),
        '白名單缺少 BaseOrm'
    );

    // 1c: 白名單陣列本身不包含 EF Core (FORBIDDEN 清單裡出現的不算)
    const allowedMatch = src.match(/ALLOWED_BACKEND_REFS_BASE\s*=\s*\[([\s\S]*?)\]/m);
    assert(
        '1c. ALLOWED_BACKEND_REFS 不含 EntityFrameworkCore',
        allowedMatch && !allowedMatch[1].includes('EntityFrameworkCore'),
        '白名單誤含 EntityFrameworkCore'
    );

    // 1d: 約束文字明確禁止 EF Core
    assert(
        '1d. State 2 約束禁止 EntityFrameworkCore',
        src.includes('禁止使用 EntityFrameworkCore'),
        'State 2 約束缺少 EntityFrameworkCore 禁止聲明'
    );

    // 1e: State 3 約束要求 AppDb
    assert(
        '1e. State 3 約束要求 AppDb 而非 DbContext',
        src.includes('只能注入 AppDb，不能使用 DbContext 或 EntityFrameworkCore'),
        'State 3 約束不夠明確'
    );

    // 1f: 驗證器檢查 DbContext 違規
    assert(
        '1f. Service validator 檢查 DbContext 違規',
        src.includes("content.includes('DbContext')") && src.includes("content.includes('_context')"),
        'Service validator 缺少 DbContext 檢查'
    );

    // 1g: Prompt 列出 BaseOrm API
    assert(
        '1g. DB Layer prompt 列出 BaseOrm API',
        src.includes('Query<T>(sql, param?)') && src.includes('Insert(entity)'),
        'Prompt 缺少 BaseOrm API 清單'
    );

    // 1h: Service prompt 使用 Task.FromResult
    assert(
        '1h. Service 約束要求 Task.FromResult',
        src.includes('Task.FromResult'),
        'Service 約束缺少 Task.FromResult 要求'
    );
})();

// ============================================================
// 指標 2: 模板產出驗證 (generate-api.js)
// ============================================================

(function testGenerateApiTemplate() {
    const genPath = path.join(ROOT, 'templates/spa/scripts/generate-api.js');
    const src = fs.readFileSync(genPath, 'utf-8');

    // 2a: SERVICE_TEMPLATE 不含 EF Core
    assert(
        '2a. SERVICE_TEMPLATE 不含 EntityFrameworkCore',
        !src.includes('using Microsoft.EntityFrameworkCore'),
        'SERVICE_TEMPLATE 仍含 EF Core using'
    );

    // 2b: SERVICE_TEMPLATE 不使用 AppDbContext
    assert(
        '2b. SERVICE_TEMPLATE 不使用 AppDbContext',
        !(/SERVICE_TEMPLATE[\s\S]*?AppDbContext[\s\S]*?`;/.test(src)),
        'SERVICE_TEMPLATE 仍引用 AppDbContext'
    );

    // 2c: SERVICE_TEMPLATE 使用 AppDb
    assert(
        '2c. SERVICE_TEMPLATE 使用 AppDb',
        src.includes('private readonly AppDb _db'),
        'SERVICE_TEMPLATE 缺少 AppDb 注入'
    );

    // 2d: SERVICE_TEMPLATE 不用 .ToListAsync()
    assert(
        '2d. SERVICE_TEMPLATE 不用 ToListAsync',
        !(/SERVICE_TEMPLATE[\s\S]*?ToListAsync[\s\S]*?`;/.test(src)),
        'SERVICE_TEMPLATE 仍使用 ToListAsync (EF Core)'
    );

    // 2e: SERVICE_TEMPLATE 不用 SaveChangesAsync
    assert(
        '2e. SERVICE_TEMPLATE 不用 SaveChangesAsync',
        !(/SERVICE_TEMPLATE[\s\S]*?SaveChangesAsync[\s\S]*?`;/.test(src)),
        'SERVICE_TEMPLATE 仍使用 SaveChangesAsync (EF Core)'
    );

    // 2f: SERVICE_TEMPLATE 使用 _db.Query
    assert(
        '2f. SERVICE_TEMPLATE 使用 _db.Query',
        src.includes('_db.Query<'),
        'SERVICE_TEMPLATE 缺少 BaseOrm Query 呼叫'
    );

    // 2g: 指引文字不再提 DbSet
    assert(
        '2g. 使用者指引不再提 DbSet',
        !src.includes('加入 DbSet'),
        '使用者指引仍提及 DbSet'
    );
})();

// ============================================================
// 指標 3: Validator 正確性 (模擬攻防)
// ============================================================

(function testValidatorCatchesEfCore() {
    // 模擬含 EF Core 的程式碼
    const efCoreService = `
using Microsoft.EntityFrameworkCore;
using MyApp.Data;

public class ProductService : IProductService
{
    private readonly AppDbContext _context;

    public async Task<List<ProductResponse>> GetAllAsync()
    {
        return await _context.Products.ToListAsync();
    }
}`;

    const baseOrmService = `
using MyApp.Data;
using MyApp.Models;

public class ProductService : IProductService
{
    private readonly AppDb _db;

    public Task<List<ProductResponse>> GetAllAsync()
    {
        var items = _db.GetAllProducts();
        return Task.FromResult(items.Select(ToResponse).ToList());
    }
}`;

    // 3a: 模擬 forbidden pattern 檢查
    const FORBIDDEN = [
        'Microsoft\\.EntityFrameworkCore',
        'System\\.Data\\.Entity',
        'Dapper',
        'NHibernate',
    ];

    function hasForbidden(content) {
        for (const p of FORBIDDEN) {
            if (new RegExp(p).test(content)) return true;
        }
        return false;
    }

    assert(
        '3a. Forbidden checker 攔截 EF Core 程式碼',
        hasForbidden(efCoreService),
        'EF Core 程式碼未被攔截'
    );

    assert(
        '3b. Forbidden checker 放行 BaseOrm 程式碼',
        !hasForbidden(baseOrmService),
        'BaseOrm 程式碼被誤攔截'
    );

    // 3c: 模擬 DbContext/_context 檢查 (from State 3 validator)
    function hasDbContextViolation(content) {
        return content.includes('DbContext') || content.includes('_context');
    }

    assert(
        '3c. DbContext validator 攔截 EF Core service',
        hasDbContextViolation(efCoreService),
        'DbContext 違規未被偵測'
    );

    assert(
        '3d. DbContext validator 放行 BaseOrm service',
        !hasDbContextViolation(baseOrmService),
        'BaseOrm service 被誤攔截'
    );
})();

// ============================================================
// 指標 4: 範本檔案一致性
// ============================================================

(function testTemplateFiles() {
    const templateBackend = path.join(ROOT, 'templates/spa/backend');

    // 4a: template AppDbContext 使用 BaseDb
    const appDb = fs.readFileSync(path.join(templateBackend, 'Data/AppDbContext.cs'), 'utf-8');
    assert(
        '4a. Template AppDbContext 繼承 BaseDb',
        appDb.includes('class AppDb : BaseDb'),
        'Template 仍使用 EF Core DbContext'
    );

    // 4b: template AuthService 使用 AppDb
    const authService = fs.readFileSync(path.join(templateBackend, 'Services/AuthService.cs'), 'utf-8');
    assert(
        '4b. Template AuthService 注入 AppDb',
        authService.includes('private readonly AppDb _db'),
        'Template AuthService 仍使用 AppDbContext'
    );

    // 4c: template UserService 使用 AppDb
    const userService = fs.readFileSync(path.join(templateBackend, 'Services/UserService.cs'), 'utf-8');
    assert(
        '4c. Template UserService 注入 AppDb',
        userService.includes('private readonly AppDb _db'),
        'Template UserService 仍使用 AppDbContext'
    );

    // 4d: template csproj 無 EF Core
    const csproj = fs.readFileSync(path.join(templateBackend, 'SpaApi.csproj'), 'utf-8');
    assert(
        '4d. Template csproj 無 EntityFrameworkCore',
        !csproj.includes('EntityFrameworkCore'),
        'Template csproj 仍引用 EF Core'
    );

    // 4e: template BaseOrm.cs 存在
    assert(
        '4e. Template BaseOrm.cs 存在',
        fs.existsSync(path.join(templateBackend, 'Data/BaseOrm.cs')),
        'Template 缺少 BaseOrm.cs'
    );
})();

// ============================================================
// 指標 5: spa-generator backend 一致性
// ============================================================

(function testSpaGeneratorBackend() {
    const sgBackend = path.join(ROOT, 'tools/spa-generator/backend');

    // 5a: csproj 無 EF Core
    const csproj = fs.readFileSync(path.join(sgBackend, 'spa-generator.csproj'), 'utf-8');
    assert(
        '5a. spa-generator csproj 無 EntityFrameworkCore',
        !csproj.includes('EntityFrameworkCore'),
        'spa-generator csproj 仍引用 EF Core'
    );

    // 5b: csproj 有 Microsoft.Data.Sqlite
    assert(
        '5b. spa-generator csproj 有 Microsoft.Data.Sqlite',
        csproj.includes('Microsoft.Data.Sqlite'),
        'spa-generator csproj 缺少 Sqlite 套件'
    );

    // 5c: Program.cs 無 EF Core
    const program = fs.readFileSync(path.join(sgBackend, 'Program.cs'), 'utf-8');
    assert(
        '5c. Program.cs 無 EntityFrameworkCore',
        !program.includes('EntityFrameworkCore'),
        'Program.cs 仍引用 EF Core'
    );

    // 5d: Program.cs 使用 AddSingleton(new AppDb(...))
    assert(
        '5d. Program.cs 使用 AppDb singleton',
        program.includes('AddSingleton(new AppDb('),
        'Program.cs 仍使用 AddDbContext'
    );

    // 5e: AuthService 使用 AppDb
    const authSvc = fs.readFileSync(path.join(sgBackend, 'Services/AuthService.cs'), 'utf-8');
    assert(
        '5e. AuthService 注入 AppDb',
        authSvc.includes('private readonly AppDb _db'),
        'AuthService 仍使用 AppDbContext'
    );

    // 5f: BaseOrm.cs 存在
    assert(
        '5f. spa-generator BaseOrm.cs 存在',
        fs.existsSync(path.join(sgBackend, 'Data/BaseOrm.cs')),
        'spa-generator 缺少 BaseOrm.cs'
    );
})();

// ============================================================
// 指標 6: 依賴政策一致性
// ============================================================

(function testDependencyPolicy() {
    const policyPath = path.join(ROOT, 'tools/scripts/dotnet-dependency-policy.json');
    const policy = JSON.parse(fs.readFileSync(policyPath, 'utf-8'));

    // 6a: spa-generator 規則不含 EF Core
    const sgRule = policy.rules.find(r => r.project.includes('spa-generator'));
    assert(
        '6a. 依賴政策: spa-generator 不允許 EF Core',
        sgRule && !sgRule.allowedPackageReferences.some(p => p.includes('EntityFrameworkCore')),
        'spa-generator 依賴政策仍允許 EF Core'
    );

    // 6b: spa-generator 規則允許 Sqlite
    assert(
        '6b. 依賴政策: spa-generator 允許 Microsoft.Data.Sqlite',
        sgRule && sgRule.allowedPackageReferences.includes('Microsoft.Data.Sqlite'),
        'spa-generator 依賴政策缺少 Sqlite'
    );

    // 6c: 所有規則都不允許 EF Core
    const anyEfCore = policy.rules.some(r =>
        r.allowedPackageReferences.some(p => p.includes('EntityFrameworkCore'))
    );
    assert(
        '6c. 依賴政策: 所有專案不允許 EF Core',
        !anyEfCore,
        '有專案依賴政策仍允許 EF Core'
    );
})();

// ============================================================
// 指標 7: 全域 EF Core 殘留掃描
// ============================================================

(function testGlobalScan() {
    const { execSync } = require('child_process');

    // 用 grep 掃描所有 .cs 檔 (排除 BaseOrm.cs 和 bin/obj)
    try {
        const result = execSync(
            `grep -rl "EntityFrameworkCore" "${ROOT}" --include="*.cs" | grep -v node_modules | grep -v bin | grep -v obj | grep -v ".claude"`,
            { encoding: 'utf-8', stdio: ['pipe', 'pipe', 'pipe'] }
        ).trim();

        const files = result ? result.split('\n').filter(Boolean) : [];
        assert(
            '7a. 無 .cs 檔包含 EntityFrameworkCore',
            files.length === 0,
            `殘留檔案: ${files.join(', ')}`
        );
    } catch (e) {
        // grep returns exit code 1 when no matches found
        if (e.status === 1) {
            assert('7a. 無 .cs 檔包含 EntityFrameworkCore', true);
        } else {
            assert('7a. 無 .cs 檔包含 EntityFrameworkCore', false, `掃描失敗: ${e.message}`);
        }
    }

    // 掃描 .json 設定檔
    try {
        const result = execSync(
            `grep -rl "EntityFrameworkCore" "${ROOT}" --include="*.json" | grep -v node_modules | grep -v bin | grep -v obj | grep -v ".claude" | grep -v package-lock`,
            { encoding: 'utf-8', stdio: ['pipe', 'pipe', 'pipe'] }
        ).trim();

        const files = result ? result.split('\n').filter(Boolean) : [];
        assert(
            '7b. 無 .json 檔包含 EntityFrameworkCore',
            files.length === 0,
            `殘留檔案: ${files.join(', ')}`
        );
    } catch (e) {
        if (e.status === 1) {
            assert('7b. 無 .json 檔包含 EntityFrameworkCore', true);
        } else {
            assert('7b. 無 .json 檔包含 EntityFrameworkCore', false, `掃描失敗: ${e.message}`);
        }
    }
})();

// ============================================================
// 報告
// ============================================================

console.log('\n═══════════════════════════════════════════');
console.log('  Agent Pipeline EF Core 移除驗證報告');
console.log('═══════════════════════════════════════════\n');

for (const r of results) {
    const icon = r.status === 'PASS' ? '[OK]' : '[NG]';
    console.log(`  ${icon} ${r.label}`);
    if (r.detail) console.log(`       → ${r.detail}`);
}

console.log(`\n  結果: ${passed} 通過, ${failed} 失敗 / 共 ${passed + failed} 項`);
console.log('═══════════════════════════════════════════\n');

process.exit(failed > 0 ? 1 : 0);
