#!/usr/bin/env node
// 產生 tools/agent/container/compose*.yml 需要的開發用金鑰組（零依賴，只用 node:crypto）。
//
// compose 檔不再附預設金鑰：broker 三把金鑰、broker 公鑰與 worker 憑證都以 ${VAR:?...} 要求呼叫端提供。
//
// 用法：
//   node tools/agent/container/gen-stack-secrets.mjs              寫到預設位置（repo 以外）
//   node tools/agent/container/gen-stack-secrets.mjs --out <path> 寫到指定位置（必須在 repo 以外）
//   node tools/agent/container/gen-stack-secrets.mjs --force      覆寫既有檔案（等同輪替金鑰）
//   node tools/agent/container/gen-stack-secrets.mjs --print-path 只印出預設位置
//   node tools/agent/container/gen-stack-secrets.mjs --self-test  只在記憶體中產生並檢查，不寫任何檔案
//
// 預設位置：$BRICKS4AGENT_SECRETS_DIR/agent-stack.env；未設定時為 <使用者目錄>/.bricks4agent/agent-stack.env。
// 之後以 `podman compose --env-file <path> -f tools/agent/container/compose.yml ...` 使用（up 與 down 都要帶）。
//
// 為什麼不能寫進 repo：file-worker 以唯讀方式把整個 repo 提供給受控 agent 經 broker 讀取
// （拒絕清單只擋固定檔名），compose 也會自動讀取 compose 檔旁的 .env；
// 放在 repo 內等於冒著把 broker 私鑰交給受控 agent 的風險。
//
// 測試腳本請 import generateStackSecrets()，把結果放進子行程的 env，不要寫檔。

import { createPrivateKey, createPublicKey, generateKeyPairSync, randomBytes } from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const SCRIPT_PATH = fileURLToPath(import.meta.url);

export const REPO_ROOT = path.resolve(path.dirname(SCRIPT_PATH), '..', '..', '..');
export const DEFAULT_FILE_NAME = 'agent-stack.env';
export const SECRETS_DIR_ENV = 'BRICKS4AGENT_SECRETS_DIR';

/** compose 檔以 ${VAR:?...} 要求的變數，順序即 env 檔的輸出順序。 */
export const STACK_SECRET_VARIABLES = Object.freeze([
    'BROKER_SCOPED_TOKEN_SECRET',
    'BROKER_MASTER_KEY_BASE64',
    'BROKER_ECDH_PRIVATE_KEY_BASE64',
    'BROKER_ECDH_PUBLIC_KEY_BASE64',
    'LINE_WORKER_AUTH_KEY_ID',
    'LINE_WORKER_AUTH_SHARED_SECRET',
    'FILE_WORKER_AUTH_KEY_ID',
    'FILE_WORKER_AUTH_SHARED_SECRET',
    'EXEC_ADAPTER_AUTH_KEY_ID',
    'EXEC_ADAPTER_AUTH_SHARED_SECRET',
]);

/** 三個 compose 檔都需要的 broker 變數（另兩個 compose 檔沒有 worker）。 */
export const BROKER_SECRET_VARIABLES = Object.freeze(STACK_SECRET_VARIABLES.slice(0, 4));

/**
 * 產生一組新的開發金鑰（只在記憶體中）。
 * - ScopedToken secret：48 bytes 亂數的 base64（broker 要求 ≥ 32 UTF-8 bytes）
 * - MasterKey：32 bytes 亂數的 base64（AES-256）
 * - ECDH：P-256 金鑰對；私鑰 PKCS#8 DER base64、公鑰 SPKI DER base64（agent 會釘選這把公鑰）
 * - worker 共享密鑰：32 bytes 亂數的 base64；KeyId 帶亂數後綴
 */
export function generateStackSecrets() {
    const { privateKey, publicKey } = generateKeyPairSync('ec', {
        namedCurve: 'P-256',
        privateKeyEncoding: { type: 'pkcs8', format: 'der' },
        publicKeyEncoding: { type: 'spki', format: 'der' },
    });
    const keySuffix = randomBytes(4).toString('hex');

    return {
        BROKER_SCOPED_TOKEN_SECRET: randomBytes(48).toString('base64'),
        BROKER_MASTER_KEY_BASE64: randomBytes(32).toString('base64'),
        BROKER_ECDH_PRIVATE_KEY_BASE64: privateKey.toString('base64'),
        BROKER_ECDH_PUBLIC_KEY_BASE64: publicKey.toString('base64'),
        LINE_WORKER_AUTH_KEY_ID: `line-worker-${keySuffix}`,
        LINE_WORKER_AUTH_SHARED_SECRET: randomBytes(32).toString('base64'),
        FILE_WORKER_AUTH_KEY_ID: `file-worker-${keySuffix}`,
        FILE_WORKER_AUTH_SHARED_SECRET: randomBytes(32).toString('base64'),
        EXEC_ADAPTER_AUTH_KEY_ID: `exec-adapter-${keySuffix}`,
        EXEC_ADAPTER_AUTH_SHARED_SECRET: randomBytes(32).toString('base64'),
    };
}

/** 檢查一組金鑰符合 broker 的格式要求；有問題時丟出 Error（訊息不含金鑰內容）。 */
export function verifyStackSecrets(secrets) {
    const problems = [];
    for (const name of STACK_SECRET_VARIABLES) {
        if (typeof secrets?.[name] !== 'string' || secrets[name].trim() === '') {
            problems.push(`${name} is missing`);
        }
    }
    if (problems.length === 0) {
        if (Buffer.byteLength(secrets.BROKER_SCOPED_TOKEN_SECRET, 'utf8') < 32) {
            problems.push('BROKER_SCOPED_TOKEN_SECRET must be at least 32 UTF-8 bytes');
        }
        if (Buffer.from(secrets.BROKER_MASTER_KEY_BASE64, 'base64').length !== 32) {
            problems.push('BROKER_MASTER_KEY_BASE64 must decode to 32 bytes');
        }
        try {
            const privateKey = createPrivateKey({
                key: Buffer.from(secrets.BROKER_ECDH_PRIVATE_KEY_BASE64, 'base64'),
                format: 'der',
                type: 'pkcs8',
            });
            if (privateKey.asymmetricKeyType !== 'ec' || privateKey.asymmetricKeyDetails?.namedCurve !== 'prime256v1') {
                problems.push('BROKER_ECDH_PRIVATE_KEY_BASE64 must be a P-256 key');
            }
            const derivedPublicKey = createPublicKey(privateKey).export({ type: 'spki', format: 'der' }).toString('base64');
            if (derivedPublicKey !== secrets.BROKER_ECDH_PUBLIC_KEY_BASE64) {
                problems.push('BROKER_ECDH_PUBLIC_KEY_BASE64 does not match BROKER_ECDH_PRIVATE_KEY_BASE64');
            }
        } catch {
            problems.push('BROKER_ECDH_PRIVATE_KEY_BASE64 must be a base64 PKCS#8 private key');
        }
        for (const name of STACK_SECRET_VARIABLES) {
            if (/[\s#"'$`\\]/.test(secrets[name])) {
                problems.push(`${name} contains characters that are unsafe in an env file`);
            }
        }
    }
    if (problems.length > 0) {
        throw new Error(`Invalid stack secrets: ${problems.join('; ')}`);
    }
    return true;
}

/** 轉成 compose --env-file 可讀的內容（KEY=value，值只含 base64／英數，不需引號）。 */
export function formatEnvFile(secrets, now = new Date()) {
    const lines = [
        '# Bricks4Agent agent stack secrets (generated by tools/agent/container/gen-stack-secrets.mjs).',
        `# Generated at ${now.toISOString()}. Keep this file outside the repository.`,
        '# Use: podman compose --env-file <this file> -f tools/agent/container/compose.yml up ...',
    ];
    for (const name of STACK_SECRET_VARIABLES) {
        lines.push(`${name}=${secrets[name]}`);
    }
    return `${lines.join('\n')}\n`;
}

/** 預設輸出位置：$BRICKS4AGENT_SECRETS_DIR/agent-stack.env，否則 <使用者目錄>/.bricks4agent/agent-stack.env。 */
export function defaultSecretsPath(env = process.env, homeDirectory = os.homedir()) {
    const configured = typeof env?.[SECRETS_DIR_ENV] === 'string' ? env[SECRETS_DIR_ENV].trim() : '';
    const directory = configured || path.join(homeDirectory, '.bricks4agent');
    return path.resolve(directory, DEFAULT_FILE_NAME);
}

// 解析到「最近一個存在的上層目錄」的實際路徑，避免以符號連結或大小寫差異繞過 repo 檢查。
function canonicalPath(target) {
    let current = path.resolve(target);
    const pending = [];
    while (!fs.existsSync(current)) {
        const parent = path.dirname(current);
        if (parent === current) {
            break;
        }
        pending.unshift(path.basename(current));
        current = parent;
    }
    let resolved = current;
    try {
        resolved = fs.realpathSync.native(current);
    } catch {
        // 無法解析時沿用原路徑
    }
    const full = path.join(resolved, ...pending);
    return process.platform === 'win32' ? full.toLowerCase() : full;
}

/** target 是否位於 directory 之內（含 directory 本身）。 */
export function isInsideDirectory(target, directory) {
    const relative = path.relative(canonicalPath(directory), canonicalPath(target));
    if (relative === '') {
        return true;
    }
    const escapes = relative === '..' || relative.startsWith(`..${path.sep}`);
    return !escapes && !path.isAbsolute(relative);
}

/** 目標在 repo 內時丟出 Error。 */
export function assertOutsideRepo(target, repoRoot = REPO_ROOT) {
    if (isInsideDirectory(target, repoRoot)) {
        throw new Error(
            `Refusing to write stack secrets inside the repository (${path.resolve(target)}). ` +
            'The file worker serves the repository to the agent; choose a path outside it ' +
            `(for example set ${SECRETS_DIR_ENV}).`
        );
    }
}

/** 寫出 env 檔（只在 repo 以外）。既有檔案需 force 才覆寫。 */
export function writeStackSecretsFile(target, secrets, { force = false, repoRoot = REPO_ROOT } = {}) {
    const resolved = path.resolve(target);
    assertOutsideRepo(resolved, repoRoot);
    verifyStackSecrets(secrets);
    if (fs.existsSync(resolved) && !force) {
        throw new Error(
            `${resolved} already exists. Re-run with --force to rotate the keys ` +
            '(a stack whose volume was created with the old keys must be recreated with `down -v`).'
        );
    }
    fs.mkdirSync(path.dirname(resolved), { recursive: true, mode: 0o700 });
    fs.writeFileSync(resolved, formatEnvFile(secrets), { encoding: 'utf8', mode: 0o600, flag: force ? 'w' : 'wx' });
    try {
        fs.chmodSync(resolved, 0o600);
    } catch {
        // Windows 不支援 POSIX 權限位元
    }
    return resolved;
}

function selfTest() {
    const first = generateStackSecrets();
    const second = generateStackSecrets();
    verifyStackSecrets(first);
    verifyStackSecrets(second);
    for (const name of STACK_SECRET_VARIABLES) {
        if (!name.endsWith('_KEY_ID') && first[name] === second[name]) {
            throw new Error(`${name} is not random`);
        }
    }

    const envText = formatEnvFile(first);
    for (const name of STACK_SECRET_VARIABLES) {
        if (!envText.includes(`\n${name}=`)) {
            throw new Error(`env file is missing ${name}`);
        }
    }

    const insideRepo = [
        path.join(REPO_ROOT, 'tools', 'agent', 'container', '.env'),
        path.join(REPO_ROOT, 'tools', 'agent', 'container', DEFAULT_FILE_NAME),
        path.join(REPO_ROOT, DEFAULT_FILE_NAME),
        REPO_ROOT,
    ];
    for (const candidate of insideRepo) {
        let refused = false;
        try {
            assertOutsideRepo(candidate);
        } catch {
            refused = true;
        }
        if (!refused) {
            throw new Error('a path inside the repository was not refused');
        }
    }
    if (isInsideDirectory(path.join(path.dirname(REPO_ROOT), 'outside-repo', DEFAULT_FILE_NAME), REPO_ROOT)) {
        throw new Error('a sibling path of the repository was treated as inside it');
    }

    const repoSecretsDir = defaultSecretsPath({ [SECRETS_DIR_ENV]: path.join(REPO_ROOT, 'secrets') });
    if (!isInsideDirectory(repoSecretsDir, REPO_ROOT)) {
        throw new Error(`${SECRETS_DIR_ENV} pointing into the repository was not detected`);
    }
    return true;
}

function parseArgs(argv) {
    const options = { out: null, force: false, printPath: false, selfTest: false, help: false };
    for (let index = 0; index < argv.length; index += 1) {
        const arg = argv[index];
        if (arg === '--force') {
            options.force = true;
        } else if (arg === '--print-path') {
            options.printPath = true;
        } else if (arg === '--self-test') {
            options.selfTest = true;
        } else if (arg === '--help' || arg === '-h') {
            options.help = true;
        } else if (arg === '--out') {
            options.out = argv[index + 1] ?? null;
            index += 1;
            if (!options.out) {
                throw new Error('--out requires a path');
            }
        } else if (arg.startsWith('--out=')) {
            options.out = arg.slice('--out='.length);
        } else {
            throw new Error(`Unknown argument: ${arg}`);
        }
    }
    return options;
}

function printHelp() {
    console.log([
        'Usage: node tools/agent/container/gen-stack-secrets.mjs [--out <path>] [--force] [--print-path] [--self-test]',
        '',
        `Writes broker keys and worker credentials for the agent compose stacks to ${DEFAULT_FILE_NAME}`,
        `outside the repository (default: $${SECRETS_DIR_ENV}/${DEFAULT_FILE_NAME} or ~/.bricks4agent/${DEFAULT_FILE_NAME}).`,
        'Secret values are never printed.',
    ].join('\n'));
}

export function main(argv = process.argv.slice(2)) {
    const options = parseArgs(argv);
    if (options.help) {
        printHelp();
        return 0;
    }
    if (options.selfTest) {
        selfTest();
        console.log('gen-stack-secrets self-test passed (nothing was written).');
        return 0;
    }

    const target = path.resolve(options.out || defaultSecretsPath());
    if (options.printPath) {
        assertOutsideRepo(target);
        console.log(target);
        return 0;
    }

    const written = writeStackSecretsFile(target, generateStackSecrets(), { force: options.force });
    const relativeCompose = 'tools/agent/container/compose.yml';
    console.log(`Wrote ${STACK_SECRET_VARIABLES.length} variables to ${written} (values are not printed).`);
    console.log('Use the same file for up and down, for example:');
    console.log(`  podman compose --env-file "${written}" -f ${relativeCompose} up --abort-on-container-exit --exit-code-from agent`);
    console.log(`  podman compose --env-file "${written}" -f ${relativeCompose} down -v`);
    return 0;
}

function isEntryPoint() {
    if (!process.argv[1]) {
        return false;
    }
    try {
        return canonicalPath(process.argv[1]) === canonicalPath(SCRIPT_PATH);
    } catch {
        return false;
    }
}

if (isEntryPoint()) {
    try {
        process.exitCode = main();
    } catch (error) {
        console.error(error instanceof Error ? error.message : String(error));
        process.exitCode = 1;
    }
}
