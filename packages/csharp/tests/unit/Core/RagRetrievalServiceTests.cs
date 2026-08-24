using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Unit.Tests.Helpers;

namespace Unit.Tests.Core;

public class RagRetrievalServiceTests
{
    [Fact]
    public async Task SemanticRetrieval_UsesOnlyCurrentEmbeddingModelVectors()
    {
        using var db = TestDb.CreateInMemory();
        InsertEntry(db, "old-model-hit", "old model content");
        InsertEntry(db, "current-model-hit", "current model content");
        InsertVector(db, "old-model-hit", "old-model", [1f, 0f]);
        InsertVector(db, "current-model-hit", "current-model", [0f, 1f]);

        var retrieval = new RagRetrievalService(
            db,
            new FixedEmbeddingService("current-model", [1f, 0f]));

        var result = await retrieval.RetrieveAsync(new RagRetrieveRequest
        {
            Query = "semantic query",
            Mode = "semantic",
            Limit = 5,
            Threshold = -1f,
            Rewrite = false,
            Rerank = false
        }, "global");

        result.VectorCandidates.Should().Be(1);
        result.Results.Should().ContainSingle();
        result.Results[0].Key.Should().Be("current-model-hit");
    }

    [Fact]
    public void VectorSchema_AllowsSameContentHashForDifferentEmbeddingModels()
    {
        using var db = TestDb.CreateInMemory();

        InsertVector(db, "same-content", "old-model", [1f, 0f], contentHash: "shared-content-hash");
        InsertVector(db, "same-content", "current-model", [0f, 1f], contentHash: "shared-content-hash");

        db.GetAll<VectorEntry>()
            .Where(vector => vector.ContentHash == "shared-content-hash")
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public async Task KeywordMode_IsAliasForFulltextRetrieval()
    {
        using var db = TestDb.CreateInMemory();
        InsertEntry(db, "consumer-law-19", "通訊交易七日解除契約");
        db.Execute(
            "INSERT INTO memory_fts(source_key, content, task_id) VALUES(@key, @content, @taskId)",
            new
            {
                key = "consumer-law-19",
                content = Fts5TextNormalizer.PrepareContent("通訊交易七日解除契約"),
                taskId = "global"
            });

        var retrieval = new RagRetrievalService(db);

        var result = await retrieval.RetrieveAsync(new RagRetrieveRequest
        {
            Query = "通訊交易",
            Mode = "keyword",
            Limit = 5,
            Rewrite = false,
            Rerank = false
        }, "global");

        result.Mode.Should().Be("fulltext");
        result.Bm25Candidates.Should().Be(1);
        result.Results.Should().ContainSingle(item => item.Key == "consumer-law-19");
    }

    private static void InsertEntry(BrokerDb db, string key, string content)
    {
        db.Insert(new SharedContextEntry
        {
            EntryId = $"ctx_{Guid.NewGuid():N}",
            TaskId = "global",
            DocumentId = key,
            Key = key,
            ContentRef = content,
            ContentType = "text/plain",
            AuthorPrincipalId = "test",
            Acl = "{}",
            Version = 1,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static void InsertVector(BrokerDb db, string key, string model, float[] vector, string? contentHash = null)
    {
        db.Insert(new VectorEntry
        {
            EntryId = $"vec_{Guid.NewGuid():N}",
            SourceKey = key,
            TaskId = "global",
            TextPreview = key,
            ContentHash = contentHash ?? key,
            Embedding = EmbeddingService.VectorToBytes(vector),
            EmbeddingModel = model,
            Dimension = vector.Length,
            CreatedAt = DateTime.UtcNow
        });
    }

    private sealed class FixedEmbeddingService : EmbeddingService
    {
        private readonly float[] _vector;

        public FixedEmbeddingService(string model, float[] vector)
            : base(new EmbeddingOptions
            {
                Enabled = true,
                Model = model,
                Dimension = vector.Length
            })
        {
            _vector = vector;
        }

        public override Task<float[]?> EmbedAsync(string text)
            => Task.FromResult<float[]?>(_vector);
    }
}
