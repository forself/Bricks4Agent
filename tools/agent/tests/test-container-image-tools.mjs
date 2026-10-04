#!/usr/bin/env node
// 離線測試 resolve-base-image-digests.mjs 與 verify-container-images.mjs 的純函式（不呼叫 docker／podman、不連網）。

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const resolver = await import(pathToFileURL(path.join(ROOT, 'tools', 'agent', 'container', 'resolve-base-image-digests.mjs')).href);
const verifier = await import(pathToFileURL(path.join(ROOT, 'tools', 'scripts', 'verify-container-images.mjs')).href);
const { default: containerUser } = await import(pathToFileURL(path.join(ROOT, 'tools', 'agent', 'container', 'container-user.js')).href);

const OLD = `sha256:${'a'.repeat(64)}`;
const NEW = `sha256:${'b'.repeat(64)}`;
const ARM = `sha256:${'c'.repeat(64)}`;
const AMD = `sha256:${'d'.repeat(64)}`;

// ── 映像參照解析 ──
assert.deepEqual(resolver.parseImageReference('mcr.microsoft.com/dotnet/sdk:10.0'), {
    repository: 'mcr.microsoft.com/dotnet/sdk', tag: '10.0', digest: null,
});
assert.deepEqual(resolver.parseImageReference(`registry.local:5000/team/node:22-bookworm-slim@${OLD}`), {
    repository: 'registry.local:5000/team/node', tag: '22-bookworm-slim', digest: OLD,
});
assert.deepEqual(resolver.parseImageReference('registry.local:5000/team/node'), {
    repository: 'registry.local:5000/team/node', tag: null, digest: null,
});
assert.equal(resolver.formatImageReference({ repository: 'node', tag: '22', digest: NEW }), `node:22@${NEW}`);

const fromLine = resolver.parseFromLine(`FROM --platform=$BUILDPLATFORM node:22-bookworm-slim@${OLD} AS build`);
assert.equal(fromLine.alias, 'build');
assert.equal(fromLine.tag, '22-bookworm-slim');
assert.equal(fromLine.prefix + fromLine.reference + fromLine.suffix, `FROM --platform=$BUILDPLATFORM node:22-bookworm-slim@${OLD} AS build`);
assert.equal(resolver.parseFromLine('RUN echo FROM node'), null);

// ── 多階段：引用先前 stage 的 FROM 不算外部映像 ──
const multiStage = [
    `FROM mcr.microsoft.com/dotnet/sdk:10.0@${OLD} AS build`,
    'RUN dotnet publish',
    'FROM build AS test',
    `FROM mcr.microsoft.com/dotnet/aspnet:10.0@${OLD}`,
    'COPY --from=build /app/publish .',
].join('\n');
assert.deepEqual(resolver.listBaseImages(multiStage).map((base) => `${base.repository}:${base.tag}`), [
    'mcr.microsoft.com/dotnet/sdk:10.0',
    'mcr.microsoft.com/dotnet/aspnet:10.0',
]);
assert.deepEqual(resolver.collectTagReferences([multiStage, `FROM node:22-bookworm-slim@${OLD}\n`]), [
    'mcr.microsoft.com/dotnet/aspnet:10.0',
    'mcr.microsoft.com/dotnet/sdk:10.0',
    'node:22-bookworm-slim',
]);
assert.throws(() => resolver.collectTagReferences(['FROM node\n']), /no tag/);

// ── 改寫 digest：只改有變動的行、保留 CRLF 與 alias ──
const crlf = multiStage.replace(/\n/g, '\r\n');
const rewritten = resolver.rewriteDigests(crlf, { 'mcr.microsoft.com/dotnet/sdk:10.0': NEW, 'mcr.microsoft.com/dotnet/aspnet:10.0': OLD });
assert.equal(rewritten.changes.length, 1);
assert.deepEqual(rewritten.changes[0], { reference: 'mcr.microsoft.com/dotnet/sdk:10.0', from: OLD, to: NEW });
assert(rewritten.text.includes(`FROM mcr.microsoft.com/dotnet/sdk:10.0@${NEW} AS build\r\n`));
assert(rewritten.text.includes(`FROM mcr.microsoft.com/dotnet/aspnet:10.0@${OLD}`));
const unpinned = resolver.rewriteDigests('FROM node:22-bookworm-slim\nUSER 1\n', { 'node:22-bookworm-slim': NEW });
assert.equal(unpinned.text, `FROM node:22-bookworm-slim@${NEW}\nUSER 1\n`);
assert.equal(unpinned.changes[0].from, null);
assert.throws(() => resolver.rewriteDigests('FROM node:22\n', { 'node:22': 'sha256:short' }), /malformed/);

// ── docker imagetools：只接受 index ──
assert.equal(resolver.parseImagetoolsManifest(JSON.stringify({
    mediaType: 'application/vnd.oci.image.index.v1+json', digest: NEW,
})), NEW);
assert.equal(resolver.parseImagetoolsManifest(JSON.stringify({
    mediaType: 'application/vnd.docker.distribution.manifest.list.v2+json', digest: NEW,
})), NEW);
assert.throws(() => resolver.parseImagetoolsManifest(JSON.stringify({
    mediaType: 'application/vnd.oci.image.manifest.v1+json', digest: NEW,
})), /not a multi-arch index/);
assert.throws(() => resolver.parseImagetoolsManifest('not json'), /Unexpected/);

// ── podman：從 RepoDigests 挑出不屬於任何平台 manifest 的 digest ──
const instances = resolver.parsePodmanManifestList(JSON.stringify({
    manifests: [{ digest: AMD, platform: { architecture: 'amd64' } }, { digest: ARM, platform: { architecture: 'arm64' } }],
}));
assert.deepEqual(instances, [AMD, ARM]);
assert.equal(resolver.selectIndexDigest([`docker.io/library/node@${AMD}`, `docker.io/library/node@${NEW}`], instances), NEW);
assert.throws(() => resolver.selectIndexDigest([`node@${AMD}`], instances), /exactly one/);
assert.throws(() => resolver.parsePodmanManifestList(JSON.stringify({ schemaVersion: 2, layers: [] })), /not a multi-arch index/);

// ── queryIndexDigest 以注入的 exec 測試指令組合 ──
const calls = [];
const fakeDocker = (command, args) => {
    calls.push([command, ...args]);
    return JSON.stringify({ mediaType: 'application/vnd.oci.image.index.v1+json', digest: NEW });
};
assert.equal(resolver.queryIndexDigest('docker', 'node:22-bookworm-slim', fakeDocker), NEW);
assert.deepEqual(calls[0], ['docker', 'buildx', 'imagetools', 'inspect', 'node:22-bookworm-slim', '--format', '{{json .Manifest}}']);
const fakePodman = (command, args) => {
    if (args[0] === 'manifest') return JSON.stringify({ manifests: [{ digest: AMD }, { digest: ARM }] });
    if (args[0] === 'pull') return '';
    return JSON.stringify([`docker.io/library/node@${ARM}`, `docker.io/library/node@${NEW}`]);
};
assert.equal(resolver.queryIndexDigest('podman', 'node:22-bookworm-slim', fakePodman), NEW);
assert.throws(() => resolver.queryIndexDigest('nerdctl', 'node:22', fakeDocker), /Unsupported engine/);

// ── 共用的 root 判定（container-user.js）：與 ContainerManager.ValidateUser 相同的案例（broker-tests 的 AgentContainerTests）──
const ROOT_USER_SPECS = ['0', 'root', 'ROOT', '10001:0', '0:10001', '00', '+0', '-0', '000', ' 0 ', '10001:00', '+0:10001', '10001:root', 'agent:0', 'agent:ROOT'];
const NON_ROOT_USER_SPECS = ['10001', '10001:10001', '010001', 'app', 'app:app', 'rooted', '10'];
for (const spec of ROOT_USER_SPECS) {
    assert.equal(containerUser.isRootUserSpec(spec), true, `"${spec}" must count as root`);
}
for (const spec of NON_ROOT_USER_SPECS) {
    assert.equal(containerUser.isRootUserSpec(spec), false, `"${spec}" must not count as root`);
}

// ── verify-container-images：合格與各種違規 ──
const good = [
    '# comment mentioning adduser is fine',
    `FROM mcr.microsoft.com/dotnet/sdk:10.0@${OLD} AS build`,
    'COPY . .',
    'RUN dotnet publish',
    `FROM mcr.microsoft.com/dotnet/aspnet:10.0@${OLD}`,
    'COPY --from=build /app/publish .',
    'RUN groupadd --gid 10002 svc \\',
    '    && useradd --uid 10002 --gid 10002 --no-create-home svc',
    'USER svc',
].join('\n');
const goodResult = verifier.checkContainerfile('good/Containerfile', good);
assert.deepEqual(goodResult.errors, []);
assert.equal(goodResult.uid, 10002);
assert.equal(verifier.checkContainerfile('numeric/Containerfile', `FROM node:22-bookworm-slim@${OLD}\nUSER 10001:10001\n`).uid, 10001);
const namedGroup = verifier.checkContainerfile('named-group/Containerfile', [
    `FROM node:22-bookworm-slim@${OLD}`,
    'RUN groupadd --gid 10002 svc && useradd --uid 10002 --gid 10002 svc',
    'USER svc:svc',
].join('\n'));
assert.deepEqual(namedGroup.errors, []);
assert.equal(namedGroup.uid, 10002);

function expectError(name, text, pattern) {
    const { errors } = verifier.checkContainerfile(`${name}/Containerfile`, text);
    assert(errors.some((error) => pattern.test(error)), `${name}: expected ${pattern}, got ${JSON.stringify(errors)}`);
}
expectError('unpinned', 'FROM mcr.microsoft.com/dotnet/aspnet:10.0\nUSER 10002\n', /must be pinned/);
expectError('dotnet8', `FROM mcr.microsoft.com/dotnet/aspnet:8.0@${OLD}\nUSER 10002\n`, /must use tag 10\.0/);
expectError('node20', `FROM node:20-bookworm-slim@${OLD}\nUSER 10001\n`, /must use tag 22/);
expectError('unknown-base', `FROM alpine:3.20@${OLD}\nUSER 10001\n`, /unrecognised base image/);
expectError('adduser', `FROM node:22@${OLD}\nRUN adduser --disabled-password svc\nUSER 10001\n`, /adduser/);
expectError('no-user', `FROM node:22@${OLD}\nRUN true\n`, /no USER/);
expectError('root-user', `FROM node:22@${OLD}\nUSER root\n`, /runs as root/);
expectError('uid-zero', `FROM node:22@${OLD}\nUSER 0:0\n`, /runs as root/);
// root 的每一種寫法，包含只有群組是 root 的情況（規則與 ContainerManager.ValidateUser 相同）。
for (const spec of ROOT_USER_SPECS.map((value) => value.trim())) {
    expectError(`root-spec-${spec}`, `FROM node:22@${OLD}\nRUN groupadd --gid 10002 agent && useradd --uid 10002 --gid 10002 agent\nUSER ${spec}\n`, /runs as root/);
}
expectError('group-created-with-gid-0', [
    `FROM node:22@${OLD}`,
    'RUN groupadd --gid 0 admins && useradd --uid 10002 --gid 0 svc',
    'USER svc:admins',
].join('\n'), /runs as root/);
expectError('user-created-with-uid-00', `FROM node:22@${OLD}\nRUN useradd --uid 00 svc\nUSER svc\n`, /runs as root/);
expectError('unresolved', `FROM node:22@${OLD}\nUSER someone\n`, /cannot be resolved/);
expectError('copy-all', `FROM node:22@${OLD}\nCOPY . .\nUSER 10001\n`, /whole build context/);
expectError('add-all', `FROM node:22@${OLD}\nADD --chown=1:1 ./ /app\nUSER 10001\n`, /whole build context/);
expectError('volume', `FROM node:22@${OLD}\nVOLUME ["/workspace"]\nUSER 10001\n`, /must not declare VOLUME/);
expectError('user-only-in-build', `FROM node:22@${OLD} AS build\nUSER 10001\nFROM node:22@${OLD}\nRUN true\n`, /no USER/);

const duplicate = verifier.checkContainerfiles([
    { relative: 'a/Containerfile', text: `FROM node:22@${OLD}\nUSER 10001\n` },
    { relative: 'b/Containerfile', text: `FROM node:22@${OLD}\nUSER 10001\n` },
]);
assert(duplicate.errors.some((error) => /UID 10001 is already used/.test(error)));

// ── 工具註解中指向的測試檔都要存在 ──
for (const tool of ['tools/agent/container/resolve-base-image-digests.mjs', 'tools/scripts/verify-container-images.mjs', 'tools/agent/container/container-user.js']) {
    const source = fs.readFileSync(path.join(ROOT, tool), 'utf8');
    for (const [reference] of source.matchAll(/tools\/agent\/tests\/[A-Za-z0-9_.-]+\.m?js/g)) {
        assert(fs.existsSync(path.join(ROOT, reference)), `${tool} refers to ${reference}, which does not exist`);
    }
}
const resolverSource = fs.readFileSync(path.join(ROOT, 'tools/agent/container/resolve-base-image-digests.mjs'), 'utf8');
assert(resolverSource.includes('tools/agent/tests/test-container-image-tools.mjs'), 'the resolver should point at its offline test');

// ── repo 內實際的 Containerfile 都要通過，且涵蓋全部七個映像 ──
const discovered = resolver.discoverContainerfiles(ROOT);
for (const expected of [
    'packages/csharp/broker/Containerfile',
    'packages/csharp/workers/file-worker/Containerfile',
    'packages/csharp/workers/line-worker/Containerfile',
    'packages/csharp/workers/execution-adapter-worker/Containerfile',
    'tools/agent/Containerfile',
    'tools/agent/container/mock-ollama.Containerfile',
    'tools/agent/container/mock-openai.Containerfile',
]) {
    assert(discovered.includes(expected), `expected ${expected} to be discovered`);
}

console.log('Container image tool tests passed.');
