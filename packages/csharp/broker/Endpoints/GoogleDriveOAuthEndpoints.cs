using System.Net;
using System.Text;
using Broker.Services;

namespace Broker.Endpoints;

public static class GoogleDriveOAuthEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var oauth = group.MapGroup("/google-drive/oauth");

        oauth.MapGet("/callback", async (HttpContext ctx, GoogleDriveOAuthService service, CancellationToken cancellationToken) =>
        {
            if (!IPAddress.IsLoopback(ctx.Connection.RemoteIpAddress ?? IPAddress.None))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var state = ctx.Request.Query["state"].ToString();
            var code = ctx.Request.Query["code"].ToString();
            var error = ctx.Request.Query["error"].ToString();

            GoogleDriveOAuthCallbackResult result;
            try
            {
                result = await service.CompleteAuthorizationAsync(state, code, error, cancellationToken);
            }
            catch (Exception ex)
            {
                result = new GoogleDriveOAuthCallbackResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }

            return Results.Content(BuildHtml(result), "text/html; charset=utf-8", Encoding.UTF8);
        });
    }

    private static string BuildHtml(GoogleDriveOAuthCallbackResult result)
    {
        var title = result.Success ? "Google Drive 授權完成" : "Google Drive 授權失敗";
        var heading = title;
        var detail = result.Success
            ? $"已完成 {Html(result.Channel)}/{Html(result.UserId)} 的 Google Drive 授權。Google 帳號：{Html(result.GoogleEmail)}。你可以關閉這個視窗。"
            : $"授權未完成：{Html(result.Message)}。你可以關閉這個視窗，或回到後台重新發起授權。";

        return $$"""
<!doctype html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{title}}</title>
  <style>
    :root { color-scheme: light; }
    body { font-family: "Noto Sans TC", "Microsoft JhengHei", sans-serif; margin: 0; background: #f5f7fb; color: #132238; }
    main { max-width: 640px; margin: 10vh auto; padding: 32px; background: #fff; border-radius: 16px; box-shadow: 0 18px 50px rgba(19,34,56,.12); }
    h1 { margin-top: 0; font-size: 28px; }
    p { line-height: 1.7; white-space: pre-wrap; }
    code { background: #eef3fb; padding: 2px 6px; border-radius: 6px; }
  </style>
</head>
<body>
  <main>
    <h1>{{heading}}</h1>
    <p>{{detail}}</p>
  </main>
</body>
</html>
""";
    }

    private static string Html(string? value)
        => WebUtility.HtmlEncode(value ?? string.Empty);
}
