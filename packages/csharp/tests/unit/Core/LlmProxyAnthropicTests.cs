using System.Net;
using System.Text;
using System.Text.Json;
using BrokerCore.Services;

namespace Unit.Tests.Core;

public class LlmProxyAnthropicTests
{
    [Fact]
    public async Task ChatAsync_SendsOllamaThinkFalse()
    {
        var handler = new CaptureHandler(_ => """
        {
          "model": "qwen3.6:latest",
          "message": { "role": "assistant", "content": "pong" },
          "done": true,
          "total_duration": 123,
          "eval_count": 1
        }
        """);
        var service = CreateOllamaService(handler);

        using var body = JsonDocument.Parse("""
        {
          "messages": [
            { "role": "user", "content": "ping" }
          ]
        }
        """);

        var result = await service.ChatAsync(body.RootElement);

        result.Content.Should().Be("pong");
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/api/chat");

        using var sent = JsonDocument.Parse(handler.RequestBody);
        sent.RootElement.GetProperty("model").GetString().Should().Be("qwen3.6:latest");
        sent.RootElement.GetProperty("stream").GetBoolean().Should().BeFalse();
        sent.RootElement.GetProperty("think").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ChatAsync_SendsAnthropicMessagesRequest()
    {
        var handler = new CaptureHandler(_ => """
        {
          "id": "msg_test",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-6",
          "content": [{ "type": "text", "text": "Claude says hello" }],
          "usage": { "output_tokens": 7 }
        }
        """);
        var service = CreateService(handler);

        using var body = JsonDocument.Parse("""
        {
          "messages": [
            { "role": "system", "content": "Answer briefly." },
            { "role": "user", "content": "Hello" }
          ]
        }
        """);

        var result = await service.ChatAsync(body.RootElement);

        result.Content.Should().Be("Claude says hello");
        result.Model.Should().Be("claude-sonnet-4-6");
        result.EvalCount.Should().Be(7);
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.PathAndQuery.Should().Be("/v1/messages");
        handler.Request.Headers.GetValues("x-api-key").Single().Should().Be("test-anthropic-key");
        handler.Request.Headers.GetValues("anthropic-version").Single().Should().Be("2023-06-01");
        handler.Request.Headers.Authorization.Should().BeNull();

        using var sent = JsonDocument.Parse(handler.RequestBody);
        sent.RootElement.GetProperty("model").GetString().Should().Be("claude-sonnet-4-6");
        sent.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(4096);
        sent.RootElement.GetProperty("system").GetString().Should().Be("Answer briefly.");
        var messages = sent.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(1);
        messages[0].GetProperty("role").GetString().Should().Be("user");
        messages[0].GetProperty("content").GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task ChatAsync_ConvertsAnthropicToolUseToBrokerToolCalls()
    {
        var handler = new CaptureHandler(_ => """
        {
          "id": "msg_test",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-6",
          "content": [
            { "type": "text", "text": "I will read it." },
            {
              "type": "tool_use",
              "id": "toolu_1",
              "name": "read_file",
              "input": { "path": "README.md" }
            }
          ],
          "usage": { "output_tokens": 11 }
        }
        """);
        var service = CreateService(handler);

        using var body = JsonDocument.Parse("""
        {
          "messages": [
            { "role": "user", "content": "Read the README" }
          ],
          "tools": [
            {
              "type": "function",
              "function": {
                "name": "read_file",
                "description": "Read a file",
                "parameters": {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" }
                  },
                  "required": ["path"]
                }
              }
            }
          ]
        }
        """);

        var result = await service.ChatAsync(body.RootElement);

        result.Content.Should().Be("I will read it.");
        result.ToolCalls.Should().HaveCount(1);
        result.ToolCalls[0]!["id"]!.GetValue<string>().Should().Be("toolu_1");
        result.ToolCalls[0]!["function"]!["name"]!.GetValue<string>().Should().Be("read_file");
        result.ToolCalls[0]!["function"]!["arguments"]!["path"]!.GetValue<string>().Should().Be("README.md");

        using var sent = JsonDocument.Parse(handler.RequestBody);
        var tools = sent.RootElement.GetProperty("tools");
        tools.GetArrayLength().Should().Be(1);
        tools[0].GetProperty("name").GetString().Should().Be("read_file");
        tools[0].GetProperty("input_schema").GetProperty("required")[0].GetString().Should().Be("path");
    }

    private static LlmProxyService CreateService(CaptureHandler handler)
    {
        return new LlmProxyService(new HttpClient(handler), new LlmProxyOptions
        {
            Enabled = true,
            Provider = "anthropic",
            BaseUrl = "https://api.anthropic.test",
            ApiKey = "test-anthropic-key",
            DefaultModel = "claude-sonnet-4-6",
            SupportsToolCalling = true,
        });
    }

    private static LlmProxyService CreateOllamaService(CaptureHandler handler)
    {
        return new LlmProxyService(new HttpClient(handler), new LlmProxyOptions
        {
            Enabled = true,
            Provider = "ollama",
            BaseUrl = "http://localhost:11434",
            DefaultModel = "qwen3.6:latest",
            SupportsToolCalling = true,
        });
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _responseFactory;

        public CaptureHandler(Func<HttpRequestMessage, string> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseFactory(request), Encoding.UTF8, "application/json")
            };
        }
    }
}
