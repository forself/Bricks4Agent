using BrokerCore.Data;
using BrokerCore.Services;
using FluentAssertions;
using Unit.Tests.Helpers;
using Xunit;

namespace Unit.Tests.Core;

public sealed class AuditContentIntegrityTests : IDisposable
{
    private readonly BrokerDb _db = TestDb.CreateInMemory();
    public void Dispose() => _db.Dispose();

    [Fact]
    public void Recorded_events_remain_valid_after_SQLite_round_trip()
    {
        var audit = new AuditService(_db);
        audit.RecordEvent("trace-roundtrip", "START", details: "{\"名稱\":\"課程\"}");
        audit.RecordEvent("trace-roundtrip", "DONE", details: "{\"ok\":true}");

        new AuditService(_db).VerifyTraceIntegrity("trace-roundtrip").Should().BeTrue();
        audit.VerifyTraceIntegrity("trace-empty").Should().BeTrue();
    }

    [Theory]
    [InlineData("details", "{\"ok\":false}")]
    [InlineData("payload_digest", "changed")]
    [InlineData("previous_event_hash", "changed")]
    [InlineData("event_hash", "changed")]
    [InlineData("event_type", "FORGED")]
    public void Changed_event_content_or_chain_is_rejected(string column, string value)
    {
        var audit = new AuditService(_db);
        audit.RecordEvent("trace-tamper", "START");
        audit.RecordEvent("trace-tamper", "DONE", details: "{\"ok\":true}");
        audit.VerifyTraceIntegrity("trace-tamper").Should().BeTrue();

        // The identifier comes only from the fixed InlineData cases. This is a
        // disposable test database, never a deployed audit store.
        _db.Execute($"UPDATE audit_events SET {column} = @value WHERE trace_id = @trace AND trace_seq = 1",
            new { value, trace = "trace-tamper" });

        audit.VerifyTraceIntegrity("trace-tamper").Should().BeFalse();
    }

    [Fact]
    public void Missing_middle_event_is_rejected()
    {
        var audit = new AuditService(_db);
        for (var i = 0; i < 3; i++) audit.RecordEvent("trace-gap", "STEP");
        _db.Execute("DELETE FROM audit_events WHERE trace_id = @trace AND trace_seq = 1", new { trace = "trace-gap" });
        audit.VerifyTraceIntegrity("trace-gap").Should().BeFalse();
    }
}
