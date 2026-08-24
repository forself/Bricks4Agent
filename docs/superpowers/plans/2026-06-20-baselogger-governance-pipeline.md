# BaseLogger Governance Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade `packages/csharp/logging/BaseLogger` into a governed, pluggable logging pipeline that keeps `BaseLogger` as the package/namespace name while avoiding public API names that collide with system logging abstractions.

**Architecture:** Application code continues to depend on `Microsoft.Extensions.Logging.ILogger<T>`. `BaseLoggerProvider` adapts standard .NET logging events into `BaseLoggerEvent` records, then sends them through `BaseLoggerPipeline` for correlation, redaction, formatting, and sink dispatch. Broker-specific correlation and security rejection audit are separate middleware/services that reuse the same `broker_trace_id` without turning runtime logs into audit records.

**Tech Stack:** C# / .NET 8, Microsoft.Extensions.Logging, Microsoft.Extensions.Configuration, BaseOrm/SQLite for broker audit, xUnit + FluentAssertions + NSubstitute.

---

## Scope

This plan implements the first production-grade BaseLogger slice:

- New non-conflicting public API names under `BaseLogger`.

- Standard `Microsoft.Extensions.Logging` provider integration.

- Redaction and correlation.

- Console, rolling-file, memory, and library-level database sinks.

- Broker correlation middleware and minimal security rejection audit recorder.

- Host setup for broker, rag-service, and worker programs.

- Tests and documentation updates.

This plan does not implement OTLP/SIEM exporters or audit HMAC/external anchoring.

## File Structure

Create focused BaseLogger files instead of expanding the existing monolithic `BaseLogger.cs`:

- `packages/csharp/logging/BaseLogger/BaseLoggerSeverity.cs`
 Severity enum with no name collision with `Microsoft.Extensions.Logging.LogLevel`.

- `packages/csharp/logging/BaseLogger/BaseLoggerEvent.cs`
 Redacted operational event schema.

- `packages/csharp/logging/BaseLogger/BaseLoggerOptions.cs`
 Configuration model for provider and sinks.

- `packages/csharp/logging/BaseLogger/Correlation/BaseLoggerCorrelationContext.cs`
 Correlation value object.

- `packages/csharp/logging/BaseLogger/Correlation/IBaseLoggerCorrelationAccessor.cs`
 Current correlation accessor contract.

- `packages/csharp/logging/BaseLogger/Correlation/AsyncLocalBaseLoggerCorrelationAccessor.cs`
 AsyncLocal-backed default accessor.

- `packages/csharp/logging/BaseLogger/Formatting/IBaseLoggerFormatter.cs`
 Formatter contract.

- `packages/csharp/logging/BaseLogger/Formatting/BaseLoggerJsonFormatter.cs`
 JSONL formatter.

- `packages/csharp/logging/BaseLogger/Formatting/BaseLoggerTextFormatter.cs`
 Human-readable formatter.

- `packages/csharp/logging/BaseLogger/Redaction/IBaseLoggerRedactor.cs`
 Redaction contract.

- `packages/csharp/logging/BaseLogger/Redaction/DefaultBaseLoggerRedactor.cs`
 Default secret and identifier redactor.

- `packages/csharp/logging/BaseLogger/Sinks/IBaseLoggerSink.cs`
 Sink contract.

- `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerMemorySink.cs`
 Ring buffer sink.

- `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerConsoleSink.cs`
 Console sink.

- `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerRollingFileSink.cs`
 Size/retention rolling file sink.

- `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerDatabaseSink.cs`
 BaseOrm-compatible database sink.

- `packages/csharp/logging/BaseLogger/BaseLoggerPipeline.cs`
 Pipeline that applies redaction, formats, dispatches, and isolates sink failures.

- `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerProvider.cs`
 `ILoggerProvider` implementation.

- `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerMicrosoftLogger.cs`
 Internal standard-logger adapter.

- `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerLoggingBuilderExtensions.cs`
 `builder.Logging.AddBaseLogger(...)` integration.

Modify existing files:

- `packages/csharp/logging/BaseLogger/BaseLogger.csproj`
 Add package references.

- `packages/csharp/logging/BaseLogger/BaseLogger.cs`
 Mark old `ILogger`, `Logger`, `LogLevel`, `LogEntry`, `ILogTarget`, and `Log` APIs obsolete after wrappers are wired to the new pipeline.

- `packages/csharp/broker/Program.cs`
 Add BaseLogger provider and correlation/audit services.

- `packages/csharp/rag-service/Program.cs`
 Add BaseLogger provider.

- `packages/csharp/workers/*/Program.cs`
 Add BaseLogger provider to workers that currently use console logging.

- `packages/csharp/workers/line-worker/start-sidecar-stack.ps1`
 Stop deleting previous log evidence and use retention-friendly run folders or rolling output path.

- `docs/manuals/current-technical-manual.zh-TW.md`
 Document BaseLogger runtime log vs audit boundaries.

- `docs/manuals/current-user-manual.zh-TW.md`
 Document where operators inspect logs.

- `docs/reports/follow-up-planning.zh-TW.md`
 Mark BaseLogger governance design as planned/ready for implementation.

Tests:

- Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerRedactorTests.cs`

- Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerPipelineTests.cs`

- Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerSinkTests.cs`

- Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerProviderTests.cs`

- Create `packages/csharp/tests/unit/Core/BrokerCorrelationAndAuditTests.cs`

---

### Task 1: BaseLogger Core API And Project References

**Files:**
- Modify: `packages/csharp/logging/BaseLogger/BaseLogger.csproj`
- Modify: `packages/csharp/tests/unit/Unit.Tests.csproj`
- Create: `packages/csharp/logging/BaseLogger/BaseLoggerSeverity.cs`
- Create: `packages/csharp/logging/BaseLogger/BaseLoggerEvent.cs`
- Create: `packages/csharp/logging/BaseLogger/BaseLoggerOptions.cs`
- Create: `packages/csharp/logging/BaseLogger/Correlation/BaseLoggerCorrelationContext.cs`
- Create: `packages/csharp/logging/BaseLogger/Correlation/IBaseLoggerCorrelationAccessor.cs`
- Create: `packages/csharp/logging/BaseLogger/Correlation/AsyncLocalBaseLoggerCorrelationAccessor.cs`
- Test: `packages/csharp/tests/unit/BaseLogger/BaseLoggerPipelineTests.cs`

- [ ] **Step 1: Add BaseLogger project references needed by provider/configuration**

Modify `packages/csharp/logging/BaseLogger/BaseLogger.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>BaseLogger</RootNamespace>
    <PackageId>BaseLogger</PackageId>
    <Version>1.0.0</Version>
    <Description>Bricks4Agent governed logging pipeline with Microsoft.Extensions.Logging provider integration.</Description>
    <Authors>Bricks4Agent</Authors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="8.0.2" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.3" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add a direct BaseLogger reference to the unit test project**

Modify `packages/csharp/tests/unit/Unit.Tests.csproj` by adding this item inside the existing `ProjectReference` item group:

```xml
    <ProjectReference Include="../../logging/BaseLogger/BaseLogger.csproj" />
```

- [ ] **Step 3: Write the failing core API test**

Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerPipelineTests.cs` with this initial test:

```csharp
using BaseLogger;

namespace Unit.Tests.BaseLogger;

public class BaseLoggerPipelineTests
{
    [Fact]
    public void BaseLoggerEvent_CarriesCorrelationAndDoesNotUseSystemCollisionNames()
    {
        var ev = new BaseLoggerEvent
        {
            EventId = "log_001",
            TimestampUtc = new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero),
            Severity = BaseLoggerSeverity.Info,
            Category = "Broker.Runtime",
            EventKind = "runtime",
            MessageTemplate = "Hello {Name}",
            RenderedMessage = "Hello world",
            TraceId = "trace_123",
            Component = "broker"
        };

        ev.TraceId.Should().Be("trace_123");
        ev.Severity.Should().Be(BaseLoggerSeverity.Info);
        typeof(BaseLoggerEvent).Name.Should().Be("BaseLoggerEvent");
        typeof(BaseLoggerSeverity).Name.Should().Be("BaseLoggerSeverity");
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger.BaseLoggerPipelineTests.BaseLoggerEvent_CarriesCorrelationAndDoesNotUseSystemCollisionNames
```

Expected: build fails because `BaseLoggerEvent` and `BaseLoggerSeverity` do not exist.

- [ ] **Step 5: Add `BaseLoggerSeverity`**

Create `packages/csharp/logging/BaseLogger/BaseLoggerSeverity.cs`:

```csharp
namespace BaseLogger;

public enum BaseLoggerSeverity
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}
```

- [ ] **Step 6: Add `BaseLoggerEvent`**

Create `packages/csharp/logging/BaseLogger/BaseLoggerEvent.cs`:

```csharp
namespace BaseLogger;

public sealed class BaseLoggerEvent
{
    public string EventId { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public BaseLoggerSeverity Severity { get; set; } = BaseLoggerSeverity.Info;
    public string Category { get; set; } = string.Empty;
    public string EventKind { get; set; } = "runtime";
    public string MessageTemplate { get; set; } = string.Empty;
    public string RenderedMessage { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public Dictionary<string, object?> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string TraceId { get; set; } = string.Empty;
    public string? SpanId { get; set; }
    public string? PrincipalId { get; set; }
    public string? TaskId { get; set; }
    public string? SessionId { get; set; }
    public string? RequestId { get; set; }
    public string Component { get; set; } = string.Empty;
    public int ProcessId { get; set; } = Environment.ProcessId;
    public string ThreadId { get; set; } = Environment.CurrentManagedThreadId.ToString();
    public string Source { get; set; } = string.Empty;
    public string Sensitivity { get; set; } = "internal";
}
```

- [ ] **Step 7: Add options model**

Create `packages/csharp/logging/BaseLogger/BaseLoggerOptions.cs`:

```csharp
namespace BaseLogger;

public sealed class BaseLoggerOptions
{
    public bool Enabled { get; set; } = true;
    public BaseLoggerSeverity MinimumSeverity { get; set; } = BaseLoggerSeverity.Info;
    public string Format { get; set; } = "json";
    public string Component { get; set; } = "app";
    public string Source { get; set; } = "";
    public string RedactionProfile { get; set; } = "production";
    public BaseLoggerSinkOptions Sinks { get; set; } = new();
}

public sealed class BaseLoggerSinkOptions
{
    public BaseLoggerConsoleSinkOptions Console { get; set; } = new();
    public BaseLoggerRollingFileSinkOptions RollingFile { get; set; } = new();
    public BaseLoggerDatabaseSinkOptions Database { get; set; } = new();
    public BaseLoggerMemorySinkOptions Memory { get; set; } = new();
}

public sealed class BaseLoggerConsoleSinkOptions
{
    public bool Enabled { get; set; } = true;
}

public sealed class BaseLoggerRollingFileSinkOptions
{
    public bool Enabled { get; set; }
    public string Path { get; set; } = ".run/logs/app.jsonl";
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxFiles { get; set; } = 10;
    public int RetentionDays { get; set; } = 14;
}

public sealed class BaseLoggerDatabaseSinkOptions
{
    public bool Enabled { get; set; }
    public string TableName { get; set; } = "operational_logs";
}

public sealed class BaseLoggerMemorySinkOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxEntries { get; set; } = 500;
}
```

- [ ] **Step 8: Add correlation context and accessor**

Create `packages/csharp/logging/BaseLogger/Correlation/BaseLoggerCorrelationContext.cs`:

```csharp
namespace BaseLogger.Correlation;

public sealed class BaseLoggerCorrelationContext
{
    public string TraceId { get; set; } = string.Empty;
    public string? TraceSource { get; set; }
    public string? RequestId { get; set; }
    public string? PrincipalId { get; set; }
    public string? TaskId { get; set; }
    public string? SessionId { get; set; }
    public string? Component { get; set; }
}
```

Create `packages/csharp/logging/BaseLogger/Correlation/IBaseLoggerCorrelationAccessor.cs`:

```csharp
namespace BaseLogger.Correlation;

public interface IBaseLoggerCorrelationAccessor
{
    BaseLoggerCorrelationContext Current { get; set; }
}
```

Create `packages/csharp/logging/BaseLogger/Correlation/AsyncLocalBaseLoggerCorrelationAccessor.cs`:

```csharp
namespace BaseLogger.Correlation;

public sealed class AsyncLocalBaseLoggerCorrelationAccessor : IBaseLoggerCorrelationAccessor
{
    private static readonly AsyncLocal<BaseLoggerCorrelationContext?> CurrentContext = new();

    public BaseLoggerCorrelationContext Current
    {
        get => CurrentContext.Value ??= new BaseLoggerCorrelationContext();
        set => CurrentContext.Value = value ?? new BaseLoggerCorrelationContext();
    }
}
```

- [ ] **Step 9: Run the focused test to verify it passes**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger.BaseLoggerPipelineTests.BaseLoggerEvent_CarriesCorrelationAndDoesNotUseSystemCollisionNames
```

Expected: test passes.

- [ ] **Step 10: Commit Task 1**

Run:

```powershell
git add packages/csharp/logging/BaseLogger/BaseLogger.csproj `
  packages/csharp/tests/unit/Unit.Tests.csproj `
  packages/csharp/logging/BaseLogger/BaseLoggerSeverity.cs `
  packages/csharp/logging/BaseLogger/BaseLoggerEvent.cs `
  packages/csharp/logging/BaseLogger/BaseLoggerOptions.cs `
  packages/csharp/logging/BaseLogger/Correlation `
  packages/csharp/tests/unit/BaseLogger/BaseLoggerPipelineTests.cs
git commit -m "feat: add BaseLogger governance core types"
```

---

### Task 2: Redaction, Formatting, And Pipeline Dispatch

**Files:**
- Create: `packages/csharp/logging/BaseLogger/Redaction/IBaseLoggerRedactor.cs`
- Create: `packages/csharp/logging/BaseLogger/Redaction/DefaultBaseLoggerRedactor.cs`
- Create: `packages/csharp/logging/BaseLogger/Formatting/IBaseLoggerFormatter.cs`
- Create: `packages/csharp/logging/BaseLogger/Formatting/BaseLoggerJsonFormatter.cs`
- Create: `packages/csharp/logging/BaseLogger/Formatting/BaseLoggerTextFormatter.cs`
- Create: `packages/csharp/logging/BaseLogger/Sinks/IBaseLoggerSink.cs`
- Create: `packages/csharp/logging/BaseLogger/BaseLoggerPipeline.cs`
- Test: `packages/csharp/tests/unit/BaseLogger/BaseLoggerRedactorTests.cs`
- Test: `packages/csharp/tests/unit/BaseLogger/BaseLoggerPipelineTests.cs`

- [ ] **Step 1: Add failing redactor tests**

Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerRedactorTests.cs`:

```csharp
using BaseLogger;
using BaseLogger.Redaction;

namespace Unit.Tests.BaseLogger;

public class BaseLoggerRedactorTests
{
    [Fact]
    public void Redact_ReturnsSecretMaskForSecretPropertyNames()
    {
        var redactor = new DefaultBaseLoggerRedactor();
        var value = redactor.RedactProperty("Authorization", "Bearer abc123");
        value.Should().Be("[redacted]");
    }

    [Fact]
    public void RedactEvent_MasksSecretPropertiesBeforeSinksSeeTheEvent()
    {
        var redactor = new DefaultBaseLoggerRedactor();
        var ev = new BaseLoggerEvent
        {
            EventId = "log_001",
            RenderedMessage = "token abc123",
            Properties = new Dictionary<string, object?>
            {
                ["api_key"] = "sk-test",
                ["message"] = "hello"
            }
        };

        var redacted = redactor.Redact(ev);

        redacted.RenderedMessage.Should().Be("token [redacted]");
        redacted.Properties["api_key"].Should().Be("[redacted]");
        redacted.Properties["message"].Should().Be("hello");
    }
}
```

- [ ] **Step 2: Add failing pipeline dispatch test**

Append to `packages/csharp/tests/unit/BaseLogger/BaseLoggerPipelineTests.cs`:

```csharp
    [Fact]
    public void Write_DispatchesRedactedEventToSink()
    {
        var sink = new CapturingSink();
        var pipeline = new BaseLoggerPipeline(
            new BaseLoggerOptions { Component = "broker" },
            new BaseLogger.Redaction.DefaultBaseLoggerRedactor(),
            new BaseLogger.Formatting.BaseLoggerJsonFormatter(),
            [sink]);

        pipeline.Write(new BaseLoggerEvent
        {
            EventId = "log_001",
            Severity = BaseLoggerSeverity.Info,
            Category = "Test",
            RenderedMessage = "Authorization abc",
            Properties = new Dictionary<string, object?> { ["token"] = "secret" }
        });

        sink.Events.Should().HaveCount(1);
        sink.Events[0].RenderedMessage.Should().Be("Authorization [redacted]");
        sink.Events[0].Properties["token"].Should().Be("[redacted]");
    }

    private sealed class CapturingSink : BaseLogger.Sinks.IBaseLoggerSink
    {
        public List<BaseLoggerEvent> Events { get; } = new();
        public void Write(BaseLoggerEvent ev) => Events.Add(ev);
        public void Flush() { }
        public void Dispose() { }
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger
```

Expected: build fails because redaction, formatter, sink, and pipeline types do not exist.

- [ ] **Step 4: Add redactor contract and default redactor**

Create `packages/csharp/logging/BaseLogger/Redaction/IBaseLoggerRedactor.cs`:

```csharp
namespace BaseLogger.Redaction;

public interface IBaseLoggerRedactor
{
    BaseLoggerEvent Redact(BaseLoggerEvent ev);
    object? RedactProperty(string name, object? value);
}
```

Create `packages/csharp/logging/BaseLogger/Redaction/DefaultBaseLoggerRedactor.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BaseLogger.Redaction;

public sealed class DefaultBaseLoggerRedactor : IBaseLoggerRedactor
{
    private static readonly string[] SecretNameFragments =
    [
        "token", "secret", "key", "password", "authorization", "cookie", "signature", "credential"
    ];

    private static readonly Regex SecretValuePattern = new(
        "(Bearer\\s+)[A-Za-z0-9._\\-]+|sk-[A-Za-z0-9_\\-]+|sk-ant-[A-Za-z0-9_\\-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public BaseLoggerEvent Redact(BaseLoggerEvent ev)
    {
        var copy = new BaseLoggerEvent
        {
            EventId = ev.EventId,
            TimestampUtc = ev.TimestampUtc,
            Severity = ev.Severity,
            Category = ev.Category,
            EventKind = ev.EventKind,
            MessageTemplate = RedactString(ev.MessageTemplate),
            RenderedMessage = RedactString(ev.RenderedMessage),
            Exception = ev.Exception == null ? null : RedactString(ev.Exception),
            TraceId = ev.TraceId,
            SpanId = ev.SpanId,
            PrincipalId = RedactIdentifier(ev.PrincipalId),
            TaskId = ev.TaskId,
            SessionId = RedactIdentifier(ev.SessionId),
            RequestId = ev.RequestId,
            Component = ev.Component,
            ProcessId = ev.ProcessId,
            ThreadId = ev.ThreadId,
            Source = ev.Source,
            Sensitivity = ev.Sensitivity
        };

        foreach (var pair in ev.Properties)
            copy.Properties[pair.Key] = RedactProperty(pair.Key, pair.Value);

        return copy;
    }

    public object? RedactProperty(string name, object? value)
    {
        if (value == null)
            return null;

        if (IsSecretName(name))
            return "[redacted]";

        if (name.Equals("user_id", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("principal_id", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("session_id", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ip", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("remote_ip", StringComparison.OrdinalIgnoreCase))
            return StableHash(value.ToString() ?? string.Empty);

        return value is string text ? RedactString(text) : value;
    }

    private static bool IsSecretName(string name)
        => SecretNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static string RedactString(string value)
        => SecretValuePattern.Replace(value, match =>
            match.Value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? "Bearer [redacted]"
                : "[redacted]");

    private static string? RedactIdentifier(string? value)
        => string.IsNullOrWhiteSpace(value) || value.StartsWith("system:", StringComparison.OrdinalIgnoreCase)
            ? value
            : StableHash(value);

    private static string StableHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256:" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Add formatter contracts and implementations**

Create `packages/csharp/logging/BaseLogger/Formatting/IBaseLoggerFormatter.cs`:

```csharp
namespace BaseLogger.Formatting;

public interface IBaseLoggerFormatter
{
    string Format(BaseLoggerEvent ev);
}
```

Create `packages/csharp/logging/BaseLogger/Formatting/BaseLoggerJsonFormatter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaseLogger.Formatting;

public sealed class BaseLoggerJsonFormatter : IBaseLoggerFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format(BaseLoggerEvent ev) => JsonSerializer.Serialize(ev, Options);
}
```

Create `packages/csharp/logging/BaseLogger/Formatting/BaseLoggerTextFormatter.cs`:

```csharp
namespace BaseLogger.Formatting;

public sealed class BaseLoggerTextFormatter : IBaseLoggerFormatter
{
    public string Format(BaseLoggerEvent ev)
    {
        var trace = string.IsNullOrWhiteSpace(ev.TraceId) ? "-" : ev.TraceId;
        return $"[{ev.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff}] [{ev.Severity}] [{ev.Category}] [trace={trace}] {ev.RenderedMessage}";
    }
}
```

- [ ] **Step 6: Add sink contract and pipeline**

Create `packages/csharp/logging/BaseLogger/Sinks/IBaseLoggerSink.cs`:

```csharp
namespace BaseLogger.Sinks;

public interface IBaseLoggerSink : IDisposable
{
    void Write(BaseLoggerEvent ev);
    void Flush();
}
```

Create `packages/csharp/logging/BaseLogger/BaseLoggerPipeline.cs`:

```csharp
using BaseLogger.Formatting;
using BaseLogger.Redaction;
using BaseLogger.Sinks;

namespace BaseLogger;

public sealed class BaseLoggerPipeline : IDisposable
{
    private readonly BaseLoggerOptions _options;
    private readonly IBaseLoggerRedactor _redactor;
    private readonly IBaseLoggerFormatter _formatter;
    private readonly IReadOnlyList<IBaseLoggerSink> _sinks;
    private bool _disposed;

    public BaseLoggerPipeline(
        BaseLoggerOptions options,
        IBaseLoggerRedactor redactor,
        IBaseLoggerFormatter formatter,
        IEnumerable<IBaseLoggerSink> sinks)
    {
        _options = options;
        _redactor = redactor;
        _formatter = formatter;
        _sinks = sinks.ToList();
    }

    public void Write(BaseLoggerEvent ev)
    {
        if (!_options.Enabled || ev.Severity < _options.MinimumSeverity)
            return;

        ev.Component = string.IsNullOrWhiteSpace(ev.Component) ? _options.Component : ev.Component;
        ev.Source = string.IsNullOrWhiteSpace(ev.Source) ? _options.Source : ev.Source;

        var redacted = _redactor.Redact(ev);
        _ = _formatter.Format(redacted);

        foreach (var sink in _sinks)
        {
            try { sink.Write(redacted); }
            catch { }
        }
    }

    public void Flush()
    {
        foreach (var sink in _sinks)
        {
            try { sink.Flush(); }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Flush();
        foreach (var sink in _sinks)
            sink.Dispose();

        _disposed = true;
    }
}
```

- [ ] **Step 7: Run BaseLogger tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger
```

Expected: BaseLogger tests pass.

- [ ] **Step 8: Commit Task 2**

Run:

```powershell
git add packages/csharp/logging/BaseLogger/Redaction `
  packages/csharp/logging/BaseLogger/Formatting `
  packages/csharp/logging/BaseLogger/Sinks/IBaseLoggerSink.cs `
  packages/csharp/logging/BaseLogger/BaseLoggerPipeline.cs `
  packages/csharp/tests/unit/BaseLogger
git commit -m "feat: add BaseLogger pipeline redaction and formatting"
```

---

### Task 3: Console, Memory, Rolling File, And Database Sinks

**Files:**
- Create: `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerMemorySink.cs`
- Create: `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerConsoleSink.cs`
- Create: `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerRollingFileSink.cs`
- Create: `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerDatabaseSink.cs`
- Test: `packages/csharp/tests/unit/BaseLogger/BaseLoggerSinkTests.cs`

- [ ] **Step 1: Write failing sink tests**

Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerSinkTests.cs`:

```csharp
using BaseLogger;
using BaseLogger.Formatting;
using BaseLogger.Sinks;

namespace Unit.Tests.BaseLogger;

public class BaseLoggerSinkTests
{
    [Fact]
    public void MemorySink_KeepsOnlyConfiguredRecentEvents()
    {
        var sink = new BaseLoggerMemorySink(maxEntries: 2);

        sink.Write(Event("one"));
        sink.Write(Event("two"));
        sink.Write(Event("three"));

        sink.GetRecent(10).Select(e => e.RenderedMessage).Should().Equal("two", "three");
    }

    [Fact]
    public void RollingFileSink_RotatesWhenFileExceedsLimit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "baselogger_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "app.jsonl");
        var sink = new BaseLoggerRollingFileSink(
            path,
            new BaseLoggerJsonFormatter(),
            maxFileSizeBytes: 80,
            maxFiles: 2,
            retentionDays: 14);

        sink.Write(Event(new string('a', 120)));
        sink.Write(Event(new string('b', 120)));
        sink.Flush();

        File.Exists(path).Should().BeTrue();
        File.Exists(Path.Combine(dir, "app.1.jsonl")).Should().BeTrue();
    }

    [Fact]
    public void DatabaseSink_RejectsUnsafeTableName()
    {
        Action act = () => new BaseLoggerDatabaseSink(new object(), "logs;drop table audit_events");
        act.Should().Throw<ArgumentException>();
    }

    private static BaseLoggerEvent Event(string message) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        TimestampUtc = DateTimeOffset.UtcNow,
        Severity = BaseLoggerSeverity.Info,
        Category = "Test",
        RenderedMessage = message,
        Component = "test"
    };
}
```

- [ ] **Step 2: Run sink tests to verify they fail**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger.BaseLoggerSinkTests
```

Expected: build fails because sink classes do not exist.

- [ ] **Step 3: Add memory sink**

Create `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerMemorySink.cs`:

```csharp
namespace BaseLogger.Sinks;

public sealed class BaseLoggerMemorySink : IBaseLoggerSink
{
    private readonly object _gate = new();
    private readonly Queue<BaseLoggerEvent> _events = new();
    private readonly int _maxEntries;

    public BaseLoggerMemorySink(int maxEntries)
    {
        _maxEntries = Math.Max(1, maxEntries);
    }

    public void Write(BaseLoggerEvent ev)
    {
        lock (_gate)
        {
            _events.Enqueue(ev);
            while (_events.Count > _maxEntries)
                _events.Dequeue();
        }
    }

    public IReadOnlyList<BaseLoggerEvent> GetRecent(int count)
    {
        lock (_gate)
        {
            return _events.Reverse().Take(Math.Clamp(count, 1, _maxEntries)).Reverse().ToList();
        }
    }

    public void Flush() { }
    public void Dispose() { }
}
```

- [ ] **Step 4: Add console sink**

Create `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerConsoleSink.cs`:

```csharp
using BaseLogger.Formatting;

namespace BaseLogger.Sinks;

public sealed class BaseLoggerConsoleSink : IBaseLoggerSink
{
    private readonly IBaseLoggerFormatter _formatter;
    private readonly object _gate = new();

    public BaseLoggerConsoleSink(IBaseLoggerFormatter formatter)
    {
        _formatter = formatter;
    }

    public void Write(BaseLoggerEvent ev)
    {
        lock (_gate)
            Console.WriteLine(_formatter.Format(ev));
    }

    public void Flush() { }
    public void Dispose() { }
}
```

- [ ] **Step 5: Add rolling file sink**

Create `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerRollingFileSink.cs`:

```csharp
using System.Text;
using BaseLogger.Formatting;

namespace BaseLogger.Sinks;

public sealed class BaseLoggerRollingFileSink : IBaseLoggerSink
{
    private readonly string _path;
    private readonly IBaseLoggerFormatter _formatter;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxFiles;
    private readonly int _retentionDays;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private long _currentSize;

    public BaseLoggerRollingFileSink(
        string path,
        IBaseLoggerFormatter formatter,
        long maxFileSizeBytes,
        int maxFiles,
        int retentionDays)
    {
        ValidatePath(path);
        _path = path;
        _formatter = formatter;
        _maxFileSizeBytes = Math.Max(1024, maxFileSizeBytes);
        _maxFiles = Math.Max(1, maxFiles);
        _retentionDays = Math.Max(1, retentionDays);
        EnsureWriter();
    }

    public void Write(BaseLoggerEvent ev)
    {
        lock (_gate)
        {
            EnsureWriter();
            var text = _formatter.Format(ev);
            var bytes = Encoding.UTF8.GetByteCount(text + Environment.NewLine);
            if (_currentSize + bytes > _maxFileSizeBytes)
                Rotate();

            _writer!.WriteLine(text);
            _currentSize += bytes;
        }
    }

    public void Flush()
    {
        lock (_gate)
            _writer?.Flush();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void EnsureWriter()
    {
        if (_writer != null)
            return;

        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _currentSize = File.Exists(_path) ? new FileInfo(_path).Length : 0;
        _writer = new StreamWriter(_path, append: true, Encoding.UTF8);
    }

    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;

        var dir = Path.GetDirectoryName(_path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(_path);
        var ext = Path.GetExtension(_path);

        for (var i = _maxFiles - 1; i >= 1; i--)
        {
            var oldPath = Path.Combine(dir, $"{name}.{i}{ext}");
            var newPath = Path.Combine(dir, $"{name}.{i + 1}{ext}");
            if (File.Exists(newPath))
                File.Delete(newPath);
            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
        }

        var first = Path.Combine(dir, $"{name}.1{ext}");
        if (File.Exists(first))
            File.Delete(first);
        if (File.Exists(_path))
            File.Move(_path, first);

        foreach (var file in Directory.GetFiles(dir, $"{name}.*{ext}"))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-_retentionDays))
                File.Delete(file);
        }

        _currentSize = 0;
        EnsureWriter();
    }

    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Log path cannot be empty.", nameof(path));

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not ".log" and not ".txt" and not ".json" and not ".jsonl")
            throw new ArgumentException("Log path extension must be .log, .txt, .json, or .jsonl.", nameof(path));
    }
}
```

- [ ] **Step 6: Add database sink**

Create `packages/csharp/logging/BaseLogger/Sinks/BaseLoggerDatabaseSink.cs`:

```csharp
using System.Text.Json;

namespace BaseLogger.Sinks;

public sealed class BaseLoggerDatabaseSink : IBaseLoggerSink
{
    private readonly dynamic _db;
    private readonly string _tableName;
    private bool _initialized;

    public BaseLoggerDatabaseSink(object db, string tableName)
    {
        ValidateTableName(tableName);
        _db = db;
        _tableName = tableName;
    }

    public void Write(BaseLoggerEvent ev)
    {
        EnsureTable();
        _db.Execute($@"INSERT INTO {_tableName}
            (event_id, timestamp_utc, severity, category, event_kind, rendered_message, trace_id, component, properties)
            VALUES (@EventId, @TimestampUtc, @Severity, @Category, @EventKind, @RenderedMessage, @TraceId, @Component, @Properties)",
            new
            {
                ev.EventId,
                TimestampUtc = ev.TimestampUtc.UtcDateTime,
                Severity = ev.Severity.ToString(),
                ev.Category,
                ev.EventKind,
                ev.RenderedMessage,
                ev.TraceId,
                ev.Component,
                Properties = JsonSerializer.Serialize(ev.Properties)
            });
    }

    public void Flush() { }
    public void Dispose() { }

    private void EnsureTable()
    {
        if (_initialized)
            return;

        _db.Execute($@"CREATE TABLE IF NOT EXISTS {_tableName} (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            event_id TEXT NOT NULL,
            timestamp_utc TEXT NOT NULL,
            severity TEXT NOT NULL,
            category TEXT NOT NULL,
            event_kind TEXT NOT NULL,
            rendered_message TEXT NOT NULL,
            trace_id TEXT,
            component TEXT,
            properties TEXT
        )");
        _db.Execute($"CREATE INDEX IF NOT EXISTS ix_{_tableName}_trace ON {_tableName}(trace_id)");
        _db.Execute($"CREATE INDEX IF NOT EXISTS ix_{_tableName}_timestamp ON {_tableName}(timestamp_utc)");
        _initialized = true;
    }

    private static void ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be empty.", nameof(tableName));
        if (!char.IsLetter(tableName[0]) && tableName[0] != '_')
            throw new ArgumentException("Table name must start with a letter or underscore.", nameof(tableName));
        if (tableName.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
            throw new ArgumentException("Table name can only contain letters, digits, and underscores.", nameof(tableName));
    }
}
```

- [ ] **Step 7: Run sink tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger.BaseLoggerSinkTests
```

Expected: sink tests pass.

- [ ] **Step 8: Commit Task 3**

Run:

```powershell
git add packages/csharp/logging/BaseLogger/Sinks `
  packages/csharp/tests/unit/BaseLogger/BaseLoggerSinkTests.cs
git commit -m "feat: add BaseLogger operational sinks"
```

---

### Task 4: Microsoft.Extensions.Logging Provider

**Files:**
- Create: `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerProvider.cs`
- Create: `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerMicrosoftLogger.cs`
- Create: `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerLoggingBuilderExtensions.cs`
- Test: `packages/csharp/tests/unit/BaseLogger/BaseLoggerProviderTests.cs`

- [ ] **Step 1: Write failing provider tests**

Create `packages/csharp/tests/unit/BaseLogger/BaseLoggerProviderTests.cs`:

```csharp
using BaseLogger;
using BaseLogger.Correlation;
using BaseLogger.MicrosoftExtensions;
using BaseLogger.Sinks;
using Microsoft.Extensions.Logging;

namespace Unit.Tests.BaseLogger;

public class BaseLoggerProviderTests
{
    [Fact]
    public void Logger_WritesScopeAndCorrelationIntoBaseLoggerEvent()
    {
        var sink = new CapturingSink();
        var accessor = new AsyncLocalBaseLoggerCorrelationAccessor
        {
            Current = new BaseLoggerCorrelationContext
            {
                TraceId = "trace_provider",
                PrincipalId = "user_123",
                Component = "broker"
            }
        };
        using var provider = BaseLoggerProvider.CreateForTesting(sink, accessor);
        var logger = provider.CreateLogger("Broker.Test");

        using (logger.BeginScope(new Dictionary<string, object?> { ["task_id"] = "task_123" }))
        {
            logger.LogInformation("Hello {Name}", "world");
        }

        sink.Events.Should().HaveCount(1);
        sink.Events[0].TraceId.Should().Be("trace_provider");
        sink.Events[0].Category.Should().Be("Broker.Test");
        sink.Events[0].Properties["Name"].Should().Be("world");
        sink.Events[0].Properties["task_id"].Should().Be("task_123");
    }

    private sealed class CapturingSink : IBaseLoggerSink
    {
        public List<BaseLoggerEvent> Events { get; } = new();
        public void Write(BaseLoggerEvent ev) => Events.Add(ev);
        public void Flush() { }
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: Run provider test to verify it fails**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger.BaseLoggerProviderTests
```

Expected: build fails because provider types do not exist.

- [ ] **Step 3: Add provider**

Create `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerProvider.cs`:

```csharp
using BaseLogger.Correlation;
using BaseLogger.Formatting;
using BaseLogger.Redaction;
using BaseLogger.Sinks;
using Microsoft.Extensions.Logging;

namespace BaseLogger.MicrosoftExtensions;

public sealed class BaseLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly BaseLoggerPipeline _pipeline;
    private readonly IBaseLoggerCorrelationAccessor _correlationAccessor;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public BaseLoggerProvider(
        BaseLoggerPipeline pipeline,
        IBaseLoggerCorrelationAccessor correlationAccessor)
    {
        _pipeline = pipeline;
        _correlationAccessor = correlationAccessor;
    }

    public ILogger CreateLogger(string categoryName)
        => new BaseLoggerMicrosoftLogger(categoryName, _pipeline, _correlationAccessor, _scopeProvider);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        => _scopeProvider = scopeProvider;

    public void Dispose() => _pipeline.Dispose();

    public static BaseLoggerProvider CreateForTesting(
        IBaseLoggerSink sink,
        IBaseLoggerCorrelationAccessor accessor)
    {
        var pipeline = new BaseLoggerPipeline(
            new BaseLoggerOptions { Component = "test" },
            new DefaultBaseLoggerRedactor(),
            new BaseLoggerJsonFormatter(),
            [sink]);
        return new BaseLoggerProvider(pipeline, accessor);
    }
}
```

- [ ] **Step 4: Add internal Microsoft logger adapter**

Create `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerMicrosoftLogger.cs`:

```csharp
using BaseLogger.Correlation;
using Microsoft.Extensions.Logging;

namespace BaseLogger.MicrosoftExtensions;

internal sealed class BaseLoggerMicrosoftLogger : ILogger
{
    private readonly string _category;
    private readonly BaseLoggerPipeline _pipeline;
    private readonly IBaseLoggerCorrelationAccessor _correlationAccessor;
    private readonly IExternalScopeProvider _scopeProvider;

    public BaseLoggerMicrosoftLogger(
        string category,
        BaseLoggerPipeline pipeline,
        IBaseLoggerCorrelationAccessor correlationAccessor,
        IExternalScopeProvider scopeProvider)
    {
        _category = category;
        _pipeline = pipeline;
        _correlationAccessor = correlationAccessor;
        _scopeProvider = scopeProvider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => _scopeProvider.Push(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var correlation = _correlationAccessor.Current;
        var properties = ExtractProperties(state);
        _scopeProvider.ForEachScope((scope, target) => MergeScope(scope, target), properties);

        var ev = new BaseLoggerEvent
        {
            EventId = string.IsNullOrWhiteSpace(eventId.Name)
                ? Guid.NewGuid().ToString("N")
                : eventId.Name!,
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = MapSeverity(logLevel),
            Category = _category,
            EventKind = properties.TryGetValue("event_kind", out var kind) ? kind?.ToString() ?? "runtime" : "runtime",
            MessageTemplate = state?.ToString() ?? string.Empty,
            RenderedMessage = formatter(state, exception),
            Exception = exception?.ToString(),
            Properties = properties,
            TraceId = FirstNonEmpty(correlation.TraceId, ReadString(properties, "broker_trace_id")),
            PrincipalId = FirstNonEmpty(correlation.PrincipalId, ReadString(properties, "broker_principal_id")),
            TaskId = FirstNonEmpty(correlation.TaskId, ReadString(properties, "broker_task_id"), ReadString(properties, "task_id")),
            SessionId = FirstNonEmpty(correlation.SessionId, ReadString(properties, "broker_session_id")),
            RequestId = FirstNonEmpty(correlation.RequestId, ReadString(properties, "broker_request_id")),
            Component = correlation.Component ?? "",
            Source = "Microsoft.Extensions.Logging"
        };

        _pipeline.Write(ev);
    }

    private static Dictionary<string, object?> ExtractProperties<TState>(TState state)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (pair.Key != "{OriginalFormat}")
                    result[pair.Key] = pair.Value;
            }
        }
        return result;
    }

    private static void MergeScope(object? scope, Dictionary<string, object?> target)
    {
        if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
                target[pair.Key] = pair.Value;
        }
    }

    private static string? ReadString(Dictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static BaseLoggerSeverity MapSeverity(LogLevel level) => level switch
    {
        LogLevel.Trace => BaseLoggerSeverity.Trace,
        LogLevel.Debug => BaseLoggerSeverity.Debug,
        LogLevel.Information => BaseLoggerSeverity.Info,
        LogLevel.Warning => BaseLoggerSeverity.Warning,
        LogLevel.Error => BaseLoggerSeverity.Error,
        LogLevel.Critical => BaseLoggerSeverity.Critical,
        _ => BaseLoggerSeverity.None
    };
}
```

- [ ] **Step 5: Add `AddBaseLogger` extension**

Create `packages/csharp/logging/BaseLogger/MicrosoftExtensions/BaseLoggerLoggingBuilderExtensions.cs`:

```csharp
using BaseLogger.Correlation;
using BaseLogger.Formatting;
using BaseLogger.Redaction;
using BaseLogger.Sinks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BaseLogger.MicrosoftExtensions;

public static class BaseLoggerLoggingBuilderExtensions
{
    public static ILoggingBuilder AddBaseLogger(
        this ILoggingBuilder builder,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("BaseLogger").Get<BaseLoggerOptions>() ?? new BaseLoggerOptions();
        return builder.AddBaseLogger(options);
    }

    public static ILoggingBuilder AddBaseLogger(
        this ILoggingBuilder builder,
        BaseLoggerOptions options)
    {
        var accessor = new AsyncLocalBaseLoggerCorrelationAccessor();
        var formatter = string.Equals(options.Format, "text", StringComparison.OrdinalIgnoreCase)
            ? new BaseLoggerTextFormatter()
            : new BaseLoggerJsonFormatter();
        var sinks = BuildSinks(options, formatter);
        var pipeline = new BaseLoggerPipeline(options, new DefaultBaseLoggerRedactor(), formatter, sinks);
        builder.AddProvider(new BaseLoggerProvider(pipeline, accessor));
        return builder;
    }

    private static List<IBaseLoggerSink> BuildSinks(BaseLoggerOptions options, IBaseLoggerFormatter formatter)
    {
        var sinks = new List<IBaseLoggerSink>();
        if (options.Sinks.Console.Enabled)
            sinks.Add(new BaseLoggerConsoleSink(formatter));
        if (options.Sinks.RollingFile.Enabled)
            sinks.Add(new BaseLoggerRollingFileSink(
                options.Sinks.RollingFile.Path,
                formatter,
                options.Sinks.RollingFile.MaxFileSizeBytes,
                options.Sinks.RollingFile.MaxFiles,
                options.Sinks.RollingFile.RetentionDays));
        if (options.Sinks.Memory.Enabled)
            sinks.Add(new BaseLoggerMemorySink(options.Sinks.Memory.MaxEntries));
        return sinks;
    }
}
```

- [ ] **Step 6: Run provider tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.BaseLogger.BaseLoggerProviderTests
```

Expected: provider tests pass.

- [ ] **Step 7: Commit Task 4**

Run:

```powershell
git add packages/csharp/logging/BaseLogger/MicrosoftExtensions `
  packages/csharp/tests/unit/BaseLogger/BaseLoggerProviderTests.cs
git commit -m "feat: add Microsoft logging provider for BaseLogger"
```

---

### Task 5: Broker Correlation Middleware And Security Rejection Audit

**Files:**
- Create: `packages/csharp/broker/Middleware/BrokerCorrelationMiddleware.cs`
- Create: `packages/csharp/broker/Services/SecurityRejectionAuditRecorder.cs`
- Modify: `packages/csharp/broker/Middleware/AuditMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/ExceptionHandlingMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/BodySizeLimitMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/BrokerIpRateLimitMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/EncryptionMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/BrokerAuthMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/WorkerIdentityAuthMiddleware.cs`
- Test: `packages/csharp/tests/unit/Core/BrokerCorrelationAndAuditTests.cs`

- [ ] **Step 1: Write failing broker correlation tests**

Create `packages/csharp/tests/unit/Core/BrokerCorrelationAndAuditTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using Broker.Middleware;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Unit.Tests.Helpers;

namespace Unit.Tests.Core;

public class BrokerCorrelationAndAuditTests
{
    [Fact]
    public async Task CorrelationMiddleware_SetsBrokerTraceIdBeforeAuth()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/llm/chat";
        context.Response.Body = new MemoryStream();
        var nextCalled = false;
        var sut = new BrokerCorrelationMiddleware(next =>
        {
            nextCalled = true;
            next.Items.ContainsKey(BrokerCorrelationMiddleware.TraceIdItemKey).Should().BeTrue();
            return Task.CompletedTask;
        }, NullLogger<BrokerCorrelationMiddleware>.Instance);

        await sut.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Items[BrokerCorrelationMiddleware.TraceIdItemKey].Should().BeOfType<string>();
    }

    [Fact]
    public async Task ExceptionMiddleware_ReturnsSameBrokerTraceId()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/runtime/spec";
        context.Response.Body = new MemoryStream();
        context.Items[BrokerCorrelationMiddleware.TraceIdItemKey] = "trace_error";
        var sut = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("secret detail"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(context.Response.Body);
        doc.RootElement.GetProperty("traceId").GetString().Should().Be("trace_error");
        doc.RootElement.ToString().Should().NotContain("secret detail");
    }

    [Fact]
    public async Task BodySizeLimit_RecordsSecurityRejectionAudit()
    {
        using var db = TestDb.CreateInMemory();
        var audit = new AuditService(db);
        var recorder = new SecurityRejectionAuditRecorder(audit);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/llm/chat";
        context.Request.ContentLength = 2048;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.99");
        context.Items[BrokerCorrelationMiddleware.TraceIdItemKey] = "trace_reject";
        context.Response.Body = new MemoryStream();
        var sut = new BodySizeLimitMiddleware(
            _ => Task.CompletedTask,
            maxBodyBytes: 16,
            NullLogger<BodySizeLimitMiddleware>.Instance,
            recorder);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        var events = audit.GetTraceEvents("trace_reject");
        events.Should().Contain(e => e.EventType == "SECURITY_REJECTION");
        events.Single(e => e.EventType == "SECURITY_REJECTION").Details.Should().Contain("body_too_large");
    }
}
```

- [ ] **Step 2: Run broker tests to verify they fail**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.Core.BrokerCorrelationAndAuditTests
```

Expected: build fails because middleware/recorder types and constructor overloads do not exist.

- [ ] **Step 3: Add correlation middleware**

Create `packages/csharp/broker/Middleware/BrokerCorrelationMiddleware.cs`:

```csharp
namespace Broker.Middleware;

public sealed class BrokerCorrelationMiddleware
{
    public const string TraceIdItemKey = "broker_trace_id";
    public const string TraceSourceItemKey = "broker_trace_source";
    public const string RequestIdItemKey = "broker_request_id";

    private readonly RequestDelegate _next;
    private readonly ILogger<BrokerCorrelationMiddleware> _logger;

    public BrokerCorrelationMiddleware(RequestDelegate next, ILogger<BrokerCorrelationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = context.Request.Headers.TryGetValue("X-Broker-Trace-Id", out var header) &&
                      !string.IsNullOrWhiteSpace(header.FirstOrDefault())
            ? header.First()!
            : context.TraceIdentifier;

        context.Items[TraceIdItemKey] = traceId;
        context.Items[TraceSourceItemKey] = context.Request.Headers.ContainsKey("X-Broker-Trace-Id") ? "header" : "server";
        context.Items[RequestIdItemKey] = context.TraceIdentifier;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["broker_trace_id"] = traceId,
            ["broker_request_id"] = context.TraceIdentifier,
            ["path"] = context.Request.Path.Value ?? ""
        }))
        {
            await _next(context);
        }
    }
}

public static class BrokerCorrelationMiddlewareExtensions
{
    public static IApplicationBuilder UseBrokerCorrelation(this IApplicationBuilder builder)
        => builder.UseMiddleware<BrokerCorrelationMiddleware>();
}
```

- [ ] **Step 4: Add security rejection audit recorder**

Create `packages/csharp/broker/Services/SecurityRejectionAuditRecorder.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broker.Middleware;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class SecurityRejectionAuditRecorder
{
    private readonly IAuditService _auditService;

    public SecurityRejectionAuditRecorder(IAuditService auditService)
    {
        _auditService = auditService;
    }

    public void Record(HttpContext context, int statusCode, string reasonCode)
    {
        var traceId = context.Items.TryGetValue(BrokerCorrelationMiddleware.TraceIdItemKey, out var trace)
            ? trace as string ?? context.TraceIdentifier
            : context.TraceIdentifier;

        var details = JsonSerializer.Serialize(new
        {
            method = context.Request.Method,
            path = context.Request.Path.Value ?? "",
            status_code = statusCode,
            reason_code = reasonCode,
            client_hash = HashClient(context.Connection.RemoteIpAddress),
            component = "broker"
        });

        _auditService.RecordEvent(
            traceId,
            "SECURITY_REJECTION",
            resourceRef: context.Request.Path.Value,
            details: details);
    }

    private static string HashClient(IPAddress? address)
    {
        if (address == null)
            return "unknown";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(address.ToString()));
        return "sha256:" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
```

- [ ] **Step 5: Update AuditMiddleware and ExceptionHandlingMiddleware**

Modify `packages/csharp/broker/Middleware/AuditMiddleware.cs` so trace id is read with:

```csharp
var traceId = context.Items.TryGetValue(BrokerCorrelationMiddleware.TraceIdItemKey, out var traceObj)
    ? traceObj as string ?? context.TraceIdentifier
    : context.TraceIdentifier;
context.Items["audit_trace_id"] = traceId;
```

Modify `packages/csharp/broker/Middleware/ExceptionHandlingMiddleware.cs` response body:

```csharp
var traceId = context.Items.TryGetValue(BrokerCorrelationMiddleware.TraceIdItemKey, out var traceObj)
    ? traceObj as string ?? context.TraceIdentifier
    : context.TraceIdentifier;

_logger.LogError(
    ex,
    "Unhandled broker request exception: method={Method} path={Path} trace={TraceId}",
    context.Request.Method,
    context.Request.Path,
    traceId);

context.Response.Clear();
context.Response.StatusCode = StatusCodes.Status500InternalServerError;
context.Response.ContentType = "application/json; charset=utf-8";
var response = ApiResponseHelper.Error("Internal server error.", StatusCodes.Status500InternalServerError);
response.TraceId = traceId;
await context.Response.WriteAsJsonAsync(response);
```

- [ ] **Step 6: Add recorder to rejection middleware constructors and calls**

Modify `BodySizeLimitMiddleware` constructor:

```csharp
private readonly SecurityRejectionAuditRecorder? _securityAudit;

public BodySizeLimitMiddleware(
    RequestDelegate next,
    long maxBodyBytes,
    ILogger<BodySizeLimitMiddleware> logger,
    SecurityRejectionAuditRecorder? securityAudit = null)
{
    _next = next;
    _maxBodyBytes = maxBodyBytes;
    _logger = logger;
    _securityAudit = securityAudit;
}
```

Before writing `413`, call:

```csharp
_securityAudit?.Record(context, StatusCodes.Status413PayloadTooLarge, "body_too_large");
```

Modify `BrokerIpRateLimitMiddleware` by adding the same optional constructor dependency and call:

```csharp
_securityAudit?.Record(context, StatusCodes.Status429TooManyRequests, "rate_limit_exceeded");
```

Modify `EncryptionMiddleware` by adding the same optional constructor dependency and these calls at the corresponding rejection branches:

```csharp
_securityAudit?.Record(context, StatusCodes.Status400BadRequest, "invalid_envelope");
_securityAudit?.Record(context, StatusCodes.Status400BadRequest, "handshake_decryption_failed");
_securityAudit?.Record(context, StatusCodes.Status400BadRequest, "missing_session");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "session_key_not_found");
_securityAudit?.Record(context, StatusCodes.Status400BadRequest, "replay_detected");
_securityAudit?.Record(context, StatusCodes.Status400BadRequest, "decryption_failed");
```

Modify `BrokerAuthMiddleware` by adding the same optional constructor dependency and these calls at the corresponding rejection branches:

```csharp
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "missing_token");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "invalid_token");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "epoch_mismatch");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "revoked_jti");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "revoked_session");
```

Modify `WorkerIdentityAuthMiddleware` by adding the same optional constructor dependency and these calls at the corresponding rejection branches:

```csharp
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "missing_worker_auth_headers");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "invalid_worker_auth_timestamp");
_securityAudit?.Record(context, StatusCodes.Status401Unauthorized, "unauthorized_worker_identity");
```

- [ ] **Step 7: Run broker correlation tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter FullyQualifiedName~Unit.Tests.Core.BrokerCorrelationAndAuditTests
```

Expected: tests pass.

- [ ] **Step 8: Commit Task 5**

Run:

```powershell
git add packages/csharp/broker/Middleware `
  packages/csharp/broker/Services/SecurityRejectionAuditRecorder.cs `
  packages/csharp/tests/unit/Core/BrokerCorrelationAndAuditTests.cs
git commit -m "feat: add broker log correlation and rejection audit"
```

---

### Task 6: Host Setup, Sidecar Retention, Compatibility, Docs, And Full Verification

**Files:**
- Modify: `packages/csharp/broker/Program.cs`
- Modify: `packages/csharp/rag-service/Program.cs`
- Modify: `packages/csharp/rag-service/RagService.csproj`
- Modify: `packages/csharp/workers/line-worker/Program.cs`
- Modify: `packages/csharp/workers/line-worker/LineWorker.csproj`
- Modify: `packages/csharp/workers/file-worker/Program.cs`
- Modify: `packages/csharp/workers/file-worker/FileWorker.csproj`
- Modify: `packages/csharp/workers/browser-worker/Program.cs`
- Modify: `packages/csharp/workers/browser-worker/BrowserWorker.csproj`
- Modify: `packages/csharp/workers/execution-adapter-worker/Program.cs`
- Modify: `packages/csharp/workers/execution-adapter-worker/ExecutionAdapterWorker.csproj`
- Modify: `packages/csharp/workers/transport-tdx-worker/Program.cs`
- Modify: `packages/csharp/workers/transport-tdx-worker/TransportTdxWorker.csproj`
- Modify: `packages/csharp/workers/line-worker/start-sidecar-stack.ps1`
- Modify: `packages/csharp/logging/BaseLogger/BaseLogger.cs`
- Modify: `packages/csharp/logging/BaseLogger/README.md`
- Modify: `docs/manuals/current-technical-manual.zh-TW.md`
- Modify: `docs/manuals/current-user-manual.zh-TW.md`
- Modify: `docs/reports/follow-up-planning.zh-TW.md`

- [ ] **Step 1: Add BaseLogger provider to broker**

Modify `packages/csharp/broker/Program.cs` after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
using BaseLogger.MicrosoftExtensions;
```

Add:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddBaseLogger(builder.Configuration);
```

Register services:

```csharp
builder.Services.AddSingleton<Broker.Services.SecurityRejectionAuditRecorder>();
```

Add middleware immediately after `app` creation and before dev guard/body/auth middleware:

```csharp
app.UseBrokerCorrelation();
```

- [ ] **Step 2: Add BaseLogger configuration to broker appsettings**

Modify `packages/csharp/broker/appsettings.json`:

```json
"BaseLogger": {
  "Enabled": true,
  "MinimumSeverity": "Info",
  "Format": "json",
  "Component": "broker",
  "RedactionProfile": "production",
  "Sinks": {
    "Console": { "Enabled": true },
    "RollingFile": {
      "Enabled": true,
      "Path": ".run/logs/broker.jsonl",
      "MaxFileSizeBytes": 10485760,
      "MaxFiles": 10,
      "RetentionDays": 14
    },
    "Database": { "Enabled": false, "TableName": "operational_logs" },
    "Memory": { "Enabled": true, "MaxEntries": 500 }
  }
}
```

- [ ] **Step 3: Add provider to rag-service and workers**

Add a direct BaseLogger project reference to each csproj that will call `AddBaseLogger`.

For `packages/csharp/rag-service/RagService.csproj`, add:

```xml
    <ProjectReference Include="..\logging\BaseLogger\BaseLogger.csproj" />
```

For each worker csproj, add:

```xml
    <ProjectReference Include="..\..\logging\BaseLogger\BaseLogger.csproj" />
```

Worker csproj files:

- `packages/csharp/workers/line-worker/LineWorker.csproj`

- `packages/csharp/workers/file-worker/FileWorker.csproj`

- `packages/csharp/workers/browser-worker/BrowserWorker.csproj`

- `packages/csharp/workers/execution-adapter-worker/ExecutionAdapterWorker.csproj`

- `packages/csharp/workers/transport-tdx-worker/TransportTdxWorker.csproj`

For each `Program.cs` listed in this task, add:

```csharp
using BaseLogger.MicrosoftExtensions;
```

After creating the builder or logging builder, add:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddBaseLogger(builder.Configuration);
```

For worker programs that use `Host.CreateDefaultBuilder`, add the provider inside `ConfigureLogging`:

```csharp
logging.ClearProviders();
logging.AddBaseLogger(context.Configuration);
```

- [ ] **Step 4: Update sidecar retention**

Modify `packages/csharp/workers/line-worker/start-sidecar-stack.ps1`:

Replace the block that deletes `$brokerLog`, `$brokerErrLog`, `$workerLog`, `$workerErrLog` with:

```powershell
$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$runLogDir = Join-Path $logDir $runId
New-Item -ItemType Directory -Force -Path $runLogDir | Out-Null
$brokerLog = Join-Path $runLogDir "broker.out.log"
$brokerErrLog = Join-Path $runLogDir "broker.err.log"
$workerLog = Join-Path $runLogDir "line-worker.out.log"
$workerErrLog = Join-Path $runLogDir "line-worker.err.log"

$retentionCutoff = (Get-Date).AddDays(-14)
Get-ChildItem -Path $logDir -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt $retentionCutoff } |
    Remove-Item -Recurse -Force
```

Keep tunnel logs under the run-specific directory too, so each sidecar start has an evidence folder.

- [ ] **Step 5: Mark old BaseLogger names obsolete**

Modify `packages/csharp/logging/BaseLogger/BaseLogger.cs`:

```csharp
[Obsolete("Use BaseLoggerSeverity for new code.")]
public enum LogLevel
```

```csharp
[Obsolete("Use BaseLoggerEvent for new code.")]
public class LogEntry
```

```csharp
[Obsolete("Use Microsoft.Extensions.Logging.ILogger<T> at application boundaries or BaseLoggerPipeline internally.")]
public interface ILogger
```

```csharp
[Obsolete("Use BaseLoggerPipeline for new code.")]
public class Logger : ILogger, IDisposable
```

```csharp
[Obsolete("Use IBaseLoggerSink for new code.")]
public interface ILogTarget
```

```csharp
[Obsolete("Use Microsoft.Extensions.Logging plus AddBaseLogger for application code.")]
public static class Log
```

- [ ] **Step 6: Update docs**

Update `packages/csharp/logging/BaseLogger/README.md` with a new first section:

````markdown
## Current API Direction

`BaseLogger` is the package and namespace name. New code should not use the old `ILogger`, `Logger`, `LogLevel`, `LogEntry`, `ILogTarget`, or static `Log` types directly.

Application code should continue to depend on `Microsoft.Extensions.Logging.ILogger<T>`.
Hosts integrate BaseLogger with:

```csharp
builder.Logging.ClearProviders();
builder.Logging.AddBaseLogger(builder.Configuration);
````

BaseLogger-specific extension points use explicit names such as `BaseLoggerPipeline`, `BaseLoggerEvent`, `BaseLoggerSeverity`, `IBaseLoggerSink`, `IBaseLoggerRedactor`, and `BaseLoggerProvider`.

````
Update manuals:

- `docs/manuals/current-technical-manual.zh-TW.md`: add a "BaseLogger / Audit / Observability" section that explains runtime log vs audit vs raw interaction log vs observation.
- `docs/manuals/current-user-manual.zh-TW.md`: update troubleshooting log paths to note per-run sidecar log folders and 14-day retention.
- `docs/reports/follow-up-planning.zh-TW.md`: mention that BaseLogger governance design now has an implementation plan.

- [ ] **Step 7: Run focused tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "FullyQualifiedName~Unit.Tests.BaseLogger|FullyQualifiedName~Unit.Tests.Core.BrokerCorrelationAndAuditTests"
````

Expected: BaseLogger and broker correlation tests pass.

- [ ] **Step 8: Run repository build**

Run:

```powershell
dotnet build packages/csharp/ControlPlane.slnx
```

Expected: build exits 0. Existing warnings are acceptable unless new errors appear.

- [ ] **Step 9: Run broker unit test entrypoint**

Run:

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
```

Expected: command exits 0 and prints final pass summary.

- [ ] **Step 10: Cleanup test artifacts**

Run:

```powershell
Remove-Item -LiteralPath packages/csharp/broker/broker.db -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath packages/csharp/broker/broker.db-shm -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath packages/csharp/broker/broker.db-wal -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath .test-output -Recurse -Force -ErrorAction SilentlyContinue
```

Expected: command exits 0 or silently skips missing files.

- [ ] **Step 11: Commit Task 6**

Run:

```powershell
git add packages/csharp/broker/Program.cs `
  packages/csharp/broker/appsettings.json `
  packages/csharp/rag-service/Program.cs `
  packages/csharp/rag-service/RagService.csproj `
  packages/csharp/workers/line-worker/Program.cs `
  packages/csharp/workers/line-worker/LineWorker.csproj `
  packages/csharp/workers/file-worker/Program.cs `
  packages/csharp/workers/file-worker/FileWorker.csproj `
  packages/csharp/workers/browser-worker/Program.cs `
  packages/csharp/workers/browser-worker/BrowserWorker.csproj `
  packages/csharp/workers/execution-adapter-worker/Program.cs `
  packages/csharp/workers/execution-adapter-worker/ExecutionAdapterWorker.csproj `
  packages/csharp/workers/transport-tdx-worker/Program.cs `
  packages/csharp/workers/transport-tdx-worker/TransportTdxWorker.csproj `
  packages/csharp/workers/line-worker/start-sidecar-stack.ps1 `
  packages/csharp/logging/BaseLogger/BaseLogger.cs `
  packages/csharp/logging/BaseLogger/README.md `
  docs/manuals/current-technical-manual.zh-TW.md `
  docs/manuals/current-user-manual.zh-TW.md `
  docs/reports/follow-up-planning.zh-TW.md
git commit -m "feat: integrate BaseLogger governance pipeline"
```

---

## Final Verification

- [ ] Run complete unit test command:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
```

Expected: all xUnit tests pass.

- [ ] Run broker console tests:

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
```

Expected: all broker console tests pass.

- [ ] Run solution build:

```powershell
dotnet build packages/csharp/ControlPlane.slnx
```

Expected: exit code 0.

- [ ] Confirm no accidental staging of unrelated files:

```powershell
git status --short
```

Expected: only intended BaseLogger/logging changes remain uncommitted before final commit; unrelated pre-existing RAG/signing files remain unstaged unless the user explicitly asks to include them.

## Spec Coverage Self-Review

- Naming rule covered by Tasks 1 and 6.

- Provider integration covered by Task 4 and Task 6.

- Redaction covered by Task 2.

- Sinks and retention covered by Task 3 and Task 6.

- Correlation model covered by Task 5.

- Security rejection audit covered by Task 5.

- Runtime log vs audit boundaries covered by Task 6 documentation.

- Compatibility wrapper covered by Task 6.
