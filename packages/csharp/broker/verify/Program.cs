using System.Text.Json;
using Broker.Adapters;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);

    Console.WriteLine($"  ✓ {message}");
}

var sandboxRoot = Path.Combine(Path.GetTempPath(), "broker-verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandboxRoot);
var verifyProjectDirectory = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "packages", "csharp", "broker", "verify"));

try
{
    Console.WriteLine("=== Broker Verify ===");

    var parserOptions = new HighLevelCoordinatorOptions();
    var parser = new HighLevelCommandParser(parserOptions);
    var trustPolicy = new HighLevelInputTrustPolicy();
    var workflowMachine = new HighLevelWorkflowStateMachine();
    var promotionGate = new HighLevelExecutionPromotionGate();
    var browserRequest = new BrowserExecutionRequest
    {
        RequestId = "req_browser_1",
        ToolId = "browser.reference.anonymous.read",
        CapabilityId = "browser.read",
        Route = "browser_read",
        IdentityMode = "anonymous",
        CredentialBinding = "none",
        SessionBindingMode = "ephemeral",
        SessionReuseScope = "none",
        SiteBindingMode = "public_open",
        AllowedSiteClasses = new[] { "public_web" },
        MaxActionLevel = "navigate",
        RequiresHumanConfirmationOn = Array.Empty<string>(),
        PrincipalId = "principal_1",
        TaskId = "task_1",
        SessionId = "session_1",
        StartUrl = "https://example.com",
        IntendedActionLevel = "read"
    };
    var browserRequestJson = JsonSerializer.Serialize(browserRequest);
    var browserRequestRoundtrip = JsonSerializer.Deserialize<BrowserExecutionRequest>(browserRequestJson);
    AssertTrue(browserRequestRoundtrip != null && browserRequestRoundtrip.SiteBindingMode == "public_open", "browser execution request round-trips canonical site policy");
    var browserResult = BrowserExecutionResult.Ok("req_browser_1", "browser.reference.anonymous.read", "read", "https://example.com", title: "Example");
    var browserResultJson = JsonSerializer.Serialize(browserResult);
    var browserResultRoundtrip = JsonSerializer.Deserialize<BrowserExecutionResult>(browserResultJson);
    AssertTrue(browserResultRoundtrip != null && browserResultRoundtrip.ActionLevelReached == "read", "browser execution result round-trips canonical action level");
    AssertTrue(parser.Parse("?help").Kind == HighLevelInputKind.Help, "parser recognizes explicit help command");
    AssertTrue(parser.Parse("?h").Kind == HighLevelInputKind.Help, "parser recognizes help alias");
    AssertTrue(parser.Parse("/build website").Kind == HighLevelInputKind.Production, "parser recognizes production prefix");
    AssertTrue(parser.Parse("?weather taipei").Kind == HighLevelInputKind.Query, "parser recognizes query prefix");
    var searchParsed = parser.Parse("?search taipei weather");
    AssertTrue(searchParsed.QueryCommand == "search" && searchParsed.QueryArgument == "taipei weather", "parser extracts explicit search query subcommand");
    var searchAliasParsed = parser.Parse("?s taipei weather");
    AssertTrue(searchAliasParsed.QueryCommand == "search" && searchAliasParsed.QueryArgument == "taipei weather", "parser extracts search alias subcommand");
    var railParsed = parser.Parse("?rail 台北 台中 今天 18:00");
    AssertTrue(railParsed.QueryCommand == "rail" && railParsed.QueryArgument == "台北 台中 今天 18:00", "parser extracts explicit rail query subcommand");
    var railAliasParsed = parser.Parse("?r 台北 台中 今天 18:00");
    AssertTrue(railAliasParsed.QueryCommand == "rail" && railAliasParsed.QueryArgument == "台北 台中 今天 18:00", "parser extracts rail alias subcommand");
    var hsrParsed = parser.Parse("?hsr 台北 台中 今天 18:00");
    AssertTrue(hsrParsed.QueryCommand == "hsr" && hsrParsed.QueryArgument == "台北 台中 今天 18:00", "parser extracts explicit hsr query subcommand");
    var hsrAliasParsed = parser.Parse("?thsr 台北 台中 今天 18:00");
    AssertTrue(hsrAliasParsed.QueryCommand == "hsr" && hsrAliasParsed.QueryArgument == "台北 台中 今天 18:00", "parser extracts thsr alias subcommand");
    var busParsed = parser.Parse("?bus 台北 台中 今天 18:00");
    AssertTrue(busParsed.QueryCommand == "bus" && busParsed.QueryArgument == "台北 台中 今天 18:00", "parser extracts explicit bus query subcommand");
    var busAliasParsed = parser.Parse("?b 台北 台中 今天 18:00");
    AssertTrue(busAliasParsed.QueryCommand == "bus" && busAliasParsed.QueryArgument == "台北 台中 今天 18:00", "parser extracts bus alias subcommand");
    var flightParsed = parser.Parse("?flight TPE KIX tomorrow");
    AssertTrue(flightParsed.QueryCommand == "flight" && flightParsed.QueryArgument == "TPE KIX tomorrow", "parser extracts explicit flight query subcommand");
    var flightAliasParsed = parser.Parse("?f TPE KIX tomorrow");
    AssertTrue(flightAliasParsed.QueryCommand == "flight" && flightAliasParsed.QueryArgument == "TPE KIX tomorrow", "parser extracts flight alias subcommand");
    var profileParsed = parser.Parse("?profile");
    AssertTrue(profileParsed.QueryCommand == "profile", "parser extracts explicit profile query subcommand");
    var profileAliasParsed = parser.Parse("?p");
    AssertTrue(profileAliasParsed.QueryCommand == "profile", "parser extracts profile alias subcommand");
    var plainQueryParsed = parser.Parse("?weather taipei");
    AssertTrue(string.IsNullOrWhiteSpace(plainQueryParsed.QueryCommand), "parser keeps plain query text outside tool subcommands");
    var nameParsed = parser.Parse("/name 小布");
    AssertTrue(nameParsed.ProductionCommand == "name" && nameParsed.ProductionArgument == "小布", "parser extracts display-name production command");
    var nameAliasParsed = parser.Parse("/n 小布");
    AssertTrue(nameAliasParsed.ProductionCommand == "name" && nameAliasParsed.ProductionArgument == "小布", "parser extracts display-name alias command");
    var idParsed = parser.Parse("/id bricks001");
    AssertTrue(idParsed.ProductionCommand == "id" && idParsed.ProductionArgument == "bricks001", "parser extracts alphanumeric-id production command");
    var idAliasParsed = parser.Parse("/i bricks001");
    AssertTrue(idAliasParsed.ProductionCommand == "id" && idAliasParsed.ProductionArgument == "bricks001", "parser extracts alphanumeric-id alias command");
    AssertTrue(parser.Parse("#MySite").Kind == HighLevelInputKind.ProjectName, "parser recognizes project-name prefix");
    AssertTrue(parser.Parse("confirm").Kind == HighLevelInputKind.Confirm, "parser recognizes confirm token");
    AssertTrue(parser.Parse("cancel").Kind == HighLevelInputKind.Cancel, "parser recognizes cancel token");
    AssertTrue(parser.Parse("hello world").Kind == HighLevelInputKind.Conversation, "parser keeps bare text as conversation");

    var trustedUserCommand = trustPolicy.Apply(
        new HighLevelInputEnvelope
        {
            RawText = "/build website",
            Source = HighLevelInputSource.UserMessage,
            Taint = HighLevelInputTaint.UserText
        },
        parser.Parse("/build website"));
    AssertTrue(trustedUserCommand.Parsed.Kind == HighLevelInputKind.Production, "trust policy allows raw user commands");

    var transformedCommand = trustPolicy.Apply(
        new HighLevelInputEnvelope
        {
            RawText = "/build website",
            Source = HighLevelInputSource.DecodedPayload,
            Taint = HighLevelInputTaint.TransformedText,
            Transforms = new List<HighLevelTransformKind> { HighLevelTransformKind.Base64Decode }
        },
        parser.Parse("/build website"));
    AssertTrue(transformedCommand.Parsed.Kind == HighLevelInputKind.Conversation, "trust policy downgrades transformed command-like content");
    AssertTrue(!transformedCommand.Trust.Allowed, "trust policy denies command extraction from transformed content");

    var externalInstruction = trustPolicy.Apply(
        new HighLevelInputEnvelope
        {
            RawText = "#InjectedProject",
            Source = HighLevelInputSource.RetrievedDocument,
            Taint = HighLevelInputTaint.ExternalText
        },
        parser.Parse("#InjectedProject"));
    AssertTrue(externalInstruction.Parsed.Kind == HighLevelInputKind.Conversation, "trust policy downgrades external instruction-like content");

    var awaitingProjectDraft = new HighLevelTaskDraft
    {
        RequiresProjectName = true,
        ProjectName = null
    };
    AssertTrue(
        workflowMachine.Evaluate(awaitingProjectDraft, parser.Parse("#MySite")).Action == HighLevelWorkflowAction.CaptureProjectName,
        "workflow accepts project-name command only in awaiting-project-name state");
    AssertTrue(
        workflowMachine.Evaluate(awaitingProjectDraft, parser.Parse("confirm")).Action == HighLevelWorkflowAction.RequestProjectNameFirst,
        "workflow blocks confirmation before project name is captured");

    var pendingDraft = new HighLevelTaskDraft
    {
        RequiresProjectName = true,
        ProjectName = "MySite"
    };
    AssertTrue(
        workflowMachine.Evaluate(pendingDraft, parser.Parse("confirm")).Action == HighLevelWorkflowAction.ConfirmDraft,
        "workflow accepts confirm after draft requirements are satisfied");
    AssertTrue(
        workflowMachine.Evaluate(null, parser.Parse("/build website")).Action == HighLevelWorkflowAction.StartProduction,
        "workflow starts production only from explicit production command");

    var promotableMemory = new HighLevelMemoryState
    {
        Channel = "line",
        UserId = "tester",
        CurrentGoal = "build website",
        CurrentGoalCommitLevel = HighLevelMemoryCommitLevel.Candidate.ToString(),
        LastRouteMode = HighLevelRouteMode.Production.ToString(),
        ProjectName = "MySite",
        ProjectNameCommitLevel = HighLevelMemoryCommitLevel.Confirmed.ToString()
    };
    var promotableDraft = new HighLevelTaskDraft
    {
        DraftId = "draft_123",
        Channel = "line",
        UserId = "tester",
        TaskType = "code_gen",
        OriginalMessage = "/build website",
        ScopeDescriptor = "{}",
        RuntimeDescriptor = "{}",
        RequiresProjectName = true,
        ProjectName = "MySite"
    };
    var promotion = promotionGate.Evaluate(promotableMemory, promotableDraft);
    AssertTrue(promotion.Allowed, "promotion gate allows confirmed project metadata plus candidate user goal");

    var deniedPromotion = promotionGate.Evaluate(promotableMemory, new HighLevelTaskDraft
    {
        DraftId = "draft_456",
        Channel = "line",
        UserId = "tester",
        TaskType = "code_gen",
        OriginalMessage = "/build website",
        ScopeDescriptor = "{}",
        RuntimeDescriptor = "{}",
        RequiresProjectName = true,
        ProjectName = null
    });
    AssertTrue(!deniedPromotion.Allowed, "promotion gate denies execution without required project name");

    var mediator = new HighLevelQueryToolMediator(
        new FakeToolSpecRegistry(),
        new FakeExecutionDispatcher(),
        NullLogger<HighLevelQueryToolMediator>.Instance);
    var mediatedSearch = await mediator.SearchWebAsync("line", "tester", "taipei weather");
    AssertTrue(mediatedSearch.Success, "query tool mediator executes explicit search through broker-owned tool binding");
    AssertTrue(mediatedSearch.Reply.Contains("google", StringComparison.OrdinalIgnoreCase), "query tool mediator reply cites Google as the primary search engine");
    AssertTrue(mediatedSearch.Reply.Contains("https://example.com/weather", StringComparison.OrdinalIgnoreCase), "query tool mediator reply includes ranked URLs");
    AssertTrue(HighLevelRelationQueryService.TryExtractAdministrativeRelationQuery("北京市附近的行政區", out var relationPlan), "relation query service recognizes administrative relation prompt");
    AssertTrue(relationPlan.Subject == "北京市", "relation query service extracts core subject");
    var relationQueryService = new HighLevelRelationQueryService(
        mediator,
        new HighLevelLlmOptions
        {
            Provider = "ollama",
            BaseUrl = "http://localhost:11434",
            DefaultModel = "verify"
        },
        new FakeHttpClientFactory(),
        NullLogger<HighLevelRelationQueryService>.Instance);
    var relationAnswer = await relationQueryService.TryAnswerAsync("line", "tester", "北京市附近的行政區");
    AssertTrue(relationAnswer.Handled, "relation query service handles administrative relation queries");
    AssertTrue(relationAnswer.Reply.Contains("verify-reply", StringComparison.OrdinalIgnoreCase), "relation query service uses high-level model to synthesize final answer");
    var mediatedRail = await mediator.SearchRailAsync("line", "tester", "台北 台中 今天 18:00");
    AssertTrue(mediatedRail.Success, "query tool mediator executes explicit rail query through broker-owned transport tool");
    AssertTrue(mediatedRail.Reply.Contains("18:30", StringComparison.OrdinalIgnoreCase), "rail query mediator reply includes candidate time information");
    var mediatedHsr = await mediator.SearchHsrAsync("line", "tester", "台北 台中 今天 18:00");
    AssertTrue(mediatedHsr.Success, "query tool mediator executes explicit hsr query through broker-owned transport tool");
    AssertTrue(mediatedHsr.Reply.Contains("06:15", StringComparison.OrdinalIgnoreCase), "hsr query mediator reply includes candidate time information");
    var mediatedBus = await mediator.SearchBusAsync("line", "tester", "台北 台中 今天 18:00");
    AssertTrue(mediatedBus.Success, "query tool mediator executes explicit bus query through broker-owned transport tool");
    var mediatedFlight = await mediator.SearchFlightAsync("line", "tester", "TPE KIX tomorrow");
    AssertTrue(mediatedFlight.Success, "query tool mediator executes explicit flight query through broker-owned transport tool");

    var specRoot = Path.Combine(sandboxRoot, "tool-specs");
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.reference.anonymous.read"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.anonymous.read", "tool.json"),
        """
        {
          "tool_id": "browser.reference.anonymous.read",
          "display_name": "Browser Reference",
          "summary": "reference",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "anonymous",
            "credential_source": "none",
            "session_owner": "none",
            "allowed_actions": ["read", "navigate"],
            "confirmation_policy": "broker_policy"
          },
          "browser_session_policy": {
            "binding_mode": "ephemeral",
            "credential_binding": "none",
            "reuse_scope": "none",
            "lease_minutes": 15,
            "requires_consent_record": false,
            "requires_interactive_login": false
          },
          "browser_site_policy": {
            "site_binding_mode": "public_open",
            "allowed_site_classes": ["public_web"],
            "requires_registered_site_binding": false,
            "requires_exact_origin_match": false,
            "allows_cross_origin_navigation": true
          },
          "browser_action_policy": {
            "max_action_level": "navigate",
            "requires_human_confirmation_on": [],
            "allows_form_fill": false,
            "allows_submit": false,
            "allows_download": false,
            "allows_file_upload": false
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["public_web"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.anonymous.read", "TOOL.md"),
        "# Reference");
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.reference.system-account.read"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.system-account.read", "tool.json"),
        """
        {
          "tool_id": "browser.reference.system-account.read",
          "display_name": "Browser System Reference",
          "summary": "system",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "system_account",
            "credential_source": "system_vault",
            "session_owner": "system",
            "allowed_actions": ["read", "navigate", "authenticate"],
            "confirmation_policy": "broker_policy"
          },
          "browser_session_policy": {
            "binding_mode": "broker_managed",
            "credential_binding": "system_vault",
            "reuse_scope": "site",
            "lease_minutes": 240,
            "requires_consent_record": false,
            "requires_interactive_login": false
          },
          "browser_site_policy": {
            "site_binding_mode": "registered_site",
            "allowed_site_classes": ["broker_managed_site"],
            "requires_registered_site_binding": true,
            "requires_exact_origin_match": true,
            "allows_cross_origin_navigation": false
          },
          "browser_action_policy": {
            "max_action_level": "authenticate",
            "requires_human_confirmation_on": [],
            "allows_form_fill": false,
            "allows_submit": false,
            "allows_download": false,
            "allows_file_upload": false
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["broker_managed_site_bindings"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.reference.user-delegated.read"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.user-delegated.read", "tool.json"),
        """
        {
          "tool_id": "browser.reference.user-delegated.read",
          "display_name": "Browser User Reference",
          "summary": "user",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "user_delegated",
            "credential_source": "user_grant",
            "session_owner": "user",
            "allowed_actions": ["read", "navigate", "authenticate"],
            "confirmation_policy": "user_required"
          },
          "browser_session_policy": {
            "binding_mode": "user_bound",
            "credential_binding": "user_grant",
            "reuse_scope": "user",
            "lease_minutes": 120,
            "requires_consent_record": true,
            "requires_interactive_login": true
          },
          "browser_site_policy": {
            "site_binding_mode": "user_authorized_site",
            "allowed_site_classes": ["user_authorized_site"],
            "requires_registered_site_binding": true,
            "requires_exact_origin_match": true,
            "allows_cross_origin_navigation": false
          },
          "browser_action_policy": {
            "max_action_level": "authenticate",
            "requires_human_confirmation_on": ["authenticate"],
            "allows_form_fill": false,
            "allows_submit": false,
            "allows_download": false,
            "allows_file_upload": false
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["user_authorized_site_bindings"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.invalid.missing-action"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.invalid.missing-action", "tool.json"),
        """
        {
          "tool_id": "browser.invalid.missing-action",
          "display_name": "Invalid Browser Reference",
          "summary": "invalid",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "anonymous",
            "credential_source": "none",
            "session_owner": "none",
            "allowed_actions": ["read"],
            "confirmation_policy": "broker_policy"
          },
          "browser_session_policy": {
            "binding_mode": "ephemeral",
            "credential_binding": "none",
            "reuse_scope": "none",
            "lease_minutes": 15,
            "requires_consent_record": false,
            "requires_interactive_login": false
          },
          "browser_site_policy": {
            "site_binding_mode": "public_open",
            "allowed_site_classes": ["public_web"],
            "requires_registered_site_binding": false,
            "requires_exact_origin_match": false,
            "allows_cross_origin_navigation": true
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["public_web"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    Directory.CreateDirectory(Path.Combine(specRoot, "deploy.azure-vm-iis"));
    File.WriteAllText(
        Path.Combine(specRoot, "deploy.azure-vm-iis", "tool.json"),
        """
        {
          "tool_id": "deploy.azure-vm-iis",
          "display_name": "Deploy to Azure VM IIS",
          "summary": "deployment",
          "kind": "deployment",
          "status": "beta",
          "version": "2026-03-22",
          "tags": ["deployment", "azure", "iis"],
          "capability_template": {
            "action_type": "write",
            "resource_type": "deployment",
            "risk_level": "high",
            "approval_policy": "require_approval",
            "ttl_seconds": 1800,
            "audit_level": "full",
            "quota": { "max_calls": 10, "per_window_seconds": 3600 }
          },
          "capability_bindings": [
            {
              "capability_id": "deploy.azure-vm-iis",
              "route": "deploy_azure_vm_iis",
              "purpose": "test deployment"
            }
          ],
          "input_schema": {
            "type": "object",
            "properties": {
              "target_id": { "type": "string" },
              "project_path": { "type": "string" }
            },
            "required": ["target_id", "project_path"]
          },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["broker_registered_deployment_targets"] },
          "execution_rules": { "transport": "winrm_powershell" },
          "response_contract": { "must_identify_target": true }
        }
        """);
    File.WriteAllText(
        Path.Combine(specRoot, "deploy.azure-vm-iis", "TOOL.md"),
        "# Deploy");

    var toolSpecDbPath = Path.Combine(sandboxRoot, "tool-spec-registry.db");
    using (var toolSpecDb = BrokerDb.UseSqlite($"Data Source={toolSpecDbPath}"))
    {
        var initializer = new BrokerDbInitializer(toolSpecDb);
        initializer.Initialize();
        toolSpecDb.Insert(new BrowserSiteBinding
        {
            SiteBindingId = "site_binding_public",
            DisplayName = "Public Example",
            IdentityMode = "anonymous",
            SiteClass = "public_web",
            Origin = "https://example.com",
            Status = "active"
        });
        toolSpecDb.Insert(new BrowserSiteBinding
        {
            SiteBindingId = "site_binding_system",
            DisplayName = "System Example",
            IdentityMode = "system_account",
            SiteClass = "broker_managed_site",
            Origin = "https://system.example.com",
            Status = "active"
        });
        toolSpecDb.Insert(new BrowserSiteBinding
        {
            SiteBindingId = "site_binding_user",
            DisplayName = "User Example",
            IdentityMode = "user_delegated",
            SiteClass = "user_authorized_site",
            Origin = "https://user.example.com",
            PrincipalId = "principal_user",
            Status = "active"
        });
        toolSpecDb.Insert(new BrowserUserGrant
        {
            UserGrantId = "grant_user_1",
            PrincipalId = "principal_user",
            SiteBindingId = "site_binding_user",
            Status = "active",
            ConsentRef = "consent_1"
        });
        toolSpecDb.Insert(new BrowserSystemBinding
        {
            SystemBindingId = "system_binding_1",
            DisplayName = "System Binding",
            SiteBindingId = "site_binding_system",
            Status = "active",
            SecretRef = "vault://system/example"
        });
        toolSpecDb.Insert(new BrowserSessionLease
        {
            SessionLeaseId = "lease_user_1",
            ToolId = "browser.reference.user-delegated.read",
            SiteBindingId = "site_binding_user",
            PrincipalId = "principal_user",
            IdentityMode = "user_delegated",
            LeaseState = "active",
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        });
        var registry = new ToolSpecRegistry(
            new FakeWebHostEnvironment(Path.Combine(sandboxRoot, "content-root")),
            new ToolSpecRegistryOptions { Root = specRoot },
            toolSpecDb,
            NullLogger<ToolSpecRegistry>.Instance);
        var browserSpec = registry.Get("browser.reference.anonymous.read");
        AssertTrue(browserSpec != null, "tool spec registry loads browser reference spec");
        AssertTrue(browserSpec!.BrowserProfile != null, "tool spec registry preserves browser profile");
        AssertTrue(browserSpec.BrowserProfile!.IdentityMode == "anonymous", "browser profile keeps identity mode");
        AssertTrue(browserSpec.BrowserProfile.CredentialSource == "none", "browser profile keeps credential source");
        AssertTrue(browserSpec.BrowserSessionPolicy != null, "tool spec registry preserves browser session policy");
        AssertTrue(browserSpec.BrowserSessionPolicy!.BindingMode == "ephemeral", "browser session policy keeps binding mode");
        AssertTrue(browserSpec.BrowserSessionPolicy.CredentialBinding == "none", "browser session policy keeps credential binding");
        AssertTrue(browserSpec.BrowserSitePolicy != null, "tool spec registry preserves browser site policy");
        AssertTrue(browserSpec.BrowserSitePolicy!.SiteBindingMode == "public_open", "browser site policy keeps site binding mode");
        AssertTrue(browserSpec.BrowserSitePolicy.AllowedSiteClasses.Length == 1 && browserSpec.BrowserSitePolicy.AllowedSiteClasses[0] == "public_web", "browser site policy keeps allowed site classes");
        AssertTrue(browserSpec.BrowserActionPolicy != null, "tool spec registry preserves browser action policy");
        AssertTrue(browserSpec.BrowserActionPolicy!.MaxActionLevel == "navigate", "browser action policy keeps max action level");
        AssertTrue(browserSpec.BrowserActionPolicy.RequiresHumanConfirmationOn.Length == 0, "browser action policy keeps confirmation requirements");
        AssertTrue(registry.Get("browser.invalid.missing-action") == null, "tool spec registry rejects incomplete browser specs");

        var builder = new BrowserExecutionRequestBuilder(registry, toolSpecDb);
        var builtAnonymous = builder.TryBuild("browser.reference.anonymous.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_1",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_public"
        });
        AssertTrue(builtAnonymous.Success, "browser request builder builds anonymous browser request from registry metadata");
        AssertTrue(builtAnonymous.Request!.IdentityMode == "anonymous", "browser request builder projects identity mode into runtime contract");

        var builtTooPowerful = builder.TryBuild("browser.reference.anonymous.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_2",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "committed_action"
        });
        AssertTrue(!builtTooPowerful.Success && builtTooPowerful.Error == "browser_request_action_level_exceeds_policy", "browser request builder denies action level above browser policy");

        var builtSystemMissing = builder.TryBuild("browser.reference.system-account.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_3",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_system"
        });
        AssertTrue(!builtSystemMissing.Success && builtSystemMissing.Error == "browser_request_missing_system_binding", "browser request builder requires system binding for system-account tools");

        var builtSystemSuccess = builder.TryBuild("browser.reference.system-account.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_3b",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_system",
            SystemBindingId = "system_binding_1"
        });
        AssertTrue(builtSystemSuccess.Success, "browser request builder accepts matching system binding record");

        var builtUserMissingGrant = builder.TryBuild("browser.reference.user-delegated.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_4",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_user",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_user"
        });
        AssertTrue(!builtUserMissingGrant.Success && builtUserMissingGrant.Error == "browser_request_missing_user_grant", "browser request builder requires user grant for user-delegated tools");

        var builtUserPrincipalMismatch = builder.TryBuild("browser.reference.user-delegated.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_5",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_other",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_user",
            UserGrantId = "grant_1"
        });
        AssertTrue(!builtUserPrincipalMismatch.Success && builtUserPrincipalMismatch.Error == "browser_request_site_binding_principal_mismatch", "browser request builder enforces user-delegated site binding ownership");

        var builtUserSuccess = builder.TryBuild("browser.reference.user-delegated.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_6",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_user",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_user",
            UserGrantId = "grant_user_1"
        });
        AssertTrue(builtUserSuccess.Success, "browser request builder accepts matching user grant record");
        AssertTrue(builtUserSuccess.Request!.SiteBindingId == "site_binding_user", "browser request builder preserves selected site binding in runtime request");

        var builtUserLeaseMismatch = builder.TryBuild("browser.reference.user-delegated.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_7",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_other",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_user",
            UserGrantId = "grant_user_1",
            SessionLeaseId = "lease_user_1"
        });
        AssertTrue(!builtUserLeaseMismatch.Success && builtUserLeaseMismatch.Error == "browser_request_site_binding_principal_mismatch", "browser request builder still denies mismatched site owner before lease acceptance");

        var builtUserWithLease = builder.TryBuild("browser.reference.user-delegated.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_8",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_user",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_user",
            UserGrantId = "grant_user_1",
            SessionLeaseId = "lease_user_1"
        });
        AssertTrue(builtUserWithLease.Success, "browser request builder accepts matching active session lease");

        var bindingService = new BrowserBindingService(toolSpecDb);
        var createdSite = bindingService.UpsertSiteBinding(new BrowserSiteBinding
        {
            DisplayName = "Created Site",
            IdentityMode = "anonymous",
            SiteClass = "public_web",
            Origin = "https://created.example.com",
            Status = "active"
        });
        AssertTrue(!string.IsNullOrWhiteSpace(createdSite.SiteBindingId), "browser binding service creates site bindings");

        var createdGrant = bindingService.UpsertUserGrant(new BrowserUserGrant
        {
            PrincipalId = "principal_created",
            ConsentRef = "consent_created",
            SiteBindingId = createdSite.SiteBindingId,
            Status = "active"
        });
        AssertTrue(!string.IsNullOrWhiteSpace(createdGrant.UserGrantId), "browser binding service creates user grants");

        var createdSystemBinding = bindingService.UpsertSystemBinding(new BrowserSystemBinding
        {
            DisplayName = "Created System",
            SiteBindingId = "site_binding_system",
            Status = "active",
            SecretRef = "vault://created/system"
        });
        AssertTrue(!string.IsNullOrWhiteSpace(createdSystemBinding.SystemBindingId), "browser binding service creates system bindings");

        var issuedLease = bindingService.IssueSessionLease(
            toolId: "browser.reference.anonymous.read",
            principalId: "principal_created",
            identityMode: "anonymous",
            expiresAt: DateTime.UtcNow.AddMinutes(15),
            siteBindingId: createdSite.SiteBindingId);
        AssertTrue(!string.IsNullOrWhiteSpace(issuedLease.SessionLeaseId), "browser binding service issues session leases");

        var revokedLease = bindingService.RevokeSessionLease(issuedLease.SessionLeaseId);
        AssertTrue(revokedLease != null && revokedLease.LeaseState == "revoked", "browser binding service revokes session leases");

        var previewHttpClient = new HttpClient(new FakeBrowserPreviewHandler())
        {
            BaseAddress = new Uri("https://preview.test/")
        };
        var previewService = new BrowserExecutionPreviewService(builder, previewHttpClient);
        var previewResult = await previewService.ExecuteAnonymousReadAsync("browser.reference.anonymous.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_preview_1",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_public"
        });
        AssertTrue(previewResult.Success, "browser preview service fetches anonymous public content");
        AssertTrue(previewResult.Result != null && previewResult.Result.Title == "Example Preview", "browser preview service extracts page title");

        var deploymentTargetService = new AzureIisDeploymentTargetService(toolSpecDb);
        var deploymentTarget = deploymentTargetService.UpsertTarget(new AzureIisDeploymentTarget
        {
            DisplayName = "Azure IIS Test",
            Provider = "azure_vm_iis",
            VmHost = "vm.example.com",
            Port = 5986,
            UseSsl = true,
            Transport = "winrm_powershell",
            SiteName = "Default Web Site",
            AppPoolName = "DefaultAppPool",
            PhysicalPath = @"C:\inetpub\wwwroot\TestApp",
            SecretRef = "vault://deploy/test",
            Status = "active"
        });
        AssertTrue(!string.IsNullOrWhiteSpace(deploymentTarget.TargetId), "deployment target service upserts azure iis target");

        var deploymentSubAppTarget = deploymentTargetService.UpsertTarget(new AzureIisDeploymentTarget
        {
            DisplayName = "Azure IIS Child App",
            Provider = "azure_vm_iis",
            VmHost = "vm.example.com",
            Port = 5986,
            UseSsl = true,
            Transport = "winrm_powershell",
            SiteName = "Default Web Site",
            DeploymentMode = "iis_application",
            ApplicationPath = "/apps/verify",
            AppPoolName = "VerifyChildPool",
            PhysicalPath = @"C:\inetpub\apps\verify",
            HealthCheckPath = "/apps/verify/health",
            SecretRef = "vault://deploy/test",
            Status = "active"
        });
        AssertTrue(!string.IsNullOrWhiteSpace(deploymentSubAppTarget.TargetId), "deployment target service upserts child-application target");
        try
        {
            deploymentTargetService.UpsertTarget(new AzureIisDeploymentTarget
            {
                DisplayName = "Azure IIS Child App Duplicate",
                Provider = "azure_vm_iis",
                VmHost = "vm.example.com",
                Port = 5986,
                UseSsl = true,
                Transport = "winrm_powershell",
                SiteName = "Default Web Site",
                DeploymentMode = "iis_application",
                ApplicationPath = "/apps/verify",
                AppPoolName = "VerifyChildPool2",
                PhysicalPath = @"C:\inetpub\apps\verify-dup",
                SecretRef = "vault://deploy/test",
                Status = "active"
            });
            throw new Exception("Expected duplicate child-application target registration to fail.");
        }
        catch (InvalidOperationException)
        {
            AssertTrue(true, "deployment target service rejects duplicate child-application route on the same site");
        }

        var deploymentBuilder = new AzureIisDeploymentRequestBuilder(registry, toolSpecDb);
        var deploymentBuild = deploymentBuilder.TryBuild("deploy.azure-vm-iis", new AzureIisDeploymentBuildInput
        {
            RequestId = "dreq_1",
            CapabilityId = "deploy.azure-vm-iis",
            Route = "deploy_azure_vm_iis",
            PrincipalId = "principal_deployer",
            TaskId = "task_deploy",
            SessionId = "session_deploy",
            TargetId = deploymentTarget.TargetId,
            ProjectPath = verifyProjectDirectory
        });
        AssertTrue(deploymentBuild.Success && deploymentBuild.Request != null, "deployment builder resolves target and project file");
        AssertTrue(deploymentBuild.Request!.ProjectFile.EndsWith("Broker.Verify.csproj", StringComparison.OrdinalIgnoreCase), "deployment builder resolves the unique project file from directory");

        var deploymentPreviewService = new AzureIisDeploymentPreviewService(deploymentBuilder);
        var deploymentPreview = deploymentPreviewService.Preview("deploy.azure-vm-iis", new AzureIisDeploymentBuildInput
        {
            RequestId = "dreq_preview",
            CapabilityId = "deploy.azure-vm-iis",
            Route = "deploy_azure_vm_iis",
            PrincipalId = "principal_deployer",
            TaskId = "task_deploy",
            SessionId = "session_deploy",
            TargetId = deploymentTarget.TargetId,
            ProjectPath = verifyProjectDirectory
        });
        AssertTrue(deploymentPreview.Success && deploymentPreview.Result != null, "deployment preview builds dry-run deployment result");
        AssertTrue(deploymentPreview.Result!.ScriptPreview.Contains("New-PSSession", StringComparison.Ordinal), "deployment preview renders winrm powershell script");
        AssertTrue(deploymentPreview.Result!.DetailsJson.Contains("dotnet publish", StringComparison.Ordinal), "deployment preview includes dotnet publish command");

        var deploymentSubAppBuild = deploymentBuilder.TryBuild("deploy.azure-vm-iis", new AzureIisDeploymentBuildInput
        {
            RequestId = "dreq_subapp",
            CapabilityId = "deploy.azure-vm-iis",
            Route = "deploy_azure_vm_iis",
            PrincipalId = "principal_deployer",
            TaskId = "task_deploy",
            SessionId = "session_deploy",
            TargetId = deploymentSubAppTarget.TargetId,
            ProjectPath = verifyProjectDirectory
        });
        AssertTrue(deploymentSubAppBuild.Success && deploymentSubAppBuild.Request != null, "deployment builder supports iis child-application targets");
        AssertTrue(deploymentSubAppBuild.Request!.DeploymentMode == "iis_application", "deployment builder preserves child-application mode");
        AssertTrue(deploymentSubAppBuild.Request!.ApplicationPath == "/apps/verify", "deployment builder normalizes child-application path");

        var deploymentSubAppPreview = deploymentPreviewService.Preview("deploy.azure-vm-iis", new AzureIisDeploymentBuildInput
        {
            RequestId = "dreq_preview_subapp",
            CapabilityId = "deploy.azure-vm-iis",
            Route = "deploy_azure_vm_iis",
            PrincipalId = "principal_deployer",
            TaskId = "task_deploy",
            SessionId = "session_deploy",
            TargetId = deploymentSubAppTarget.TargetId,
            ProjectPath = verifyProjectDirectory
        });
        AssertTrue(deploymentSubAppPreview.Success && deploymentSubAppPreview.Result != null, "deployment preview supports iis child-application targets");
        AssertTrue(deploymentSubAppPreview.Result!.ScriptPreview.Contains("New-WebApplication", StringComparison.Ordinal), "deployment preview creates or updates IIS child application");
        AssertTrue(deploymentSubAppPreview.Result!.DetailsJson.Contains("\"DeploymentMode\":\"iis_application\"", StringComparison.OrdinalIgnoreCase) ||
                   deploymentSubAppPreview.Result!.DetailsJson.Contains("\"deploymentmode\":\"iis_application\"", StringComparison.OrdinalIgnoreCase),
            "deployment preview details include child-application mode");

        var fakeProcessRunner = new FakeProcessRunner();
        var executionService = new AzureIisDeploymentExecutionService(
            deploymentBuilder,
            new FakeAzureIisDeploymentSecretResolver(),
            fakeProcessRunner);
        var dryRunExecution = await executionService.ExecuteAsync(
            "deploy.azure-vm-iis",
            new AzureIisDeploymentBuildInput
            {
                RequestId = "dreq_exec_dry",
                CapabilityId = "deploy.azure-vm-iis",
                Route = "deploy_azure_vm_iis",
                PrincipalId = "principal_deployer",
                TaskId = "task_deploy",
                SessionId = "session_deploy",
                TargetId = deploymentTarget.TargetId,
                ProjectPath = verifyProjectDirectory
            },
            dryRun: true);
        AssertTrue(dryRunExecution.Success && dryRunExecution.Result != null, "deployment execution service supports dry-run package preparation");
        AssertTrue(dryRunExecution.Result!.Stage == "dry_run", "deployment execution service marks dry-run stage");
        AssertTrue(File.Exists(dryRunExecution.Result.PackagePath), "deployment execution service produces zip package during dry run");
        AssertTrue(fakeProcessRunner.Invocations.Count == 1 && fakeProcessRunner.Invocations[0].FileName == "dotnet", "deployment execution dry-run only invokes dotnet publish");

        var executeProcessRunner = new FakeProcessRunner();
        var actualExecutionService = new AzureIisDeploymentExecutionService(
            deploymentBuilder,
            new FakeAzureIisDeploymentSecretResolver(),
            executeProcessRunner);
        var actualExecution = await actualExecutionService.ExecuteAsync(
            "deploy.azure-vm-iis",
            new AzureIisDeploymentBuildInput
            {
                RequestId = "dreq_exec_live",
                CapabilityId = "deploy.azure-vm-iis",
                Route = "deploy_azure_vm_iis",
                PrincipalId = "principal_deployer",
                TaskId = "task_deploy",
                SessionId = "session_deploy",
                TargetId = deploymentTarget.TargetId,
                ProjectPath = verifyProjectDirectory
            },
            dryRun: false);
        AssertTrue(actualExecution.Success && actualExecution.Result != null, "deployment execution service completes with resolved secret and fake runner");
        AssertTrue(actualExecution.Result!.Stage == "deployed", "deployment execution service marks deployed stage");
        AssertTrue(executeProcessRunner.Invocations.Count == 2 && executeProcessRunner.Invocations[1].FileName == "powershell", "deployment execution invokes remote powershell after publish");
    }

    var logDbPath = Path.Combine(sandboxRoot, "interaction-log.db");
    using (var logDb = BrokerDb.UseSqlite($"Data Source={logDbPath}"))
    {
        var initializer = new BrokerDbInitializer(logDb);
        initializer.Initialize();
        var recorder = new HighLevelInteractionRecorder(logDb);
        recorder.Record(new HighLevelInteractionRecord
        {
            Channel = "line",
            UserId = "tester",
            RawInput = "/build website",
            RawReply = "project name required",
            ParsedKind = HighLevelInputKind.Production.ToString(),
            WorkflowState = HighLevelWorkflowState.Idle.ToString(),
            WorkflowAction = HighLevelWorkflowAction.StartProduction.ToString(),
            RouteMode = HighLevelRouteMode.Production.ToString()
        });
        recorder.Record(new HighLevelInteractionRecord
        {
            Channel = "line",
            UserId = "tester",
            RawInput = "#MySite",
            RawReply = "draft updated",
            ParsedKind = HighLevelInputKind.ProjectName.ToString(),
            WorkflowState = HighLevelWorkflowState.AwaitingProjectName.ToString(),
            WorkflowAction = HighLevelWorkflowAction.CaptureProjectName.ToString(),
            RouteMode = HighLevelRouteMode.Production.ToString()
        });

        var logEntries = recorder.ReadLatest("line", "tester", 10);
        AssertTrue(logEntries.Count == 2, "interaction recorder persists append-only records by channel and user");
        AssertTrue(logEntries[0].RawInput == "/build website", "interaction recorder preserves raw input");
        AssertTrue(logEntries[1].WorkflowAction == HighLevelWorkflowAction.CaptureProjectName.ToString(), "interaction recorder preserves interpreted workflow action");

        var memoryStore = new HighLevelMemoryStore(logDb);
        memoryStore.Write(new HighLevelMemoryState
        {
            Channel = "line",
            UserId = "tester",
            CurrentGoal = "build website",
            LastRouteMode = HighLevelRouteMode.Production.ToString(),
            WorkflowState = HighLevelWorkflowState.AwaitingProjectName.ToString(),
            WorkflowAction = HighLevelWorkflowAction.CaptureProjectName.ToString(),
            PendingDraftId = "draft_123",
            PendingProjectName = true,
            LastTaskType = "code_gen"
        });

        var memoryState = memoryStore.ReadLatest("line", "tester");
        AssertTrue(memoryState != null, "memory store persists projected memory state");
        AssertTrue(memoryState!.CurrentGoal == "build website", "memory store keeps de-commanded current goal");
        AssertTrue(memoryState.PendingDraftId == "draft_123", "memory store preserves reusable workflow state");

        var interpretationStore = new HighLevelInterpretationStore(logDb);
        interpretationStore.Record(new HighLevelInterpretationRecord
        {
            Channel = "line",
            UserId = "tester",
            InteractionType = HighLevelRouteMode.Production.ToString(),
            ParsedKind = HighLevelInputKind.Production.ToString(),
            WorkflowState = HighLevelWorkflowState.Idle.ToString(),
            WorkflowAction = HighLevelWorkflowAction.StartProduction.ToString(),
            CommandExtractionAllowed = true,
            TrustReason = "raw user input may issue commands",
            CandidateGoal = "build website",
            TaskType = "code_gen",
            DraftId = "draft_123"
        });

        var interpretations = interpretationStore.ReadLatest("line", "tester", 10);
        AssertTrue(interpretations.Count == 1, "interpretation store persists append-only interpreted records");
        AssertTrue(interpretations[0].CandidateGoal == "build website", "interpretation store keeps candidate goal separate from raw log");

        var executionIntentStore = new HighLevelExecutionIntentStore(logDb);
        executionIntentStore.Write(new HighLevelExecutionIntent
        {
            Channel = "line",
            UserId = "tester",
            Stage = "executable",
            PromotionReason = "explicit confirm",
            Goal = "build website",
            TaskType = "code_gen",
            ProjectName = "MySite",
            DraftId = "draft_123"
        });

        var executionIntent = executionIntentStore.ReadLatest("line", "tester");
        AssertTrue(executionIntent != null, "execution intent store persists promoted executable intent");
        AssertTrue(executionIntent!.Stage == "executable", "execution intent store preserves promotion stage");
        AssertTrue(HighLevelExecutionIntentStore.BuildDocumentId("line", "tester") == "hlm.execution.line.tester", "execution intent document id is stable for downstream references");
    }

    var coordinatorDbPath = Path.Combine(sandboxRoot, "coordinator-profile.db");
    using (var coordinatorDb = BrokerDb.UseSqlite($"Data Source={coordinatorDbPath}"))
    {
        var initializer = new BrokerDbInitializer(coordinatorDb);
        initializer.Initialize();

        var queryMediator = new HighLevelQueryToolMediator(
            new FakeToolSpecRegistry(),
            new FakeExecutionDispatcher(),
            NullLogger<HighLevelQueryToolMediator>.Instance);

        var lineGateway = new LineChatGateway(
            new LineChatGatewayOptions
            {
                Enabled = true,
                RagEnabled = false,
                SystemPrompt = "verify"
            },
            new HighLevelLlmOptions
            {
                Provider = "ollama",
                BaseUrl = "http://localhost:11434",
                DefaultModel = "verify"
            },
            new FakeHttpClientFactory(),
            coordinatorDb,
            new EmbeddingService(new EmbeddingOptions { Enabled = false }),
            new RagPipelineService(new RagPipelineOptions
            {
                QueryRewriteEnabled = false,
                RerankEnabled = false,
                CacheEnabled = false
            }),
            NullLogger<LineChatGateway>.Instance);

        var coordinator = new HighLevelCoordinator(
            coordinatorDb,
            new FakeBrokerService(),
            new FakePlanService(),
            new FakeTaskRouter(),
            lineGateway,
            queryMediator,
            new HighLevelRelationQueryService(
                queryMediator,
                new HighLevelLlmOptions
                {
                    Provider = "ollama",
                    BaseUrl = "http://localhost:11434",
                    DefaultModel = "verify"
                },
                new FakeHttpClientFactory(),
                NullLogger<HighLevelRelationQueryService>.Instance),
            new HighLevelCoordinatorOptions
            {
                AccessRoot = Path.Combine(sandboxRoot, "managed"),
                CommandGuideReminderMinutes = 60
            },
            new FakeHighLevelExecutionModelPlanner(),
            new BrowserBindingService(coordinatorDb),
            NullLogger<HighLevelCoordinator>.Instance);

        var setName = await coordinator.ProcessLineMessageAsync("line-user-a", "/name 小布");
        AssertTrue(setName.Error == null, "coordinator accepts explicit display-name command");
        var profileAfterName = coordinator.GetLineUserProfile("line-user-a");
        AssertTrue(profileAfterName?.PreferredDisplayName == "小布", "coordinator persists preferred display name");

        var setId = await coordinator.ProcessLineMessageAsync("line-user-a", "/id bricks001");
        AssertTrue(setId.Error == null, "coordinator accepts explicit alphanumeric user id command");
        var profileAfterId = coordinator.GetLineUserProfile("line-user-a");
        AssertTrue(profileAfterId?.PreferredUserCode == "bricks001", "coordinator persists preferred alphanumeric user id");

        var profileView = await coordinator.ProcessLineMessageAsync("line-user-a", "?profile");
        AssertTrue(profileView.Reply.Contains("display_name: 小布", StringComparison.Ordinal), "profile query shows preferred display name");
        AssertTrue(profileView.Reply.Contains("user_code: bricks001", StringComparison.Ordinal), "profile query shows preferred alphanumeric user id");
        AssertTrue(profileView.Reply.Contains("目前擁有的權限：", StringComparison.Ordinal), "profile query shows current permission summary");
        AssertTrue(profileView.Reply.Contains("交通查詢：?rail、?hsr、?bus、?flight", StringComparison.Ordinal), "profile query lists transport query permissions");

        var updatedPermissions = coordinator.SetLineUserPermissions("line-user-a", new HighLevelUserPermissionsPatch
        {
            AllowQuery = false,
            AllowTransport = false,
            AllowProduction = false
        });
        AssertTrue(updatedPermissions?.Permissions.AllowQuery == false, "coordinator persists per-user query permission updates");
        AssertTrue(updatedPermissions?.Permissions.AllowTransport == false, "coordinator persists per-user transport permission updates");
        AssertTrue(updatedPermissions?.Permissions.AllowProduction == false, "coordinator persists per-user production permission updates");

        var deniedSearch = await coordinator.ProcessLineMessageAsync("line-user-a", "?search 中央氣象署官網");
        AssertTrue(deniedSearch.Error == "query_disabled", "coordinator denies explicit search when query permission is disabled");

        var deniedTransport = await coordinator.ProcessLineMessageAsync("line-user-a", "?hsr 台北 台中 今天 18:00");
        AssertTrue(deniedTransport.Error == "transport_disabled", "coordinator denies explicit transport query when transport permission is disabled");

        var deniedProduction = await coordinator.ProcessLineMessageAsync("line-user-a", "/build website prototype");
        AssertTrue(deniedProduction.Error == "production_disabled", "coordinator denies production command when production permission is disabled");

        coordinator.SetLineUserPermissions("line-user-a", new HighLevelUserPermissionsPatch
        {
            AllowQuery = true,
            AllowTransport = true,
            AllowProduction = true
        });

        var buildDraft = await coordinator.ProcessLineMessageAsync("line-user-a", "/build website prototype");
        AssertTrue(buildDraft.Draft != null, "production command still creates draft after profile customization");
        AssertTrue(buildDraft.Draft!.ManagedPaths.UserRoot.Contains("bricks001", StringComparison.OrdinalIgnoreCase), "managed paths use preferred alphanumeric user id");

        var projectNamed = await coordinator.ProcessLineMessageAsync("line-user-a", "#VerifySite");
        AssertTrue(projectNamed.Draft?.ProjectName == "VerifySite", "project-name command updates production draft");
        var confirmedDraft = await coordinator.ProcessLineMessageAsync("line-user-a", "confirm");
        AssertTrue(confirmedDraft.CreatedTask != null, "confirm creates broker task");
        using var runtimeDescriptorDoc = JsonDocument.Parse(confirmedDraft.CreatedTask!.RuntimeDescriptor);
        AssertTrue(runtimeDescriptorDoc.RootElement.TryGetProperty("requested_execution_model", out var requestedModelNode), "runtime descriptor includes requested execution model");
        AssertTrue(requestedModelNode.GetProperty("alias").GetString() == "execution-strong", "runtime descriptor preserves planner-requested model alias");
        AssertTrue(runtimeDescriptorDoc.RootElement.TryGetProperty("llm", out var llmNode), "runtime descriptor includes forwarded llm overrides");
        AssertTrue(llmNode.GetProperty("default_model").GetString() == "verify-strong-model", "runtime descriptor forwards validated execution model");

        var duplicateId = await coordinator.ProcessLineMessageAsync("line-user-b", "/id bricks001");
        AssertTrue(duplicateId.Error == "invalid_user_code", "coordinator rejects duplicate preferred user id");

        var firstConversation = await coordinator.ProcessLineMessageAsync("line-user-c", "你好");
        AssertTrue(firstConversation.Reply.Contains("目前擁有的權限：", StringComparison.Ordinal), "first interaction guide shows current permission summary");

        var querySuggestion = await coordinator.ProcessLineMessageAsync("line-user-d", "?中央氣象署官網");
        AssertTrue(querySuggestion.Reply.Contains("?search", StringComparison.Ordinal), "generic high-level query prompts controlled search when lookup is likely");

        var railSuggestion = await coordinator.ProcessLineMessageAsync("line-user-e", "台北到台中最晚高鐵班次");
        AssertTrue(railSuggestion.Reply.Contains("?hsr", StringComparison.Ordinal), "lookup-style transport conversation prompts controlled hsr search");

        var explicitSearchReply = await coordinator.ProcessLineMessageAsync("line-user-f", "?search 中央氣象署官網");
        AssertTrue(explicitSearchReply.Reply.Contains("verify-reply", StringComparison.OrdinalIgnoreCase), "explicit search uses high-level model to synthesize broker search results");
    }

    var readmePath = Path.Combine(sandboxRoot, "README.txt");
    File.WriteAllText(readmePath, "hello broker");

    var policy = new PolicyEngine(new SchemaValidator(), new PolicyEngineOptions());
    var capability = new Capability
    {
        CapabilityId = "file.read",
        Route = "read_file",
        ActionType = ActionType.Read,
        ResourceType = "file",
        RiskLevel = RiskLevel.Low,
        ApprovalPolicy = "auto",
        ParamSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string" }
            },
            required = new[] { "path" }
        })
    };

    var grant = new CapabilityGrant
    {
        CapabilityId = "file.read",
        ScopeOverride = JsonSerializer.Serialize(new
        {
            paths = new[] { sandboxRoot },
            routes = new[] { "read_file" }
        }),
        RemainingQuota = -1,
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        Status = GrantStatus.Active
    };

    var task = new BrokerTask
    {
        TaskId = "task_verify",
        TaskType = "analysis",
        SubmittedBy = "tester",
        ScopeDescriptor = "{}",
        State = TaskState.Active,
        RiskLevel = RiskLevel.Low
    };

    var allowed = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { path = "README.txt" },
            project_root = sandboxRoot
        })
    };

    var allowedResult = policy.Evaluate(allowed, capability, grant, task, 1, 1);
    AssertTrue(allowedResult.Decision == PolicyDecision.Allow, "policy allows matching capability + route + path scope");

    var wrongRoute = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "list_directory",
            args = new { path = "README.txt" },
            project_root = sandboxRoot
        })
    };

    var wrongRouteResult = policy.Evaluate(wrongRoute, capability, grant, task, 1, 1);
    AssertTrue(wrongRouteResult.Decision == PolicyDecision.Deny, "policy denies payload route outside capability route");

    var wrongPath = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { path = "../secret.txt" },
            project_root = sandboxRoot
        })
    };

    var wrongPathResult = policy.Evaluate(wrongPath, capability, grant, task, 1, 1);
    AssertTrue(wrongPathResult.Decision == PolicyDecision.Deny, "policy denies path outside granted path scope");

    var missingPath = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { },
            project_root = sandboxRoot
        })
    };

    var missingPathResult = policy.Evaluate(missingPath, capability, grant, task, 1, 1);
    AssertTrue(missingPathResult.Decision == PolicyDecision.Deny, "policy validates args schema instead of wrapper payload");

    var dispatcher = new InProcessDispatcher(NullLogger<InProcessDispatcher>.Instance, sandboxRoot);

    var readResult = await dispatcher.DispatchAsync(new BrokerCore.Contracts.ApprovedRequest
    {
        RequestId = "req_read",
        Route = "read_file",
        Payload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { path = "README.txt" }
        })
    });
    AssertTrue(readResult.Success, "dispatcher reads file from canonical route+args payload");

    var searchResult = await dispatcher.DispatchAsync(new BrokerCore.Contracts.ApprovedRequest
    {
        RequestId = "req_search",
        Route = "search_content",
        Payload = JsonSerializer.Serialize(new
        {
            route = "search_content",
            args = new
            {
                directory = ".",
                pattern = "hello",
                file_pattern = "*.txt"
            }
        })
    });
    AssertTrue(searchResult.Success, "dispatcher searches content from canonical route+args payload");

    var routeMismatchResult = await dispatcher.DispatchAsync(new BrokerCore.Contracts.ApprovedRequest
    {
        RequestId = "req_bad_route",
        Route = "read_file",
        Payload = JsonSerializer.Serialize(new
        {
            route = "list_directory",
            args = new { path = "README.txt" }
        })
    });
    AssertTrue(!routeMismatchResult.Success, "dispatcher denies mismatched payload route");

    Console.WriteLine("Broker verify passed.");
}
finally
{
    var deleted = false;
    for (var attempt = 0; attempt < 5; attempt++)
    {
        try
        {
            Directory.Delete(sandboxRoot, recursive: true);
            deleted = true;
            break;
        }
        catch (IOException)
        {
            if (attempt < 4)
            {
                Thread.Sleep(250 * (attempt + 1));
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (attempt < 4)
            {
                Thread.Sleep(250 * (attempt + 1));
            }
        }
    }

    if (!deleted)
    {
        Console.WriteLine($"Warning: verify temp directory not deleted: {sandboxRoot}");
    }
}

file sealed class FakeToolSpecRegistry : IToolSpecRegistry
{
    private readonly IReadOnlyList<ToolSpecView> _views =
    [
        new ToolSpecView
        {
            ToolId = "web.search.google",
            DisplayName = "Google Web Search",
            Summary = "broker mediated search",
            Kind = "search",
            Status = "active",
            CapabilityBindings =
            [
                new ToolCapabilityBindingView
                {
                    CapabilityId = "web.search.google",
                    Route = "web_search_google",
                    Purpose = "test",
                    Registered = true,
                    RegisteredRoute = "web_search_google"
                }
            ]
        },
        new ToolSpecView
        {
            ToolId = "web.search.duckduckgo",
            DisplayName = "DuckDuckGo Web Search",
            Summary = "broker mediated search",
            Kind = "search",
            Status = "active",
            CapabilityBindings =
            [
                new ToolCapabilityBindingView
                {
                    CapabilityId = "web.search.duckduckgo",
                    Route = "web_search_duckduckgo",
                    Purpose = "test",
                    Registered = true,
                    RegisteredRoute = "web_search_duckduckgo"
                }
            ]
        },
        new ToolSpecView
        {
            ToolId = "travel.rail.search",
            DisplayName = "Rail Search",
            Summary = "broker mediated rail search",
            Kind = "travel",
            Status = "active",
            CapabilityBindings =
            [
                new ToolCapabilityBindingView
                {
                    CapabilityId = "travel.rail.search",
                    Route = "travel_rail_search",
                    Purpose = "test",
                    Registered = true,
                    RegisteredRoute = "travel_rail_search"
                }
            ]
        },
        new ToolSpecView
        {
            ToolId = "travel.hsr.search",
            DisplayName = "HSR Search",
            Summary = "broker mediated hsr search",
            Kind = "travel",
            Status = "active",
            CapabilityBindings =
            [
                new ToolCapabilityBindingView
                {
                    CapabilityId = "travel.hsr.search",
                    Route = "travel_hsr_search",
                    Purpose = "test",
                    Registered = true,
                    RegisteredRoute = "travel_hsr_search"
                }
            ]
        },
        new ToolSpecView
        {
            ToolId = "travel.bus.search",
            DisplayName = "Bus Search",
            Summary = "broker mediated bus search",
            Kind = "travel",
            Status = "active",
            CapabilityBindings =
            [
                new ToolCapabilityBindingView
                {
                    CapabilityId = "travel.bus.search",
                    Route = "travel_bus_search",
                    Purpose = "test",
                    Registered = true,
                    RegisteredRoute = "travel_bus_search"
                }
            ]
        },
        new ToolSpecView
        {
            ToolId = "travel.flight.search",
            DisplayName = "Flight Search",
            Summary = "broker mediated flight search",
            Kind = "travel",
            Status = "active",
            CapabilityBindings =
            [
                new ToolCapabilityBindingView
                {
                    CapabilityId = "travel.flight.search",
                    Route = "travel_flight_search",
                    Purpose = "test",
                    Registered = true,
                    RegisteredRoute = "travel_flight_search"
                }
            ]
        }
    ];

    public ToolSpecView? Get(string toolId)
        => _views.FirstOrDefault(view => string.Equals(toolId, view.ToolId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ToolSpecDocument> GetDefinitions()
        => Array.Empty<ToolSpecDocument>();

    public IReadOnlyList<ToolSpecView> List(string? filter = null)
        => _views;
}

file sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => new(new FakeLlmHandler())
        {
            BaseAddress = new Uri("http://localhost/")
        };
}

file sealed class FakeHighLevelExecutionModelPlanner : IHighLevelExecutionModelPlanner
{
    public Task<HighLevelExecutionModelRequest?> RecommendAsync(
        HighLevelTaskDraft draft,
        HighLevelMemoryState memory,
        CancellationToken cancellationToken = default)
        => Task.FromResult<HighLevelExecutionModelRequest?>(new HighLevelExecutionModelRequest
        {
            Alias = "execution-strong",
            Model = "verify-strong-model",
            Tier = "strong",
            Reason = $"verify planner request for {draft.TaskType}",
            RequestedBy = "high-level-entry-model",
            ValidationStatus = "validated"
        });
}

file sealed class FakeLlmHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = """
        {
          "message": {
            "content": "verify-reply"
          }
        }
        """;

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body)
        });
    }
}

file sealed class FakeBrokerService : IBrokerService
{
    public BrokerTask CreateTask(string submittedBy, string taskType, string scopeDescriptor, string? assignedPrincipalId = null, string? assignedRoleId = null, string? runtimeDescriptor = null)
        => new()
        {
            TaskId = "task_fake",
            TaskType = taskType,
            SubmittedBy = submittedBy,
            ScopeDescriptor = scopeDescriptor,
            RuntimeDescriptor = runtimeDescriptor ?? "{}",
            State = TaskState.Active,
            RiskLevel = RiskLevel.Low
        };

    public BrokerTask? GetTask(string taskId) => null;

    public bool CancelTask(string taskId, string cancelledBy, string reason) => true;

    public Task<ExecutionRequest> SubmitExecutionRequestAsync(string principalId, string taskId, string sessionId, string capabilityId, string intent, string requestPayload, string idempotencyKey, string traceId)
        => Task.FromResult(new ExecutionRequest
        {
            RequestId = "req_fake",
            PrincipalId = principalId,
            TaskId = taskId,
            SessionId = sessionId,
            CapabilityId = capabilityId,
            Intent = intent,
            RequestPayload = requestPayload,
            IdempotencyKey = idempotencyKey,
            TraceId = traceId
        });

    public ExecutionRequest? GetExecutionRequest(string requestId) => null;
}

file sealed class FakePlanService : IPlanService
{
    public Plan CreatePlan(string taskId, string submittedBy, string title, string? description)
        => new()
        {
            PlanId = "plan_fake",
            TaskId = taskId,
            SubmittedBy = submittedBy,
            Title = title,
            Description = description,
            State = PlanState.Draft
        };

    public Plan? GetPlan(string planId) => null;
    public bool UpdatePlanState(string planId, PlanState newState) => true;
    public PlanNode AddNode(string planId, string capabilityId, string intent, string requestPayload, string? outputContextKey, int maxRetries) => new();
    public List<PlanNode> GetNodes(string planId) => [];
    public bool UpdateNodeState(string nodeId, NodeState state, string? requestId) => true;
    public bool IncrementRetryCount(string nodeId) => true;
    public PlanEdge AddEdge(string planId, string fromNodeId, string toNodeId, EdgeType edgeType, string? contextKey, string? condition) => new();
    public List<PlanEdge> GetEdges(string planId) => [];
    public List<PlanEdge> GetIncomingEdges(string nodeId) => [];
    public (bool IsValid, string? Error) ValidateDag(string planId) => (true, null);
    public List<PlanNode> GetReadyNodes(string planId) => [];
    public Checkpoint CreateCheckpoint(string planId, string nodeId, string snapshotRef) => new();
    public List<Checkpoint> GetCheckpoints(string planId) => [];
}

file sealed class FakeTaskRouter : ITaskRouter
{
    public RiskLevel AssessRisk(string taskType, string scopeDescriptor) => RiskLevel.Low;
    public string? RecommendRole(string taskType) => "role_admin";
}

file sealed class FakeExecutionDispatcher : IExecutionDispatcher
{
    public Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
    {
        using var payloadDoc = JsonDocument.Parse(approvedRequest.Payload);
        var query = payloadDoc.RootElement
            .GetProperty("args")
            .TryGetProperty("query", out var queryNode)
            ? queryNode.GetString() ?? string.Empty
            : string.Empty;

        object payloadObject = approvedRequest.Route switch
        {
            "web_search_google" => new
            {
                engine = "google",
                query = string.IsNullOrWhiteSpace(query) ? "taipei weather" : query,
                results = BuildGoogleResults(query)
            },
            "travel_rail_search" => new
            {
                mode = "rail",
                query = "台北 台中 今天 18:00",
                retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                sources_used = new[] { "DuckDuckGo / railway.gov.tw" },
                results = new[]
                {
                    new
                    {
                        rank = 1,
                        title = "台北到台中列車候選",
                        url = "https://example.com/rail",
                        snippet = "晚間班次候選",
                        time_candidates = new[] { "18:30", "19:00" }
                    }
                }
            },
            "travel_hsr_search" => new
            {
                mode = "hsr",
                query = "台北 台中 今天 18:00",
                retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                sources_used = new[] { "DuckDuckGo / thsrc.com.tw" },
                results = new[]
                {
                    new
                    {
                        rank = 1,
                        title = "台北到台中高鐵班次",
                        url = "https://example.com/hsr",
                        snippet = "高鐵候選班次",
                        time_candidates = new[] { "06:15", "06:30" }
                    }
                }
            },
            "travel_bus_search" => new
            {
                mode = "bus",
                query = "台北 台中 今天 18:00",
                retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                sources_used = new[] { "DuckDuckGo / public transport web" },
                results = new[]
                {
                    new
                    {
                        rank = 1,
                        title = "台北到台中客運候選",
                        url = "https://example.com/bus",
                        snippet = "晚間客運候選",
                        time_candidates = new[] { "18:10", "18:40" }
                    }
                }
            },
            "travel_flight_search" => new
            {
                mode = "flight",
                query = "TPE KIX tomorrow",
                retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                sources_used = new[] { "DuckDuckGo / public travel web" },
                results = new[]
                {
                    new
                    {
                        rank = 1,
                        title = "TPE 到 KIX 航班候選",
                        url = "https://example.com/flight",
                        snippet = "航班時刻候選",
                        time_candidates = new[] { "09:15", "13:40" }
                    }
                }
            },
            _ => new
            {
                engine = "duckduckgo",
                query = "taipei weather",
                results = new[]
                {
                    new
                    {
                        rank = 1,
                        title = "Taipei Weather",
                        url = "https://example.com/weather",
                        snippet = "Rain later this afternoon."
                    }
                }
            }
        };

        var payload = JsonSerializer.Serialize(payloadObject);

        return Task.FromResult(ExecutionResult.Ok(approvedRequest.RequestId, payload));
    }

    private static object[] BuildGoogleResults(string query)
    {
        if (query.Contains("北京", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new
                {
                    rank = 1,
                    title = "北京市行政區劃 - 維基百科",
                    url = "https://zh.wikipedia.org/wiki/%E5%8C%97%E4%BA%AC%E5%B8%82%E8%A1%8C%E6%94%BF%E5%8D%80%E5%8A%83",
                    snippet = "北京市現轄東城區、西城區、朝陽區、海淀區、豐台區等多個市轄區。"
                },
                new
                {
                    rank = 2,
                    title = "行政區劃_首都之窗",
                    url = "https://www.beijing.gov.cn/renwen/bjgk/xzqh/",
                    snippet = "北京市行政區包括朝陽區、海淀區、豐台區、通州區等。"
                }
            ];
        }

        return
        [
            new
            {
                rank = 1,
                title = "Taipei Weather",
                url = "https://example.com/weather",
                snippet = "Rain later this afternoon."
            }
        ];
    }
}

file sealed class FakeBrowserPreviewHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var html = "<html><head><title>Example Preview</title></head><body><h1>Hello</h1><p>Preview body text.</p></body></html>";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(html)
        });
    }
}

file sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = contentRootPath;
        ApplicationName = "Broker.Verify";
        EnvironmentName = "Development";
    }

    public string ApplicationName { get; set; }
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; }
    public string EnvironmentName { get; set; }
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

file sealed class FakeAzureIisDeploymentSecretResolver : IAzureIisDeploymentSecretResolver
{
    public AzureIisDeploymentSecret? Resolve(string secretRef)
        => new()
        {
            UserName = "vm-user",
            Password = "vm-password"
        };
}

file sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRunSpec> Invocations { get; } = new();

    public Task<ProcessRunResult> RunAsync(ProcessRunSpec spec, CancellationToken cancellationToken = default)
    {
        Invocations.Add(spec);
        return Task.FromResult(new ProcessRunResult
        {
            ExitCode = 0,
            StandardOutput = $"ok:{spec.FileName}",
            StandardError = string.Empty
        });
    }
}
