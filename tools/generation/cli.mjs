#!/usr/bin/env node
// 受治理生成的確定性生成器 CLI（generation-worker 以子程序呼叫）。
//
//   node tools/generation/cli.mjs <catalog|validate|build>
//
// 從 stdin 讀一個 JSON，向 stdout 寫一行 JSON；stderr 只放診斷。
//   catalog  { section?: overview|field_types|example|component, name? }
//            → { ok, section, content, catalog_sha256, matrix_sha256, summary_version }
//   validate { template, page_ids? }
//            → { ok, errors:[{code,path,message,hint}], warnings, pages:[{id,type,field_count}], validation_digest, validator_version }
//   build    { template, page_ids?, title?, out_dir }
//            → 先跑與 validate 相同的驗證；不通過時 { ok:false, errors, ... } 且不寫任何檔案；
//              通過時寫出 out_dir/site/**、out_dir/report/* 並回
//              { ok:true, pages, files:[{path,sha256,size}], generator_version, catalog_sha256, validation_digest, ... }
//
// 結束碼：正常處理（含驗證不通過）一律 0；用法錯誤 64、輸入不是 JSON 或超過上限 65、內部例外 70。
// 輸出位置只取自輸入的 out_dir（絕對路徑），不讀環境變數，也不寫 out_dir 以外的地方。
import process from 'node:process';
import { loadCatalogSummary, queryCatalog } from './catalog-summary.mjs';
import { validateRequest } from './validate-definition.mjs';
import { buildSite } from './build-site.mjs';

const EXIT_OK = 0;
const EXIT_USAGE = 64;
const EXIT_INPUT = 65;
const EXIT_INTERNAL = 70;
const MAX_STDIN_BYTES = 1024 * 1024;

const COMMANDS = ['catalog', 'validate', 'build'];

function writeResult(result) {
    process.stdout.write(`${JSON.stringify(result)}\n`);
}

function failure(code, message, hint = '') {
    return { ok: false, errors: [{ code, path: '', message, hint }] };
}

function readStdin() {
    return new Promise((resolve, reject) => {
        const chunks = [];
        let size = 0;
        let overflow = false;
        process.stdin.on('data', (chunk) => {
            size += chunk.length;
            if (size > MAX_STDIN_BYTES) {
                overflow = true;
                return; // 繼續讀完以免寫入端阻塞，但不再保留內容
            }
            chunks.push(chunk);
        });
        process.stdin.on('end', () => resolve({ text: Buffer.concat(chunks).toString('utf8'), overflow }));
        process.stdin.on('error', reject);
    });
}

async function main(argv) {
    const command = argv[0];
    if (argv.length !== 1 || !COMMANDS.includes(command)) {
        process.stderr.write(`usage: node tools/generation/cli.mjs <${COMMANDS.join('|')}>  (JSON on stdin)\n`);
        writeResult(failure('USAGE', `Expected exactly one command: ${COMMANDS.join(', ')}.`));
        return EXIT_USAGE;
    }

    const { text, overflow } = await readStdin();
    if (overflow) {
        writeResult(failure('INPUT_TOO_LARGE', `stdin is limited to ${MAX_STDIN_BYTES} bytes.`));
        return EXIT_INPUT;
    }
    let request;
    try {
        request = JSON.parse(text);
    } catch {
        writeResult(failure('INPUT_NOT_JSON', 'stdin is not valid JSON.'));
        return EXIT_INPUT;
    }

    if (command === 'catalog') {
        const { summary, source } = loadCatalogSummary();
        process.stderr.write(`catalog summary source: ${source}\n`);
        writeResult(queryCatalog(request, summary));
        return EXIT_OK;
    }
    if (command === 'validate') {
        writeResult(await validateRequest(request, 'validate'));
        return EXIT_OK;
    }
    const started = Date.now();
    const result = await buildSite(request);
    process.stderr.write(`build ${result.ok ? 'completed' : 'rejected'} in ${Date.now() - started} ms\n`);
    writeResult(result);
    return EXIT_OK;
}

main(process.argv.slice(2)).then(
    (code) => { process.exitCode = code; },
    (error) => {
        process.stderr.write(`internal error: ${error?.stack || error}\n`);
        writeResult(failure('INTERNAL_ERROR', 'The generator failed unexpectedly.'));
        process.exitCode = EXIT_INTERNAL;
    }
);
