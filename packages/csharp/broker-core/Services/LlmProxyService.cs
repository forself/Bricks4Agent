using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BrokerCore.Models;

namespace BrokerCore.Services;

public class LlmProxyService : ILlmProxyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly LlmProxyOptions _options;

    public LlmProxyService(HttpClient httpClient, LlmProxyOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        _httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public bool IsEnabled => _options.Enabled;

    public AgentRuntimeSpec BuildRuntimeSpec(BrokerTask? task = null, IEnumerable<string>? capabilityIds = null)
    {
        var runtime = ResolveRuntime(task);
        return new AgentRuntimeSpec
        {
            Provider = _options.Provider,
            ApiFormat = _options.ApiFormat,
            DefaultModel = runtime.DefaultModel,
            AllowModelOverride = runtime.AllowModelOverride,
            SupportsToolCalling = runtime.SupportsToolCalling,
            StreamingEnabled = runtime.StreamingEnabled,
            Source = runtime.Source,
            TaskId = task?.TaskId ?? string.Empty,
            TaskType = task?.TaskType ?? string.Empty,
            AssignedRoleId = task?.AssignedRoleId ?? string.Empty,
            ScopeDescriptor = task?.ScopeDescriptor ?? "{}",
            CapabilityIds = capabilityIds?
                .Where(capabilityId => !string.IsNullOrWhiteSpace(capabilityId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? Array.Empty<string>(),
        };
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        if (IsOllama())
        {
            using var response = await _httpClient.GetAsync("", cancellationToken);
            return response.IsSuccessStatusCode;
        }

        using var modelsResponse = await _httpClient.GetAsync("v1/models", cancellationToken);
        return modelsResponse.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        if (IsOllama())
        {
            var root = await SendJsonAsync(HttpMethod.Get, "api/tags", null, cancellationToken);
            return ParseOllamaModels(root);
        }

        var modelsRoot = await SendJsonAsync(HttpMethod.Get, "v1/models", null, cancellationToken);
        return ParseOpenAiModels(modelsRoot);
    }

    public async Task<LlmChatResult> ChatAsync(JsonElement body, BrokerTask? task = null, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var runtime = ResolveRuntime(task);

        var resolvedModel = ResolveModel(body.TryGetProperty("model", out var modelProp)
            ? modelProp.GetString()
            : null, runtime);

        if (IsOllama())
        {
            return await SendOllamaChatAsync(body, resolvedModel, runtime, cancellationToken);
        }

        return string.Equals(_options.ApiFormat, "responses", StringComparison.OrdinalIgnoreCase)
            ? await SendResponsesChatAsync(body, resolvedModel, runtime, cancellationToken)
            : await SendChatCompletionsAsync(body, resolvedModel, runtime, cancellationToken);
    }

    private async Task<LlmChatResult> SendOllamaChatAsync(
        JsonElement body,
        string model,
        ResolvedRuntimeOptions runtime,
        CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
        };

        if (body.TryGetProperty("messages", out var messages))
        {
            request["messages"] = JsonNode.Parse(messages.GetRawText());
        }

        if (runtime.SupportsToolCalling && body.TryGetProperty("tools", out var tools))
        {
            request["tools"] = JsonNode.Parse(tools.GetRawText());
        }

        var root = await SendJsonAsync(HttpMethod.Post, "api/chat", request, cancellationToken);
        var message = root["message"] as JsonObject;

        return new LlmChatResult
        {
            Content = message?["content"]?.GetValue<string>() ?? string.Empty,
            ToolCalls = CloneArray(message?["tool_calls"] as JsonArray),
            Thinking = message?["thinking"]?.GetValue<string>() ?? string.Empty,
            Done = true,
            Model = root["model"]?.GetValue<string>() ?? model,
            TotalDuration = root["total_duration"]?.GetValue<long>() ?? 0,
            EvalCount = root["eval_count"]?.GetValue<int>() ?? 0,
        };
    }

    private async Task<LlmChatResult> SendChatCompletionsAsync(
        JsonElement body,
        string model,
        ResolvedRuntimeOptions runtime,
        CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
        };

        if (body.TryGetProperty("messages", out var messages))
        {
            request["messages"] = JsonNode.Parse(messages.GetRawText());
        }

        if (runtime.SupportsToolCalling && body.TryGetProperty("tools", out var tools))
        {
            request["tools"] = JsonNode.Parse(tools.GetRawText());
        }

        var root = await SendJsonAsync(HttpMethod.Post, "v1/chat/completions", request, cancellationToken);
        var message = root["choices"]?[0]?["message"] as JsonObject;

        return new LlmChatResult
        {
            Content = ExtractTextContent(message?["content"]),
            ToolCalls = NormalizeToolCalls(message?["tool_calls"] as JsonArray),
            Thinking = string.Empty,
            Done = true,
            Model = root["model"]?.GetValue<string>() ?? model,
            TotalDuration = 0,
            EvalCount = root["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0,
        };
    }

    private async Task<LlmChatResult> SendResponsesChatAsync(
        JsonElement body,
        string model,
        ResolvedRuntimeOptions runtime,
        CancellationToken cancellationToken)
    {
        var request = new JsonObject
        {
            ["model"] = model,
            ["stream"] = false,
        };

        var (instructions, input) = ConvertToResponsesInput(body);
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            request["instructions"] = instructions;
        }
        request["input"] = input;

        if (runtime.SupportsToolCalling && body.TryGetProperty("tools", out var tools))
        {
            request["tools"] = ConvertToolsForResponses(tools);
        }

        var root = await SendJsonAsync(HttpMethod.Post, "v1/responses", request, cancellationToken);
        return ParseResponsesResult(root, model);
    }

    private async Task<JsonNode> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        JsonNode? payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (payload != null)
        {
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"LLM upstream returned HTTP {(int)response.StatusCode}: {text}");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(text) ?? new JsonObject();
    }

    private IReadOnlyList<LlmModelInfo> ParseOllamaModels(JsonNode root)
    {
        var models = root["models"] as JsonArray ?? [];
        return models
            .Select(item => new LlmModelInfo
            {
                Name = item?["name"]?.GetValue<string>() ?? string.Empty,
                Size = item?["size"]?.GetValue<long?>(),
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Name))
            .ToList();
    }

    private IReadOnlyList<LlmModelInfo> ParseOpenAiModels(JsonNode root)
    {
        var models = root["data"] as JsonArray ?? root["models"] as JsonArray ?? [];
        return models
            .Select(item => new LlmModelInfo
            {
                Name = item?["id"]?.GetValue<string>()
                    ?? item?["name"]?.GetValue<string>()
                    ?? string.Empty,
                Size = null,
            })
            .Where(model => !string.IsNullOrWhiteSpace(model.Name))
            .ToList();
    }

    private (string Instructions, JsonArray Input) ConvertToResponsesInput(JsonElement body)
    {
        var input = new JsonArray();
        var instructions = string.Empty;

        if (!body.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return (instructions, input);
        }

        foreach (var message in messages.EnumerateArray())
        {
            var role = message.TryGetProperty("role", out var roleProp)
                ? roleProp.GetString()
                : string.Empty;
            var content = message.TryGetProperty("content", out var contentProp)
                ? contentProp
                : default;

            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            {
                instructions = content.ValueKind == JsonValueKind.String
                    ? content.GetString() ?? string.Empty
                    : ExtractTextContent(JsonNode.Parse(content.GetRawText()));
                continue;
            }

            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                input.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = content.ValueKind == JsonValueKind.Undefined
                        ? string.Empty
                        : JsonNode.Parse(content.GetRawText()),
                });
                continue;
            }

            if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                if (content.ValueKind != JsonValueKind.Undefined &&
                    !(content.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(content.GetString())))
                {
                    input.Add(new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["type"] = "output_text",
                                ["text"] = content.ValueKind == JsonValueKind.String
                                    ? content.GetString() ?? string.Empty
                                    : ExtractTextContent(JsonNode.Parse(content.GetRawText())),
                            }
                        },
                    });
                }

                if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        var function = toolCall.TryGetProperty("function", out var fn) ? fn : default;
                        var arguments = function.ValueKind != JsonValueKind.Undefined &&
                                        function.TryGetProperty("arguments", out var args)
                            ? args
                            : default;

                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = toolCall.TryGetProperty("id", out var idProp)
                                ? idProp.GetString()
                                : $"call_{Guid.NewGuid():N}",
                            ["name"] = function.ValueKind != JsonValueKind.Undefined &&
                                        function.TryGetProperty("name", out var nameProp)
                                ? nameProp.GetString()
                                : string.Empty,
                            ["arguments"] = SerializeArguments(arguments),
                            ["status"] = "completed",
                        });
                    }
                }
                continue;
            }

            if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                input.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message.TryGetProperty("tool_call_id", out var toolCallId)
                        ? toolCallId.GetString()
                        : string.Empty,
                    ["output"] = content.ValueKind == JsonValueKind.String
                        ? content.GetString() ?? string.Empty
                        : content.ValueKind == JsonValueKind.Undefined
                            ? string.Empty
                            : content.GetRawText(),
                });
            }
        }

        return (instructions, input);
    }

    private JsonArray ConvertToolsForResponses(JsonElement tools)
    {
        var converted = new JsonArray();
        if (tools.ValueKind != JsonValueKind.Array)
        {
            return converted;
        }

        foreach (var tool in tools.EnumerateArray())
        {
            var function = tool.TryGetProperty("function", out var fn) ? fn : tool;
            converted.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = function.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString()
                    : string.Empty,
                ["description"] = function.TryGetProperty("description", out var descProp)
                    ? descProp.GetString()
                    : string.Empty,
                ["parameters"] = function.TryGetProperty("parameters", out var paramsProp)
                    ? JsonNode.Parse(paramsProp.GetRawText())
                    : new JsonObject(),
            });
        }

        return converted;
    }

    private LlmChatResult ParseResponsesResult(JsonNode root, string model)
    {
        var toolCalls = new JsonArray();
        var output = root["output"] as JsonArray ?? [];

        foreach (var item in output)
        {
            if (!string.Equals(item?["type"]?.GetValue<string>(), "function_call", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            JsonNode argumentsNode = new JsonObject();
            var argumentsText = item?["arguments"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(argumentsText))
            {
                argumentsNode = JsonNode.Parse(argumentsText) ?? argumentsText;
            }

            toolCalls.Add(new JsonObject
            {
                ["id"] = item?["call_id"]?.GetValue<string>() ?? string.Empty,
                ["function"] = new JsonObject
                {
                    ["name"] = item?["name"]?.GetValue<string>() ?? string.Empty,
                    ["arguments"] = argumentsNode,
                },
            });
        }

        return new LlmChatResult
        {
            Content = root["output_text"]?.GetValue<string>() ?? string.Empty,
            ToolCalls = toolCalls,
            Thinking = string.Empty,
            Done = true,
            Model = root["model"]?.GetValue<string>() ?? model,
            TotalDuration = 0,
            EvalCount = root["usage"]?["output_tokens"]?.GetValue<int>() ?? 0,
        };
    }

    private JsonArray NormalizeToolCalls(JsonArray? toolCalls)
    {
        if (toolCalls == null)
        {
            return [];
        }

        var normalized = new JsonArray();
        foreach (var toolCall in toolCalls)
        {
            JsonNode argumentsNode = new JsonObject();
            var arguments = toolCall?["function"]?["arguments"];
            if (arguments is JsonValue jsonValue &&
                jsonValue.TryGetValue<string>(out var argumentsText) &&
                !string.IsNullOrWhiteSpace(argumentsText))
            {
                argumentsNode = JsonNode.Parse(argumentsText) ?? argumentsText;
            }
            else if (arguments != null)
            {
                argumentsNode = arguments.DeepClone();
            }

            normalized.Add(new JsonObject
            {
                ["id"] = toolCall?["id"]?.GetValue<string>() ?? string.Empty,
                ["function"] = new JsonObject
                {
                    ["name"] = toolCall?["function"]?["name"]?.GetValue<string>() ?? string.Empty,
                    ["arguments"] = argumentsNode,
                },
            });
        }

        return normalized;
    }

    private static JsonArray CloneArray(JsonArray? source)
    {
        if (source == null)
        {
            return [];
        }

        var clone = new JsonArray();
        foreach (var item in source)
        {
            clone.Add(item?.DeepClone());
        }
        return clone;
    }

    private static string ExtractTextContent(JsonNode? contentNode)
    {
        if (contentNode == null)
        {
            return string.Empty;
        }

        if (contentNode is JsonValue value && value.TryGetValue<string>(out var textValue))
        {
            return textValue;
        }

        if (contentNode is JsonArray array)
        {
            var parts = array
                .Select(item => item?["text"]?.GetValue<string>()
                    ?? item?["content"]?.GetValue<string>()
                    ?? item?.ToJsonString())
                .Where(item => !string.IsNullOrWhiteSpace(item));
            return string.Join("", parts);
        }

        return contentNode.ToJsonString();
    }

    private static string SerializeArguments(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.String)
        {
            return arguments.GetString() ?? "{}";
        }

        if (arguments.ValueKind == JsonValueKind.Undefined || arguments.ValueKind == JsonValueKind.Null)
        {
            return "{}";
        }

        return arguments.GetRawText();
    }

    private string ResolveModel(string? requestedModel, ResolvedRuntimeOptions runtime)
    {
        if (runtime.AllowModelOverride && !string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel;
        }

        return !string.IsNullOrWhiteSpace(runtime.DefaultModel)
            ? runtime.DefaultModel
            : requestedModel ?? string.Empty;
    }

    private ResolvedRuntimeOptions ResolveRuntime(BrokerTask? task)
    {
        var descriptor = TaskRuntimeDescriptor.Parse(task?.RuntimeDescriptor);
        return new ResolvedRuntimeOptions
        {
            DefaultModel = !string.IsNullOrWhiteSpace(descriptor.Llm.DefaultModel)
                ? descriptor.Llm.DefaultModel
                : _options.DefaultModel,
            AllowModelOverride = descriptor.Llm.AllowModelOverride ?? _options.AllowModelOverride,
            SupportsToolCalling = descriptor.Llm.SupportsToolCalling ?? _options.SupportsToolCalling,
            StreamingEnabled = descriptor.Llm.StreamingEnabled ?? _options.StreamingEnabled,
            Source = descriptor.Llm.HasOverrides || descriptor.HasCapabilityOverrides
                ? "task_runtime_descriptor"
                : "broker_default"
        };
    }

    private bool IsOllama()
        => string.Equals(_options.Provider, "ollama", StringComparison.OrdinalIgnoreCase);

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("LlmProxy is disabled.");
        }
    }

    private sealed class ResolvedRuntimeOptions
    {
        public string DefaultModel { get; set; } = string.Empty;
        public bool AllowModelOverride { get; set; }
        public bool SupportsToolCalling { get; set; }
        public bool StreamingEnabled { get; set; }
        public string Source { get; set; } = "broker_default";
    }
}
