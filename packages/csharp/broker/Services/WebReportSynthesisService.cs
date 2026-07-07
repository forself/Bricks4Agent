using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// End-to-end web→report pipeline: search the web for a topic, fetch the top sources,
/// synthesize a Markdown report with the LLM (grounded in the fetched content), and
/// append source citations. Robust to search/fetch/LLM failures — it always returns a report.
/// </summary>
public sealed class WebReportSynthesisService
{
    private readonly IWebContentProvider _content;
    private readonly IWebReportLlm _llm;
    private readonly ILogger<WebReportSynthesisService> _logger;
    private readonly LineArtifactDeliveryService? _delivery;

    public WebReportSynthesisService(
        IWebContentProvider content,
        IWebReportLlm llm,
        ILogger<WebReportSynthesisService> logger,
        LineArtifactDeliveryService? delivery = null)
    {
        _content = content;
        _llm = llm;
        _logger = logger;
        _delivery = delivery;
    }

    public async Task<WebReportResult> GenerateReportAsync(
        string topic,
        int maxSources = 5,
        int fetchLength = 4000,
        CancellationToken ct = default)
    {
        topic = (topic ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(topic))
        {
            return new WebReportResult
            {
                Topic = topic,
                Markdown = "# 研究報告\n\n請提供查詢主題。\n",
                Sources = [],
                LlmSucceeded = false,
                FileName = "report.md",
            };
        }

        IReadOnlyList<WebReportSource> hits;
        try
        {
            hits = await _content.SearchAsync(topic, Math.Clamp(maxSources, 1, 10), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web report search failed for topic {Topic}", topic);
            hits = [];
        }

        if (hits.Count == 0)
        {
            return new WebReportResult
            {
                Topic = topic,
                Markdown = $"# {topic} 研究報告\n\n找不到相關的網路來源,請改用更明確的關鍵字後重試。\n",
                Sources = [],
                LlmSucceeded = false,
                FileName = BuildFileName(topic),
            };
        }

        var sources = new List<WebReportSource>(hits.Count);
        foreach (var hit in hits)
        {
            var content = string.Empty;
            try
            {
                content = await _content.FetchAsync(hit.Url, fetchLength, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Web report fetch failed for {Url}", hit.Url);
            }

            sources.Add(hit with { Content = content });
        }

        string? body = null;
        try
        {
            var prompt = WebReportComposer.BuildSynthesisPrompt(topic, sources);
            body = await _llm.SynthesizeAsync(prompt, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web report LLM synthesis failed for topic {Topic}", topic);
        }

        var markdown = WebReportComposer.Compose(topic, body, sources);
        return new WebReportResult
        {
            Topic = topic,
            Markdown = markdown,
            Sources = sources,
            LlmSucceeded = !string.IsNullOrWhiteSpace(body),
            FileName = BuildFileName(topic),
        };
    }

    /// <summary>Generates the report then delivers it as a Markdown artifact (writes file + queues LINE notification).</summary>
    public async Task<(WebReportResult Report, LineArtifactDeliveryResult? Delivery)> GenerateAndDeliverAsync(
        string userId,
        string topic,
        int maxSources = 5,
        CancellationToken ct = default)
    {
        var report = await GenerateReportAsync(topic, maxSources, ct: ct);
        if (_delivery is null)
        {
            return (report, null);
        }

        var delivery = await _delivery.GenerateAndDeliverAsync(new LineArtifactDeliveryRequest
        {
            UserId = userId,
            FileName = report.FileName,
            Format = "md",
            Content = report.Markdown,
            UploadToGoogleDrive = false,
            SendLineNotification = true,
            NotificationTitle = $"{topic} 研究報告",
            Source = "web_report",
            RelatedTaskType = "web_report",
        }, ct);

        return (report, delivery);
    }

    private static string BuildFileName(string topic)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var slug = new string(topic.Where(c => !char.IsControl(c) && Array.IndexOf(invalid, c) < 0).ToArray()).Trim();
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim();
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "report";
        }

        return $"{slug}-report.md";
    }
}
