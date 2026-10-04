#!/usr/bin/env node
// 靜態檢查 repo 內所有 Containerfile（不需要 docker 或 podman）：
// - 每個外部基底映像的 FROM 都以 @sha256 釘選 index digest；.NET 必須是 10.0，node 必須是 22。
// - 不得出現 adduser（.NET 10 的 Ubuntu 映像沒有）與 node:20。
// - 最終階段要有非 root 的 USER，且各映像的 UID 不重複。
// - 最終階段不得 `COPY . .`／`ADD . .`（整個 repo 進入執行映像），也不得宣告 VOLUME
//   （沒有掛載時會產生不受 --read-only 限制的可寫匿名卷）。
//
// 用法：node tools/scripts/verify-container-images.mjs
// 純函式 checkContainerfile()/checkContainerfiles() 供測試直接呼叫。

import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

import {
    discoverContainerfiles,
    listBaseImages,
    REPO_ROOT,
} from '../agent/container/resolve-base-image-digests.mjs';

const DIGEST_PATTERN = /^sha256:[0-9a-f]{64}$/;

const BASE_IMAGE_RULES = [
    {
        name: '.NET',
        matches: (repository) => repository.startsWith('mcr.microsoft.com/dotnet/'),
        tagOk: (tag) => /^10\.0(?:$|[-.])/.test(tag),
        expected: 'tag 10.0',
    },
    {
        name: 'node',
        matches: (repository) => repository === 'node' || repository === 'docker.io/library/node' || repository === 'library/node',
        tagOk: (tag) => /^22(?:$|[-.])/.test(tag),
        expected: 'tag 22',
    },
];

/** 合併以反斜線續行的指令，略過註解與空行。回傳 [{ keyword, args, line }]（line 為起始行號，從 1 起算）。 */
export function parseInstructions(text) {
    const instructions = [];
    let current = null;
    text.split(/\r?\n/).forEach((rawLine, index) => {
        const trimmed = rawLine.trim();
        if (!current && (trimmed === '' || trimmed.startsWith('#'))) {
            return;
        }
        if (current && trimmed.startsWith('#')) {
            return;
        }
        const continues = trimmed.endsWith('\\');
        const content = continues ? trimmed.slice(0, -1) : trimmed;
        if (current) {
            current.args += ` ${content.trim()}`;
        } else {
            const match = /^(\S+)\s*(.*)$/.exec(content);
            current = { keyword: match[1].toUpperCase(), args: match[2], line: index + 1 };
        }
        if (!continues) {
            instructions.push(current);
            current = null;
        }
    });
    if (current) {
        instructions.push(current);
    }
    return instructions;
}

/** 依 FROM 切出各階段。 */
export function splitStages(instructions) {
    const stages = [];
    for (const instruction of instructions) {
        if (instruction.keyword === 'FROM') {
            stages.push({ from: instruction, instructions: [] });
        } else if (stages.length > 0) {
            stages[stages.length - 1].instructions.push(instruction);
        }
    }
    return stages;
}

/** 把 USER 的值換成數字 UID；名稱以同一階段 `useradd ... --uid N ... name` 解析。無法判定時回傳 null。 */
export function resolveUid(userValue, stageInstructions) {
    const user = userValue.split(':')[0].trim();
    if (/^\d+$/.test(user)) {
        return Number(user);
    }
    if (user === 'root') {
        return 0;
    }
    for (const instruction of stageInstructions) {
        if (instruction.keyword !== 'RUN') {
            continue;
        }
        for (const command of instruction.args.split(/&&|;/)) {
            const tokens = command.trim().split(/\s+/);
            if (tokens[0] !== 'useradd' || tokens[tokens.length - 1] !== user) {
                continue;
            }
            const uidIndex = tokens.findIndex((token) => token === '--uid' || token === '-u');
            if (uidIndex >= 0 && /^\d+$/.test(tokens[uidIndex + 1] || '')) {
                return Number(tokens[uidIndex + 1]);
            }
        }
    }
    return null;
}

function copiesWholeContext(instruction) {
    if (instruction.keyword !== 'COPY' && instruction.keyword !== 'ADD') {
        return false;
    }
    const tokens = instruction.args.split(/\s+/).filter(Boolean);
    if (tokens.some((token) => token.startsWith('--from'))) {
        return false;
    }
    const operands = tokens.filter((token) => !token.startsWith('--'));
    const sources = operands.slice(0, -1);
    return sources.some((source) => source === '.' || source === './');
}

/** 檢查一份 Containerfile。回傳 { errors: string[], uid: number|null }。 */
export function checkContainerfile(relativePath, text) {
    const errors = [];
    const fail = (message) => errors.push(`${relativePath}: ${message}`);

    for (const base of listBaseImages(text)) {
        if (!base.digest || !DIGEST_PATTERN.test(base.digest)) {
            fail(`FROM ${base.reference} must be pinned with @sha256:<index digest>.`);
        }
        const rule = BASE_IMAGE_RULES.find((candidate) => candidate.matches(base.repository));
        if (!rule) {
            fail(`FROM ${base.reference} uses an unrecognised base image; add a rule to verify-container-images.mjs.`);
        } else if (!base.tag || !rule.tagOk(base.tag)) {
            fail(`FROM ${base.reference}: ${rule.name} base must use ${rule.expected}.`);
        }
    }

    const instructions = parseInstructions(text);
    for (const instruction of instructions) {
        if (/\badduser\b/.test(instruction.args)) {
            fail(`line ${instruction.line}: adduser is not available on the .NET 10 images; use groupadd/useradd.`);
        }
        if (/\bnode:20\b/.test(instruction.args)) {
            fail(`line ${instruction.line}: node:20 is no longer allowed; use node:22.`);
        }
    }

    const stages = splitStages(instructions);
    if (stages.length === 0) {
        fail('no FROM instruction.');
        return { errors, uid: null };
    }
    const finalStage = stages[stages.length - 1];

    for (const instruction of finalStage.instructions) {
        if (copiesWholeContext(instruction)) {
            fail(`line ${instruction.line}: the final stage must not copy the whole build context (${instruction.keyword} ${instruction.args}).`);
        }
        if (instruction.keyword === 'VOLUME') {
            fail(`line ${instruction.line}: the final stage must not declare VOLUME; mounts are provided at run time.`);
        }
    }

    const users = finalStage.instructions.filter((instruction) => instruction.keyword === 'USER');
    let uid = null;
    if (users.length === 0) {
        fail('the final stage has no USER instruction (it would run as root).');
    } else {
        const last = users[users.length - 1];
        uid = resolveUid(last.args, finalStage.instructions);
        if (uid === null) {
            fail(`USER ${last.args} cannot be resolved to a numeric UID.`);
        } else if (uid === 0) {
            fail(`USER ${last.args} runs as root.`);
            uid = null;
        }
    }

    return { errors, uid };
}

/** 檢查多份 Containerfile，另外檢查 UID 不重複。files: [{ relative, text }]。 */
export function checkContainerfiles(files) {
    const errors = [];
    const owners = new Map();
    for (const file of files) {
        const result = checkContainerfile(file.relative, file.text);
        errors.push(...result.errors);
        if (result.uid !== null) {
            if (owners.has(result.uid)) {
                errors.push(`${file.relative}: UID ${result.uid} is already used by ${owners.get(result.uid)}.`);
            } else {
                owners.set(result.uid, file.relative);
            }
        }
    }
    return { errors, uids: Object.fromEntries([...owners].map(([uid, owner]) => [owner, uid])) };
}

function main() {
    const files = discoverContainerfiles(REPO_ROOT).map((relative) => ({
        relative,
        text: fs.readFileSync(path.join(REPO_ROOT, relative), 'utf8'),
    }));
    if (files.length === 0) {
        console.error('No Containerfile found.');
        return 1;
    }
    const { errors, uids } = checkContainerfiles(files);
    if (errors.length > 0) {
        for (const error of errors) {
            console.error(`  ✗ ${error}`);
        }
        console.error(`Container image check failed: ${errors.length} problem(s) in ${files.length} Containerfile(s).`);
        return 1;
    }
    for (const [file, uid] of Object.entries(uids)) {
        console.log(`  ✓ ${file} (uid ${uid})`);
    }
    console.log(`Container image check passed: ${files.length} Containerfile(s).`);
    return 0;
}

const invokedPath = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : '';
if (import.meta.url === invokedPath || fileURLToPath(import.meta.url) === process.argv[1]) {
    process.exitCode = main();
}
