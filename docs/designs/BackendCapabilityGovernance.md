# Backend Capability Governance

## Goal

Support generated backend code for complex enterprise or government systems
without letting generated code depend directly on arbitrary third-party
libraries.

The rule is not "generated code may only use the standard library".
The rule is:

- generated code may use the standard library
- generated code may use approved Bricks4Agent platform abstractions
- third-party libraries are allowed only inside adapter implementations

## Layering

### 1. Generated application layer

This layer includes:

- controllers
- request and response DTOs
- use cases
- query orchestration
- report request composition

Allowed dependencies:

- `System.*`
- approved Bricks4Agent abstraction packages
- application query interfaces

Forbidden dependencies:

- direct third-party SDK usage
- direct filesystem writes for export features
- direct message-bus or cache client construction
- direct cryptography or auth protocol implementations

### 2. Platform abstraction layer

This layer defines narrow contracts for generated code.

Examples:

- `IExcelReportService`
- `IPdfReportService`
- `IObjectStorage`
- `IMessagePublisher`
- `IIdentityProvider`
- `IClock`
- `IIdGenerator`

Generated code should call these abstractions and nothing below them.

### 3. Adapter layer

This layer owns the actual third-party library integration.

Examples:

- `ClosedXmlExcelReportAdapter`
- `QuestPdfReportAdapter`
- `S3ObjectStorageAdapter`
- `RabbitMqMessagePublisher`
- `OpenIdConnectIdentityAdapter`

This is the only layer allowed to reference third-party implementation
packages directly.

### 4. Host layer

This layer wires the system together.

Responsibilities:

- dependency injection registration
- profile selection
- environment configuration
- feature toggles

## Capability profiles

Generated backend systems should be assigned a capability profile instead of
having unrestricted access to every backend feature.

Suggested profiles:

- `core`
- `enterprise`
- `government`
- `internet_facing`

Example:

- `core`: CRUD, logging, validation, persistence
- `enterprise`: `core` plus Excel, PDF, mail, workflow, directory integration
- `government`: `enterprise` plus audit, document signing, certificate-backed flows
- `internet_facing`: `core` plus public auth, antiforgery, rate limiting, storage

## Generator-friendly design rules

To keep low-level AI and rule-based generators effective, platform contracts
must stay narrow and stable.

Good contract:

```csharp
Task<ReportFile> GenerateAsync(ExcelReportRequest request, CancellationToken cancellationToken = default);
```

Bad contract:

```csharp
Task<object> ExportAnythingAsync(object data, object options);
```

The generator should only do these steps:

1. query business data
2. map rows into a contract object
3. call a platform abstraction
4. return or persist the result

The generator should never be asked to:

1. choose an Excel library
2. manage workbook styling APIs
3. handle OpenXML details
4. decide infrastructure wiring

## Excel report example

The reporting abstraction in [Reporting.Abstractions.csproj](/d:/Bricks4Agent/packages/csharp/reporting/Reporting.Abstractions.csproj)
shows the intended pattern:

- generated code composes `ExcelReportRequest`
- generated code calls `IExcelReportService`
- the adapter package owns the third-party Excel library

See:

- [README.md](/d:/Bricks4Agent/packages/csharp/reporting/README.md)
- [IExcelReportService.cs](/d:/Bricks4Agent/packages/csharp/reporting/Services/IExcelReportService.cs)
- [ExportEmployeesReportUseCase.cs](/d:/Bricks4Agent/packages/csharp/reporting/Examples/ExportEmployeesReportUseCase.cs)
- [excel-report.enterprise.json](/d:/Bricks4Agent/packages/csharp/reporting/CapabilitySchemas/excel-report.enterprise.json)

## Suggested enforcement

1. Use `.csproj` dependency policies to restrict project references.
2. Add package version governance with a central props file.
3. Add banned API rules for direct filesystem, clock, and third-party usage.
4. Add architecture tests for generated layer -> abstraction layer -> adapter layer boundaries.
5. Emit capability schema metadata for low-level AI and rule-based generators.

## Initial enforcement assets in this repo

The first enforcement pass is intentionally small and explicit.

- policy file:
  [dotnet-dependency-policy.json](/d:/Bricks4Agent/tools/scripts/dotnet-dependency-policy.json)
- validator:
  [validate-dotnet-dependencies.mjs](/d:/Bricks4Agent/tools/scripts/validate-dotnet-dependencies.mjs)
- API usage policy:
  [dotnet-api-usage-policy.json](/d:/Bricks4Agent/tools/scripts/dotnet-api-usage-policy.json)
- API usage validator:
  [validate-dotnet-api-usage.mjs](/d:/Bricks4Agent/tools/scripts/validate-dotnet-api-usage.mjs)

Current policy coverage includes:

- [Broker.csproj](/d:/Bricks4Agent/packages/csharp/broker/Broker.csproj)
- [BrokerCore.csproj](/d:/Bricks4Agent/packages/csharp/broker-core/BrokerCore.csproj)
- [Reporting.Abstractions.csproj](/d:/Bricks4Agent/packages/csharp/reporting/Reporting.Abstractions.csproj)
- [ClosedXmlAdapter.csproj](/d:/Bricks4Agent/packages/csharp/reporting/ClosedXmlAdapter/ClosedXmlAdapter.csproj)
- [ReportingExampleHost.csproj](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/ReportingExampleHost.csproj)
- [SpaApi.csproj](/d:/Bricks4Agent/templates/spa/backend/SpaApi.csproj)
- [spa-generator.csproj](/d:/Bricks4Agent/tools/spa-generator/backend/spa-generator.csproj)
- [ShopBricks.csproj](/d:/Bricks4Agent/projects/ShopBricks-Gen/backend/ShopBricks.csproj)

Run it with:

```bash
node tools/scripts/validate-dotnet-dependencies.mjs
```

Run the source-level API policy with:

```bash
node tools/scripts/validate-dotnet-api-usage.mjs
```

Or run both checks together with:

```bash
npm run validate:backend-governance
```

The current API usage policy is intentionally narrow. It now covers:

- reporting abstraction, adapter, and host layers
- template SPA backend service/model/data layers
- SPA generator backend service/model/data layers
- generated ShopBricks backend service/model/data layers
- selected generated backend host `Program.cs` files

For a concrete host/composition example, see:

- [Program.cs](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/Program.cs)
- [README.md](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/README.md)
