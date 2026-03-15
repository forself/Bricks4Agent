'use strict';

const fs = require('fs');
const path = require('path');
const { getToolDescriptions } = require('./tool-registry');
const { logInfo, logWarn } = require('./utils');

const MAX_AGENT_MD_CHARS_NATIVE = 8000;
const MAX_AGENT_MD_CHARS_REACT = 4000;

const BASE_PROMPT = `You are an AI coding agent operating inside the user's project.
Follow project instructions exactly, inspect before changing code, and stay within the allowed tool and policy boundaries.
- Be concrete and technical.
- Prefer deterministic edits over speculative changes.
- Do not claim to have performed actions you did not actually perform.
- If access is governed, only use the routes, capabilities, and scopes explicitly granted.`;

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
    } = options;

    const parts = [BASE_PROMPT];

    if (governed) {
        parts.push(buildGovernedSection(governed));
    } else {
        parts.push('\n## Execution Mode\n\nYou may use the locally registered tools directly.');
    }

    const agentMdPath = findAgentMd(projectRoot);
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
    } else if (verbose) {
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

module.exports = { buildSystemPrompt };
