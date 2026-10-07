using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GenerationWorker.Support;

/// <summary>
/// 以決定性方式打包目錄：同樣的檔案內容永遠產生逐位元組相同的 zip。
/// - 條目依相對路徑（以 <c>/</c> 分隔）做序數排序，只放檔案、不放目錄條目
/// - 所有條目使用固定的修改時間，不帶任何時間戳或主機資訊
/// - 所有條目使用固定的權限位元（一般檔案 0644）
/// </summary>
public static class DeterministicZip
{
    /// <summary>固定的條目時間（zip 的 DOS 時間格式不接受 1980 年以前的值）。</summary>
    public static readonly DateTimeOffset FixedTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>一般檔案、權限 0644（Unix 模式放在外部屬性的高 16 位元）。</summary>
    public const int RegularFileAttributes = unchecked((int)(0x81A4u << 16));

    /// <summary>
    /// 把 <paramref name="sourceRoot"/> 底下列出的檔案寫成 <paramref name="destinationPath"/>。
    /// <paramref name="relativeFiles"/> 是以 <c>/</c> 分隔的相對路徑，會重新排序。
    /// </summary>
    public static void Create(string sourceRoot, IEnumerable<string> relativeFiles, string destinationPath)
    {
        var entries = relativeFiles
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
        foreach (var relative in entries)
        {
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            entry.LastWriteTime = FixedTimestamp;
            entry.ExternalAttributes = RegularFileAttributes;

            var sourcePath = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var target = entry.Open();
            source.CopyTo(target);
        }
    }

    /// <summary>檔案的 SHA-256（小寫十六進位）。</summary>
    public static string Sha256Hex(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
