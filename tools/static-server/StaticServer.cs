/**
 * 極簡靜態檔案伺服器
 *
 * 用法:
 *   dotnet run [目錄] [埠號]
 *   dotnet run ./frontend 3000
 *
 * 編譯為獨立執行檔:
 *   dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
 */

using System.Net;
using System.Text;

class StaticServer
{
    static readonly Dictionary<string, string> MimeTypes = new()
    {
        { ".html", "text/html; charset=utf-8" },
        { ".css", "text/css; charset=utf-8" },
        { ".js", "application/javascript; charset=utf-8" },
        { ".json", "application/json; charset=utf-8" },
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".gif", "image/gif" },
        { ".svg", "image/svg+xml" },
        { ".ico", "image/x-icon" },
        { ".woff", "font/woff" },
        { ".woff2", "font/woff2" },
        { ".ttf", "font/ttf" },
        { ".eot", "application/vnd.ms-fontobject" },
        { ".txt", "text/plain; charset=utf-8" },
        { ".xml", "application/xml; charset=utf-8" },
        { ".pdf", "application/pdf" },
        { ".zip", "application/zip" }
    };

    static string RootDir = ".";
    static int Port = 3000;

    static async Task Main(string[] args)
    {
        // 解析參數
        if (args.Length > 0) RootDir = args[0];
        if (args.Length > 1) int.TryParse(args[1], out Port);

        RootDir = Path.GetFullPath(RootDir);

        if (!Directory.Exists(RootDir))
        {
            Console.WriteLine($"錯誤: 目錄不存在 - {RootDir}");
            return;
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{Port}/");
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"無法啟動伺服器: {ex.Message}");
            Console.WriteLine("提示: 嘗試使用其他埠號，或以管理員身分執行");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════╗");
        Console.WriteLine("║       Static File Server (C#)          ║");
        Console.WriteLine("╚════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"  目錄: {RootDir}");
        Console.WriteLine($"  網址: http://localhost:{Port}");
        Console.WriteLine();
        Console.WriteLine("  按 Ctrl+C 停止伺服器");
        Console.WriteLine();

        // 處理 Ctrl+C
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\n正在停止伺服器...");
            listener.Stop();
        };

        // 主迴圈
        while (listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = HandleRequest(context);
            }
            catch (HttpListenerException)
            {
                break;
            }
        }
    }

    static async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        var urlPath = request.Url?.LocalPath ?? "/";
        var method = request.HttpMethod;

        // 記錄請求
        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        try
        {
            // CORS 標頭
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, HEAD, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            // OPTIONS 預檢請求
            if (method == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            // 只允許 GET 和 HEAD
            if (method != "GET" && method != "HEAD")
            {
                await SendError(response, 405, "Method Not Allowed");
                Console.WriteLine($"[{timestamp}] {method} {urlPath} -> 405");
                return;
            }

            // 解析檔案路徑
            var filePath = GetFilePath(urlPath);

            // 安全檢查：防止目錄遍歷
            if (!filePath.StartsWith(RootDir, StringComparison.OrdinalIgnoreCase))
            {
                await SendError(response, 403, "Forbidden");
                Console.WriteLine($"[{timestamp}] {method} {urlPath} -> 403");
                return;
            }

            // 檢查檔案是否存在
            if (!File.Exists(filePath))
            {
                // SPA fallback: 回傳 index.html
                var indexPath = Path.Combine(RootDir, "index.html");
                if (File.Exists(indexPath))
                {
                    filePath = indexPath;
                }
                else
                {
                    await SendError(response, 404, "Not Found");
                    Console.WriteLine($"[{timestamp}] {method} {urlPath} -> 404");
                    return;
                }
            }

            // 取得 MIME 類型
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var contentType = MimeTypes.GetValueOrDefault(ext, "application/octet-stream");

            // 設定回應標頭
            response.ContentType = contentType;
            response.StatusCode = 200;

            // 快取控制
            if (ext == ".html")
            {
                response.Headers.Add("Cache-Control", "no-cache");
            }
            else
            {
                response.Headers.Add("Cache-Control", "max-age=3600");
            }

            // 安全標頭
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.Add("X-Frame-Options", "SAMEORIGIN");

            // 讀取並傳送檔案
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            response.ContentLength64 = fileBytes.Length;

            if (method == "GET")
            {
                await response.OutputStream.WriteAsync(fileBytes);
            }

            response.Close();

            Console.WriteLine($"[{timestamp}] {method} {urlPath} -> 200 ({fileBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{timestamp}] {method} {urlPath} -> 500 ({ex.Message})");
            try
            {
                await SendError(response, 500, "Internal Server Error");
            }
            catch { }
        }
    }

    static string GetFilePath(string urlPath)
    {
        // 移除開頭的斜線
        var relativePath = urlPath.TrimStart('/');

        // 預設檔案
        if (string.IsNullOrEmpty(relativePath))
        {
            relativePath = "index.html";
        }

        // 如果是目錄，加上 index.html
        var fullPath = Path.Combine(RootDir, relativePath);
        if (Directory.Exists(fullPath))
        {
            fullPath = Path.Combine(fullPath, "index.html");
        }

        return Path.GetFullPath(fullPath);
    }

    static async Task SendError(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";

        var html = $@"<!DOCTYPE html>
<html>
<head><title>{statusCode} {message}</title></head>
<body style=""font-family: system-ui; text-align: center; padding: 50px;"">
<h1>{statusCode}</h1>
<p>{message}</p>
</body>
</html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
