using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed record PathDepthEvaluation(bool IsAllowed, int Depth, string Reason)
{
    public static PathDepthEvaluation Allow(int depth) => new(true, depth, string.Empty);

    public static PathDepthEvaluation Deny(string reason, int depth) => new(false, depth, reason);
}

public sealed class PathDepthScope
{
    private readonly int maxDepth;
    private readonly bool sameOriginOnly;
    private readonly bool pathPrefixLock;

    private PathDepthScope(string origin, string pathPrefix, SiteCrawlScope scope)
    {
        Origin = origin;
        PathPrefix = pathPrefix;
        maxDepth = scope.MaxDepth;
        sameOriginOnly = scope.SameOriginOnly;
        pathPrefixLock = scope.PathPrefixLock;
    }

    public string Origin { get; }

    public string PathPrefix { get; }

    public static PathDepthScope Create(Uri startUri, SiteCrawlScope scope)
    {
        ArgumentNullException.ThrowIfNull(startUri);
        ArgumentNullException.ThrowIfNull(scope);

        return new PathDepthScope(
            NormalizeOrigin(startUri),
            EnsureTrailingSlash(NormalizePath(startUri.AbsolutePath)),
            scope);
    }

    public PathDepthEvaluation Evaluate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (sameOriginOnly && !string.Equals(NormalizeOrigin(uri), Origin, StringComparison.OrdinalIgnoreCase))
        {
            return PathDepthEvaluation.Deny("outside_origin", -1);
        }

        var path = NormalizePath(uri.AbsolutePath);
        var isWithinPrefix = IsWithinPathPrefix(path);
        if (pathPrefixLock && !isWithinPrefix)
        {
            return PathDepthEvaluation.Deny("outside_path_prefix", -1);
        }

        var depth = CalculateDepth(path, isWithinPrefix);
        if (depth > maxDepth)
        {
            return PathDepthEvaluation.Deny("outside_path_depth", depth);
        }

        return PathDepthEvaluation.Allow(depth);
    }

    private static string NormalizeOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    private static string NormalizePath(string path)
    {
        var normalizedPath = string.IsNullOrEmpty(path) ? "/" : Uri.UnescapeDataString(path);
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedPath = "/" + normalizedPath;
        }

        return normalizedPath;
    }

    private static string EnsureTrailingSlash(string path)
    {
        return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    }

    private bool IsWithinPathPrefix(string path)
    {
        if (PathPrefix == "/")
        {
            return true;
        }

        var pathPrefixWithoutTrailingSlash = PathPrefix.TrimEnd('/');
        return string.Equals(path, PathPrefix, StringComparison.Ordinal) ||
            string.Equals(path, pathPrefixWithoutTrailingSlash, StringComparison.Ordinal) ||
            path.StartsWith(PathPrefix, StringComparison.Ordinal);
    }

    private int CalculateDepth(string path, bool isWithinPrefix)
    {
        if (!isWithinPrefix)
        {
            return CountSegments(path);
        }

        if (PathPrefix == "/")
        {
            return CountSegments(path);
        }

        var pathPrefixWithoutTrailingSlash = PathPrefix.TrimEnd('/');
        if (string.Equals(path, PathPrefix, StringComparison.Ordinal) ||
            string.Equals(path, pathPrefixWithoutTrailingSlash, StringComparison.Ordinal))
        {
            return 0;
        }

        return CountSegments(path[PathPrefix.Length..]);
    }

    private static int CountSegments(string path)
    {
        var trimmedPath = path.Trim('/');
        return trimmedPath.Length == 0
            ? 0
            : trimmedPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
