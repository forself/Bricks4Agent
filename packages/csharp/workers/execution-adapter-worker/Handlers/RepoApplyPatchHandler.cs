using System.Collections.Concurrent;
using System.Text.Json;
using ExecutionAdapterWorker.Support;
using WorkerSdk;

namespace ExecutionAdapterWorker.Handlers;

/// <summary>
/// repo.patch.apply 能力處理器（route: execution.repo.apply_patch）— §14.2 Repository Adapter。
///
/// 責任：套用 patch、驗證 base commit、限制在授權路徑、產出變更摘要、保存 diff artifact。
/// 禁止：接受自由 shell、寫入未授權路徑、套用非 patch 內容。
/// 只接受已被 broker 核可（grant/quota/scope/policy 已過）的請求；此處對 scope 再驗一次（不放寬）。
/// </summary>
public class RepoApplyPatchHandler : ICapabilityHandler
{
    private readonly string _repoRoot;
    private readonly string _evidenceRoot;
    private readonly TimeSpan _gitTimeout;

    // §14.1 idempotency：同一 idempotency_key 重放回傳前次結果，不重複套用
    private readonly ConcurrentDictionary<string, string> _idempotencyCache = new();

    public string CapabilityId => "repo.patch.apply";

    public RepoApplyPatchHandler(string repoRoot, string? evidenceRoot = null, TimeSpan? gitTimeout = null)
    {
        _repoRoot = Path.GetFullPath(repoRoot);
        _evidenceRoot = Path.GetFullPath(evidenceRoot ?? Path.Combine(_repoRoot, ".b4a-evidence"));
        _gitTimeout = gitTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            JsonElement root;
            using var doc = JsonDocument.Parse(payload);
            root = doc.RootElement.TryGetProperty("args", out var argsEl) ? argsEl : doc.RootElement;

            var patch = GetString(root, "patch");
            var baseCommit = GetString(root, "base_commit");
            var idempotencyKey = GetString(root, "idempotency_key");

            // (1) idempotency 重放
            if (!string.IsNullOrEmpty(idempotencyKey)
                && _idempotencyCache.TryGetValue(idempotencyKey, out var cached))
            {
                return (true, cached, null);
            }

            // (2) §14.2 必須是 patch，不得是自由 shell
            if (!UnifiedDiff.IsLikelyPatch(patch))
                return Fail("Payload is not a valid unified diff patch (free-form shell or empty is rejected).");

            // (3) 抽出目標路徑
            var targetPaths = UnifiedDiff.ExtractPaths(patch!);
            if (targetPaths.Count == 0)
                return Fail("Patch does not reference any file paths.");

            // (4) scope 再驗：max_patch_files 與 allowed_paths（不放寬）
            var allowedPaths = ReadAllowedPaths(scope);
            var maxPatchFiles = ReadMaxPatchFiles(scope);
            if (maxPatchFiles is int max && targetPaths.Count > max)
                return Fail($"Patch touches {targetPaths.Count} files, exceeds max_patch_files={max}.");

            // Normalize allowed paths to repo-relative. The grant scope often carries the
            // sandbox root ("/workspace" or repoRoot) which means "the whole repo" — i.e.
            // no sub-path restriction. Sub-paths (e.g. "frontend/") still restrict.
            var allowAll = allowedPaths.Count == 0;
            var normalizedAllowed = new List<string>();
            foreach (var a in allowedPaths)
            {
                var rel = ToRepoRelative(a);
                if (rel.Length == 0) { allowAll = true; break; }
                normalizedAllowed.Add(rel);
            }

            if (!allowAll)
            {
                foreach (var p in targetPaths)
                {
                    if (!UnifiedDiff.IsUnderAllowed(p, normalizedAllowed))
                        return Fail($"Patch path outside allowed scope: {p}");
                }
            }

            // (5) base_commit 驗證（非空且非 HEAD 時，需等於目前 HEAD）
            if (!string.IsNullOrEmpty(baseCommit)
                && !string.Equals(baseCommit, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                var head = await Git(new[] { "rev-parse", "HEAD" }, ct);
                var headSha = head.Stdout.Trim();
                if (head.ExitCode != 0)
                    return Fail($"Cannot resolve repo HEAD: {head.Stderr.Trim()}");
                if (!headSha.StartsWith(baseCommit, StringComparison.OrdinalIgnoreCase)
                    && !baseCommit.StartsWith(headSha, StringComparison.OrdinalIgnoreCase))
                    return Fail($"base_commit mismatch: request={baseCommit} repoHEAD={headSha}");
            }

            // patch 寫到暫存檔
            Directory.CreateDirectory(_evidenceRoot);
            var patchFile = Path.Combine(_evidenceRoot, $"{Sanitize(requestId)}.patch");
            await File.WriteAllTextAsync(patchFile, patch!, ct);

            // (6) numstat（不套用，僅報告 +/-）
            var numstat = await Git(new[] { "apply", "--numstat", patchFile }, ct);

            // (7) --check 後套用
            var check = await Git(new[] { "apply", "--check", patchFile }, ct);
            if (check.ExitCode != 0)
                return Fail($"git apply --check failed: {check.Stderr.Trim()}");

            var apply = await Git(new[] { "apply", patchFile }, ct);
            if (apply.ExitCode != 0)
                return Fail($"git apply failed: {apply.Stderr.Trim()}");

            // (8) 摘要 + 證據
            var (additions, deletions) = ParseNumstat(numstat.Stdout);
            var result = JsonSerializer.Serialize(new
            {
                capability = CapabilityId,
                success = true,
                summary = new
                {
                    files = targetPaths,
                    file_count = targetPaths.Count,
                    additions,
                    deletions,
                },
                evidence_ref = patchFile,
                applied = true,
            });

            if (!string.IsNullOrEmpty(idempotencyKey))
                _idempotencyCache[idempotencyKey] = result;

            return (true, result, null);
        }
        catch (Exception ex)
        {
            return Fail($"repo.patch.apply error: {ex.Message}");
        }
    }

    private Task<ProcessRunner.ProcessResult> Git(IReadOnlyList<string> args, CancellationToken ct)
    {
        // The repo is typically a bind mount owned by a different uid and (on some
        // hosts, e.g. WSL) presented with synthetic file modes. safe.directory=*
        // avoids git's "dubious ownership" refusal; core.fileMode=false stops mode
        // artifacts from failing `git apply`. Both are no-ops on a normal repo.
        var full = new List<string> { "-c", "safe.directory=*", "-c", "core.fileMode=false" };
        full.AddRange(args);
        return ProcessRunner.RunAsync("git", full, _repoRoot, _gitTimeout, ct: ct);
    }

    private static (int additions, int deletions) ParseNumstat(string numstat)
    {
        int add = 0, del = 0;
        foreach (var line in numstat.Split('\n'))
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                if (int.TryParse(parts[0].Trim(), out var a)) add += a;
                if (int.TryParse(parts[1].Trim(), out var d)) del += d;
            }
        }
        return (add, del);
    }

    private static IReadOnlyList<string> ReadAllowedPaths(string scope)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(scope)) return list;
        try
        {
            using var doc = JsonDocument.Parse(scope);
            var r = doc.RootElement;
            foreach (var key in new[] { "allowed_paths", "paths" })
            {
                if (r.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString()!);
                    if (list.Count > 0) break;
                }
            }
        }
        catch { /* scope 非 JSON 或無此欄位 → 視為未限制 */ }
        return list;
    }

    private static int? ReadMaxPatchFiles(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope)) return null;
        try
        {
            using var doc = JsonDocument.Parse(scope);
            if (doc.RootElement.TryGetProperty("max_patch_files", out var v)
                && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Convert an allowed-path entry to repo-relative. The sandbox root (repoRoot,
    /// "/workspace", ".", "/") becomes "" meaning "allow all"; absolute sub-paths
    /// under the root are stripped to repo-relative; already-relative paths pass through.
    /// </summary>
    private string ToRepoRelative(string path)
    {
        var p = path.Replace('\\', '/').Trim();
        if (p is "." or "/" or "") return string.Empty;
        foreach (var root in new[] { _repoRoot.Replace('\\', '/').TrimEnd('/'), "/workspace" })
        {
            if (root.Length == 0) continue;
            if (string.Equals(p, root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                return p[(root.Length + 1)..].TrimStart('/');
        }
        return p.TrimStart('/');
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Sanitize(string s)
        => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private static (bool, string?, string?) Fail(string msg) => (false, null, msg);
}
