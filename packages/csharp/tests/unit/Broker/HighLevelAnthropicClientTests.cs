using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Broker.Services;

namespace Unit.Tests.Broker;

public class HighLevelAnthropicClientTests
{
    [Fact]
    public async Task SendAsync_UsesAnthropicMessagesFormat()
    {
        var handler = new CaptureHandler(_ => """
        {
          "id": "msg_high_level",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-6",
          "content": [{ "type": "text", "text": "Claude high-level reply" }],
          "usage": { "output_tokens": 9 }
        }
        """);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.anthropic.test/")
        };
        var options = new HighLevelLlmOptions
        {
            Provider = "claude",
            ApiKey = "test-anthropic-key",
            DefaultModel = "claude-sonnet-4-6",
        };
        client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var reply = await HighLevelAnthropicClient.SendAsync(client, options, new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = "Answer briefly." },
            new JsonObject { ["role"] = "user", ["content"] = "Hello" }
        }, CancellationToken.None);

        reply.Should().Be("Claude high-level reply");
        handler.Request!.RequestUri!.PathAndQuery.Should().Be("/v1/messages");
        using var sent = JsonDocument.Parse(handler.RequestBody);
        sent.RootElement.GetProperty("model").GetString().Should().Be("claude-sonnet-4-6");
        sent.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(4096);
        sent.RootElement.GetProperty("system").GetString().Should().Be("Answer briefly.");
        sent.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be("Hello");
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
