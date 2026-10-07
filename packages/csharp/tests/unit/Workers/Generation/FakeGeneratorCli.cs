using GenerationWorker.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Workers.Generation;

/// <summary>
/// 測試用的生成器 CLI：依 tools/generation/cli.mjs 的契約（stdin 一個 JSON、stdout 一個單行 JSON、
/// 正常處理一律結束碼 0）回固定內容，並在 build 時寫出幾個檔案。它不是正式的生成器：
/// 每次測試把它寫進暫存的 ToolsRoot，repo 中沒有這支假的 cli.mjs。
/// 行為由 template.fake（validate／build）或 name（catalog）選擇，用來重現逾時、輸出過大、結束碼非 0 等情況。
/// </summary>
internal sealed class FakeGeneratorCli : IDisposable
{
    public const string Script = """
        import fs from 'node:fs';
        import path from 'node:path';
        import crypto from 'node:crypto';
        import { fileURLToPath } from 'node:url';

        const toolsRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
        const command = process.argv[2];
        const chunks = [];
        for await (const chunk of process.stdin) chunks.push(chunk);
        const input = JSON.parse(Buffer.concat(chunks).toString('utf8'));
        fs.appendFileSync(path.join(toolsRoot, 'invocations.log'), `${command}\n`);
        fs.writeFileSync(path.join(toolsRoot, `last-${command}.json`), JSON.stringify(input));

        const out = (value) => process.stdout.write(`${JSON.stringify(value)}\n`);
        const sha = (data) => crypto.createHash('sha256').update(data).digest('hex');
        const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
        const flood = async () => {
            const block = 'x'.repeat(64 * 1024);
            for (let i = 0; i < 256; i += 1) {
                if (!process.stdout.write(block)) await new Promise((resolve) => process.stdout.once('drain', resolve));
            }
        };

        const mode = command === 'catalog'
            ? String(input.name ?? '')
            : String(input.template?.fake ?? '');

        if (mode === 'sleep') { await sleep(60000); }
        if (mode === 'flood') { await flood(); process.exit(0); }
        if (mode === 'crash') { process.stderr.write(`failure at ${toolsRoot}\n`); process.exit(3); }
        if (mode === 'badjson') { process.stdout.write('not json\n'); process.exit(0); }

        const pagesFor = (template) => {
            const match = /^pages:(\d+)$/.exec(mode);
            if (match) {
                return Array.from({ length: Number(match[1]) }, (_, i) => ({ id: `Item${i}Page`, type: 'form', field_count: 2 }));
            }
            return (template.pages ?? []).map((page) => ({ id: page.id, type: page.type, field_count: page.fields ?? 0 }));
        };

        if (command === 'catalog') {
            if (mode === 'Missing') {
                out({ ok: false, errors: [{ code: 'COMPONENT_NOT_FOUND', path: 'name', message: 'No such component.', hint: 'Use the overview section.' }] });
            } else if (mode === 'leak') {
                out({ ok: true, section: 'component', content: `see ${toolsRoot}/tools`, catalog_sha256: 'a'.repeat(64), matrix_sha256: 'b'.repeat(64), summary_version: '1' });
            } else if (mode === 'heap-check') {
                out({ ok: true, section: 'overview', content: process.execArgv.join(' '), catalog_sha256: 'a'.repeat(64), matrix_sha256: 'b'.repeat(64), summary_version: '1' });
            } else if (mode === 'env-check') {
                const names = Object.keys(process.env).filter((key) => /^WORKER_|SECRET|NODE_OPTIONS/i.test(key));
                out({ ok: true, section: 'overview', content: names.join(','), catalog_sha256: 'a'.repeat(64), matrix_sha256: 'b'.repeat(64), summary_version: '1' });
            } else {
                out({ ok: true, section: input.section ?? 'overview', content: 'catalog content', catalog_sha256: 'a'.repeat(64), matrix_sha256: 'b'.repeat(64), summary_version: '1' });
            }
            process.exit(0);
        }

        const invalid = mode === 'invalid'
            ? [{ code: 'FIELD_TYPE_UNSUPPORTED', path: 'definitions.pages[0].definition.fields[0].type', message: 'slider is not supported.', hint: 'Use number.' }]
            : [];

        if (command === 'validate') {
            out({ ok: invalid.length === 0, errors: invalid, warnings: [], pages: pagesFor(input.template), validation_digest: 'd'.repeat(64), validator_version: 'fake-1' });
            process.exit(0);
        }

        if (command === 'build') {
            if (invalid.length > 0) {
                out({ ok: false, errors: invalid });
                process.exit(0);
            }
            const outDir = input.out_dir;
            if (typeof outDir !== 'string' || !path.isAbsolute(outDir)) { process.stderr.write('out_dir must be absolute\n'); process.exit(2); }
            if (fs.existsSync(outDir) && fs.readdirSync(outDir).length > 0) { process.stderr.write('out_dir not empty\n'); process.exit(2); }
            const pages = pagesFor(input.template);
            const files = new Map();
            files.set('site/index.html', `<!doctype html><title>${input.title ?? 'Prototype'}</title><script type="module" src="./boot.js"></script>\n`);
            files.set('site/boot.js', 'export const ready = true;\n');
            files.set('site/definition-template.json', JSON.stringify(input.template));
            for (const page of pages) files.set(`site/definitions/${page.id}.json`, JSON.stringify(page));
            files.set('report/validation.json', JSON.stringify({ ok: true }));
            if (mode === 'extra') files.set('extra.txt', 'unexpected');
            if (mode === 'unlisted') files.set('site/unlisted.js', 'export {};\n');
            const listed = [...files.keys()].filter((name) => name.startsWith('site/') && name !== 'site/unlisted.js' || name === 'report/validation.json')
                .sort()
                .map((name) => ({ path: name, sha256: sha(files.get(name)), size: Buffer.byteLength(files.get(name)) }));
            const manifest = mode === 'report-leak'
                ? { files: listed, out_dir: outDir }
                : { files: listed, generator_version: 'fake-1' };
            files.set('report/manifest.json', JSON.stringify(manifest));
            for (const [name, content] of files) {
                const target = path.join(outDir, ...name.split('/'));
                fs.mkdirSync(path.dirname(target), { recursive: true });
                fs.writeFileSync(target, content);
            }
            out({ ok: true, pages, files: listed, generator_version: 'fake-1', catalog_sha256: 'a'.repeat(64), validation_digest: 'd'.repeat(64) });
            process.exit(0);
        }

        process.stderr.write(`unknown command ${command}\n`);
        process.exit(2);
        """;

    public FakeGeneratorCli(Action<GenerationWorkerOptions>? configure = null)
    {
        Root = Path.Combine(Path.GetTempPath(), $"b4a-genworker-{Guid.NewGuid():N}");
        ToolsRoot = Path.Combine(Root, "tools-root");
        OutputRoot = Path.Combine(Root, "out");
        var cliPath = Path.Combine(ToolsRoot, "tools", "generation", "cli.mjs");
        Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
        File.WriteAllText(cliPath, Script);

        Options = new GenerationWorkerOptions
        {
            NodePath = GenerationWorkerOptions.ResolveNodePath(null),
            ToolsRoot = ToolsRoot,
            OutputRoot = OutputRoot,
            QueryTimeout = TimeSpan.FromSeconds(30),
            BuildTimeout = TimeSpan.FromSeconds(60),
        };
        configure?.Invoke(Options);
        Cli = new NodeGeneratorCli(Options, NullLogger.Instance);
    }

    public string Root { get; }
    public string ToolsRoot { get; }
    public string OutputRoot { get; }
    public GenerationWorkerOptions Options { get; }
    public NodeGeneratorCli Cli { get; }

    /// <summary>CLI 被呼叫的次數（依指令）。</summary>
    public int Invocations(string command)
    {
        var log = Path.Combine(ToolsRoot, "invocations.log");
        return File.Exists(log)
            ? File.ReadAllLines(log).Count(line => line == command)
            : 0;
    }

    /// <summary>CLI 最後一次收到的 stdin JSON。</summary>
    public string LastInput(string command) => File.ReadAllText(Path.Combine(ToolsRoot, $"last-{command}.json"));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
