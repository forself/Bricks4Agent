'use strict';

const fs = require('fs');
const path = require('path');
const { StateMachine } = require('../state-machine');
const { buildCrudPipeline, detectProjectNamespace } = require('./crud-pipeline');
const { buildServicePipeline, classifyAndFillReport, REPORT_TEMPLATES } = require('./service-pipeline');
const { bold, colorize, logInfo, logError, logSuccess, logWarn, formatDuration } = require('../utils');

/**
 * Pipeline Runner — 從 project.json 讀取定義，自動化執行生成管線
 *
 * 執行流程:
 *   Batch A (CRUD entities):
 *     1. 讀取 project.json → entities[]
 *     2. 過濾掉 builtin + 已完成的實體
 *     3. 對每個待生成實體建構管線並依序執行
 *     4. 匯總報告
 *
 *   Batch B (Extended services):
 *     1. 讀取 project.json → extendedServices[]
 *     2. 拓撲排序解決依賴順序
 *     3. 依賴前置檢查
 *     4. 對每個服務建構管線並執行
 *     5. 結構化回報（模板化，非自然語言）
 *
 * 設計原則:
 *   設計（entities / services 定義）與執行（管線生成）解耦。
 *   project.json 是「設計端產出」，可由人類、高階 AI、或協作產生。
 *   pipeline runner 是「執行端引擎」，由狀態機 + 檢核點保證品質。
 */

// ============================================================
// 實體完成狀態偵測
// ============================================================

/**
 * 偵測某個實體是否已經存在產出檔案
 * 用於跳過已完成的實體（除非 --force）
 *
 * @param {string} projectRoot - 專案根目錄
 * @param {string} projectPath - 專案子路徑
 * @param {string} entityName - 實體名稱
 * @returns {{ exists: boolean, files: {path: string, exists: boolean}[] }}
 */
function detectEntityStatus(projectRoot, projectPath, entityName) {
    const be = (rel) => projectPath ? path.join(projectPath, rel) : rel;
    const checks = [
        be(`backend/Models/${entityName}.cs`),
        be(`backend/Services/${entityName}Service.cs`),
    ];

    const results = checks.map(relPath => ({
        path: relPath,
        exists: fs.existsSync(path.join(projectRoot, relPath)),
    }));

    return {
        exists: results.every(r => r.exists),
        files: results,
    };
}

// ============================================================
// 服務完成狀態偵測 (Batch B)
// ============================================================

/**
 * 偵測某個服務是否已經存在產出檔案
 *
 * @param {string} projectRoot - 專案根目錄
 * @param {string} projectPath - 專案子路徑
 * @param {string} serviceName - 服務名稱
 * @param {string} serviceType - 'repository' | 'service'
 * @returns {{ exists: boolean, files: {path: string, exists: boolean}[] }}
 */
function detectServiceStatus(projectRoot, projectPath, serviceName, serviceType) {
    const be = (rel) => projectPath ? path.join(projectPath, rel) : rel;
    const dir = serviceType === 'repository' ? 'Repositories' : 'Services';
    const checks = [
        be(`backend/${dir}/I${serviceName}.cs`),
        be(`backend/${dir}/${serviceName}.cs`),
    ];

    const results = checks.map(relPath => ({
        path: relPath,
        exists: fs.existsSync(path.join(projectRoot, relPath)),
    }));

    return {
        exists: results.every(r => r.exists),
        files: results,
    };
}

/**
 * 檢查服務的依賴是否已經存在
 * 用於在執行前驗證前置服務是否已生成
 *
 * @param {string} projectRoot - 專案根目錄
 * @param {string} projectPath - 專案子路徑
 * @param {Object} serviceConfig - extendedService 定義
 * @returns {{ satisfied: boolean, missing: string[] }}
 */
function checkDependenciesExist(projectRoot, projectPath, serviceConfig) {
    const dependencies = serviceConfig.dependencies || [];
    const missing = [];

    for (const dep of dependencies) {
        // 依賴格式: "IProjectFileRepository", "BaseCache", "AppDb"
        // 只檢查自訂介面（I 開頭且非框架），框架依賴 (BaseCache, AppDb) 假設已存在
        if (dep.startsWith('I') && dep !== 'IConfiguration') {
            const be = (rel) => projectPath ? path.join(projectPath, rel) : rel;
            // 嘗試 Repositories 和 Services 兩個目錄
            const repoPath = path.join(projectRoot, be(`backend/Repositories/${dep}.cs`));
            const svcPath = path.join(projectRoot, be(`backend/Services/${dep}.cs`));
            if (!fs.existsSync(repoPath) && !fs.existsSync(svcPath)) {
                missing.push(dep);
            }
        }
    }

    return { satisfied: missing.length === 0, missing };
}

// ============================================================
// 拓撲排序（Kahn's Algorithm）
// ============================================================

/**
 * 根據依賴關係對服務進行拓撲排序
 *
 * 依賴圖範例:
 *   ProjectFileRepository (無依賴) ─┬─→ EditorService (dep: IProjectFileRepository)
 *                                    └─→ GeneratorService (dep: IProjectFileRepository)
 *
 * @param {Object[]} services - extendedServices 陣列
 * @returns {Object[][]} 分層排序結果: [[第一層無依賴], [第二層], ...]
 */
function resolveServiceDependencyOrder(services) {
    // 建立名稱 → 服務的映射
    const byName = new Map();
    for (const svc of services) {
        byName.set(svc.name, svc);
    }

    // 建立依賴圖（只計算指向 extendedServices 內部的依賴）
    // 依賴格式: "IProjectFileRepository" → 對應 "ProjectFileRepository"
    const depGraph = new Map(); // name → Set<name>
    const inDegree = new Map();

    for (const svc of services) {
        depGraph.set(svc.name, new Set());
        inDegree.set(svc.name, 0);
    }

    for (const svc of services) {
        for (const dep of (svc.dependencies || [])) {
            // IProjectFileRepository → ProjectFileRepository
            const depName = dep.startsWith('I') ? dep.slice(1) : dep;
            if (byName.has(depName)) {
                depGraph.get(svc.name).add(depName);
                inDegree.set(svc.name, inDegree.get(svc.name) + 1);
            }
        }
    }

    // Kahn's Algorithm — 分層
    const layers = [];
    const remaining = new Set(services.map(s => s.name));

    while (remaining.size > 0) {
        // 找出入度為 0 的節點
        const layer = [];
        for (const name of remaining) {
            if (inDegree.get(name) === 0) {
                layer.push(name);
            }
        }

        if (layer.length === 0) {
            // 循環依賴
            logError(`  循環依賴! 剩餘: ${[...remaining].join(', ')}`);
            // 強制加入剩餘的（避免無限迴圈）
            layers.push([...remaining].map(n => byName.get(n)));
            break;
        }

        layers.push(layer.map(n => byName.get(n)));

        // 移除已處理的節點，更新入度
        for (const name of layer) {
            remaining.delete(name);
            // 更新所有依賴此節點的服務的入度
            for (const otherName of remaining) {
                if (depGraph.get(otherName).has(name)) {
                    inDegree.set(otherName, inDegree.get(otherName) - 1);
                }
            }
        }
    }

    return layers;
}

// ============================================================
// Convention 基礎設施（程式化產生，非 Agent）
// ============================================================

/**
 * ServiceRegistration.cs 模板
 * Convention-Based DI：掃描 Assembly 中所有 IXxx → Xxx 配對自動註冊
 * 已顯式註冊的介面會被跳過（Batch A 的 DI 不受影響）
 */
function SERVICE_REGISTRATION_TEMPLATE(ns) {
    return `using System.Reflection;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace ${ns}.Extensions;

public static class ServiceRegistration
{
    public static IServiceCollection AddServicesByConvention(this IServiceCollection services)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var implType in asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericType))
        {
            var iface = implType.GetInterfaces()
                .FirstOrDefault(i => i.Name == "I" + implType.Name);
            if (iface != null && !services.Any(sd => sd.ServiceType == iface))
            {
                services.AddScoped(iface, implType);
            }
        }
        return services;
    }
}
`;
}

/**
 * EndpointRegistration.cs 模板
 * Convention-Based Endpoints：掃描所有 static class 的 Map(WebApplication) 方法並自動掛載
 */
function ENDPOINT_REGISTRATION_TEMPLATE(ns) {
    return `using System.Reflection;

namespace ${ns}.Extensions;

public static class EndpointRegistration
{
    public static WebApplication MapEndpointsByConvention(this WebApplication app)
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var type in asm.GetTypes()
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed)) // static classes
        {
            var method = type.GetMethod("Map",
                BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(WebApplication) });
            if (method != null)
            {
                method.Invoke(null, new object[] { app });
            }
        }
        return app;
    }
}
`;
}

/**
 * 確保 Convention 基礎設施存在
 *
 * 程式化建立以下內容（非 Agent，而是確定性程式碼）:
 *   1. backend/Extensions/ServiceRegistration.cs  — DI 自動掃描
 *   2. backend/Extensions/EndpointRegistration.cs  — Endpoint 自動掛載
 *   3. backend/Endpoints/ 目錄                    — Agent 的端點檔案放這裡
 *   4. Program.cs 注入 convention 呼叫（一次性）
 *
 * 設計原則: Agent 只建立新檔案，永遠不碰 Program.cs。
 * 這裡是唯一碰 Program.cs 的地方，而且只追加 3 行（1 using + 2 呼叫）。
 *
 * @param {string} projectRoot - 專案根目錄（絕對路徑）
 * @param {string} projectPath - 專案子路徑（相對於 projectRoot）
 * @param {string} namespace - 專案命名空間 (e.g. "WebEditor")
 */
function ensureConventionInfra(projectRoot, projectPath, namespace) {
    const be = (rel) => path.join(projectRoot, projectPath || '', rel);

    const extensionsDir = be('backend/Extensions');
    const endpointsDir = be('backend/Endpoints');
    const programCsPath = be('backend/Program.cs');

    // 0. 前置檢查: Program.cs 必須存在
    if (!fs.existsSync(programCsPath)) {
        logWarn('  ⚠ Program.cs 不存在，跳過 Convention 基礎設施');
        return;
    }

    let anyChange = false;

    // 1. 建立 Extensions 目錄
    if (!fs.existsSync(extensionsDir)) {
        fs.mkdirSync(extensionsDir, { recursive: true });
    }

    // 2. 建立 Endpoints 目錄
    if (!fs.existsSync(endpointsDir)) {
        fs.mkdirSync(endpointsDir, { recursive: true });
    }

    // 3. ServiceRegistration.cs
    const svcRegPath = path.join(extensionsDir, 'ServiceRegistration.cs');
    if (!fs.existsSync(svcRegPath)) {
        fs.writeFileSync(svcRegPath, SERVICE_REGISTRATION_TEMPLATE(namespace));
        logSuccess('  ✓ 建立 Extensions/ServiceRegistration.cs');
        anyChange = true;
    }

    // 4. EndpointRegistration.cs
    const epRegPath = path.join(extensionsDir, 'EndpointRegistration.cs');
    if (!fs.existsSync(epRegPath)) {
        fs.writeFileSync(epRegPath, ENDPOINT_REGISTRATION_TEMPLATE(namespace));
        logSuccess('  ✓ 建立 Extensions/EndpointRegistration.cs');
        anyChange = true;
    }

    // 5. 在 Program.cs 注入 convention 呼叫（如果尚未存在）
    let programCs = fs.readFileSync(programCsPath, 'utf8');
    let modified = false;

    // 5a. using 聲明
    const usingLine = `using ${namespace}.Extensions;`;
    if (!programCs.includes('AddServicesByConvention') || !programCs.includes(usingLine)) {
        if (!programCs.includes(usingLine)) {
            programCs = usingLine + '\n' + programCs;
            modified = true;
        }
    }

    // 5b. AddServicesByConvention — 插在 builder.Build() 之前
    if (!programCs.includes('AddServicesByConvention')) {
        // 策略: 找 var app = builder.Build() 並在其前插入
        const buildPattern = /(var\s+app\s*=\s*builder\.Build\(\);)/;
        if (buildPattern.test(programCs)) {
            programCs = programCs.replace(
                buildPattern,
                `builder.Services.AddServicesByConvention();\n\n$1`
            );
            modified = true;
        } else {
            // 備用: 找 AddEndpointsApiExplorer 並在其前插入
            const fallback = /(builder\.Services\.AddEndpointsApiExplorer\(\);)/;
            if (fallback.test(programCs)) {
                programCs = programCs.replace(
                    fallback,
                    `builder.Services.AddServicesByConvention();\n\n$1`
                );
                modified = true;
            } else {
                logWarn('  ⚠ 無法定位 DI 區塊，請手動加入 builder.Services.AddServicesByConvention()');
            }
        }
    }

    // 5c. MapEndpointsByConvention — 插在 app.Run() 之前
    if (!programCs.includes('MapEndpointsByConvention')) {
        const runPattern = /(app\.Run\(\);)/;
        if (runPattern.test(programCs)) {
            programCs = programCs.replace(
                runPattern,
                `app.MapEndpointsByConvention();\n\n$1`
            );
            modified = true;
        } else {
            logWarn('  ⚠ 無法定位 app.Run()，請手動加入 app.MapEndpointsByConvention()');
        }
    }

    if (modified) {
        fs.writeFileSync(programCsPath, programCs);
        logSuccess('  ✓ Program.cs 注入 Convention 呼叫');
        anyChange = true;
    }

    if (!anyChange) {
        logInfo('  Convention 基礎設施已存在，跳過');
    }
}

// ============================================================
// Batch B: Service Pipeline Runner
// ============================================================

/**
 * 執行 Batch B 服務管線
 *
 * @param {Object} options
 * @param {import('../agent-loop').AgentLoop} options.agent
 * @param {string} options.projectRoot
 * @param {string} options.projectPath
 * @param {Object[]} options.services - extendedServices 陣列
 * @param {Object} options.config - 完整 project.json 配置
 * @param {string} [options.serviceFilter] - 只執行特定服務
 * @param {boolean} [options.force]
 * @param {boolean} [options.dryRun]
 * @param {boolean} [options.validateOnly]
 * @returns {Promise<Object>}
 */
async function runServicePipelines(options) {
    const {
        agent,
        projectRoot,
        projectPath = '',
        services,
        config,
        serviceFilter = null,
        force = false,
        dryRun = false,
        validateOnly = false,
    } = options;

    console.log(colorize(`\n${'═'.repeat(60)}`, 'magenta'));
    console.log(bold(`  Batch B: Extended Services`));
    console.log(colorize(`${'═'.repeat(60)}`, 'magenta'));

    // 0. Convention 基礎設施（一次性，程式化建立）
    if (!dryRun) {
        const namespace = detectProjectNamespace(projectRoot, projectPath);
        ensureConventionInfra(projectRoot, projectPath, namespace);
    }

    // 1. 過濾服務
    let targetServices = [...services];
    if (serviceFilter) {
        targetServices = targetServices.filter(s =>
            s.name.toLowerCase() === serviceFilter.toLowerCase()
        );
        if (targetServices.length === 0) {
            logError(`找不到服務 "${serviceFilter}"`);
            return { completed: false, results: [], failureReports: [] };
        }
    }

    // 2. 拓撲排序
    const layers = resolveServiceDependencyOrder(targetServices);

    console.log(bold(`\n  執行順序 (拓撲排序):`));
    for (let li = 0; li < layers.length; li++) {
        const layerNames = layers[li].map(s => s.name).join(', ');
        console.log(`  第 ${li + 1} 層: ${layerNames}`);
    }
    console.log('');

    // 3. 偵測狀態 & 依賴檢查
    const servicePlan = [];
    for (const layer of layers) {
        for (const svc of layer) {
            const status = detectServiceStatus(projectRoot, projectPath, svc.name, svc.type || 'service');
            const skip = status.exists && !force;
            const depCheck = checkDependenciesExist(projectRoot, projectPath, svc);
            servicePlan.push({ service: svc, status, skip, depCheck });
        }
    }

    // 4. 顯示計畫
    console.log(bold(`  服務生成計畫:`));
    for (const { service, status, skip, depCheck } of servicePlan) {
        const icon = skip ? '⏭' : !depCheck.satisfied ? '🔒' : status.exists ? '🔄' : '🆕';
        let label = skip ? '跳過 (已存在)' : status.exists ? '重新生成 (--force)' : '待生成';
        if (!depCheck.satisfied) label = `依賴未滿足: ${depCheck.missing.join(', ')}`;
        const methodCount = (service.methods || []).length;
        const typeLabel = service.type === 'repository' ? 'repo' : 'svc';
        console.log(`  ${icon} ${bold(service.name)} (${typeLabel}) — ${methodCount} 方法 — ${label}`);
    }
    console.log('');

    if (dryRun) {
        logInfo('乾跑模式: 以上是執行計畫，不會實際執行');
        return {
            completed: true,
            dryRun: true,
            plan: servicePlan.map(p => ({
                service: p.service.name,
                action: p.skip ? 'skip' : !p.depCheck.satisfied ? 'blocked' : 'generate',
            })),
            failureReports: [],
        };
    }

    // 5. 逐層逐服務執行
    const results = [];
    const failureReports = [];
    const completedServices = new Set();

    for (const layer of layers) {
        for (const svc of layer) {
            const plan = servicePlan.find(p => p.service.name === svc.name);
            if (plan.skip) {
                completedServices.add(svc.name);
                continue;
            }

            // 依賴檢查（包含已完成的服務）
            const depCheck = checkDependenciesExist(projectRoot, projectPath, svc);
            if (!depCheck.satisfied) {
                logWarn(`  🔒 ${svc.name}: 跳過 — 依賴未滿足 (${depCheck.missing.join(', ')})`);
                const depReport = {
                    type: 'dependency_missing',
                    service: svc.name,
                    missingDependencies: depCheck.missing.map(d => ({
                        name: d, expectedPath: '', searchedPaths: [],
                    })),
                    blockingServices: [],
                    actionRequired: '先執行依賴服務的管線',
                };
                failureReports.push(depReport);
                results.push({
                    service: svc.name,
                    completed: false,
                    failedState: 'dependency-check',
                    states: [],
                    report: depReport,
                });
                continue;
            }

            const num = `[${results.length + 1}/${servicePlan.filter(p => !p.skip).length}]`;
            console.log(colorize(`\n${'━'.repeat(60)}`, 'yellow'));
            console.log(bold(`  ${num} 生成 ${svc.name} (${svc.type || 'service'})`));
            console.log(colorize(`${'━'.repeat(60)}`, 'yellow'));

            // 建構管線
            const states = buildServicePipeline({
                serviceConfig: svc,
                projectPath,
                projectRoot,
            });

            if (validateOnly) {
                logInfo('  驗證模式: 只檢查 postcondition + preCheck');
                const validationResults = [];
                for (const state of states) {
                    // 先跑 preCheck
                    if (state.preCheck) {
                        const preResult = state.preCheck(projectRoot);
                        if (!preResult.passed) {
                            console.log(colorize(`  ✗ ${state.name} (preCheck 失敗)`, 'red'));
                            (preResult.errors || []).forEach(e => logError(`    ${e}`));
                            validationResults.push({
                                stateId: state.id,
                                stateName: state.name,
                                passed: false,
                                validation: preResult,
                                report: preResult.report || null,
                                skippedByPreCheck: true,
                            });
                            if (preResult.report) failureReports.push(preResult.report);
                            continue;
                        }
                    }
                    // 正常 postcondition 驗證
                    const validation = state.contract.validate(projectRoot);
                    const icon = validation.passed ? '✓' : '✗';
                    const color = validation.passed ? 'green' : 'red';
                    console.log(colorize(`  ${icon} ${state.name}`, color));
                    if (!validation.passed) {
                        validation.errors.forEach(e => logError(`    ${e}`));
                        if (state.reportBuilder) {
                            const report = state.reportBuilder(validation, {});
                            failureReports.push(report);
                        }
                    }
                    validationResults.push({
                        stateId: state.id,
                        stateName: state.name,
                        passed: validation.passed,
                        validation,
                    });
                }

                const allPassed = validationResults.every(r => r.passed);
                results.push({
                    service: svc.name,
                    completed: allPassed,
                    states: validationResults,
                });
                if (allPassed) completedServices.add(svc.name);
            } else {
                // 正式執行模式
                const sm = new StateMachine({
                    states,
                    agent,
                    projectRoot,
                });

                const smResult = await sm.run({});

                results.push({
                    service: svc.name,
                    completed: smResult.completed,
                    failedState: smResult.failedState || null,
                    states: smResult.results,
                });

                if (smResult.completed) {
                    completedServices.add(svc.name);
                } else {
                    // 收集失敗回報
                    const failedStates = (smResult.results || []).filter(r => !r.passed && r.report);
                    for (const fs of failedStates) {
                        failureReports.push(fs.report);
                    }
                    // 注意: 同一層的其他服務仍可繼續（它們互相獨立）
                    logError(`  ✗ ${svc.name} 生成失敗`);
                }
            }
        }
    }

    return {
        completed: results.every(r => r.completed),
        results,
        failureReports,
        completedServices: [...completedServices],
    };
}

// ============================================================
// 結構化回報輸出
// ============================================================

/**
 * 輸出模板化的結構化回報
 * 格式化為固定寬度的框線報告，供人類快速掃描
 */
function printStructuredReports(reports) {
    if (!reports || reports.length === 0) return;

    console.log(colorize(`\n${'══════════════════════════════════════════'}`, 'red'));
    console.log(bold(`  Batch B 結構化回報 (${reports.length} 份)`));
    console.log(colorize(`${'══════════════════════════════════════════'}`, 'red'));

    for (const report of reports) {
        console.log('');
        const header = `[${report.type}] ${report.service || report.page || ''}`;
        console.log(bold(`  ${header}`));
        console.log(`  ${'┌' + '─'.repeat(40) + '┐'}`);

        switch (report.type) {
            case 'component_missing':
                console.log(`  │ 頁面: ${(report.page || '').padEnd(31)}│`);
                console.log(`  │ 缺失元件:${' '.repeat(30)}│`);
                for (const c of (report.missingComponents || [])) {
                    const line = `  - ${c.name} (${c.category}/${c.priority})`;
                    console.log(`  │${line.padEnd(40)}│`);
                }
                break;

            case 'dependency_missing':
                console.log(`  │ 服務: ${(report.service || '').padEnd(31)}│`);
                console.log(`  │ 缺失依賴:${' '.repeat(30)}│`);
                for (const d of (report.missingDependencies || [])) {
                    const line = `  - ${d.name || d}`;
                    console.log(`  │${line.padEnd(40)}│`);
                }
                break;

            case 'module_gap':
                console.log(`  │ 服務: ${(report.service || '').padEnd(31)}│`);
                console.log(`  │ 狀態: ${(report.state || '').padEnd(31)}│`);
                console.log(`  │ 缺失: ${(report.missingModule || '').padEnd(31)}│`);
                break;

            case 'contract_extension_needed':
                console.log(`  │ 服務: ${(report.service || '').padEnd(31)}│`);
                console.log(`  │ 現有: ${(report.currentCapability || '').padEnd(31)}│`);
                console.log(`  │ 需要: ${(report.requiredCapability || '').padEnd(31)}│`);
                break;

            case 'validation_failed':
                console.log(`  │ 服務: ${(report.service || '').padEnd(31)}│`);
                console.log(`  │ 狀態: ${(report.state || '').padEnd(31)}│`);
                console.log(`  │ 重試: ${String(report.retryCount || 0).padEnd(31)}│`);
                for (const e of (report.errors || []).slice(0, 5)) {
                    const msg = typeof e === 'string' ? e : (e.message || '');
                    console.log(`  │  ${msg.substring(0, 38).padEnd(38)}│`);
                }
                break;

            case 'rate_limit':
                console.log(`  │ 服務: ${(report.service || '').padEnd(31)}│`);
                console.log(`  │ 狀態: ${(report.state || '').padEnd(31)}│`);
                console.log(`  │ 模型: ${(report.model || '').padEnd(31)}│`);
                if (report.retryAfter) {
                    console.log(`  │ API 建議等待: ${String(report.retryAfter + 's').padEnd(23)}│`);
                }
                console.log(`  │ 建議替代模型:${' '.repeat(26)}│`);
                for (const m of (report.suggestedModels || [])) {
                    console.log(`  │   → ${m.padEnd(33)}│`);
                }
                break;

            default:
                console.log(`  │ (未知報告類型: ${report.type})`.padEnd(41) + '│');
        }

        console.log(`  │ 建議: ${(report.actionRequired || '').padEnd(31)}│`);
        console.log(`  ${'└' + '─'.repeat(40) + '┘'}`);
    }
    console.log('');
}

// ============================================================
// Runner
// ============================================================

/**
 * 從 project.json 載入實體定義
 *
 * @param {string} projectRoot - 專案根目錄
 * @param {string} projectPath - 專案子路徑
 * @returns {Object} { project, entities }
 */
function loadProjectConfig(projectRoot, projectPath) {
    const configPath = path.join(projectRoot, projectPath, 'project.json');
    if (!fs.existsSync(configPath)) {
        throw new Error(`找不到專案配置: ${configPath}`);
    }

    const raw = fs.readFileSync(configPath, 'utf-8');
    let config;
    try {
        config = JSON.parse(raw);
    } catch (e) {
        throw new Error(`project.json 解析失敗: ${e.message}`);
    }

    if (!config.entities || !Array.isArray(config.entities)) {
        throw new Error('project.json 中缺少 entities 陣列');
    }

    return config;
}

/**
 * 執行管線
 *
 * @param {Object} options
 * @param {import('../agent-loop').AgentLoop} options.agent - Agent 實例
 * @param {string} options.projectRoot - 專案根目錄
 * @param {string} options.projectPath - 專案子路徑
 * @param {string} [options.entityFilter] - 只執行特定實體（名稱）
 * @param {boolean} [options.force] - 強制重新生成已存在的實體
 * @param {boolean} [options.dryRun] - 乾跑模式：只顯示會做什麼
 * @param {boolean} [options.validateOnly] - 只驗證，不執行 Agent
 * @returns {Promise<Object>} 執行結果
 */
async function runPipelines(options) {
    const {
        agent,
        projectRoot,
        projectPath = '',
        entityFilter = null,
        force = false,
        dryRun = false,
        validateOnly = false,
    } = options;

    const totalStart = Date.now();

    // 1. 載入配置
    const config = loadProjectConfig(projectRoot, projectPath);
    const projectName = config.project?.name || path.basename(projectPath);

    console.log(colorize(`\n${'═'.repeat(60)}`, 'cyan'));
    console.log(bold(`  Pipeline Runner: ${projectName}`));
    console.log(colorize(`  專案路徑: ${projectPath || '(root)'}`, 'gray'));
    console.log(colorize(`${'═'.repeat(60)}`, 'cyan'));

    // 2. 篩選要處理的實體
    let entities = config.entities.filter(e => !e.builtin);

    if (entityFilter) {
        entities = entities.filter(e =>
            e.name.toLowerCase() === entityFilter.toLowerCase()
        );
        if (entities.length === 0) {
            logError(`找不到實體 "${entityFilter}" (排除 builtin 後)`);
            return { completed: false, results: [], error: 'entity_not_found' };
        }
    }

    if (entities.length === 0) {
        logWarn('沒有需要生成的實體 (所有實體都標記為 builtin)');
        return { completed: true, results: [] };
    }

    // 3. 偵測已存在狀態
    const entityPlan = [];
    for (const entity of entities) {
        const status = detectEntityStatus(projectRoot, projectPath, entity.name);
        const skip = status.exists && !force;
        entityPlan.push({ entity, status, skip });
    }

    // 4. 顯示計畫
    console.log(bold(`\n  實體生成計畫:`));
    for (const { entity, status, skip } of entityPlan) {
        const icon = skip ? '⏭' : status.exists ? '🔄' : '🆕';
        const label = skip ? '跳過 (已存在)' : status.exists ? '重新生成 (--force)' : '待生成';
        const fieldCount = (entity.fields || []).length;
        console.log(`  ${icon} ${bold(entity.name)} — ${fieldCount} 個欄位 — ${label}`);
    }
    console.log('');

    if (dryRun) {
        logInfo('乾跑模式: 以上是 Batch A 執行計畫');

        // Batch B dry run
        let serviceResults = null;
        if (config.extendedServices && config.extendedServices.length > 0) {
            serviceResults = await runServicePipelines({
                agent: null,
                projectRoot,
                projectPath,
                services: config.extendedServices,
                config,
                serviceFilter: entityFilter,
                force,
                dryRun: true,
                validateOnly: false,
            });
        }

        return {
            completed: true,
            dryRun: true,
            plan: entityPlan.map(p => ({
                entity: p.entity.name,
                action: p.skip ? 'skip' : 'generate',
            })),
            serviceResults: serviceResults || null,
        };
    }

    // 5. 逐實體執行
    const results = [];
    const toGenerate = entityPlan.filter(p => !p.skip);

    for (let i = 0; i < toGenerate.length; i++) {
        const { entity } = toGenerate[i];
        const num = `[${i + 1}/${toGenerate.length}]`;

        console.log(colorize(`\n${'━'.repeat(60)}`, 'yellow'));
        console.log(bold(`  ${num} 生成 ${entity.name}`));
        console.log(colorize(`${'━'.repeat(60)}`, 'yellow'));

        // 找到 reference entity（優先用 builtin 的 User）
        const builtinEntity = config.entities.find(e => e.builtin);
        const refEntity = builtinEntity
            ? {
                  name: builtinEntity.name,
                  modelPath: `backend/Models/${builtinEntity.name}.cs`,
                  servicePath: `backend/Services/${builtinEntity.name}Service.cs`,
              }
            : undefined;

        // 建構管線
        const states = buildCrudPipeline({
            entityName: entity.name,
            fields: entity.fields || [],
            projectPath,
            projectRoot,
            plural: entity.plural,
            apiPath: entity.apiPath,
            referenceEntity: refEntity,
        });

        if (validateOnly) {
            // 只驗證模式：跳過 Agent 執行，直接用 postcondition 驗證
            logInfo('  驗證模式: 只檢查 postcondition');
            const validationResults = [];
            for (const state of states) {
                const validation = state.contract.validate(projectRoot);
                const icon = validation.passed ? '✓' : '✗';
                const color = validation.passed ? 'green' : 'red';
                console.log(colorize(`  ${icon} ${state.name}`, color));
                if (!validation.passed) {
                    validation.errors.forEach(e => logError(`    ${e}`));
                }
                validationResults.push({
                    stateId: state.id,
                    stateName: state.name,
                    passed: validation.passed,
                    validation,
                });
            }

            const allPassed = validationResults.every(r => r.passed);
            results.push({
                entity: entity.name,
                completed: allPassed,
                states: validationResults,
            });
        } else {
            // 正式執行模式
            const sm = new StateMachine({
                states,
                agent,
                projectRoot,
            });

            const smResult = await sm.run({});

            results.push({
                entity: entity.name,
                completed: smResult.completed,
                failedState: smResult.failedState || null,
                states: smResult.results,
            });

            if (!smResult.completed) {
                logError(`  ✗ ${entity.name} 生成失敗，中止後續實體`);
                break;
            }
        }
    }

    // 6. Batch B: Extended Services
    let serviceResults = null;
    if (config.extendedServices && config.extendedServices.length > 0) {
        serviceResults = await runServicePipelines({
            agent,
            projectRoot,
            projectPath,
            services: config.extendedServices,
            config,
            serviceFilter: entityFilter, // 同一個 filter 可用於服務名
            force,
            dryRun,
            validateOnly,
        });

        // 輸出結構化回報
        if (serviceResults.failureReports && serviceResults.failureReports.length > 0) {
            printStructuredReports(serviceResults.failureReports);
        }
    }

    // 7. 匯總報告（Batch A）
    const elapsed = Date.now() - totalStart;
    const report = buildSummaryReport(results, entityPlan, elapsed, validateOnly);
    console.log(report);

    // 合併 Batch B 結果
    const allCompleted = results.every(r => r.completed) &&
        (!serviceResults || serviceResults.completed);

    return {
        completed: allCompleted,
        results,
        serviceResults: serviceResults || null,
        skipped: entityPlan.filter(p => p.skip).map(p => p.entity.name),
        elapsed,
        report,
    };
}

/**
 * 建構匯總報告
 */
function buildSummaryReport(results, entityPlan, elapsed, validateOnly) {
    const lines = [
        '',
        colorize(`${'═'.repeat(60)}`, results.every(r => r.completed) ? 'green' : 'red'),
        bold(`  ${validateOnly ? '驗證' : '管線執行'}報告`),
        `  耗時: ${formatDuration(elapsed)}`,
        colorize(`${'═'.repeat(60)}`, 'gray'),
        '',
    ];

    // 跳過的
    const skipped = entityPlan.filter(p => p.skip);
    if (skipped.length > 0) {
        lines.push(`  ⏭ 跳過 (已存在): ${skipped.map(p => p.entity.name).join(', ')}`);
    }

    // 每個實體的結果
    for (const r of results) {
        const icon = r.completed ? '✓' : '✗';
        const stateResults = (r.states || []).map(s => {
            const si = s.passed ? '✓' : '✗';
            return `${si}${s.stateName}`;
        }).join(' → ');

        lines.push(`  ${icon} ${bold(r.entity)}: ${stateResults}`);

        if (!r.completed && r.failedState) {
            lines.push(`    停在: ${r.failedState}`);
        }
    }

    // 統計
    const passed = results.filter(r => r.completed).length;
    const total = results.length;
    lines.push('');
    lines.push(`  結果: ${passed}/${total} 個實體${validateOnly ? '通過驗證' : '成功生成'}`);
    lines.push('');

    return lines.join('\n');
}

module.exports = {
    runPipelines,
    runServicePipelines,
    loadProjectConfig,
    detectEntityStatus,
    detectServiceStatus,
    checkDependenciesExist,
    resolveServiceDependencyOrder,
    printStructuredReports,
};
