using FluentAssertions;

namespace Unit.Tests.Helpers;

public class SmokeTests
{
    [Fact]
    public void InMemoryDb_Initializes_WithoutError()
    {
        using var db = TestDb.CreateInMemory();
        db.Should().NotBeNull();
    }
}
