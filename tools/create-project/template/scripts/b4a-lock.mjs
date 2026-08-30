export const B4A_LOCK_VERSION = 2;

export const isFullGitObjectId = value => /^[0-9a-f]{40}$/i.test(String(value || ''));

export function parseB4aLock(value) {
    if (value?.version === B4A_LOCK_VERSION) {
        if (!isFullGitObjectId(value.tree)) {
            throw new Error('b4a.lock.json v2 的 tree 必須是完整 40 碼 Git tree object ID。');
        }
        if (typeof value.source !== 'string' || !value.source.trim()) {
            throw new Error('b4a.lock.json v2 缺少 source。');
        }
        return {
            version: B4A_LOCK_VERSION,
            kind: 'tree',
            tree: value.tree.toLowerCase(),
            source: value.source.replaceAll('\\', '/'),
            pinnedAtUtc: value.pinnedAtUtc || null
        };
    }

    // v1 compatibility only. Re-pin immediately to remove the dependency on a
    // detached subtree-split commit which may not be present in branch-only clones.
    if (isFullGitObjectId(value?.commit)) {
        return {
            version: 1,
            kind: 'legacy-commit',
            commit: value.commit.toLowerCase(),
            pinnedAtUtc: value.pinnedAtUtc || null
        };
    }

    throw new Error('b4a.lock.json 格式無效：需要 v2 tree lock（或可遷移的舊 commit lock）。');
}

export function createB4aTreeLock(tree, source) {
    if (!isFullGitObjectId(tree)) throw new Error('無法建立 B4A tree lock：tree object ID 無效。');
    return {
        version: B4A_LOCK_VERSION,
        source: String(source || '.').replaceAll('\\', '/'),
        tree: tree.toLowerCase(),
        pinnedAtUtc: new Date().toISOString()
    };
}
