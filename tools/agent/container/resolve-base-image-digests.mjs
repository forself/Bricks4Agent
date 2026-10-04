#!/usr/bin/env node
// 查詢 Containerfile 基底映像目前的多架構 index digest，並改寫 `FROM image:tag@sha256:...`。
//
// 用法：
//   node tools/agent/container/resolve-base-image-digests.mjs            查詢並改寫（預設用 docker）
//   node tools/agent/container/resolve-base-image-digests.mjs --check    只比對，有過期的 digest 時 exit 1，不寫檔
//   node tools/agent/container/resolve-base-image-digests.mjs --engine podman
//   CONTAINER_ENGINE=podman node tools/agent/container/resolve-base-image-digests.mjs
//
// 釘選的必須是 index（manifest list）的 digest，不是單一平台 manifest 的 digest，否則 arm64 主機無法拉取。
// - docker：`docker buildx imagetools inspect <ref> --format '{{json .Manifest}}'`，檢查 mediaType 為 index。
// - podman：先 `podman manifest inspect <ref>` 取得各平台 manifest 的 digest，再 `podman pull` 後讀
//   RepoDigests，取不屬於任何平台 manifest 的那一個（也就是 index 的 digest）。
//
// 純函式（解析、改寫、挑選 digest）可以離線測試：tools/agent/tests/test-container-image-tools.mjs。

import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const SCRIPT_PATH = fileURLToPath(import.meta.url);
export const REPO_ROOT = path.resolve(path.dirname(SCRIPT_PATH), '..', '..', '..');

const EXCLUDED_DIRECTORIES = new Set([
    '.git',
    '.claude',
    '.worktrees',
    '.test-output',
    'bin',
    'node_modules',
    'obj',
    'out',
]);

const INDEX_MEDIA_TYPES = new Set([
    'application/vnd.oci.image.index.v1+json',
    'application/vnd.docker.distribution.manifest.list.v2+json',
]);

const DIGEST_PATTERN = /^sha256:[0-9a-f]{64}$/;

/** Containerfile 的檔名規則：`Containerfile` 或 `*.Containerfile`。 */
export function isContainerfileName(name) {
    return name === 'Containerfile' || name.endsWith('.Containerfile');
}

/** 找出 repo 內所有 Containerfile（回傳以 / 分隔的相對路徑，已排序）。 */
export function discoverContainerfiles(root = REPO_ROOT) {
    const found = [];
    const walk = (directory) => {
        for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
            if (entry.isDirectory()) {
                if (!EXCLUDED_DIRECTORIES.has(entry.name)) {
                    walk(path.join(directory, entry.name));
                }
            } else if (entry.isFile() && isContainerfileName(entry.name)) {
                found.push(path.relative(root, path.join(directory, entry.name)).split(path.sep).join('/'));
            }
        }
    };
    walk(root);
    return found.sort();
}

/**
 * 拆解映像參照 `registry/repo:tag@sha256:...`。
 * tag 只取最後一個 `/` 之後的冒號，避免把 registry 的埠號當成 tag。
 */
export function parseImageReference(reference) {
    let rest = reference;
    let digest = null;
    const at = rest.indexOf('@');
    if (at >= 0) {
        digest = rest.slice(at + 1);
        rest = rest.slice(0, at);
    }
    let tag = null;
    const colon = rest.lastIndexOf(':');
    if (colon > rest.lastIndexOf('/')) {
        tag = rest.slice(colon + 1);
        rest = rest.slice(0, colon);
    }
    return { repository: rest, tag, digest };
}

/** 組回映像參照。 */
export function formatImageReference({ repository, tag, digest }) {
    return `${repository}${tag ? `:${tag}` : ''}${digest ? `@${digest}` : ''}`;
}

/**
 * 解析一行 FROM 指令；不是 FROM 時回傳 null。
 * 回傳 { prefix, reference, suffix, alias, ...parseImageReference(reference) }，prefix+reference+suffix 等於原行。
 */
export function parseFromLine(line) {
    const match = /^(\s*FROM\s+(?:--\S+\s+)*)(\S+)(.*)$/i.exec(line);
    if (!match) {
        return null;
    }
    const [, prefix, reference, suffix] = match;
    const aliasMatch = /^\s+AS\s+(\S+)\s*$/i.exec(suffix);
    return {
        prefix,
        reference,
        suffix,
        alias: aliasMatch ? aliasMatch[1] : null,
        ...parseImageReference(reference),
    };
}

/**
 * 列出一份 Containerfile 中引用外部映像的 FROM（略過引用先前 stage 名稱的 FROM）。
 * 回傳 [{ lineIndex, ...parseFromLine }]。
 */
export function listBaseImages(text) {
    const stageNames = new Set();
    const result = [];
    text.split(/\r?\n/).forEach((line, lineIndex) => {
        const parsed = parseFromLine(line);
        if (!parsed) {
            return;
        }
        const isStageReference = !parsed.tag && !parsed.digest && stageNames.has(parsed.repository.toLowerCase());
        if (!isStageReference) {
            result.push({ lineIndex, ...parsed });
        }
        if (parsed.alias) {
            stageNames.add(parsed.alias.toLowerCase());
        }
    });
    return result;
}

/** 需要查詢的 `repo:tag`（去重、排序）。沒有 tag 的參照無法決定要追哪個版本，直接報錯。 */
export function collectTagReferences(texts) {
    const references = new Set();
    for (const text of texts) {
        for (const base of listBaseImages(text)) {
            if (!base.tag) {
                throw new Error(`FROM ${base.reference} has no tag; pin a tag before resolving its digest.`);
            }
            references.add(`${base.repository}:${base.tag}`);
        }
    }
    return [...references].sort();
}

/**
 * 以 digestByReference（`repo:tag` → `sha256:...`）改寫 FROM 行。
 * 回傳 { text, changes: [{ reference, from, to }] }；保留原本的換行字元。
 */
export function rewriteDigests(text, digestByReference) {
    const newline = text.includes('\r\n') ? '\r\n' : '\n';
    const lines = text.split(/\r?\n/);
    const changes = [];
    for (const base of listBaseImages(text)) {
        const key = `${base.repository}:${base.tag}`;
        const digest = digestByReference[key];
        if (!digest) {
            continue;
        }
        if (!DIGEST_PATTERN.test(digest)) {
            throw new Error(`Refusing to write malformed digest for ${key}.`);
        }
        if (base.digest === digest) {
            continue;
        }
        lines[base.lineIndex] = `${base.prefix}${formatImageReference({ ...base, digest })}${base.suffix}`;
        changes.push({ reference: key, from: base.digest, to: digest });
    }
    return { text: lines.join(newline), changes };
}

/** 解析 `docker buildx imagetools inspect --format '{{json .Manifest}}'` 的輸出，只接受 index。 */
export function parseImagetoolsManifest(jsonText, reference = 'image') {
    let manifest;
    try {
        manifest = JSON.parse(jsonText);
    } catch {
        throw new Error(`Unexpected imagetools output for ${reference}.`);
    }
    const digest = manifest?.digest;
    if (typeof digest !== 'string' || !DIGEST_PATTERN.test(digest)) {
        throw new Error(`No digest in imagetools output for ${reference}.`);
    }
    if (!INDEX_MEDIA_TYPES.has(manifest.mediaType)) {
        throw new Error(`${reference} resolves to ${manifest.mediaType}, not a multi-arch index.`);
    }
    return digest;
}

/** 從 `podman manifest inspect` 的輸出取出各平台 manifest 的 digest；不是 index 時報錯。 */
export function parsePodmanManifestList(jsonText, reference = 'image') {
    let list;
    try {
        list = JSON.parse(jsonText);
    } catch {
        throw new Error(`Unexpected podman manifest output for ${reference}.`);
    }
    if (!Array.isArray(list?.manifests) || list.manifests.length === 0) {
        throw new Error(`${reference} is not a multi-arch index.`);
    }
    return list.manifests.map((entry) => entry.digest).filter((digest) => DIGEST_PATTERN.test(digest || ''));
}

/** 在 RepoDigests 中挑出 index 的 digest（不屬於任何平台 manifest 的那一個）。 */
export function selectIndexDigest(repoDigests, instanceDigests) {
    const instances = new Set(instanceDigests);
    const candidates = [...new Set(
        repoDigests
            .map((entry) => String(entry).split('@')[1])
            .filter((digest) => DIGEST_PATTERN.test(digest || '') && !instances.has(digest))
    )];
    if (candidates.length !== 1) {
        throw new Error(`Expected exactly one index digest, found ${candidates.length}.`);
    }
    return candidates[0];
}

function defaultExec(command, args) {
    const result = spawnSync(command, args, { encoding: 'utf8' });
    if (result.error) {
        throw new Error(`${command} is not available: ${result.error.message}`);
    }
    if (result.status !== 0) {
        throw new Error(`${command} ${args[0]} failed (exit ${result.status}): ${(result.stderr || '').trim()}`);
    }
    return result.stdout;
}

/** 以指定引擎查詢 `repo:tag` 的 index digest。exec 可注入（測試用）。 */
export function queryIndexDigest(engine, reference, exec = defaultExec) {
    if (engine === 'docker') {
        const output = exec('docker', ['buildx', 'imagetools', 'inspect', reference, '--format', '{{json .Manifest}}']);
        return parseImagetoolsManifest(output.trim(), reference);
    }
    if (engine === 'podman') {
        const instances = parsePodmanManifestList(exec('podman', ['manifest', 'inspect', reference]), reference);
        exec('podman', ['pull', '--quiet', reference]);
        const repoDigests = JSON.parse(exec('podman', ['image', 'inspect', '--format', '{{json .RepoDigests}}', reference]));
        return selectIndexDigest(repoDigests, instances);
    }
    throw new Error(`Unsupported engine: ${engine} (use docker or podman).`);
}

function parseArgs(argv, env) {
    const options = { check: false, engine: env.CONTAINER_ENGINE || 'docker' };
    for (let index = 0; index < argv.length; index += 1) {
        const arg = argv[index];
        if (arg === '--check') {
            options.check = true;
        } else if (arg === '--engine') {
            options.engine = argv[index + 1];
            index += 1;
        } else if (arg === '--help' || arg === '-h') {
            options.help = true;
        } else {
            throw new Error(`Unknown argument: ${arg}`);
        }
    }
    return options;
}

function main() {
    const options = parseArgs(process.argv.slice(2), process.env);
    if (options.help) {
        console.log('Usage: node tools/agent/container/resolve-base-image-digests.mjs [--check] [--engine docker|podman]');
        return 0;
    }

    const files = discoverContainerfiles().map((relative) => ({
        relative,
        absolute: path.join(REPO_ROOT, relative),
        text: fs.readFileSync(path.join(REPO_ROOT, relative), 'utf8'),
    }));
    const references = collectTagReferences(files.map((file) => file.text));

    const digestByReference = {};
    for (const reference of references) {
        digestByReference[reference] = queryIndexDigest(options.engine, reference);
        console.log(`${reference} -> ${digestByReference[reference]}`);
    }

    let outdated = 0;
    for (const file of files) {
        const { text, changes } = rewriteDigests(file.text, digestByReference);
        for (const change of changes) {
            outdated += 1;
            console.log(`${file.relative}: ${change.reference} ${change.from || '(unpinned)'} -> ${change.to}`);
        }
        if (changes.length > 0 && !options.check) {
            fs.writeFileSync(file.absolute, text);
        }
    }

    if (options.check && outdated > 0) {
        console.error(`${outdated} FROM line(s) are not pinned to the current index digest.`);
        return 1;
    }
    console.log(outdated === 0 ? 'All base images are pinned to the current index digest.' : `Updated ${outdated} FROM line(s).`);
    return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
    try {
        process.exitCode = main();
    } catch (error) {
        console.error(error instanceof Error ? error.message : String(error));
        process.exitCode = 1;
    }
}
