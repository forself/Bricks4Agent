using Broker.Services;
using BrokerCore.Data;
using Microsoft.Data.Sqlite;

namespace Unit.Tests.Broker;

/// <summary>
/// Artifact lookups of <see cref="HighLevelLineWorkspaceService"/> match the user id and the artifact id
/// exactly: '_' and '%' are ordinary characters, case matters, and a user id that is a prefix of another
/// one up to a '.' does not reach the other user's artifacts.
/// </summary>
public sealed class HighLevelLineWorkspaceServiceTests : IDisposable
{
    private const string LineFormatUserId = "U0123456789abcdef0123456789abcdef";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-line-workspace-{Guid.NewGuid():N}");
    private readonly BrokerDb _db;
    private readonly HighLevelLineWorkspaceService _workspace;

    public HighLevelLineWorkspaceServiceTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _db = BrokerDb.UseSqlite($"Data Source={Path.Combine(_tempRoot, "broker.db")}");
        new BrokerDbInitializer(_db).Initialize();
        _workspace = new HighLevelLineWorkspaceService(_db, new HighLevelCoordinatorOptions
        {
            AccessRoot = Path.Combine(_tempRoot, "managed")
        });
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // SQLite can briefly hold the file after disposal on Windows test runners.
        }
    }

    [Theory]
    [InlineData("alic_")]
    [InlineData("ALICE")]
    [InlineData("al%")]
    [InlineData("bob")]
    [InlineData("U________________________________")]
    public void ListArtifacts_DoesNotReturnArtifactsOfOtherUsers(string userId)
    {
        Record("alice");
        Record("bob.x");
        Record(LineFormatUserId);

        _workspace.ListArtifacts(userId, limit: 100).Should().BeEmpty();
    }

    [Fact]
    public void ListArtifacts_ReturnsExactlyTheUsersOwnArtifacts()
    {
        var bob = Record("bob");
        var dotted = Record("bob.x");
        var line = Record(LineFormatUserId);

        _workspace.ListArtifacts("bob").Select(item => item.ArtifactId).Should().Equal(bob.ArtifactId);
        _workspace.ListArtifacts("bob.x").Select(item => item.ArtifactId).Should().Equal(dotted.ArtifactId);
        _workspace.ListArtifacts(LineFormatUserId).Select(item => item.ArtifactId).Should().Equal(line.ArtifactId);
    }

    [Fact]
    public void ListArtifacts_FillsTheLimitPastNewerArtifactsOfAUserSharingThePrefix()
    {
        var own = new[] { Record("bob"), Record("bob") };
        for (var i = 0; i < 5; i++)
        {
            // Newer than bob's own artifacts, so they come first in created_at order.
            Thread.Sleep(20);
            Record("bob.x");
        }

        _workspace.ListArtifacts("bob", limit: 2).Select(item => item.ArtifactId)
            .Should().BeEquivalentTo(own.Select(item => item.ArtifactId));
        _workspace.ListArtifacts("bob.x", limit: 3).Should().HaveCount(3)
            .And.OnlyContain(item => item.UserId == "bob.x");
    }

    [Fact]
    public void ReadArtifactById_MatchesTheArtifactIdExactly()
    {
        _workspace.RecordArtifact(new HighLevelLineArtifactRecord { ArtifactId = "artifact-Case-01", UserId = "alice" });
        _workspace.RecordArtifact(new HighLevelLineArtifactRecord { ArtifactId = "artifact-02", UserId = "bob.x" });

        _workspace.ReadArtifactById("artifact-Case-01")!.UserId.Should().Be("alice");
        _workspace.ReadArtifactById("artifact-02")!.UserId.Should().Be("bob.x");

        _workspace.ReadArtifactById("artifact_Case_01").Should().BeNull("'_' is not a wildcard");
        _workspace.ReadArtifactById("artifact-case-01").Should().BeNull("the artifact id is case-sensitive");
        _workspace.ReadArtifactById("artifact-Case-%").Should().BeNull("'%' is not a wildcard");
        _workspace.ReadArtifactById("x.artifact-02").Should().BeNull("the id must be the whole artifact id, not a tail of the document id");
        _workspace.ReadArtifactById(string.Empty).Should().BeNull();
    }

    private HighLevelLineArtifactRecord Record(string userId)
        => _workspace.RecordArtifact(new HighLevelLineArtifactRecord
        {
            UserId = userId,
            FileName = "artifact.txt",
            Success = true,
            OverallStatus = "completed"
        });
}
