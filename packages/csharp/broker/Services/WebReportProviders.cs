using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Services;
using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>Production <see cref="IWebContentProvider"/> backed by DuckDuckGo Lite search + HTTP fetch (no API key).</summary>
public sealed class WebSearchHelperContentProvider : IWebContentProvider
{
    public async Task<IReadOnlyList<WebReportSource>> SearchAsync(string topic, int maxSources, CancellationToken ct)
    {
        var url = $"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(topic)}";
        var html = await WebSearchHelper.SharedHttpClient.GetStringAsync(url, ct);
        var raw = WebSearchHelper.ParseDuckDuckGoLite(html, Math.Clamp(maxSources, 1, 10));

        var sources = new List<WebReportSource>();
        foreach (var item in raw)
        {
            var element = JsonSerializer.SerializeToElement(item);
            var hitUrl = element.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(hitUrl))
            {
                continue;
            }

            var title = element.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? string.Empty : string.Empty;
            var snippet = element.TryGetProperty("snippet", out var snippetNode) ? snippetNode.GetString() ?? string.Empty : string.Empty;
            sources.Add(new WebReportSource(title, hitUrl, snippet));
        }

        return sources;
    }

    public async Task<string> FetchAsync(string url, int maxLength, CancellationToken ct)
    {
        var html = await WebSearchHelper.SharedHttpClient.GetStringAsync(url, ct);
        var text = WebSearchHelper.HtmlToText(html);
        return WebSearchHelper.ShortenText(text, maxLength);
    }
}

/// <summary>Production <see cref="IWebReportLlm"/> that synthesizes via the broker LLM proxy; returns null when unavailable.</summary>
public sealed class LlmProxyReportLlm : IWebReportLlm
{
    private readonly ILlmProxyService _llm;
    private readonly ILogger<LlmProxyReportLlm> _logger;

    public LlmProxyReportLlm(ILlmProxyService llm, ILogger<LlmProxyReportLlm> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public async Task<string?> SynthesizeAsync(string prompt, CancellationToken ct)
    {
        if (!_llm.IsEnabled)
        {
            return null;
        }

        try
        {
            var spec = _llm.BuildRuntimeSpec();
            var body = JsonSerializer.SerializeToElement(new
            {
                model = spec.DefaultModel,
                messages = new[] { new { role = "user", content = prompt } },
                stream = false,
            });

            var result = await _llm.ChatAsync(body, null, ct);
            return string.IsNullOrWhiteSpace(result.Content) ? null : result.Content.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM report synthesis call failed");
            return null;
        }
    }
}
