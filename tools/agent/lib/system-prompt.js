'use strict';

const fs = require('fs');
const path = require('path');
const { getToolDescriptions } = require('./tool-registry');
const { logInfo, logWarn } = require('./utils');

const MAX_AGENT_MD_CHARS_NATIVE = 8000;
const MAX_AGENT_MD_CHARS_REACT = 4000; // ReAct 模式需為工具說明留空間

// ─── 基礎 Agent 身份 ───

const BASE_PROMPT = `你是一個由本機 Ollama 模型驅動的 AI 程式助手。你能閱讀、理解和修改使用者專案中的程式碼。

你有以下工具可以使用：讀取檔案、寫入檔案、列出目錄、搜尋檔案、搜尋內容、執行 shell 指令。

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
 * @returns {string} 系統提示詞
 */
function buildSystemPrompt(options) {
    const { projectRoot, useReact, verbose } = options;
    const parts = [BASE_PROMPT];

    // Layer 2: 載入 AGENT.md
    const agentMdPath = findAgentMd(projectRoot);
    const maxChars = useReact ? MAX_AGENT_MD_CHARS_REACT : MAX_AGENT_MD_CHARS_NATIVE;
    if (agentMdPath) {
        if (verbose) logInfo(`載入專案手冊: ${agentMdPath}`);
        try {
            let content = fs.readFileSync(agentMdPath, 'utf8');
            if (content.length > maxChars) {
                // 截斷但保留核心章節（1-6）
                const sections = content.split(/\n## /);
                let truncated = sections[0]; // header
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
    } else {
        if (verbose) logInfo('未偵測到 AGENT.md，以通用模式運行');
    }

    // Layer 3: ReAct 工具描述
    if (useReact) {
        parts.push(REACT_INSTRUCTIONS + getToolDescriptions());
    }

    return parts.join('\n');
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
