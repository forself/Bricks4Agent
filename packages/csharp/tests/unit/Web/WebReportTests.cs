using Broker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Web;

public class WebReportComposerTests
{
    private static readonly WebReportSource[] Sources =
    [
        new("台鐵時刻表改點", "https://example.com/a", "snippet A", "來源 A 的完整內容,談到改點時間。"),
        new("高鐵增班", "https://example.com/b", "snippet B", "來源 B 的完整內容,談到增班。"),
    ];

    [Fact]
    public void BuildSynthesisPrompt_includes_topic_sources_and_citation_instruction()
    {
        var prompt = WebReportComposer.BuildSynthesisPrompt("台灣鐵路改點", Sources);

        prompt.Should().Contain("台灣鐵路改點");
        prompt.Should().Contain("https://example.com/a");
        prompt.Should().Contain("https://example.com/b");
        prompt.Should().Contain("來源 A 的完整內容");
        prompt.Should().Contain("來源 B 的完整內容");
        prompt.Should().Contain("[1]");
        prompt.Should().Contain("[2]");
    }

    [Fact]
    public void Compose_with_llm_body_includes_title_body_and_numbered_citations()
    {
        var report = WebReportComposer.Compose("台灣鐵路改點", "## 摘要\n台鐵與高鐵均有調整 [1][2]。", Sources);

        report.Should().StartWith("# 台灣鐵路改點 研究報告");
        report.Should().Contain("台鐵與高鐵均有調整 [1][2]。");
        report.Should().Contain("## 參考來源");
        report.Should().Contain("1. [台鐵時刻表改點](https://example.com/a)");
        report.Should().Contain("2. [高鐵增班](https://example.com/b)");
    }

    [Fact]
    public void Compose_without_llm_body_uses_source_summary_fallback_but_still_cites()
    {
        var report = WebReportComposer.Compose("台灣鐵路改點", null, Sources);

        report.Should().Contain("未取得 LLM 綜合結果");
        report.Should().Contain("## 來源摘要");
        report.Should().Contain("來源 A 的完整內容");
        report.Should().Contain("## 參考來源");
        report.Should().Contain("1. [台鐵時刻表改點](https://example.com/a)");
    }

    [Fact]
    public void Compose_with_empty_sources_marks_no_references()
    {
        var report = WebReportComposer.Compose("某主題", "內容", []);

        report.Should().Contain("## 參考來源");
        report.Should().Contain("(無)");
    }

    [Fact]
    public void Title_falls_back_to_url_when_missing()
    {
        var report = WebReportComposer.Compose("t", "body", [new("", "https://example.com/x", "s", "c")]);

        report.Should().Contain("1. [https://example.com/x](https://example.com/x)");
    }
}

public class WebReportSynthesisServiceTests
{
    private sealed class FakeContentProvider : IWebContentProvider
    {
        public List<WebReportSource> Hits { get; init; } = new();
        public Func<string, string>? Fetch { get; init; }
        public bool ThrowOnFetch { get; init; }

        public Task<IReadOnlyList<WebReportSource>> SearchAsync(string topic, int maxSources, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<WebReportSource>>(Hits);

        public Task<string> FetchAsync(string url, int maxLength, CancellationToken ct)
        {
            if (ThrowOnFetch)
            {
                throw new HttpRequestException("boom");
            }

            return Task.FromResult(Fetch?.Invoke(url) ?? $"content::{url}");
        }
    }

    private sealed class FakeLlm : IWebReportLlm
    {
        public string? Body { get; init; }
        public string? LastPrompt { get; private set; }

        public Task<string?> SynthesizeAsync(string prompt, CancellationToken ct)
        {
            LastPrompt = prompt;
            return Task.FromResult(Body);
        }
    }

    private static WebReportSynthesisService Build(IWebContentProvider content, IWebReportLlm llm)
        => new(content, llm, NullLogger<WebReportSynthesisService>.Instance);

    [Fact]
    public async Task GenerateReportAsync_synthesizes_report_with_all_sources()
    {
        var content = new FakeContentProvider
        {
            Hits =
            {
                new("A", "https://example.com/a", "sa"),
                new("B", "https://example.com/b", "sb"),
                new("C", "https://example.com/c", "sc"),
            },
        };
        var llm = new FakeLlm { Body = "## 結論\n綜合結果 [1][2][3]。" };
        var service = Build(content, llm);

        var result = await service.GenerateReportAsync("交通新聞");

        result.LlmSucceeded.Should().BeTrue();
        result.Sources.Should().HaveCount(3);
        result.Sources[0].Content.Should().Be("content::https://example.com/a");
        result.Markdown.Should().Contain("綜合結果 [1][2][3]。");
        result.Markdown.Should().Contain("https://example.com/a");
        result.Markdown.Should().Contain("https://example.com/b");
        result.Markdown.Should().Contain("https://example.com/c");
        result.FileName.Should().Be("交通新聞-report.md");
        llm.LastPrompt.Should().Contain("content::https://example.com/a");
    }

    [Fact]
    public async Task GenerateReportAsync_falls_back_when_llm_returns_null()
    {
        var content = new FakeContentProvider
        {
            Hits = { new("A", "https://example.com/a", "sa") },
        };
        var service = Build(content, new FakeLlm { Body = null });

        var result = await service.GenerateReportAsync("主題");

        result.LlmSucceeded.Should().BeFalse();
        result.Markdown.Should().Contain("未取得 LLM 綜合結果");
        result.Markdown.Should().Contain("https://example.com/a");
    }

    [Fact]
    public async Task GenerateReportAsync_returns_helpful_message_when_no_sources()
    {
        var service = Build(new FakeContentProvider(), new FakeLlm { Body = "x" });

        var result = await service.GenerateReportAsync("冷門主題");

        result.Sources.Should().BeEmpty();
        result.LlmSucceeded.Should().BeFalse();
        result.Markdown.Should().Contain("找不到相關的網路來源");
    }

    [Fact]
    public async Task GenerateReportAsync_rejects_empty_topic()
    {
        var service = Build(new FakeContentProvider(), new FakeLlm { Body = "x" });

        var result = await service.GenerateReportAsync("   ");

        result.Markdown.Should().Contain("請提供查詢主題");
    }

    [Fact]
    public async Task GenerateReportAsync_survives_fetch_failures()
    {
        var content = new FakeContentProvider
        {
            Hits = { new("A", "https://example.com/a", "sa") },
            ThrowOnFetch = true,
        };
        var service = Build(content, new FakeLlm { Body = "本文 [1]。" });

        var result = await service.GenerateReportAsync("主題");

        result.Sources.Should().HaveCount(1);
        result.Sources[0].Content.Should().BeEmpty();
        result.Markdown.Should().Contain("本文 [1]。");
        result.Markdown.Should().Contain("https://example.com/a");
    }
}
