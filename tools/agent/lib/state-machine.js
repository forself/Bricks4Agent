'use strict';

const fs = require('fs');
const path = require('path');
const { logInfo, logWarn, logError, logSuccess, bold, colorize, formatDuration } = require('./utils');

/**
 * 狀態機檢核點機制
 *
 * 核心原則：
 *   每個狀態被一對檢核點包夾 —— precondition 與 postcondition 共享同一份 completion contract。
 *   precondition 用 contract 建構注入上下文（就源規範），
 *   postcondition 用同一份 contract 驗證產出物。
 *
 * 架構：
 *   [Precondition: 注入合法素材] → [Agent 執行] → [Postcondition: 驗證產出]
 *          ↑                                                ↑
 *          └──────── 同一份 Completion Contract ────────────┘
 *
 * 任務分類：
 *   Category 1 (強可驗證): 白名單選擇、格式填值、命名規則、欄位映射
 *   Category 2 (弱可驗證): code transform、依範例改寫、需編譯/AST 輔助
 *   Category 3 (暫不處理): 隱含需求、跨層語義、UX 合理性
 */

// ============================================================
// CompletionContract — 完成合約
// ============================================================

/**
 * 定義一個子任務的「什麼算完成」。
 * 同一份規則用於兩端：
 *   - 輸入端：buildContext() 產生 Agent 的限定上下文
 *   - 輸出端：validate() 檢查產出是否滿足合約
 */
class CompletionContract {
    /**
     * @param {Object} config
     * @param {string} config.id - 合約 ID
     * @param {string} config.description - 描述
     * @param {number} config.category - 任務類別 (1=強可驗證, 2=弱可驗證)
     * @param {string[]} config.allowedRefs - 允許的引用白名單
     * @param {string[]} config.requiredOutputs - 必須產出的檔案路徑
     * @param {Object[]} config.fileChecks - 檔案層級的檢查規則
     * @param {string[]} config.forbiddenPatterns - 禁止出現的模式 (regex)
     * @param {string[]} config.requiredPatterns - 必須出現的模式 (regex)
     * @param {Object[]} config.expectedFields - 預期的 schema 欄位 [{name, type}]
     * @param {string[]} config.contextFiles - 要注入的參考檔案路徑
     * @param {string[]} config.constraints - 明文約束描述
     */
    constructor(config) {
        this.id = config.id;
        this.description = config.description || '';
        this.category = config.category || 1;

        // 白名單
        this.allowedRefs = config.allowedRefs || [];

        // 預期產出檔案
        this.requiredOutputs = config.requiredOutputs || [];

        // 禁止模式
        this.forbiddenPatterns = (config.forbiddenPatterns || []).map(p =>
            p instanceof RegExp ? p : new RegExp(p, 'g')
        );

        // 必要模式
        this.requiredPatterns = (config.requiredPatterns || []).map(p =>
            p instanceof RegExp ? p : new RegExp(p, 'g')
        );

        // Schema 欄位
        this.expectedFields = config.expectedFields || [];

        // 檔案檢查
        this.fileChecks = config.fileChecks || [];

        // 注入的參考檔案
        this.contextFiles = config.contextFiles || [];

        // 明文約束
        this.constraints = config.constraints || [];

        // 跨檔案驗證器（接收所有已讀取的檔案內容 map）
        this.crossFileValidators = config.crossFileValidators || [];
    }

    /**
     * 【輸入端】建構就源規範上下文
     * 從 contract 的規則推導出 Agent 該看到的東西
     */
    buildContext(projectRoot) {
        const ctx = {
            allowedRefs: [...this.allowedRefs],
            constraints: [...this.constraints],
            referenceCode: {},
            expectedOutputs: [...this.requiredOutputs],
        };

        // 從禁止模式推導約束
        if (this.forbiddenPatterns.length > 0) {
            ctx.constraints.push(
                '【禁止】不得使用: ' + this.forbiddenPatterns.map(p => p.source).join(', ')
            );
        }

        // 注入參考檔案內容
        for (const relPath of this.contextFiles) {
            const absPath = path.join(projectRoot, relPath);
            try {
                ctx.referenceCode[relPath] = fs.readFileSync(absPath, 'utf-8');
            } catch {
                // 檔案不存在，跳過
            }
        }

        // Schema 資訊
        if (this.expectedFields.length > 0) {
            ctx.schema = this.expectedFields;
        }

        return ctx;
    }

    /**
     * 【輸出端】驗證產出是否滿足合約
     * @returns {{ passed: boolean, errors: string[], warnings: string[] }}
     */
    validate(projectRoot) {
        const errors = [];
        const warnings = [];
        const filesContent = {}; // 收集各檔案內容供跨檔案驗證

        // 1. 必要檔案存在性
        for (const relPath of this.requiredOutputs) {
            const absPath = path.join(projectRoot, relPath);
            if (!fs.existsSync(absPath)) {
                errors.push(`[檔案缺失] ${relPath}`);
            }
        }

        // 2. 逐檔檢查
        for (const fc of this.fileChecks) {
            const absPath = path.join(projectRoot, fc.path);
            let content;
            try {
                content = fs.readFileSync(absPath, 'utf-8');
            } catch {
                if (fc.mustExist !== false) {
                    errors.push(`[檔案缺失] ${fc.path}`);
                }
                continue;
            }

            // 2a. 白名單引用檢查
            if (fc.whitelistCheck && this.allowedRefs.length > 0) {
                const refType = fc.refPattern || 'using';
                const refs = extractReferences(content, refType);
                const allowed = new Set(this.allowedRefs);
                for (const ref of refs) {
                    if (!allowed.has(ref)) {
                        errors.push(`[白名單違規] ${fc.path}: 引用了 "${ref}" 不在允許清單中`);
                    }
                }
            }

            // 2b. 禁止模式檢查
            for (const pattern of this.forbiddenPatterns) {
                pattern.lastIndex = 0;
                const match = pattern.exec(content);
                if (match) {
                    errors.push(`[禁止模式] ${fc.path}: 包含 "${match[0]}" (規則: ${pattern.source})`);
                }
            }

            // 2c. 必要模式檢查（只對必要檔案執行，跳過 mustExist:false 的可選檔案）
            if (fc.mustExist !== false) {
                for (const pattern of this.requiredPatterns) {
                    pattern.lastIndex = 0;
                    if (!pattern.test(content)) {
                        errors.push(`[缺少必要模式] ${fc.path}: 未找到 "${pattern.source}"`);
                    }
                }
            }

            // 2d. Schema 欄位檢查
            if (fc.schemaCheck && this.expectedFields.length > 0) {
                for (const field of this.expectedFields) {
                    const propRegex = new RegExp(
                        `(public|private)\\s+\\S+\\s+${field.name}\\s*\\{`, 'i'
                    );
                    if (!propRegex.test(content)) {
                        errors.push(`[Schema 欄位缺失] ${fc.path}: 缺少 "${field.name}" 屬性`);
                    }
                }
            }

            // 2e. 自定義驗證器
            if (fc.validators) {
                for (const validator of fc.validators) {
                    const result = validator(content, fc.path);
                    if (result.errors) errors.push(...result.errors);
                    if (result.warnings) warnings.push(...result.warnings);
                }
            }

            // 收集檔案內容供跨檔案驗證使用
            filesContent[fc.path] = content;
        }

        // 3. 跨檔案驗證（interface 可在獨立檔案或內嵌在實作檔案中）
        for (const validator of this.crossFileValidators) {
            const result = validator(filesContent);
            if (result.errors) errors.push(...result.errors);
            if (result.warnings) warnings.push(...result.warnings);
        }

        return { passed: errors.length === 0, errors, warnings };
    }
}

// ============================================================
// State — 狀態節點
// ============================================================

class State {
    /**
     * @param {Object} config
     * @param {string} config.id - 狀態 ID
     * @param {string} config.name - 顯示名稱
     * @param {CompletionContract} config.contract - 完成合約
     * @param {Function} config.promptBuilder - (context, taskContext) => string
     * @param {number} config.maxRetries - 最大重試次數 (預設 2)
     * @param {Function} [config.preCheck] - (projectRoot) => { passed, errors, report? }
     *   前置檢查：在 Agent 執行前先檢查外部條件（例如依賴是否存在、元件是否到位）。
     *   若 passed=false，直接跳過 Agent 執行並記錄結構化回報。
     * @param {Function} [config.reportBuilder] - (validationResult, taskContext) => Object
     *   結構化回報建構器：將 postcondition 失敗轉換為模板化回報（非自然語言）。
     */
    constructor(config) {
        this.id = config.id;
        this.name = config.name;
        this.contract = config.contract;
        this.promptBuilder = config.promptBuilder;
        this.maxRetries = config.maxRetries ?? 2;
        this.preCheck = config.preCheck || null;
        this.reportBuilder = config.reportBuilder || null;
    }
}

// ============================================================
// Rate Limit 偵測
// ============================================================

/**
 * 從 agentResponse 偵測 Rate Limit 錯誤
 * agent-loop 回傳格式: "錯誤: API 串流錯誤: Rate limit reached for gpt-4o ..."
 *                    或 "錯誤: API 回傳 HTTP 429: ..."
 *
 * @param {string} response - agent.send() 回傳的字串
 * @returns {{ detected: boolean, model?: string, retryAfter?: number }}
 */
function detectRateLimit(response) {
    if (!response || typeof response !== 'string') return { detected: false };

    const isRateLimit =
        response.includes('Rate limit') ||
        response.includes('rate_limit') ||
        response.includes('HTTP 429') ||
        response.includes('Too Many Requests') ||
        response.includes('tokens per min') ||
        response.includes('requests per min');

    if (!isRateLimit) return { detected: false };

    // 從 "Rate limit reached for gpt-4o in organization ..." 中提取模型名
    const modelMatch = response.match(/for\s+([\w.-]+)\s+in\s+organization/);
    // 從 "Please try again in 14.444s." 中提取等待秒數
    const retryMatch = response.match(/try again in\s+([\d.]+)s/);

    return {
        detected: true,
        model: modelMatch ? modelMatch[1] : null,
        retryAfter: retryMatch ? parseFloat(retryMatch[1]) : null,
    };
}

/**
 * 根據當前模型，建議替代模型
 * @param {string} currentModel - 當前使用的模型名
 * @returns {string[]} 建議的替代模型列表
 */
function getSuggestedModels(currentModel) {
    const FALLBACK_MAP = {
        'gpt-4o':        ['gpt-4o-mini', 'gpt-4-turbo'],
        'gpt-4o-mini':   ['gpt-4o', 'gpt-4-turbo'],
        'gpt-4-turbo':   ['gpt-4o-mini', 'gpt-4o'],
        'gpt-4.1':       ['gpt-4.1-mini', 'gpt-4.1-nano'],
        'gpt-4.1-mini':  ['gpt-4.1-nano', 'gpt-4.1'],
    };
    return FALLBACK_MAP[currentModel] || ['gpt-4o-mini'];
}

// ============================================================
// StateMachine — 狀態機
// ============================================================

class StateMachine {
    /**
     * @param {Object} config
     * @param {State[]} config.states - 狀態列表（順序執行）
     * @param {import('./agent-loop').AgentLoop} config.agent - Agent 實例
     * @param {string} config.projectRoot - 專案根目錄
     */
    constructor(config) {
        this.states = config.states || [];
        this.agent = config.agent;
        this.projectRoot = config.projectRoot;
        this.results = [];
    }

    /**
     * 執行狀態機
     * @param {Object} taskContext - 任務上下文（跨狀態共享資料）
     */
    async run(taskContext = {}) {
        const totalStart = Date.now();
        console.log(colorize(`\n${'═'.repeat(60)}`, 'cyan'));
        console.log(bold(`  狀態機啟動: ${this.states.length} 個狀態`));
        console.log(colorize(`${'═'.repeat(60)}`, 'cyan'));

        for (let i = 0; i < this.states.length; i++) {
            const state = this.states[i];
            const stateNum = `[${i + 1}/${this.states.length}]`;

            console.log(colorize(`\n${'─'.repeat(50)}`, 'gray'));
            console.log(bold(`  ${stateNum} ${state.name}`));
            console.log(colorize(`  合約: ${state.contract.description}`, 'gray'));
            console.log(colorize(`${'─'.repeat(50)}`, 'gray'));

            // === PRE-CHECK: 前置條件檢查（元件/依賴/模組） ===
            if (state.preCheck) {
                const preResult = state.preCheck(this.projectRoot);
                if (!preResult.passed) {
                    logError(`  前置檢查失敗 (${(preResult.errors || []).length} 個問題)`);
                    (preResult.errors || []).forEach(e => logError(`    ${e}`));

                    this.results.push({
                        stateId: state.id,
                        stateName: state.name,
                        attempt: 0,
                        passed: false,
                        validation: preResult,
                        report: preResult.report || null,
                        skippedByPreCheck: true,
                    });

                    return {
                        completed: false,
                        failedState: state.id,
                        results: this.results,
                        report: this._buildReport(Date.now() - totalStart),
                    };
                }
                logInfo(`  前置檢查: 通過 ✓`);
            }

            let passed = false;
            let lastValidation = null;
            let lastAgentResponse = null;
            let lastAttempt = 0;

            for (let attempt = 0; attempt <= state.maxRetries; attempt++) {
                lastAttempt = attempt;

                if (attempt > 0) {
                    logWarn(`  ↻ 重試 ${attempt}/${state.maxRetries}`);
                }

                // === PRE-CHECKPOINT: 就源規範 ===
                const context = state.contract.buildContext(this.projectRoot);
                logInfo(`  前置檢核: ${context.allowedRefs.length} 個允許引用, ${context.constraints.length} 個約束`);

                // 建構 prompt
                let prompt = state.promptBuilder(context, taskContext);

                // 重試時注入上次的錯誤訊息
                if (attempt > 0 && lastValidation && lastValidation.errors.length > 0) {
                    prompt += '\n\n【上次執行的檢核失敗，請修正以下問題】:\n';
                    prompt += lastValidation.errors.map((e, i) => `${i + 1}. ${e}`).join('\n');
                }

                // === AGENT EXECUTION ===
                logInfo('  Agent 執行中...');
                this.agent.clearHistory();
                const agentResponse = await this.agent.send(prompt);
                lastAgentResponse = agentResponse;

                // === Rate Limit 偵測: 跳過無意義的重試 ===
                const rlCheck = detectRateLimit(agentResponse);
                if (rlCheck.detected) {
                    logError(`  ⚠ Rate Limit 偵測到 — 跳過剩餘重試`);
                    if (rlCheck.retryAfter) {
                        logError(`    API 建議等待: ${rlCheck.retryAfter}s`);
                    }
                    lastValidation = {
                        passed: false,
                        errors: [`[rate_limit] ${agentResponse.substring(0, 200)}`],
                        warnings: [],
                    };
                    break;
                }

                // === POST-CHECKPOINT: 驗證產出 ===
                lastValidation = state.contract.validate(this.projectRoot);

                if (lastValidation.passed) {
                    logSuccess(`  後置檢核: 通過 ✓`);
                    if (lastValidation.warnings.length > 0) {
                        lastValidation.warnings.forEach(w => logWarn(`    ⚠ ${w}`));
                    }
                    passed = true;

                    this.results.push({
                        stateId: state.id,
                        stateName: state.name,
                        attempt: attempt + 1,
                        passed: true,
                        validation: lastValidation,
                    });
                    break;
                } else {
                    logError(`  後置檢核: 失敗 ✗ (${lastValidation.errors.length} 個錯誤)`);
                    lastValidation.errors.forEach(e => logError(`    ${e}`));
                }
            }

            if (!passed) {
                // 判斷是否為 Rate Limit 導致的失敗
                const rlInfo = detectRateLimit(lastAgentResponse);

                if (rlInfo.detected) {
                    logError(`\n  ✗ 狀態 "${state.name}" 因 Rate Limit 中止 (第 ${lastAttempt + 1} 次嘗試)`);
                } else {
                    logError(`\n  ✗ 狀態 "${state.name}" 在 ${state.maxRetries + 1} 次嘗試後失敗`);
                }

                // Rate Limit 產生專用回報；一般失敗走原有 reportBuilder
                const report = rlInfo.detected
                    ? {
                        type: 'rate_limit',
                        service: taskContext.serviceName || '',
                        state: state.id,
                        model: this.agent.model || '',
                        retryAfter: rlInfo.retryAfter,
                        suggestedModels: getSuggestedModels(this.agent.model),
                        actionRequired: `模型 ${this.agent.model} 達到速率限制，建議改用其他模型重新執行`,
                    }
                    : (state.reportBuilder
                        ? state.reportBuilder(lastValidation, taskContext)
                        : null);

                this.results.push({
                    stateId: state.id,
                    stateName: state.name,
                    attempt: lastAttempt + 1,
                    passed: false,
                    validation: lastValidation,
                    report,
                });

                return {
                    completed: false,
                    failedState: state.id,
                    results: this.results,
                    report: this._buildReport(Date.now() - totalStart),
                };
            }
        }

        const elapsed = Date.now() - totalStart;
        console.log(colorize(`\n${'═'.repeat(60)}`, 'green'));
        logSuccess(`  狀態機完成: ${this.states.length}/${this.states.length} 通過 (${formatDuration(elapsed)})`);
        console.log(colorize(`${'═'.repeat(60)}`, 'green'));

        return {
            completed: true,
            results: this.results,
            report: this._buildReport(elapsed),
        };
    }

    _buildReport(elapsed) {
        const lines = [
            '',
            '=== 狀態機執行報告 ===',
            `耗時: ${formatDuration(elapsed)}`,
            '',
        ];

        for (const r of this.results) {
            const icon = r.passed ? '✓' : '✗';
            const color = r.passed ? 'green' : 'red';
            lines.push(`  ${icon} ${r.stateName} (嘗試 ${r.attempt} 次)`);
            if (!r.passed && r.validation) {
                r.validation.errors.forEach(e => lines.push(`    錯誤: ${e}`));
            }
        }

        lines.push('');
        return lines.join('\n');
    }
}

// ============================================================
// 工具函數
// ============================================================

/**
 * 從原始碼中擷取引用
 * @param {string} content - 檔案內容
 * @param {string} type - 'using' (C#) 或 'import' (JS)
 * @returns {Set<string>}
 */
function extractReferences(content, type = 'using') {
    const refs = new Set();

    if (type === 'using') {
        const regex = /^using\s+([\w.]+)\s*;/gm;
        let m;
        while ((m = regex.exec(content)) !== null) {
            refs.add(m[1]);
        }
    } else if (type === 'import') {
        // import ... from '...'
        const regex = /import\s+.*?from\s+['"](.+?)['"]/gm;
        let m;
        while ((m = regex.exec(content)) !== null) {
            refs.add(m[1]);
        }
        // import '...'
        const regex2 = /import\s+['"](.+?)['"]/gm;
        while ((m = regex2.exec(content)) !== null) {
            refs.add(m[1]);
        }
    }

    return refs;
}

module.exports = { CompletionContract, State, StateMachine, extractReferences };
