using System.Text.Json;
using System.Text.Json.Nodes;

namespace BrokerCore.Services;

public static class AnthropicMessagesAdapter
{
    public const string ApiVersion = "2023-06-01";
    public const int DefaultMaxOutputTokens = 4096;

    public static bool IsAnthropicProvider(string? provider)
        => string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase)
           || string.Equals(provider, "claude", StringComparison.OrdinalIgnoreCase);

    public static void ConfigureHeaders(HttpClient client, string? apiKey)
    {
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Remove("x-api-key");
        client.DefaultRequestHeaders.Remove("anthropic-version");
        client.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("x-api-key", apiKey.Trim());
        }
    }

    public static JsonObject BuildRequestFromChatBody(
        JsonElement body,
        string model,
        bool supportsToolCalling,
        int maxOutputTokens)
    {
        var convertedMessages = body.TryGetProperty("messages", out var messageElement)
            ? ConvertMessages(messageElement)
            : (string.Empty, new JsonArray());

        var request = BuildRequest(model, maxOutputTokens, convertedMessages.Item1, convertedMessages.Item2);

        if (supportsToolCalling && body.TryGetProperty("tools", out var toolsElement))
        {
            var tools = ConvertTools(toolsElement);
            if (tools.Count > 0)
            {
                request["tools"] = tools;
            }
        }

        return request;
    }

    public static JsonObject BuildRequestFromMessages(JsonArray messages, string model, int maxOutputTokens)
    {
        var converted = ConvertMessages(messages);
        return BuildRequest(model, maxOutputTokens, converted.System, converted.Messages);
    }

    public static string ExtractText(JsonNode root)
    {
        var content = root["content"] as JsonArray ?? [];
        return string.Concat(content
            .Where(part => string.Equals(part?["type"]?.GetValue<string>(), "text", StringComparison.OrdinalIgnoreCase))
            .Select(part => part?["text"]?.GetValue<string>() ?? string.Empty));
    }

    public static JsonArray ExtractToolCalls(JsonNode root)
    {
        var calls = new JsonArray();
        var content = root["content"] as JsonArray ?? [];

        foreach (var part in content)
        {
            if (!string.Equals(part?["type"]?.GetValue<string>(), "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            calls.Add(new JsonObject
            {
                ["id"] = part?["id"]?.GetValue<string>() ?? string.Empty,
                ["function"] = new JsonObject
                {
                    ["name"] = part?["name"]?.GetValue<string>() ?? string.Empty,
                    ["arguments"] = part?["input"]?.DeepClone() ?? new JsonObject(),
                },
            });
        }

        return calls;
    }

    private static JsonObject BuildRequest(string model, int maxOutputTokens, string system, JsonArray messages)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxOutputTokens > 0 ? maxOutputTokens : DefaultMaxOutputTokens,
            ["messages"] = messages,
        };

        if (!string.IsNullOrWhiteSpace(system))
        {
            request["system"] = system;
        }

        return request;
    }

    private static (string System, JsonArray Messages) ConvertMessages(JsonElement messagesElement)
    {
        var messages = new JsonArray();
        var systemParts = new List<string>();

        if (messagesElement.ValueKind != JsonValueKind.Array)
        {
            return (string.Empty, messages);
        }

        foreach (var message in messagesElement.EnumerateArray())
        {
            AddConvertedMessage(
                messages,
                systemParts,
                message.TryGetProperty("role", out var roleProp) ? roleProp.GetString() : null,
                message.TryGetProperty("content", out var contentProp) ? contentProp : default,
                message.TryGetProperty("tool_calls", out var toolCallsProp) ? toolCallsProp : default,
                message.TryGetProperty("tool_call_id", out var toolCallIdProp) ? toolCallIdProp.GetString() : null);
        }

        return (string.Join("\n\n", systemParts.Where(part => !string.IsNullOrWhiteSpace(part))), messages);
    }

    private static (string System, JsonArray Messages) ConvertMessages(JsonArray sourceMessages)
    {
        var messages = new JsonArray();
        var systemParts = new List<string>();

        foreach (var item in sourceMessages)
        {
            if (item is not JsonObject obj)
            {
                continue;
            }

            AddConvertedMessage(
                messages,
                systemParts,
                obj["role"]?.GetValue<string>(),
                obj["content"],
                obj["tool_calls"] as JsonArray,
                obj["tool_call_id"]?.GetValue<string>());
        }

        return (string.Join("\n\n", systemParts.Where(part => !string.IsNullOrWhiteSpace(part))), messages);
    }

    private static void AddConvertedMessage(
        JsonArray messages,
        List<string> systemParts,
        string? role,
        JsonElement content,
        JsonElement toolCalls,
        string? toolCallId)
    {
        AddConvertedMessage(
            messages,
            systemParts,
            role,
            content.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(content.GetRawText()),
            toolCalls.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(toolCalls.GetRawText()) as JsonArray,
            toolCallId);
    }

    private static void AddConvertedMessage(
        JsonArray messages,
        List<string> systemParts,
        string? role,
        JsonNode? content,
        JsonArray? toolCalls,
        string? toolCallId)
    {
        var normalizedRole = string.IsNullOrWhiteSpace(role)
            ? "user"
            : role.Trim().ToLowerInvariant();

        if (normalizedRole == "system")
        {
            var system = ExtractContentText(content);
            if (!string.IsNullOrWhiteSpace(system))
            {
                systemParts.Add(system);
            }
            return;
        }

        if (normalizedRole == "tool")
        {
            messages.Add(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolCallId ?? string.Empty,
                        ["content"] = ExtractContentText(content),
                    }
                }
            });
            return;
        }

        var anthropicRole = normalizedRole == "assistant" ? "assistant" : "user";
        var text = ExtractContentText(content);
        var contentBlocks = new JsonArray();

        if (!string.IsNullOrWhiteSpace(text))
        {
            contentBlocks.Add(new JsonObject
            {
                ["type"] = "text",
                ["text"] = text,
            });
        }

        if (anthropicRole == "assistant" && toolCalls != null)
        {
            foreach (var toolCall in toolCalls)
            {
                var function = toolCall?["function"] as JsonObject;
                contentBlocks.Add(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = toolCall?["id"]?.GetValue<string>() ?? string.Empty,
                    ["name"] = function?["name"]?.GetValue<string>() ?? string.Empty,
                    ["input"] = ParseArguments(function?["arguments"]),
                });
            }
        }

        messages.Add(new JsonObject
        {
            ["role"] = anthropicRole,
            ["content"] = contentBlocks.Count == 1 &&
                         string.Equals(contentBlocks[0]?["type"]?.GetValue<string>(), "text", StringComparison.OrdinalIgnoreCase)
                ? contentBlocks[0]?["text"]?.GetValue<string>() ?? string.Empty
                : contentBlocks,
        });
    }

    private static JsonArray ConvertTools(JsonElement toolsElement)
    {
        var converted = new JsonArray();
        if (toolsElement.ValueKind != JsonValueKind.Array)
        {
            return converted;
        }

        foreach (var tool in toolsElement.EnumerateArray())
        {
            var function = tool.TryGetProperty("function", out var fn) ? fn : tool;
            converted.Add(new JsonObject
            {
                ["name"] = function.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString()
                    : string.Empty,
                ["description"] = function.TryGetProperty("description", out var descProp)
                    ? descProp.GetString()
                    : string.Empty,
                ["input_schema"] = function.TryGetProperty("parameters", out var paramsProp)
                    ? JsonNode.Parse(paramsProp.GetRawText())
                    : new JsonObject(),
            });
        }

        return converted;
    }

    private static string ExtractContentText(JsonNode? content)
    {
        if (content == null)
        {
            return string.Empty;
        }

        if (content is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (content is JsonArray array)
        {
            return string.Concat(array.Select(part =>
                part?["text"]?.GetValue<string>()
                ?? part?["content"]?.GetValue<string>()
                ?? string.Empty));
        }

        return content.ToJsonString();
    }

    private static JsonNode ParseArguments(JsonNode? arguments)
    {
        if (arguments == null)
        {
            return new JsonObject();
        }

        if (arguments is JsonValue value && value.TryGetValue<string>(out var text))
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new JsonObject();
            }

            return JsonNode.Parse(text) ?? new JsonObject();
        }

        return arguments.DeepClone();
    }
}
