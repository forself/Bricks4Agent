using Broker.Helpers;
using BrokerCore.Services;

namespace Broker.Endpoints;

public static class RuntimeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var runtime = group.MapGroup("/runtime");
        runtime.MapPost("/spec", (HttpContext ctx, ILlmProxyService llmProxy) =>
        {
            if (!llmProxy.IsEnabled)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var spec = llmProxy.BuildRuntimeSpec();
            var brokerBaseUrl = BuildBaseUrl(ctx);
            var llmHealthUrl = $"{brokerBaseUrl}/api/v1/llm/health";
            var llmModelsUrl = $"{brokerBaseUrl}/api/v1/llm/models";
            var llmChatUrl = $"{brokerBaseUrl}/api/v1/llm/chat";

            return Results.Ok(ApiResponseHelper.Success(new
            {
                provider = spec.Provider,
                api_format = spec.ApiFormat,
                default_model = spec.DefaultModel,
                allow_model_override = spec.AllowModelOverride,
                supports_tool_calling = spec.SupportsToolCalling,
                streaming_enabled = spec.StreamingEnabled,
                llm_routes = new
                {
                    health = llmHealthUrl,
                    models = llmModelsUrl,
                    chat = llmChatUrl,
                },
                request_bodies = new
                {
                    health = new
                    {
                        method = "POST",
                        url = llmHealthUrl,
                        body = new
                        {
                            scoped_token = "<scoped token issued for this session>"
                        }
                    },
                    models = new
                    {
                        method = "POST",
                        url = llmModelsUrl,
                        body = new
                        {
                            scoped_token = "<scoped token issued for this session>"
                        }
                    },
                    chat = new
                    {
                        method = "POST",
                        url = llmChatUrl,
                        body = new
                        {
                            scoped_token = "<scoped token issued for this session>",
                            model = spec.DefaultModel,
                            messages = new[]
                            {
                                new { role = "system", content = "You are a governed agent." },
                                new { role = "user", content = "<prompt>" }
                            },
                            tools = new object[0],
                            stream = false,
                        }
                    }
                }
            }));
        });

        var llm = group.MapGroup("/llm");

        llm.MapPost("/health", async (ILlmProxyService llmProxy, CancellationToken cancellationToken) =>
        {
            if (!llmProxy.IsEnabled)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var healthy = await llmProxy.HealthCheckAsync(cancellationToken);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                healthy
            }));
        });

        llm.MapPost("/models", async (ILlmProxyService llmProxy, CancellationToken cancellationToken) =>
        {
            if (!llmProxy.IsEnabled)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var models = await llmProxy.ListModelsAsync(cancellationToken);
            var payload = models.Select(model => new
            {
                name = model.Name,
                size = model.Size
            }).ToList();

            return Results.Ok(ApiResponseHelper.Success(payload));
        });

        llm.MapPost("/chat", async (HttpContext ctx, ILlmProxyService llmProxy, CancellationToken cancellationToken) =>
        {
            if (!llmProxy.IsEnabled)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            var body = RequestBodyHelper.GetBody(ctx);
            var result = await llmProxy.ChatAsync(body, cancellationToken);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                content = result.Content,
                tool_calls = result.ToolCalls,
                thinking = result.Thinking,
                done = result.Done,
                model = result.Model,
                total_duration = result.TotalDuration,
                eval_count = result.EvalCount
            }));
        });
    }

    private static string BuildBaseUrl(HttpContext ctx)
    {
        return $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}".TrimEnd('/');
    }
}
