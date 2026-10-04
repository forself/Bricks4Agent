namespace FileWorker;

/// <summary>
/// file-worker 的 sandbox 邊界與敏感路徑政策，read、list、search、write、delete 一致套用。
///
/// - 邊界：請求路徑先正規化，再以「根目錄 + 分隔字元」做完整前綴比對；接著逐段解析
///   symlink／junction，解析後的實際路徑也必須留在（同樣解析過的）根目錄內。
/// - 拒絕清單：路徑中任何一段符合 <see cref="IsSensitiveName"/>，或路徑結尾符合特定位置的本機設定檔，
///   即拒絕（版本控制中繼資料、代理工具設定、本機執行期狀態、環境變數檔、本機設定、資料庫與金鑰類檔案）。
///   列舉與搜尋時直接略過這些項目，也不進入 symlink（避免經由連結走出 sandbox）。
///   拒絕清單是過渡措施；只提供白名單快照的唯讀視圖列為後續。
/// - 路徑段含冒號一律拒絕（所有平台都一樣，與檔名 pattern 的規則一致）。
/// - 搜尋：檔名 pattern 只能比對檔名（<see cref="IsFileNamePattern"/>），目錄一律由 directory（或 path）指定並經 <see cref="Resolve"/>。
/// </summary>
public sealed class SandboxPolicy
{
    public const string OutsideSandboxError = "Path outside sandbox.";
    public const string BlockedPathError = "Path is blocked by the sandbox policy.";
    public const string InvalidPatternError = "Search pattern must match file names only (no directory part); use directory for the folder to search.";

    private const int MaxLinkHops = 40;

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private static readonly string[] SensitiveExactNames =
    {
        ".git",
        ".claude",
        ".codegraph-cache",
        ".ssh",
        // 本機執行期狀態（.gitignore）：本機啟動腳本在這裡產生執行期的設定覆寫檔。
        ".run",
        ".env",
        "appsettings.Development.json",
        "appsettings.Production.json",
        "agent-stack.env",
        "Api.txt",
        "ngrok_recovery_codes.txt",
    };

    private static readonly string[] SensitivePrefixes =
    {
        ".env.",
        "id_rsa",
        "id_dsa",
        "id_ecdsa",
        "id_ed25519",
        "client_secret_",
    };

    private static readonly string[] SensitiveExtensions =
    {
        ".pem",
        ".key",
        ".pfx",
        ".p12",
        ".db",
        ".db-wal",
        ".db-shm",
        ".db-journal",
    };

    // 下載的雲端服務帳戶金鑰的預設檔名：<專案>-<12 位十六進位>.json。
    private static readonly System.Text.RegularExpressions.Regex ServiceAccountKeyName = new(
        @"^.+-[0-9a-f]{12}\.json$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // 只在特定位置才算敏感的檔案：比對路徑的最後幾段（不分大小寫），不擋其他位置的同名檔。
    private static readonly string[][] SensitivePathSuffixes =
    {
        new[] { "line-worker", "appsettings.json" },
    };

    private readonly string _realRoot;

    public SandboxPolicy(string sandboxRoot)
    {
        Root = TrimTrailingSeparator(Path.GetFullPath(sandboxRoot));
        _realRoot = TrimTrailingSeparator(ResolveRealPath(Root, 0) ?? Root);
    }

    /// <summary>正規化後的 sandbox 根目錄（不含結尾分隔字元）。</summary>
    public string Root { get; }

    /// <summary>
    /// 把請求路徑解析成 sandbox 內的完整路徑；越界或命中拒絕清單時回傳 Error。
    /// </summary>
    public (string? FullPath, string? Error) Resolve(string? requestedPath)
    {
        var requested = requestedPath ?? string.Empty;
        if (requested.IndexOf('\0') >= 0)
            return (null, OutsideSandboxError);

        if (HasColonInPath(requested))
            return (null, BlockedPathError);

        string fullPath;
        try
        {
            fullPath = TrimTrailingSeparator(Path.GetFullPath(Path.Combine(Root, requested)));
        }
        catch
        {
            return (null, OutsideSandboxError);
        }

        if (!IsWithin(fullPath, Root))
            return (null, OutsideSandboxError);

        if (HasSensitiveSegment(Path.GetRelativePath(Root, fullPath)))
            return (null, BlockedPathError);

        var realPath = ResolveRealPath(fullPath, 0);
        if (realPath == null || !IsWithin(realPath, _realRoot))
            return (null, OutsideSandboxError);

        if (HasSensitiveSegment(Path.GetRelativePath(_realRoot, realPath)))
            return (null, BlockedPathError);

        return (fullPath, null);
    }

    /// <summary>單一路徑段是否屬於拒絕清單（不分大小寫）；含冒號的名稱一律視為拒絕。</summary>
    public static bool IsSensitiveName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        if (name.IndexOf(':') >= 0)
            return true;

        foreach (var exact in SensitiveExactNames)
        {
            if (string.Equals(name, exact, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var prefix in SensitivePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var extension in SensitiveExtensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return ServiceAccountKeyName.IsMatch(name);
    }

    /// <summary>
    /// sandbox 內的相對路徑是否被拒絕：任何一段屬於拒絕清單，或結尾符合只在特定位置才算敏感的檔案。
    /// </summary>
    public static bool IsSensitiveRelativePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath) || relativePath == ".")
            return false;

        var segments = relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (IsSensitiveName(segment))
                return true;
        }

        foreach (var suffix in SensitivePathSuffixes)
        {
            if (segments.Length < suffix.Length)
                continue;

            var matched = true;
            for (var index = 0; index < suffix.Length; index++)
            {
                if (!string.Equals(segments[segments.Length - suffix.Length + index], suffix[index], StringComparison.OrdinalIgnoreCase))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 列出目錄的直接子項目：略過 symlink／junction 與拒絕清單中的名稱。
    /// </summary>
    public IEnumerable<FileSystemInfo> EnumerateEntries(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options))
        {
            if (entry.LinkTarget != null || IsSensitiveName(entry.Name))
                continue;
            var fullEntry = TrimTrailingSeparator(Path.GetFullPath(entry.FullName));
            if (!IsWithin(fullEntry, Root) || HasSensitiveSegment(Path.GetRelativePath(Root, fullEntry)))
                continue;
            yield return entry;
        }
    }

    /// <summary>
    /// 搜尋用的 pattern 只能比對檔名：不得含目錄部分（任何平台的分隔字元）、磁碟代號或 NUL。
    /// 搜尋的目錄由 directory（或 path）參數指定，並經 <see cref="Resolve"/> 檢查。
    /// </summary>
    public static bool IsFileNamePattern(string? pattern)
    {
        if (pattern == null)
            return false;

        if (pattern.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
            return false;

        return pattern != "." && pattern != "..";
    }

    /// <summary>
    /// 遞迴列舉符合 pattern 的檔案（與 Directory.GetFiles 相同的萬用字元語意）：
    /// 不進入 symlink／junction，也不進入或回傳拒絕清單中的項目。
    /// pattern 必須通過 <see cref="IsFileNamePattern"/>，否則丟出 <see cref="ArgumentException"/>；
    /// 回傳的每一個檔案都再確認仍在 sandbox 內、路徑中沒有拒絕清單的段落。
    /// </summary>
    public IEnumerable<string> EnumerateFilesRecursive(string directory, string pattern)
    {
        if (!IsFileNamePattern(pattern))
            throw new ArgumentException(InvalidPatternError, nameof(pattern));

        return EnumerateFilesRecursiveCore(directory, pattern);
    }

    private IEnumerable<string> EnumerateFilesRecursiveCore(string directory, string pattern)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchType = MatchType.Win32,
        };

        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> files;
            List<string> subdirectories;
            try
            {
                files = Directory.EnumerateFiles(current, pattern, options).ToList();
                subdirectories = Directory.EnumerateDirectories(current, "*", options).ToList();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var file in files)
            {
                if (IsSensitiveName(Path.GetFileName(file)) || new FileInfo(file).LinkTarget != null)
                    continue;

                // 縱深防禦：結果必須仍在 sandbox 內，且相對路徑沒有拒絕清單的段落。
                var fullFile = TrimTrailingSeparator(Path.GetFullPath(file));
                if (!IsWithin(fullFile, Root) || HasSensitiveSegment(Path.GetRelativePath(Root, fullFile)))
                    continue;

                yield return fullFile;
            }

            for (var i = subdirectories.Count - 1; i >= 0; i--)
            {
                var subdirectory = subdirectories[i];
                if (IsSensitiveName(Path.GetFileName(subdirectory)) || new DirectoryInfo(subdirectory).LinkTarget != null)
                    continue;
                pending.Push(subdirectory);
            }
        }
    }

    private static bool HasSensitiveSegment(string relativePath)
        => IsSensitiveRelativePath(relativePath);

    /// <summary>
    /// 請求路徑中含冒號（所有平台一律拒絕）。Windows 上完整路徑開頭的磁碟代號除外，
    /// 那種路徑仍要通過後面的邊界檢查。
    /// </summary>
    private static bool HasColonInPath(string requested)
    {
        var start = 0;
        if (OperatingSystem.IsWindows() && Path.IsPathFullyQualified(requested))
            start = (Path.GetPathRoot(requested) ?? string.Empty).Length;

        return requested.IndexOf(':', start) >= 0;
    }

    private static bool IsWithin(string candidate, string root)
    {
        if (string.Equals(candidate, root, PathComparison))
            return true;

        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, PathComparison);
    }

    private static string TrimTrailingSeparator(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        return path.Length > root.Length ? path.TrimEnd(Separators) : path;
    }

    /// <summary>
    /// 逐段解析 symlink／junction，回傳實際路徑；尚不存在的尾段原樣接上。
    /// 連結層數過多時回傳 null（視為越界）。
    /// </summary>
    private static string? ResolveRealPath(string fullPath, int hops)
    {
        if (hops > MaxLinkHops)
            return null;

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return null;

        var segments = fullPath[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var next = Path.Combine(current, segments[index]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);

            string? linkTarget;
            try
            {
                linkTarget = info.LinkTarget;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            if (linkTarget != null)
            {
                var target = Path.GetFullPath(linkTarget, current);
                var resolved = ResolveRealPath(target, hops + 1);
                if (resolved == null)
                    return null;
                current = resolved;
                continue;
            }

            if (!info.Exists)
            {
                // 尚不存在（例如要寫入的新檔）：剩下的段落沒有連結可解析。
                return segments.Length == index + 1
                    ? next
                    : Path.Combine(next, Path.Combine(segments[(index + 1)..]));
            }

            current = next;
        }

        return TrimTrailingSeparator(current);
    }
}
