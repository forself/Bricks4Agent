'use strict';

const fs = require('fs');
const path = require('path');
const { GENERATION_SCAFFOLD_CAPABILITY_ID, getToolDescriptions } = require('./tool-registry');
const { logInfo, logWarn } = require('./utils');

const MAX_AGENT_MD_CHARS_NATIVE = 8000;
const MAX_AGENT_MD_CHARS_REACT = 4000;

const BASE_PROMPT = `You are an AI coding agent operating inside the user's project.
Follow project instructions exactly, inspect before changing code, and stay within the allowed tool and policy boundaries.
- Be concrete and technical.
- Prefer deterministic edits over speculative changes.
- Do not claim to have performed actions you did not actually perform.
- If access is governed, only use the routes, capabilities, and scopes explicitly granted.
- For any web page, website, frontend, HTML, SPA, or browser UI work in this repository, use the custom component library first.
- Prefer components from packages/javascript/browser/ui_components or the generated project runtime at ./runtime/ui_components/index.js, including BasicButton, ButtonGroup, FeatureCard, PhotoCard, ImageViewer, SideMenu, TabContainer, DataTable, InfoPanel, and PhotoWall when they match the UI need.
- Hand-roll native HTML/CSS/JS only for behavior or visuals that the custom component library does not provide, and keep that fallback narrowly scoped.`;

// 生成類任務的精簡基礎提示：不提元件清單、repo 路徑或專案手冊。生成代理只撰寫 DefinitionTemplate，
// 可用的頁型、欄位型別與規則以 query_component_catalog 的回傳為準；手冊中的 CLI 範例與型別表會和型錄衝突。
const GENERATION_BASE_PROMPT = `You are a governed generation agent. You write one DefinitionTemplate, a declarative JSON
description of a front-end prototype, and hand it to the broker-governed generation tools.
- You do not write code, HTML, styles or files, and you have no file or shell tools.
- The catalog returned by query_component_catalog is the only source for page types, field types and rules.
- Be precise and follow the catalog rules exactly; do not invent keys, types or components.
- Do not claim to have performed actions you did not actually perform.
- Only use the routes, capabilities, and scopes explicitly granted.`;

// 生成任務在還沒有成功生成前回了沒有工具呼叫的訊息時，agent loop 追加一次的提醒（之後再沒有工具呼叫就結束）。
const GENERATION_CONTINUE_REMINDER = `Reminder: this task runs unattended and nothing has been generated yet. A reply
without a tool call ends the task, and nobody will answer questions. Do not write the definition or questions as
text: submit the DefinitionTemplate with validate_definition now (then generate_scaffold), and assume what the work
item leaves open. Reply without a tool call again only if you cannot continue, for example when the
validate_definition calls are used up; that reply is your final summary.`;

const REACT_INSTRUCTIONS = `
## Tool Calls

When using ReAct mode, emit tool calls with this exact wrapper:

<tool_call>
{"name": "tool_name", "arguments": {"arg1": "value"}}
</tool_call>

After each tool result, continue reasoning from the returned data and only stop once the task is complete.

## Available Tools

`;

function buildSystemPrompt(options) {
    const {
        projectRoot,
        useReact,
        verbose,
        toolDescriptions = getToolDescriptions(),
        governed = null,
        maxIterations = null,
    } = options;

    const generationTask = Boolean(governed) && isGenerationTask(governed);
    const parts = [generationTask ? GENERATION_BASE_PROMPT : BASE_PROMPT];

    if (governed) {
        parts.push(buildGovernedSection(governed));
        if (generationTask) {
            parts.push(buildGenerationSection(governed, maxIterations));
        }
    } else {
        parts.push('\n## Execution Mode\n\nYou may use the locally registered tools directly.');
    }

    // 生成類任務不附專案手冊：手冊的 CLI 範例與欄位型別表會引導模型寫出驗證器拒絕的定義，
    // 而且生成代理沒有讀檔工具，手冊中「讀 AGENT.md 其餘部分」的指示也做不到。
    const agentMdPath = generationTask ? null : resolveAgentManualPath(projectRoot);
    if (generationTask && verbose) logInfo('Project manual skipped for a governed generation task');
    const maxChars = useReact ? MAX_AGENT_MD_CHARS_REACT : MAX_AGENT_MD_CHARS_NATIVE;
    if (agentMdPath) {
        if (verbose) logInfo(`Loading project manual: ${agentMdPath}`);
        try {
            let content = fs.readFileSync(agentMdPath, 'utf8');
            if (content.length > maxChars) {
                const sections = content.split(/\n## /);
                let truncated = sections[0];
                for (let i = 1; i < sections.length; i++) {
                    const candidate = `${truncated}\n## ${sections[i]}`;
                    if (candidate.length > maxChars) break;
                    truncated = candidate;
                }
                content = `${truncated}\n\nIf you need the rest, read AGENT.md via a file tool call.`;
                if (verbose) logWarn(`AGENT.md truncated to ${content.length} characters`);
            }

            parts.push(`\n## Project Manual\n\n<project_manual>\n${content}\n</project_manual>`);
        } catch (e) {
            if (verbose) logWarn(`Failed to load AGENT.md: ${e.message}`);
        }
    } else if (verbose && !generationTask) {
        logInfo('No AGENT.md found near the project root');
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
            return `${index + 1}. ${capability.capabilityId} | tool=${capability.toolName || capability.route || '(unmapped)'} | route=${capability.route || '(n/a)'} | risk=${capability.riskLevel} | approval=${capability.approvalPolicy} | scope=${scope} | quota=${capability.remainingQuota} | expires_at=${capability.expiresAt} | params=${schema}`;
        }).join('\n')
        : 'This agent currently has no granted capabilities.';

    const runtimeSection = governed.runtimeSpec
        ? `
## LLM Runtime Contract

All model traffic must go through the broker. Do not assume direct provider access or direct API keys.
- Upstream provider label: ${governed.runtimeSpec.provider}
- API format: ${governed.runtimeSpec.apiFormat}
- Default model: ${governed.runtimeSpec.defaultModel}
- Resolved model for this agent: ${governed.runtimeSpec.resolvedModel}
- Model override allowed: ${governed.runtimeSpec.allowModelOverride}
- Tool calling enabled at LLM layer: ${governed.runtimeSpec.supportsToolCalling}
- Streaming enabled: ${governed.runtimeSpec.streamingEnabled}
- Health route: ${governed.runtimeSpec.llmRoutes?.health || governed.brokerRoutes.llmHealth}
- Models route: ${governed.runtimeSpec.llmRoutes?.models || governed.brokerRoutes.llmModels}
- Chat route: ${governed.runtimeSpec.llmRoutes?.chat || governed.brokerRoutes.llmChat}

LLM request bodies:
\`\`\`json
${JSON.stringify({
    runtime_spec: governed.requestBodies.runtimeSpecPlaintext,
    llm_health: governed.requestBodies.llmHealthPlaintext,
    llm_models: governed.requestBodies.llmModelsPlaintext,
    llm_chat: governed.requestBodies.llmChatPlaintext,
}, null, 2)}
\`\`\`
`
        : '';

    return `
## Governed Broker Contract

This agent operates in governed mode.
- You can only request capabilities explicitly granted to this session.
- Every side-effecting action must be sent to the broker as an HTTP POST with a JSON body.
- The broker validates role, session, grants, capability, scope, schema, and policy before dispatch.
- Function permission and scope are separate:
  capability_id controls what operation may be requested;
  scope.routes and scope.paths control where that operation may apply.
- Do not assume shell, filesystem write, or direct network access outside the broker contract.

Broker routes:
- Register: POST ${governed.brokerRoutes.register}
- Submit: POST ${governed.brokerRoutes.submit}
- Heartbeat: POST ${governed.brokerRoutes.heartbeat}
- Close: POST ${governed.brokerRoutes.close}
- List capabilities: POST ${governed.brokerRoutes.capabilitiesList}
- List grants: POST ${governed.brokerRoutes.grantsList}
- Runtime spec: POST ${governed.brokerRoutes.runtimeSpec}
- LLM health: POST ${governed.brokerRoutes.llmHealth}
- LLM models: POST ${governed.brokerRoutes.llmModels}
- LLM chat: POST ${governed.brokerRoutes.llmChat}

Current session:
- principal_id: ${governed.session.principalId}
- task_id: ${governed.session.taskId}
- role_id: ${governed.session.roleId}
- session_id: ${governed.session.sessionId}
- expires_at: ${governed.session.expiresAt}

Granted capabilities:
${capabilityLines}

Broker request bodies:
1. Session register outer envelope
\`\`\`json
${JSON.stringify(governed.requestBodies.registerOuter, null, 2)}
\`\`\`

2. Execution submit outer envelope
\`\`\`json
${JSON.stringify(governed.requestBodies.submitOuter.body, null, 2)}
\`\`\`

3. Execution submit plaintext body
\`\`\`json
${JSON.stringify(governed.requestBodies.submitOuter.plaintext, null, 2)}
\`\`\`

4. Other broker plaintext POST bodies
\`\`\`json
${JSON.stringify({
    heartbeat: governed.requestBodies.heartbeatPlaintext,
    grants_list: governed.requestBodies.grantsListPlaintext,
    capabilities_list: governed.requestBodies.capabilitiesListPlaintext,
    close: governed.requestBodies.closePlaintext,
}, null, 2)}
\`\`\`
${runtimeSection}
Behavioral constraints:
- Never invent new capability IDs, routes, paths, or schemas.
- If the required action is outside the granted capabilities or scope, say so explicitly.
- If there is no grant for an operation, do not imply it can be requested.
- Use the broker contract exactly as provided above.`;
}

/**
 * 生成類任務：任務類型是 system_scaffold，或有 generate 授予且其 scope 帶輸出位置（output_slot）。
 * 只有 catalog、validate 這類低風險授予不算：一般代理可能因為預設能力或管理員選取而拿到它們，
 * 那時仍要用一般的基礎提示與專案手冊。
 */
function isGenerationTask(governed) {
    if (!governed) {
        return false;
    }
    if (governed.runtimeSpec?.taskType === 'system_scaffold') {
        return true;
    }
    return (governed.allowedCapabilities || []).some((capability) => {
        if (capability?.capabilityId !== GENERATION_SCAFFOLD_CAPABILITY_ID) {
            return false;
        }
        const slot = capability.scopeOverride?.output_slot;
        return typeof slot === 'string' && slot.trim().length > 0;
    });
}

function formatQuota(value) {
    const quota = Number(value);
    if (!Number.isFinite(quota)) {
        return 'unknown';
    }
    return quota < 0 ? 'unlimited' : String(quota);
}

/**
 * 生成類任務的工作流程與上限。只放工具名稱、步驟與授予上的數字（剩餘配額、頁數上限、迭代上限）：
 * 不放密鑰、主機路徑或 scope 中的輸出位置。
 */
function buildGenerationSection(governed, maxIterations) {
    const byCapability = new Map((governed.allowedCapabilities || []).map((capability) => [capability.capabilityId, capability]));
    const catalog = byCapability.get('generation.catalog.query');
    const validate = byCapability.get('generation.definition.validate');
    const generate = byCapability.get('generation.scaffold.generate');
    const maxPages = Number(generate?.scopeOverride?.max_pages);

    const limits = [
        `- Pages per prototype: at most ${Number.isInteger(maxPages) && maxPages > 0 ? maxPages : 12}`,
        `- query_component_catalog calls left: ${catalog ? formatQuota(catalog.remainingQuota) : 'not granted'}`,
        `- validate_definition calls left: ${validate ? formatQuota(validate.remainingQuota) : 'not granted'}`,
        `- generate_scaffold calls left: ${generate ? formatQuota(generate.remainingQuota) : 'not granted'}`,
    ];
    if (Number.isInteger(maxIterations) && maxIterations > 0) {
        limits.push(`- Model turns for the whole task: at most ${maxIterations}`);
    }

    return `
## Governed Generation Workflow

This task generates a front-end prototype from a DefinitionTemplate. The broker-governed generation tools do the
generation; you only write the definition. The task runs unattended and ends at your first reply that has no tool
call, so:
- Call a tool in every turn until the final summary in step 5. A reply without a tool call ends the task, and
  nothing is generated if generate_scaffold has not succeeded yet.
- Never write the definition in your reply text. Put it directly into the template argument of
  validate_definition and generate_scaffold.
- You cannot ask the user anything and nobody will answer. When the work item is unclear or incomplete, make
  reasonable assumptions that fit it, and state them in the final summary.
Work in this order:
1. query_component_catalog: read section "overview" first, then "field_types" and "example". Ask for
   section "component" with a name only when you need the details of one component.
2. Write one DefinitionTemplate (a single JSON object) and submit it directly with validate_definition in the same
   turn. Follow the catalog rules exactly and use only the page types and field types the catalog lists. Never put
   code, HTML, scripts, styles, absolute URLs or file system paths in it (an api value is only the base path form
   the catalog describes).
3. validate_definition: when it returns ok: false, fix every reported error (each has code, path, message and hint)
   and call validate_definition again with the corrected template. Do not generate until validation returns ok: true.
4. generate_scaffold with the same template. Do not pass any output location: the broker decides where the
   package is written and delivers it to the user.
5. Reply with a short summary without a tool call: the generated pages, the zip path and sha256 from the generate
   result, and any assumptions you made. Then stop.

Limits for this task:
${limits.join('\n')}
If validation still fails when the validate_definition calls run out, stop and report the remaining errors
instead of guessing. A validate result with truncated: true lists only the first errors (total_errors gives the
count); errors that repeat across fields are reported once with the number of places they occur, and paths lists the
first few places. Warnings do not block generation, but fix the ones that say pages of one resource do not line up:
every page of a resource needs the same api base path, field names and options.
Do not retry generate_scaffold with an unchanged template after it failed, with one exception: when a generation
tool fails with "No available worker", the generation service was busy and the request did not run. Wait briefly,
then call the same tool again with the same arguments.`;
}

function findAgentMd(startDir) {
    let dir = startDir;
    const root = path.parse(dir).root;

    for (let i = 0; i < 4; i++) {
        const candidate = path.join(dir, 'AGENT.md');
        try {
            fs.accessSync(candidate);
            return candidate;
        } catch (_) {
            // keep walking upward
        }
        const parent = path.dirname(dir);
        if (parent === dir || parent === root) break;
        dir = parent;
    }
    return null;
}

/**
 * AGENT.md near the project root wins. Only when none is found does the agent fall
 * back to AGENT_MANUAL_PATH, which the container image sets because no repository
 * is mounted into the agent. There is deliberately no fallback to this package's own
 * location: in local mode an unrelated project must not receive Bricks4Agent's manual.
 */
function resolveAgentManualPath(projectRoot, env = process.env) {
    const nearby = findAgentMd(projectRoot);
    if (nearby) return nearby;

    const configured = typeof env.AGENT_MANUAL_PATH === 'string' ? env.AGENT_MANUAL_PATH.trim() : '';
    if (!configured || !path.isAbsolute(configured)) return null;
    try {
        fs.accessSync(configured, fs.constants.R_OK);
        return configured;
    } catch (_) {
        return null;
    }
}

module.exports = {
    buildSystemPrompt,
    isGenerationTask,
    resolveAgentManualPath,
    GENERATION_BASE_PROMPT,
    GENERATION_CONTINUE_REMINDER,
};
