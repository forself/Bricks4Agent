using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var databasePath = Environment.GetEnvironmentVariable("RAG_DB_PATH")
        ?? configuration["RagService:DatabasePath"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "rag-service.db");

    if (!Path.IsPathRooted(databasePath))
        databasePath = Path.Combine(builder.Environment.ContentRootPath, databasePath);

    var directory = Path.GetDirectoryName(databasePath);
    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    var connectionString = configuration.GetConnectionString("Rag")
        ?? $"Data Source={databasePath}";

    var db = BrokerDb.UseSqlite(connectionString);
    var initializeSchema = configuration.GetValue("RagService:InitializeSchema", true);
    if (initializeSchema)
        new BrokerDbInitializer(db).Initialize();

    return db;
});

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IConfiguration>()
        .GetSection("Embedding")
        .Get<EmbeddingOptions>() ?? new EmbeddingOptions { Enabled = false };
    return new EmbeddingService(options);
});

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IConfiguration>()
        .GetSection("RagPipeline")
        .Get<RagPipelineOptions>() ?? new RagPipelineOptions
        {
            QueryRewriteEnabled = false,
            RerankEnabled = false,
            CacheEnabled = false
        };
    return new RagPipelineService(options);
});

var app = builder.Build();

app.MapGet("/healthz", (BrokerDb db) =>
{
    var entryCount = db.Scalar<long>("SELECT COUNT(*) FROM shared_context_entries");
    return Results.Ok(new
    {
        status = "ok",
        service = "rag-service",
        entries = entryCount
    });
});

app.MapPost("/rag/retrieve", async (
    JsonElement body,
    BrokerDb db,
    EmbeddingService embeddingService,
    RagPipelineService ragPipeline,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var args = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("args", out var nestedArgs)
        ? nestedArgs
        : body;

    var request = RagRetrieveRequest.FromArgs(args);
    request.TaskId ??= ReadString(body, "task_id");

    if (string.IsNullOrWhiteSpace(request.Query))
        return Results.BadRequest(new { error = "query is required" });

    var logger = loggerFactory.CreateLogger("RagService");
    var retrieval = new RagRetrievalService(
        db,
        embeddingService,
        ragPipeline,
        message => logger.LogInformation("{Message}", message));

    var response = await retrieval.RetrieveAsync(
        request,
        string.IsNullOrWhiteSpace(request.TaskId) ? "global" : request.TaskId!,
        cancellationToken);

    return Results.Ok(response);
});

app.Run();

static string? ReadString(JsonElement element, string propertyName)
{
    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        return null;

    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };
}
