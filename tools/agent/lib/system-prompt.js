'use strict';

const fs = require('fs');
const path = require('path');
const { getToolDescriptions } = require('./tool-registry');
const { logInfo, logWarn } = require('./utils');

const MAX_AGENT_MD_CHARS_NATIVE = 8000;
const MAX_AGENT_MD_CHARS_REACT = 4000; // ReAct 模式需為工具說明留空間

// ─── 基礎 Agent 身份 ───

const BASE_PROMPT = `你是一個 AI 程式助手。你會閱讀、理解和修改使用者專案中的程式碼。

工作準則：
- 修改前先閱讀相關檔案
- 做出變更前先說明你的計畫
- 做最小、聚焦的修改
- 保持既有程式碼風格
- 不確定時向使用者確認
- 使用繁體中文回應`;

// ─── ReAct 工具呼叫說明 ───

const REACT_INSTRUCTIONS = `
## 工具呼叫

你可以使用以下工具。要呼叫工具，請使用 <tool_call> 區塊：

<tool_call>
{"name": "工具名稱", "arguments": {"參數1": "值1", "參數2": "值2"}}
</tool_call>

每次工具呼叫後，你會收到一個 <tool_result> 區塊包含執行結果。
你可以連續呼叫多個工具（每次一個 <tool_call> 區塊）。
當你收集到足夠資訊後，用純文字給出最終回答（不再包含 <tool_call>）。

### 可用工具

`;

/**
 * 建構完整系統提示詞
 * @param {Object} options
 * @param {string} options.projectRoot - 專案根目錄
 * @param {boolean} options.useReact - 是否使用 ReAct 模式
 * @param {boolean} options.verbose - 是否顯示偵測資訊
 * @param {string} [options.toolDescriptions] - 自訂工具描述
 * @param {Object} [options.governed] - 受控模式 broker 契約摘要
 * @returns {string} 系統提示詞
 */
function buildSystemPrompt(options) {
    const {
        projectRoot,
        useReact,
        verbose,
        toolDescriptions = getToolDescriptions(),
        governed = null,
    } = options;
    const parts = [BASE_PROMPT];

    if (governed) {
        parts.push(buildGovernedSection(governed));
    } else {
        parts.push('\n## 執行模型\n\n你可以直接透過工具與本地工作區互動。');
    }

    const agentMdPath = findAgentMd(projectRoot);
    const maxChars = useReact ? MAX_AGENT_MD_CHARS_REACT : MAX_AGENT_MD_CHARS_NATIVE;
    if (agentMdPath) {
        if (verbose) logInfo(`載入專案手冊: ${agentMdPath}`);
        try {
            let content = fs.readFileSync(agentMdPath, 'utf8');
            if (content.length > maxChars) {
                const sections = content.split(/\n## /);
                let truncated = sections[0];
                for (let i = 1; i < sections.length; i++) {
                    const candidate = truncated + '\n## ' + sections[i];
                    if (candidate.length > maxChars) break;
                    truncated = candidate;
                }
                content = truncated + '\n\n（完整手冊可透過 read_file(\'AGENT.md\') 取得）';
                if (verbose) logWarn(`AGENT.md 已截斷至 ${content.length} 字元`);
            }

            parts.push(`\n## 專案上下文\n\n本專案提供了 AI Agent 操作手冊，以下是相關內容：\n\n<project_manual>\n${content}\n</project_manual>`);
        } catch (e) {
            if (verbose) logWarn(`無法載入 AGENT.md: ${e.message}`);
        }
    } else if (verbose) {
        logInfo('未偵測到 AGENT.md，以通用模式運行');
    }

    if (useReact) {
        parts.push(REACT_INSTRUCTIONS + toolDescriptions);
    }

    return parts.join('\n');
}

function buildGovernedSection(governed) {
    const capabilityLines = governed.allowedCapabilities.length > 0
        ? governed.allowedCapabilities.map((capability, index) => {
            const scope = JSON.stringify(capability.scopeOverride || {});
            const schema = JSON.stringify(capability.paramSchema || {});
            return `${index + 1}. ${capability.capabilityId} → tool=${capability.toolName || capability.route || '(unmapped)'} | route=${capability.route || '(n/a)'} | risk=${capability.riskLevel} | approval=${capability.approvalPolicy} | scope=${scope} | quota=${capability.remainingQuota} | expires_at=${capability.expiresAt} | params=${schema}`;
        }).join('\n')
        : '目前這個 agent 沒有任何可請求 capability。若任務需要額外權限，必須明確回報權限不足。';

    return `
## Governed Broker Contract

你目前運行於受控容器模式。你沒有直接的檔案系統、shell 或網路副作用權限。
你只能要求執行階段透過 HTTP POST + JSON body 向 Broker 中介核心送出請求，再由 Broker 依照這個 agent 的 role、session、grant、scope 與 policy 決定是否執行。

硬限制：
- 只可請求下列 Broker 路由，且只能使用 POST。
- 只可請求目前 grants 允許的 capability_id。
- 每次請求都必須對應單一 capability_id 與單一 payload。
- 如果需要的 capability 不在目前清單內，直接說明權限不足，不可自行假設已授權。
- 你不可把自己描述成直接執行 shell、直接讀寫檔案或直接呼叫外部 API；所有動作都必須經 Broker。

目前 Broker 目標：
- Base URL: ${governed.brokerUrl}
- Register: POST ${governed.brokerRoutes.register}
- Submit: POST ${governed.brokerRoutes.submit}
- Heartbeat: POST ${governed.brokerRoutes.heartbeat}
- Close: POST ${governed.brokerRoutes.close}
- List Capabilities: POST ${governed.brokerRoutes.capabilitiesList}
- List Grants: POST ${governed.brokerRoutes.grantsList}

目前這個 agent 的身分：
- principal_id: ${governed.session.principalId}
- task_id: ${governed.session.taskId}
- role_id: ${governed.session.roleId}
- session_id: ${governed.session.sessionId}
- expires_at: ${governed.session.expiresAt}

目前這個 agent 可請求的功能與範圍：
${capabilityLines}

標準 POST JSON 格式：

1. Session register 外層 POST JSON
\`\`\`json
${JSON.stringify(governed.requestBodies.registerOuter, null, 2)}
\`\`\`

2. Execution submit 外層 POST JSON
\`\`\`json
${JSON.stringify(governed.requestBodies.submitOuter.body, null, 2)}
\`\`\`

3. Execution submit 解密前的 plaintext JSON body
\`\`\`json
${JSON.stringify(governed.requestBodies.submitOuter.plaintext, null, 2)}
\`\`\`

4. 其他受控 POST JSON body
\`\`\`json
${JSON.stringify({
    heartbeat: governed.requestBodies.heartbeatPlaintext,
    grants_list: governed.requestBodies.grantsListPlaintext,
    capabilities_list: governed.requestBodies.capabilitiesListPlaintext,
    close: governed.requestBodies.closePlaintext,
}, null, 2)}
\`\`\`

執行語意：
- 你要做的不是直接執行副作用，而是選擇當前 grants 內最合適的 capability/tool 請求 Broker。
- 你的每次工具呼叫都會被轉成上面的 POST 契約，由 Broker 做權限審核後才可能實際執行。
`;
}

/**
 * 搜尋 AGENT.md 檔案
 * 從 projectRoot 開始，向上搜尋最多 3 層
 * @param {string} startDir
 * @returns {string|null}
 */
function findAgentMd(startDir) {
    let dir = startDir;
    const root = path.parse(dir).root;

    for (let i = 0; i < 4; i++) {
        const candidate = path.join(dir, 'AGENT.md');
        try {
            fs.accessSync(candidate);
            return candidate;
        } catch (_) {
            // 也檢查 .agentrc
        }
        const parent = path.dirname(dir);
        if (parent === dir || parent === root) break;
        dir = parent;
    }
    return null;
}

module.exports = { buildSystemPrompt };
