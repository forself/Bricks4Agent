// Phase 2 LLM 接口：spawn `claude --print` subprocess、輸入 prompt、拿 text 回應。
//
// 用 Max 訂閱跑、不另外付 token 費。每次 call ~2-3s cold start、phase 2 還沒 tool calling、
// 純對話用，可接受。
//
// 多輪：caller 維護 history，本模組只是 stateless 的 prompt → response。
//
// claude --print 的 contract：
//   stdin: prompt（被當成 user 訊息）
//   stdout: 純文字回應（--output-format text）
//   exit code: 0 OK / 非零 = 失敗（stderr 有錯誤訊息）
//   --append-system-prompt: 加 system 指令
//
// 失敗模式：
//   - 訂閱 throttle: stderr 印 "Usage limit reached"、exit non-zero
//   - 沒 .claude/credentials: exit code 非零、stderr 印 "Not logged in"
//   - timeout: 我們自己設 60s wall clock，超過就 kill

import { spawn } from 'node:child_process';

const CLAUDE_TIMEOUT_MS = 60_000;

const SYSTEM_PROMPT = `你是 B4A trading platform 的 Discord 助理 bot。
- 用使用者訊息的語言回應（中文 / English）
- 簡潔明確、不要冗長開場白
- 目前是 phase 2、你還沒有 tool 可以呼叫；如果使用者要查行情、部位、下單，請告訴他「目前還在開發、phase 3 之後會接上 broker capability」
- 你的 governance：所有真實操作都會走 broker 治理層（ACL / Approval / Risk rules / Audit），即使之後接通 tool、也不能繞過`;

/**
 * 呼叫 claude --print headless。
 * @param {string} userMessage 當輪 user 訊息
 * @param {Array<{role:'user'|'assistant', content:string}>} history 之前的 turn（不含當輪）
 * @returns {Promise<{ok:boolean, text?:string, error?:string}>}
 */
export async function callClaude(userMessage, history = []) {
  // 把 history + 當輪訊息合成一個 prompt 餵 stdin
  // 簡單格式：用「---」分隔 turn、claude 實測能讀懂
  const pastTurns = history
    .map(t => `[${t.role === 'user' ? '使用者' : '你'}]: ${t.content}`)
    .join('\n---\n');
  const fullInput = pastTurns
    ? `${pastTurns}\n---\n[使用者]: ${userMessage}`
    : userMessage;

  return new Promise((resolve) => {
    let stdout = '';
    let stderr = '';
    let timedOut = false;

    const proc = spawn('claude', [
      '--print',
      '--output-format', 'text',
      '--append-system-prompt', SYSTEM_PROMPT,
    ], { stdio: ['pipe', 'pipe', 'pipe'] });

    const timer = setTimeout(() => {
      timedOut = true;
      try { proc.kill('SIGKILL'); } catch {}
    }, CLAUDE_TIMEOUT_MS);

    proc.stdout.on('data', (d) => { stdout += d.toString(); });
    proc.stderr.on('data', (d) => { stderr += d.toString(); });

    proc.on('error', (err) => {
      clearTimeout(timer);
      resolve({ ok: false, error: `spawn error: ${err.message}` });
    });

    proc.on('close', (code) => {
      clearTimeout(timer);
      if (timedOut) {
        resolve({ ok: false, error: `claude --print timeout (${CLAUDE_TIMEOUT_MS}ms)` });
        return;
      }
      if (code !== 0) {
        // 把 stderr 截短回去、避免 leak credentials path
        const errSnippet = stderr.slice(0, 500).trim();
        resolve({ ok: false, error: `claude exit ${code}: ${errSnippet}` });
        return;
      }
      const text = stdout.trim();
      if (!text) {
        resolve({ ok: false, error: 'claude returned empty response' });
        return;
      }
      resolve({ ok: true, text });
    });

    // 餵 prompt 進 stdin、close
    try {
      proc.stdin.write(fullInput);
      proc.stdin.end();
    } catch (e) {
      clearTimeout(timer);
      resolve({ ok: false, error: `stdin write: ${e.message}` });
    }
  });
}
