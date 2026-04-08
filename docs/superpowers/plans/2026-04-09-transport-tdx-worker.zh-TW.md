# Transport-TDX Worker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把目前 broker 內的 TDX 交通查詢邏輯遷移成獨立的 `transport-tdx worker`，並讓 broker 對外改以統一的 `transport.query` capability 提供查詢、追問與範圍回答。

**Architecture:** broker 只保留 capability schema、worker selection、auth/audit、request/response validation；`transport-tdx worker` 實作 sufficiency analysis、缺資訊追問、範圍回答、TDX token/adapter 與 provider-specific normalization。第一批保留既有 `travel_*` 路徑作 compatibility layer，但新的主路徑改為 `transport.query`。

**Tech Stack:** C#/.NET 8、broker-core、function-pool、worker-sdk、xUnit、現有 broker verify、TDX OAuth2 client credentials。

---

## File Structure

### Create

- `packages/csharp/broker-core/Contracts/Transport/TransportQueryRequest.cs`
- `packages/csharp/broker-core/Contracts/Transport/TransportQueryResponse.cs`
- `packages/csharp/broker-core/Contracts/Transport/TransportFollowUpOption.cs`
- `packages/csharp/workers/transport-tdx-worker/TransportTdxWorker.csproj`
- `packages/csharp/workers/transport-tdx-worker/Program.cs`
- `packages/csharp/workers/transport-tdx-worker/Handlers/TransportQueryHandler.cs`
- `packages/csharp/workers/transport-tdx-worker/Services/TransportQuerySufficiencyAnalyzer.cs`
- `packages/csharp/workers/transport-tdx-worker/Services/TransportFollowUpBuilder.cs`
- `packages/csharp/workers/transport-tdx-worker/Services/TransportRangeAnswerBuilder.cs`
- `packages/csharp/workers/transport-tdx-worker/Services/TdxTransportProvider.cs`
- `packages/csharp/workers/transport-tdx-worker/Services/TdxTokenService.cs`
- `packages/csharp/workers/transport-tdx-worker/Services/TdxEntityResolver.cs`
- `packages/csharp/tests/unit/Transport/TransportQuerySufficiencyAnalyzerTests.cs`
- `packages/csharp/tests/unit/Transport/TransportFollowUpBuilderTests.cs`
- `packages/csharp/tests/unit/Transport/TransportRangeAnswerBuilderTests.cs`
- `packages/csharp/tests/integration/Transport/TransportQueryCapabilityTests.cs`
- `packages/csharp/tests/integration/Transport/TransportCompatibilityRouteTests.cs`
- `packages/csharp/broker/tool-specs/transport.query/tool.json`

### Modify

- `packages/csharp/ControlPlane.slnx`
- `packages/csharp/broker/Program.cs`
- `packages/csharp/broker/Services/HighLevelQueryToolMediator.cs`
- `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- `packages/csharp/broker/Adapters/InProcessDispatcher.cs`
- `packages/csharp/broker/verify/Program.cs`
- `packages/csharp/broker/Handlers/Travel/TravelRailSearchHandler.cs`
- `packages/csharp/broker/Handlers/Travel/TravelHsrSearchHandler.cs`
- `packages/csharp/broker/Handlers/Travel/TravelBusSearchHandler.cs`
- `packages/csharp/broker/Handlers/Travel/TravelFlightSearchHandler.cs`
- `packages/csharp/broker/tool-specs/travel.rail.search/tool.json`
- `packages/csharp/broker/tool-specs/travel.hsr.search/tool.json`
- `packages/csharp/broker/tool-specs/travel.bus.search/tool.json`
- `packages/csharp/broker/tool-specs/travel.flight.search/tool.json`
- `packages/csharp/worker-sdk/WorkerHostOptions.cs`
- `packages/csharp/workers/line-worker/Program.cs`

### Reuse / Migrate logic from

- `packages/csharp/broker/Handlers/Travel/TdxTravelHelper.cs`
- `packages/csharp/broker/Handlers/Travel/TdxBusTravelHelper.cs`
- `packages/csharp/broker/Handlers/Travel/TravelTdxResponseHelper.cs`

---

### Task 1: 定義 transport.query contract

**Files:**
- Create: `packages/csharp/broker-core/Contracts/Transport/TransportQueryRequest.cs`
- Create: `packages/csharp/broker-core/Contracts/Transport/TransportQueryResponse.cs`
- Create: `packages/csharp/broker-core/Contracts/Transport/TransportFollowUpOption.cs`
- Create: `packages/csharp/broker/tool-specs/transport.query/tool.json`
- Modify: `packages/csharp/ControlPlane.slnx`
- Test: `packages/csharp/tests/unit/Transport/TransportContractSerializationTests.cs`

- [ ] **Step 1: Write the failing contract serialization test**

```csharp
using System.Text.Json;
using BrokerCore.Contracts.Transport;
using FluentAssertions;

namespace Unit.Tests.Transport;

public class TransportContractSerializationTests
{
    [Fact]
    public void TransportQueryResponse_serializes_follow_up_shape()
    {
        var response = new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.NeedFollowUp,
            Answer = "請問你要查哪一天？",
            MissingFields = ["date"],
            FollowUp = new TransportFollowUp
            {
                Question = "請問你要查哪一天？",
                FollowUpToken = "token-1",
                Options =
                [
                    new TransportFollowUpOption { Id = "today", Label = "今天" },
                    new TransportFollowUpOption { Id = "tomorrow", Label = "明天" }
                ]
            }
        };

        var json = JsonSerializer.Serialize(response);

        json.Should().Contain("\"resultType\":\"need_follow_up\"");
        json.Should().Contain("\"missingFields\":[\"date\"]");
        json.Should().Contain("\"followUpToken\":\"token-1\"");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TransportContractSerializationTests -v minimal`  
Expected: FAIL with missing `BrokerCore.Contracts.Transport` types.

- [ ] **Step 3: Write minimal transport contracts**

```csharp
namespace BrokerCore.Contracts.Transport;

public enum TransportResultType
{
    FinalAnswer,
    NeedFollowUp,
    RangeAnswer
}

public sealed class TransportQueryRequest
{
    public string Capability { get; set; } = "transport.query";
    public string TransportMode { get; set; } = "auto";
    public string UserQuery { get; set; } = string.Empty;
    public string Locale { get; set; } = "zh-TW";
    public string Channel { get; set; } = "line";
    public Dictionary<string, string?> Context { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public TransportInteraction? Interaction { get; set; }
}

public sealed class TransportInteraction
{
    public string? ConversationId { get; set; }
    public string? FollowUpToken { get; set; }
    public string? SelectedOptionId { get; set; }
}

public sealed class TransportQueryResponse
{
    public string ResultType => ResultTypeValue switch
    {
        TransportResultType.FinalAnswer => "final_answer",
        TransportResultType.NeedFollowUp => "need_follow_up",
        _ => "range_answer"
    };

    public TransportResultType ResultTypeValue { get; set; }
    public string Answer { get; set; } = string.Empty;
    public Dictionary<string, object?> NormalizedQuery { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> MissingFields { get; set; } = [];
    public TransportFollowUp? FollowUp { get; set; }
    public Dictionary<string, object?>? RangeContext { get; set; }
    public List<Dictionary<string, object?>> Records { get; set; } = [];
    public List<Dictionary<string, string>> Evidence { get; set; } = [];
    public Dictionary<string, object?> ProviderMetadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TransportFollowUp
{
    public string Question { get; set; } = string.Empty;
    public string FollowUpToken { get; set; } = string.Empty;
    public List<TransportFollowUpOption> Options { get; set; } = [];
}

public sealed class TransportFollowUpOption
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Add the tool spec**

```json
{
  "name": "transport.query",
  "description": "Unified transport query capability with follow-up and range-answer support.",
  "version": "1.0.0",
  "execution": {
    "route": "transport_query",
    "registeredRoute": "transport_query"
  },
  "input_schema": {
    "type": "object",
    "required": ["transport_mode", "user_query"],
    "properties": {
      "transport_mode": { "type": "string" },
      "user_query": { "type": "string" },
      "locale": { "type": "string" },
      "channel": { "type": "string" }
    }
  }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TransportContractSerializationTests -v minimal`  
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add packages/csharp/broker-core/Contracts/Transport packages/csharp/broker/tool-specs/transport.query/tool.json packages/csharp/ControlPlane.slnx packages/csharp/tests/unit/Transport/TransportContractSerializationTests.cs
git commit -m "feat: add transport query contract"
```

---

### Task 2: 建立 transport-tdx worker 專案骨架

**Files:**
- Create: `packages/csharp/workers/transport-tdx-worker/TransportTdxWorker.csproj`
- Create: `packages/csharp/workers/transport-tdx-worker/Program.cs`
- Create: `packages/csharp/workers/transport-tdx-worker/Handlers/TransportQueryHandler.cs`
- Modify: `packages/csharp/ControlPlane.slnx`
- Test: `packages/csharp/tests/integration/Transport/TransportWorkerRegistrationTests.cs`

- [ ] **Step 1: Write the failing worker registration test**

```csharp
using FluentAssertions;

namespace Integration.Tests.Transport;

public class TransportWorkerRegistrationTests
{
    [Fact]
    public async Task Transport_worker_registers_transport_query_capability()
    {
        var workerInfo = await TransportTestHarness.StartTransportWorkerAndReadRegistrationAsync();

        workerInfo.WorkerType.Should().Be("transport-tdx");
        workerInfo.Capabilities.Should().Contain("transport.query");
        workerInfo.Provider.Should().Be("tdx");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter TransportWorkerRegistrationTests -v minimal`  
Expected: FAIL because worker project/harness does not exist.

- [ ] **Step 3: Add worker project and minimal registration**

`TransportTdxWorker.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\worker-sdk\\WorkerSdk.csproj" />
    <ProjectReference Include="..\\..\\broker-core\\BrokerCore.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs`

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using WorkerSdk;

var options = new WorkerHostOptions
{
    WorkerName = "transport-tdx",
    Capabilities = ["transport.query", "transport.resolve"]
};

var host = new WorkerHost(options, NullLogger<WorkerHost>.Instance);
host.RegisterHandler("transport_query", new Handlers.TransportQueryHandler());
await host.RunAsync();
```

`TransportQueryHandler.cs`

```csharp
using System.Text.Json;
using WorkerSdk;

namespace TransportTdxWorker.Handlers;

public sealed class TransportQueryHandler : ICapabilityHandler
{
    public string CapabilityId => "transport_query";

    public Task<string> HandleAsync(string inputJson, CancellationToken cancellationToken = default)
    {
        var response = JsonSerializer.Serialize(new
        {
            resultType = "need_follow_up",
            answer = "transport-tdx worker skeleton",
            missingFields = new[] { "date" }
        });

        return Task.FromResult(response);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter TransportWorkerRegistrationTests -v minimal`  
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add packages/csharp/workers/transport-tdx-worker packages/csharp/ControlPlane.slnx packages/csharp/tests/integration/Transport/TransportWorkerRegistrationTests.cs
git commit -m "feat: add transport tdx worker skeleton"
```

---

### Task 3: 實作 sufficiency analyzer 與追問/範圍回覆 builder

**Files:**
- Create: `packages/csharp/workers/transport-tdx-worker/Services/TransportQuerySufficiencyAnalyzer.cs`
- Create: `packages/csharp/workers/transport-tdx-worker/Services/TransportFollowUpBuilder.cs`
- Create: `packages/csharp/workers/transport-tdx-worker/Services/TransportRangeAnswerBuilder.cs`
- Modify: `packages/csharp/workers/transport-tdx-worker/Handlers/TransportQueryHandler.cs`
- Test: `packages/csharp/tests/unit/Transport/TransportQuerySufficiencyAnalyzerTests.cs`
- Test: `packages/csharp/tests/unit/Transport/TransportFollowUpBuilderTests.cs`
- Test: `packages/csharp/tests/unit/Transport/TransportRangeAnswerBuilderTests.cs`

- [ ] **Step 1: Write failing sufficiency tests**

```csharp
using FluentAssertions;
using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TransportQuerySufficiencyAnalyzerTests
{
    [Fact]
    public void Rail_query_without_origin_or_destination_is_insufficient()
    {
        var analyzer = new TransportQuerySufficiencyAnalyzer();
        var verdict = analyzer.Analyze("rail", "幫我查火車", new Dictionary<string, string?>());

        verdict.State.Should().Be(TransportQueryState.Insufficient);
        verdict.MissingFields.Should().Contain(["origin", "destination"]);
    }

    [Fact]
    public void Rail_query_without_date_is_partially_sufficient()
    {
        var analyzer = new TransportQuerySufficiencyAnalyzer();
        var verdict = analyzer.Analyze("rail", "板橋到高雄的火車", new Dictionary<string, string?>
        {
            ["origin"] = "板橋",
            ["destination"] = "高雄"
        });

        verdict.State.Should().Be(TransportQueryState.PartiallySufficient);
        verdict.MissingFields.Should().Contain("date");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "TransportQuerySufficiencyAnalyzerTests|TransportFollowUpBuilderTests|TransportRangeAnswerBuilderTests" -v minimal`  
Expected: FAIL because analyzer/builders do not exist.

- [ ] **Step 3: Implement analyzer and builders**

`TransportQuerySufficiencyAnalyzer.cs`

```csharp
namespace TransportTdxWorker.Services;

public enum TransportQueryState
{
    Sufficient,
    PartiallySufficient,
    Insufficient
}

public sealed class TransportQueryVerdict
{
    public TransportQueryState State { get; init; }
    public List<string> MissingFields { get; init; } = [];
    public Dictionary<string, object?> NormalizedQuery { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TransportQuerySufficiencyAnalyzer
{
    public TransportQueryVerdict Analyze(string mode, string userQuery, IDictionary<string, string?> context)
    {
        var missing = new List<string>();

        if (mode is "rail" or "hsr" or "flight" or "ship")
        {
            if (string.IsNullOrWhiteSpace(Get(context, "origin"))) missing.Add("origin");
            if (string.IsNullOrWhiteSpace(Get(context, "destination"))) missing.Add("destination");
        }
        else if (mode == "bus")
        {
            if (string.IsNullOrWhiteSpace(Get(context, "city"))) missing.Add("city");
            if (string.IsNullOrWhiteSpace(Get(context, "route"))) missing.Add("route");
        }
        else if (mode == "bike")
        {
            if (string.IsNullOrWhiteSpace(Get(context, "city")) && string.IsNullOrWhiteSpace(Get(context, "geo_point")))
            {
                missing.Add("city");
            }
        }

        if (missing.Count > 0)
        {
            return new TransportQueryVerdict { State = TransportQueryState.Insufficient, MissingFields = missing };
        }

        var partialMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(Get(context, "date"))) partialMissing.Add("date");

        return new TransportQueryVerdict
        {
            State = partialMissing.Count > 0 ? TransportQueryState.PartiallySufficient : TransportQueryState.Sufficient,
            MissingFields = partialMissing,
            NormalizedQuery = new Dictionary<string, object?>
            {
                ["transport_mode"] = mode,
                ["origin"] = Get(context, "origin"),
                ["destination"] = Get(context, "destination"),
                ["date"] = Get(context, "date"),
                ["time_range"] = Get(context, "time_range"),
                ["city"] = Get(context, "city"),
                ["route"] = Get(context, "route")
            }
        };
    }

    private static string? Get(IDictionary<string, string?> context, string key)
        => context.TryGetValue(key, out var value) ? value : null;
}
```

`TransportFollowUpBuilder.cs`

```csharp
using BrokerCore.Contracts.Transport;

namespace TransportTdxWorker.Services;

public sealed class TransportFollowUpBuilder
{
    public TransportFollowUp Build(IReadOnlyList<string> missingFields)
    {
        if (missingFields.Contains("date"))
        {
            return new TransportFollowUp
            {
                Question = "請問你要查哪一天？",
                FollowUpToken = Guid.NewGuid().ToString("N"),
                Options =
                [
                    new TransportFollowUpOption { Id = "today", Label = "今天" },
                    new TransportFollowUpOption { Id = "tomorrow", Label = "明天" },
                    new TransportFollowUpOption { Id = "custom_date", Label = "指定日期" },
                    new TransportFollowUpOption { Id = "nearest_available", Label = "先看最近可用班次" }
                ]
            };
        }

        return new TransportFollowUp
        {
            Question = "目前資訊不足，請補充查詢條件。",
            FollowUpToken = Guid.NewGuid().ToString("N"),
            Options = [new TransportFollowUpOption { Id = "restatement", Label = "我重新描述" }]
        };
    }
}
```

`TransportRangeAnswerBuilder.cs`

```csharp
using BrokerCore.Contracts.Transport;

namespace TransportTdxWorker.Services;

public sealed class TransportRangeAnswerBuilder
{
    public TransportQueryResponse Build(TransportQueryVerdict verdict, IReadOnlyList<Dictionary<string, object?>> records)
    {
        return new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.RangeAnswer,
            Answer = "我先依目前資訊提供較寬範圍的結果；如果你指定日期或時段，我可以再縮小。",
            MissingFields = verdict.MissingFields,
            NormalizedQuery = verdict.NormalizedQuery,
            RangeContext = new Dictionary<string, object?>
            {
                ["assumptions"] = new[] { "date=nearest_available" },
                ["scope_note"] = "以目前可查到的近期結果先回答"
            },
            Records = records.ToList()
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "TransportQuerySufficiencyAnalyzerTests|TransportFollowUpBuilderTests|TransportRangeAnswerBuilderTests" -v minimal`  
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add packages/csharp/workers/transport-tdx-worker/Services packages/csharp/workers/transport-tdx-worker/Handlers/TransportQueryHandler.cs packages/csharp/tests/unit/Transport
git commit -m "feat: add transport query sufficiency policies"
```

---

### Task 4: 將既有 TDX helper 遷入 worker 並建立 provider service

**Files:**
- Create: `packages/csharp/workers/transport-tdx-worker/Services/TdxTokenService.cs`
- Create: `packages/csharp/workers/transport-tdx-worker/Services/TdxEntityResolver.cs`
- Create: `packages/csharp/workers/transport-tdx-worker/Services/TdxTransportProvider.cs`
- Modify: `packages/csharp/workers/transport-tdx-worker/Handlers/TransportQueryHandler.cs`
- Modify: `packages/csharp/broker/Handlers/Travel/TdxTravelHelper.cs`
- Modify: `packages/csharp/broker/Handlers/Travel/TdxBusTravelHelper.cs`
- Modify: `packages/csharp/broker/Handlers/Travel/TravelTdxResponseHelper.cs`
- Test: `packages/csharp/tests/unit/Transport/TdxTransportProviderTests.cs`

- [ ] **Step 1: Write failing provider tests**

```csharp
using FluentAssertions;
using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TdxTransportProviderTests
{
    [Fact]
    public async Task Rail_query_returns_records_and_tdx_evidence()
    {
        var provider = TransportTestFactory.CreateProvider();
        var response = await provider.QueryAsync(new Dictionary<string, object?>
        {
            ["transport_mode"] = "rail",
            ["origin"] = "板橋",
            ["destination"] = "高雄",
            ["date"] = "2026-04-10"
        });

        response.Evidence.Should().ContainSingle(x => x["source"] == "TDX");
        response.Records.Should().NotBeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TdxTransportProviderTests -v minimal`  
Expected: FAIL because provider service does not exist.

- [ ] **Step 3: Implement provider service with migrated helpers**

`TdxTransportProvider.cs`

```csharp
using BrokerCore.Contracts.Transport;

namespace TransportTdxWorker.Services;

public sealed class TdxTransportProvider
{
    private readonly TdxTokenService _tokenService;
    private readonly TdxEntityResolver _resolver;

    public TdxTransportProvider(TdxTokenService tokenService, TdxEntityResolver resolver)
    {
        _tokenService = tokenService;
        _resolver = resolver;
    }

    public async Task<TransportQueryResponse> QueryAsync(Dictionary<string, object?> normalizedQuery, CancellationToken cancellationToken = default)
    {
        var mode = normalizedQuery["transport_mode"]?.ToString() ?? "auto";

        var records = mode switch
        {
            "rail" => await QueryRailAsync(normalizedQuery, cancellationToken),
            "hsr" => await QueryHsrAsync(normalizedQuery, cancellationToken),
            "bus" => await QueryBusAsync(normalizedQuery, cancellationToken),
            "flight" => await QueryFlightAsync(normalizedQuery, cancellationToken),
            _ => []
        };

        return new TransportQueryResponse
        {
            ResultTypeValue = TransportResultType.FinalAnswer,
            Answer = "已根據 TDX 資料整理結果。",
            NormalizedQuery = normalizedQuery,
            Records = records,
            Evidence =
            [
                new Dictionary<string, string>
                {
                    ["source"] = "TDX",
                    ["kind"] = "transport.provider"
                }
            ],
            ProviderMetadata = new Dictionary<string, object?>
            {
                ["provider"] = "tdx",
                ["mode"] = mode
            }
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter TdxTransportProviderTests -v minimal`  
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add packages/csharp/workers/transport-tdx-worker/Services packages/csharp/workers/transport-tdx-worker/Handlers/TransportQueryHandler.cs packages/csharp/tests/unit/Transport/TdxTransportProviderTests.cs
git commit -m "feat: add transport tdx provider service"
```

---

### Task 5: 讓 broker 以 transport.query capability 派工到 worker

**Files:**
- Modify: `packages/csharp/broker/Program.cs`
- Modify: `packages/csharp/broker/Services/HighLevelQueryToolMediator.cs`
- Modify: `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- Modify: `packages/csharp/broker/Adapters/InProcessDispatcher.cs`
- Test: `packages/csharp/tests/integration/Transport/TransportQueryCapabilityTests.cs`

- [ ] **Step 1: Write failing integration test for broker capability dispatch**

```csharp
using FluentAssertions;

namespace Integration.Tests.Transport;

public class TransportQueryCapabilityTests
{
    [Fact]
    public async Task Broker_dispatches_transport_query_to_transport_worker()
    {
        var response = await TransportTestHarness.QueryThroughBrokerAsync("?rail 板橋到高雄的火車");

        response.ResultType.Should().Be("range_answer");
        response.ProviderMetadata["provider"].Should().Be("tdx");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter TransportQueryCapabilityTests -v minimal`  
Expected: FAIL because broker still routes to old `travel_*` paths.

- [ ] **Step 3: Update broker to use transport.query as primary path**

Mediator sketch:

```csharp
public async Task<string> SearchTransportAsync(string modeHint, string channel, string userId, string query)
{
    var request = new ApprovedRequest
    {
        Route = "transport_query",
        RegisteredRoute = "transport_query",
        Reason = $"Transport query for {modeHint}",
        Arguments = new Dictionary<string, object?>
        {
            ["transport_mode"] = modeHint,
            ["user_query"] = query,
            ["locale"] = "zh-TW",
            ["channel"] = channel
        }
    };

    var result = await _dispatcher.DispatchAsync(request);
    return BuildTransportReplyFromCapabilityResult(result);
}
```

Compatibility mapping:

```csharp
"travel_rail_search" => ForwardTravelCompatibilityAsync("rail", request),
"travel_hsr_search" => ForwardTravelCompatibilityAsync("hsr", request),
"travel_bus_search" => ForwardTravelCompatibilityAsync("bus", request),
"travel_flight_search" => ForwardTravelCompatibilityAsync("flight", request),
"transport_query" => DispatchTransportWorkerAsync(request),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter TransportQueryCapabilityTests -v minimal`  
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add packages/csharp/broker/Program.cs packages/csharp/broker/Services/HighLevelQueryToolMediator.cs packages/csharp/broker/Services/HighLevelCoordinator.cs packages/csharp/broker/Adapters/InProcessDispatcher.cs packages/csharp/tests/integration/Transport/TransportQueryCapabilityTests.cs
git commit -m "feat: dispatch transport queries through worker capability"
```

---

### Task 6: 保留舊 travel_* 路徑作 compatibility layer

**Files:**
- Modify: `packages/csharp/broker/Handlers/Travel/TravelRailSearchHandler.cs`
- Modify: `packages/csharp/broker/Handlers/Travel/TravelHsrSearchHandler.cs`
- Modify: `packages/csharp/broker/Handlers/Travel/TravelBusSearchHandler.cs`
- Modify: `packages/csharp/broker/Handlers/Travel/TravelFlightSearchHandler.cs`
- Modify: `packages/csharp/broker/tool-specs/travel.rail.search/tool.json`
- Modify: `packages/csharp/broker/tool-specs/travel.hsr.search/tool.json`
- Modify: `packages/csharp/broker/tool-specs/travel.bus.search/tool.json`
- Modify: `packages/csharp/broker/tool-specs/travel.flight.search/tool.json`
- Test: `packages/csharp/tests/integration/Transport/TransportCompatibilityRouteTests.cs`

- [ ] **Step 1: Write failing compatibility test**

```csharp
using FluentAssertions;

namespace Integration.Tests.Transport;

public class TransportCompatibilityRouteTests
{
    [Fact]
    public async Task Legacy_travel_rail_route_forwards_to_transport_query_contract()
    {
        var response = await TransportTestHarness.QueryLegacyRouteAsync("travel_rail_search", "板橋到高雄的火車");

        response.ResultType.Should().BeOneOf("range_answer", "need_follow_up", "final_answer");
        response.ProviderMetadata["provider"].Should().Be("tdx");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter TransportCompatibilityRouteTests -v minimal`  
Expected: FAIL because legacy paths still produce old direct broker behavior.

- [ ] **Step 3: Make legacy handlers forward to transport.query**

```csharp
public sealed class TravelRailSearchHandler : IRouteHandler
{
    private readonly TransportCompatibilityForwarder _forwarder;

    public string Route => "travel_rail_search";

    public Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken cancellationToken = default)
        => _forwarder.ForwardAsync("rail", request, cancellationToken);
}
```

tool spec note:

```json
"compatibility": {
  "forward_to": "transport.query",
  "mode_hint": "rail"
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter TransportCompatibilityRouteTests -v minimal`  
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add packages/csharp/broker/Handlers/Travel packages/csharp/broker/tool-specs/travel.* packages/csharp/tests/integration/Transport/TransportCompatibilityRouteTests.cs
git commit -m "refactor: forward legacy travel routes to transport query"
```

---

### Task 7: 補 verify、文件同步、全量測試

**Files:**
- Modify: `packages/csharp/broker/verify/Program.cs`
- Modify: `docs/manuals/line-sidecar-runbook.md`
- Modify: `docs/manuals/line-sidecar-runbook.zh-TW.md`
- Modify: `docs/0329/01-broker.md`
- Modify: `docs/0329/07-tests.md`

- [ ] **Step 1: Add failing verify coverage**

Add broker verify cases:

```csharp
await AssertTransportQueryAsync("?rail 板橋到高雄的火車", expectedResultType: "range_answer");
await AssertTransportQueryAsync("?rail 明天上午板橋到高雄的火車", expectedResultType: "final_answer");
await AssertTransportQueryAsync("?bus 307", expectedResultType: "need_follow_up");
await AssertTransportQueryAsync("?bus 台北市 307", expectedResultType: "final_answer");
```

- [ ] **Step 2: Run verify to confirm failure**

Run: `dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj --disable-build-servers`  
Expected: FAIL because new transport capability path is not fully wired yet.

- [ ] **Step 3: Update verify helpers and docs**

Runbook note:

```md
- 交通查詢新主路徑為 `transport.query`
- `travel_rail_search` 等舊 route 僅保留相容層
- `transport-tdx worker` 需註冊 `transport.query`
- 當資訊不足時，worker 可能回 `need_follow_up` 或 `range_answer`
```

- [ ] **Step 4: Run the full verification suite**

Run:

```bash
dotnet build packages/csharp/ControlPlane.slnx -c Release --disable-build-servers -nodeReuse:false
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj -c Release --no-build --disable-build-servers -p:UseSharedCompilation=false -m:1
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj -c Release --no-build --disable-build-servers -p:UseSharedCompilation=false -m:1
dotnet test packages/csharp/tests/broker-tests/Broker.Tests.csproj -c Release --no-build --disable-build-servers -p:UseSharedCompilation=false -m:1
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj -c Release --no-build --disable-build-servers
```

Expected:
- build: `0 warnings / 0 errors`
- unit: PASS
- integration: PASS
- broker-tests: PASS
- verify: PASS

- [ ] **Step 5: Commit**

```bash
git add packages/csharp/broker/verify/Program.cs docs/manuals/line-sidecar-runbook.md docs/manuals/line-sidecar-runbook.zh-TW.md docs/0329/01-broker.md docs/0329/07-tests.md
git commit -m "test: verify transport query worker flow"
```

---

## Self-Review

### Spec coverage

- 獨立 `transport-tdx worker`：Task 2, Task 4
- broker 僅保留 capability / dispatch：Task 5, Task 6
- `transport.query` 統一 contract：Task 1
- sufficiency / follow-up / range answer：Task 3
- compatibility layer：Task 6
- verify / docs：Task 7

無明顯 spec coverage gap。

### Placeholder scan

- 無 `TBD` / `TODO` / `implement later`
- 每個 task 都列出明確檔案、測試、命令與 commit

### Type consistency

- capability 對外名稱固定為 `transport.query`
- dispatcher route 內部固定為 `transport_query`
- response type 固定為：
  - `final_answer`
  - `need_follow_up`
  - `range_answer`

---

## Notes

- 目前 root workspace 尚有未提交的 transport TDX-only 改動；執行本 plan 前先決定要不要將現有改動納入 feature branch 起點。
- 第一批不做 MaaS/道路/停車/POI 的全量整合，只聚焦 rail/hsr/bus/flight 的 worker 邊界重建。
```
