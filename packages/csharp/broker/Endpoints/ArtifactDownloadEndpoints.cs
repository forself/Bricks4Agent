using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// Broker 簽章 artifact 下載 API(2026-05-29, AnthonyLee — 執行 Benson 2026-03-29 plan 的新檔部分)。
///
/// GET  /api/v1/artifacts/dl?id=&amp;exp=&amp;sig=     — 公開、簽章把關、串檔下載(無 admin session)
/// POST /api/v1/artifacts/{id}/download-link        — admin-authed、產生簽章連結
///
/// 補 docs/reports/CurrentArchitectureAndProgress §9「無終端下載 API」缺口。
/// Reuse Benson 的 artifact records(ReadArtifactById)不改其碼。
/// </summary>
public static class ArtifactDownloadEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var g = group.MapGroup("/artifacts");

        // 公開下載:簽章 = 唯一把關(可匿名);失敗一律不洩漏路徑/原因
        g.MapGet("/dl", (BrokerArtifactDownloadService svc, string? id, long exp, string? sig) =>
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sig))
                return Results.BadRequest(ApiResponseHelper.Error("missing id/sig"));
            if (!svc.ValidateSignature(id, exp, sig))
                return Results.StatusCode(403);
            var r = svc.ResolveFile(id);
            if (!r.Ok)
                return Results.NotFound(ApiResponseHelper.Error("artifact unavailable"));
            return Results.File(r.FullPath, r.ContentType, r.FileName);
        });

        // 產生簽章下載連結:admin only
        g.MapPost("/{id}/download-link", (HttpContext ctx, string id, LocalAdminAuthService auth, BrokerArtifactDownloadService svc) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var r = svc.ResolveFile(id);
            if (!r.Ok)
                return Results.NotFound(ApiResponseHelper.Error("artifact not found or has no local file"));
            return Results.Ok(ApiResponseHelper.Success(new { url = svc.CreateSignedUrl(id), file_name = r.FileName }));
        });
    }
}
