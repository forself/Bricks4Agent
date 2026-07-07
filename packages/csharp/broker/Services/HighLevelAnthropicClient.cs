using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Services;

namespace Broker.Services;

public static class HighLevelAnthropicClient
{
    public static bool IsProvider(string? provider)
        => AnthropicMessagesAdapter.IsAnthropicProvider(provider);

    public static async Task<string?> SendAsync(
        HttpClient httpClient,
        HighLevelLlmOptions options,
        JsonArray messages,
        CancellationToken cancellationToken)
    {
        var request = AnthropicMessagesAdapter.BuildRequestFromMessages(
            messages,
            options.DefaultModel,
            options.MaxOutputTokens);

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync("v1/messages", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseJson);
        var root = JsonNode.Parse(doc.RootElement.GetRawText()) ?? new JsonObject();
        var text = AnthropicMessagesAdapter.ExtractText(root);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static JsonArray UserPrompt(string prompt)
    {
        return new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = prompt,
            }
        };
    }
}
