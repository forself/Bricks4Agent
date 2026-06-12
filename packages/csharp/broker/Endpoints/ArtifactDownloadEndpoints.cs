using Broker.Services;

namespace Broker.Endpoints;

public static class ArtifactDownloadEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/artifacts/download/{artifactId}", (
            string artifactId,
            HttpContext ctx,
            BrokerArtifactDownloadService service) =>
        {
            var expRaw = ctx.Request.Query["exp"].ToString();
            var sig = ctx.Request.Query["sig"].ToString();
            if (!long.TryParse(expRaw, out var exp) || string.IsNullOrWhiteSpace(sig))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var resolved = service.ValidateAndResolve(artifactId, exp, sig, DateTimeOffset.UtcNow);
            if (resolved.IsExpired)
                return Results.StatusCode(StatusCodes.Status410Gone);
            if (resolved.IsMissing || string.IsNullOrWhiteSpace(resolved.FilePath))
                return Results.NotFound();
            if (!resolved.IsValid)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var stream = File.OpenRead(resolved.FilePath);
            return Results.File(
                stream,
                contentType: "application/octet-stream",
                fileDownloadName: resolved.SafeFileName,
                enableRangeProcessing: false);
        });
    }
}
