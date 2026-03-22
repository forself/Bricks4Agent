using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore.Contracts;
using BrokerCore.Models;
using BrokerCore.Services;
using BrokerCore.Data;
using Broker.Services;
using Broker.Scripts;

namespace Broker.Adapters;

/// <summary>
/// Phase 1 Inline 執行轉發器 —— 僅處理低風險讀取操作
///
/// 設計：
/// - 在 broker 進程內直接執行（不跨進程/跨網路）
/// - 僅實作 file.read、file.list、file.search_name、file.search_content
/// - 所有路徑受沙箱限制
///
/// Phase 2 替換為：
/// - HttpDispatcher（呼叫 Agent Container 的 REST API）
/// - MessageQueueDispatcher（透過訊息佇列非同步轉發）
/// </summary>
public class InProcessDispatcher : IExecutionDispatcher
{
    private readonly ILogger<InProcessDispatcher> _logger;
    private readonly string _sandboxRoot;
    private readonly AgentSpawnService? _agentSpawnService;
    private readonly BrokerDb? _db;
    private readonly EmbeddingService? _embeddingService;
    private readonly RagPipelineService? _ragPipeline;
    private readonly AzureIisDeploymentExecutionService? _azureIisDeploymentExecutionService;
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    static InProcessDispatcher()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("B4A-Agent/1.0");
    }

    public InProcessDispatcher(
        ILogger<InProcessDispatcher> logger,
        string? sandboxRoot = null,
        AgentSpawnService? agentSpawnService = null,
        BrokerDb? db = null,
        EmbeddingService? embeddingService = null,
        RagPipelineService? ragPipeline = null,
        AzureIisDeploymentExecutionService? azureIisDeploymentExecutionService = null)
    {
        _logger = logger;
        _sandboxRoot = sandboxRoot ?? Path.GetFullPath(".");
        _agentSpawnService = agentSpawnService;
        _db = db;
        _embeddingService = embeddingService;
        _ragPipeline = ragPipeline;
        _azureIisDeploymentExecutionService = azureIisDeploymentExecutionService;
    }

    /// <inheritdoc />
    public Task<ExecutionResult> DispatchAsync(ApprovedRequest request)
    {
        try
        {
            // 同步路由
            ExecutionResult? syncResult = request.Route switch
            {
                "read_file" => ExecuteReadFile(request),
                "list_directory" => ExecuteListDirectory(request),
                "search_files" => ExecuteSearchFiles(request),
                "search_content" => ExecuteSearchContent(request),
                "list_agents" => ExecuteListAgents(request),
                "create_agent" => ExecuteCreateAgent(request),
                "stop_agent" => ExecuteStopAgent(request),
                "conv_log_append" => ExecuteConvLogAppend(request),
                "conv_log_read" => ExecuteConvLogRead(request),
                "memory_store" => ExecuteMemoryStore(request),
                "memory_retrieve" => ExecuteMemoryRetrieve(request),
                "memory_delete" => ExecuteMemoryDelete(request),
                _ => null
            };

            if (syncResult != null)
                return Task.FromResult(syncResult);

            // 非同步路由（需要網路 I/O 或 embedding）
            return request.Route switch
            {
                "memory_semantic_search" => ExecuteSemanticSearchAsync(request),
                "memory_fulltext_search" => Task.FromResult(ExecuteFulltextSearch(request)),
                "rag_retrieve" => ExecuteRagRetrieveAsync(request),
                "rag_import" => ExecuteRagImportAsync(request),
                "rag_import_web" => ExecuteRagImportWebAsync(request),
                "web_search_google" => ExecuteWebSearchGoogleAsync(request),
                "web_search_duckduckgo" => ExecuteWebSearchDuckDuckGoAsync(request),
                "travel_rail_search" => ExecuteTravelRailSearchAsync(request),
                "travel_bus_search" => ExecuteTravelBusSearchAsync(request),
                "travel_flight_search" => ExecuteTravelFlightSearchAsync(request),
                "web_search" => ExecuteWebSearchAsync(request),
                "web_fetch" => ExecuteWebFetchAsync(request),
                "deploy_azure_vm_iis" => ExecuteAzureIisDeploymentAsync(request),
                _ => Task.FromResult(ExecutionResult.Fail(request.RequestId,
                    $"Route '{request.Route}' not supported in InProcessDispatcher."))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InProcessDispatcher failed for route {Route}", request.Route);
            return Task.FromResult(ExecutionResult.Fail(request.RequestId, ex.Message));
        }
    }

    private ExecutionResult ExecuteReadFile(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var filePath = TryGetString(args, "path", "file_path") ?? "";

        var fullPath = ResolveSandboxedPath(filePath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!File.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"File not found: {filePath}");

        var content = File.ReadAllText(fullPath);

        // 截斷過長內容（Phase 1 限制 100KB）
        if (content.Length > 102400)
            content = content[..102400] + "\n... [truncated]";

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { path = filePath, content, size = new FileInfo(fullPath).Length }));
    }

    private ExecutionResult ExecuteListDirectory(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var dirPath = TryGetString(args, "path", "directory") ?? ".";

        var fullPath = ResolveSandboxedPath(dirPath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!Directory.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"Directory not found: {dirPath}");

        var entries = new List<object>();

        foreach (var dir in Directory.GetDirectories(fullPath).Take(100))
        {
            entries.Add(new { name = Path.GetFileName(dir), type = "directory" });
        }

        foreach (var file in Directory.GetFiles(fullPath).Take(200))
        {
            var info = new FileInfo(file);
            entries.Add(new { name = Path.GetFileName(file), type = "file", size = info.Length });
        }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { path = dirPath, entries }));
    }

    private ExecutionResult ExecuteSearchFiles(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var pattern = TryGetString(args, "pattern") ?? "*";
        var basePath = TryGetString(args, "directory", "path") ?? ".";

        var fullPath = ResolveSandboxedPath(basePath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!Directory.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"Directory not found: {basePath}");

        var files = Directory.GetFiles(fullPath, pattern, SearchOption.AllDirectories)
            .Take(100)
            .Select(f => Path.GetRelativePath(fullPath, f).Replace('\\', '/'))
            .ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { basePath, pattern, matches = files }));
    }

    private ExecutionResult ExecuteSearchContent(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "pattern", "query") ?? "";
        var basePath = TryGetString(args, "directory", "path") ?? ".";
        var filePattern = TryGetString(args, "file_pattern") ?? "*";

        var fullPath = ResolveSandboxedPath(basePath);
        if (fullPath == null)
            return ExecutionResult.Fail(request.RequestId, "Path outside sandbox.");

        if (!Directory.Exists(fullPath))
            return ExecutionResult.Fail(request.RequestId, $"Directory not found: {basePath}");

        var results = new List<object>();
        var files = Directory.GetFiles(fullPath, filePattern, SearchOption.AllDirectories).Take(500);

        foreach (var file in files)
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new
                        {
                            file = Path.GetRelativePath(fullPath, file).Replace('\\', '/'),
                            line = i + 1,
                            content = lines[i].Length > 200 ? lines[i][..200] + "..." : lines[i]
                        });

                        if (results.Count >= 50) break;
                    }
                }
                if (results.Count >= 50) break;
            }
            catch (OutOfMemoryException) { throw; } // L-9 修復：不可恢復的例外必須重新拋出
            catch (Exception ex)
            {
                // 跳過無法讀取的檔案（IO 錯誤、權限不足等可恢復例外）
                _logger.LogDebug(ex, "Skipping unreadable file during content search: {File}", file);
            }
        }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, basePath, matches = results }));
    }

    private ExecutionResult ExecuteListAgents(ApprovedRequest request)
    {
        if (_agentSpawnService == null)
            return ExecutionResult.Fail(request.RequestId, "AgentSpawnService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var filter = TryGetString(args, "filter");

        var agents = _agentSpawnService.ListAgents();
        if (!string.IsNullOrEmpty(filter))
            agents = agents.Where(a => a.State.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var summary = agents.Select(a => new
        {
            agent_id = a.AgentId,
            state = a.State,
            role = a.RoleId,
            capabilities = a.Capabilities,
            capability_count = a.CapabilityCount,
            created_at = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { agents = summary, total = agents.Count }));
    }

    private ExecutionResult ExecuteCreateAgent(ApprovedRequest request)
    {
        if (_agentSpawnService == null)
            return ExecutionResult.Fail(request.RequestId, "AgentSpawnService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);

        var capabilityIds = new List<string>();
        if (args.TryGetProperty("capability_ids", out var capsEl) && capsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in capsEl.EnumerateArray())
            {
                var id = item.GetString();
                if (!string.IsNullOrEmpty(id)) capabilityIds.Add(id);
            }
        }

        if (capabilityIds.Count == 0)
            capabilityIds = _agentSpawnService.GetDefaultCapabilities();

        var spawnRequest = new AgentSpawnRequest
        {
            DisplayName = TryGetString(args, "display_name"),
            CapabilityIds = capabilityIds,
            TaskType = TryGetString(args, "task_type") ?? "analysis",
            RequestedBy = request.PrincipalId ?? "agent"
        };

        var result = _agentSpawnService.CreateAgent(spawnRequest);
        if (!result.Success)
            return ExecutionResult.Fail(request.RequestId, result.Error ?? "Failed to create agent");

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new
            {
                agent_id = result.AgentId,
                principal_id = result.PrincipalId,
                task_id = result.TaskId,
                role_id = result.RoleId,
                granted_capabilities = result.GrantedCapabilities,
                max_risk_level = result.MaxRiskLevel,
                warnings = result.Warnings
            }));
    }

    private ExecutionResult ExecuteStopAgent(ApprovedRequest request)
    {
        if (_agentSpawnService == null)
            return ExecutionResult.Fail(request.RequestId, "AgentSpawnService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var agentId = TryGetString(args, "agent_id") ?? "";

        if (string.IsNullOrEmpty(agentId))
            return ExecutionResult.Fail(request.RequestId, "agent_id is required.");

        var deactivated = _agentSpawnService.DeactivateAgent(agentId);
        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new
            {
                agent_id = agentId,
                deactivated,
                status = deactivated ? "stopped" : "not_found"
            }));
    }

    // ═══════════════════════════════════════════
    // Conv Log 路由（Layer 1：自動對話日誌）
    // ═══════════════════════════════════════════

    private ExecutionResult ExecuteConvLogAppend(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var userId = TryGetString(args, "user_id") ?? "";
        var role = TryGetString(args, "role") ?? "user";
        var content = TryGetString(args, "content") ?? "";
        var metadata = TryGetString(args, "metadata") ?? "";

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(content))
            return ExecutionResult.Fail(request.RequestId, "user_id and content are required.");

        var taskId = request.TaskId ?? "global";
        var principalId = request.PrincipalId ?? "system";
        var now = DateTime.UtcNow;

        // key 格式: convlog:{userId} — 每則訊息一筆，版本遞增
        var logKey = $"convlog:{userId}";
        var documentId = $"convlog_{taskId}_{userId}";

        // 查詢最新版本號
        var existing = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == logKey)
            .OrderByDescending(e => e.Version)
            .FirstOrDefault();

        var version = (existing?.Version ?? 0) + 1;

        // 內容包含角色 + 時間戳 + 原始訊息（結構化 JSON）
        var logEntry = JsonSerializer.Serialize(new
        {
            role,
            content,
            timestamp = now.ToString("o"),
            metadata
        });

        var entry = new SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("cvl"),
            DocumentId = documentId,
            Version = version,
            ParentVersion = existing?.Version,
            Key = logKey,
            ContentRef = logEntry,
            ContentType = "application/json",
            AuthorPrincipalId = principalId,
            TaskId = taskId,
            CreatedAt = now
        };

        _db.Insert(entry);

        // FTS5 索引對話內容
        try
        {
            _db.Execute(
                "INSERT INTO convlog_fts(user_id, role, content, task_id) VALUES(@userId, @role, @content, @taskId)",
                new { userId, role, content = PrepareFts5Query(content), taskId });
        }
        catch { /* FTS5 表可能不存在 */ }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { user_id = userId, version, logged = true }));
    }

    private ExecutionResult ExecuteConvLogRead(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var userId = TryGetString(args, "user_id") ?? "";
        var limitStr = TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? l : 50;
        var before = TryGetString(args, "before"); // ISO timestamp

        if (string.IsNullOrEmpty(userId))
            return ExecutionResult.Fail(request.RequestId, "user_id is required.");

        var taskId = request.TaskId ?? "global";
        var logKey = $"convlog:{userId}";

        var query = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == logKey);

        if (!string.IsNullOrEmpty(before) && DateTime.TryParse(before, out var beforeDt))
        {
            query = query.Where(e => e.CreatedAt < beforeDt);
        }

        // 取最近 N 則，按版本正序返回（舊→新）
        var entries = query
            .OrderByDescending(e => e.Version)
            .Take(limit)
            .ToList();

        entries.Reverse(); // 恢復時間正序

        var messages = entries.Select(e =>
        {
            try
            {
                var parsed = JsonDocument.Parse(e.ContentRef);
                return new
                {
                    version = e.Version,
                    role = parsed.RootElement.TryGetProperty("role", out var r) ? r.GetString() ?? "unknown" : "unknown",
                    content = parsed.RootElement.TryGetProperty("content", out var c) ? c.GetString() ?? e.ContentRef : e.ContentRef,
                    timestamp = parsed.RootElement.TryGetProperty("timestamp", out var t) ? t.GetString() ?? e.CreatedAt.ToString("o") : e.CreatedAt.ToString("o")
                };
            }
            catch
            {
                return new
                {
                    version = e.Version,
                    role = "unknown",
                    content = e.ContentRef,
                    timestamp = e.CreatedAt.ToString("o")
                };
            }
        }).ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { user_id = userId, messages, total = messages.Count }));
    }

    // ═══════════════════════════════════════════
    // Memory 路由（Layer 2：Agent 判斷式記憶）
    // ═══════════════════════════════════════════

    private ExecutionResult ExecuteMemoryStore(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var key = TryGetString(args, "key") ?? "";
        var value = TryGetString(args, "value") ?? "";
        var contentType = TryGetString(args, "content_type") ?? "text/plain";

        if (string.IsNullOrEmpty(key))
            return ExecutionResult.Fail(request.RequestId, "key is required.");

        var taskId = request.TaskId ?? "global";
        var principalId = request.PrincipalId ?? "system";
        var documentId = $"mem_{taskId}_{key}";

        // 查詢同 key 的最新版本
        var existing = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == key)
            .OrderByDescending(e => e.Version)
            .FirstOrDefault();

        var version = (existing?.Version ?? 0) + 1;

        var entry = new SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("sce"),
            DocumentId = documentId,
            Version = version,
            ParentVersion = existing?.Version,
            Key = key,
            ContentRef = value,
            ContentType = contentType,
            AuthorPrincipalId = principalId,
            TaskId = taskId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Insert(entry);

        // FTS5 索引：先刪除舊的再插入新的
        try
        {
            _db.Execute("DELETE FROM memory_fts WHERE source_key = @key AND task_id = @taskId",
                new { key, taskId });
            _db.Execute("INSERT INTO memory_fts(source_key, content, task_id) VALUES(@key, @value, @taskId)",
                new { key, value = PrepareFts5Query(value), taskId });
        }
        catch { /* FTS5 表可能不存在，忽略 */ }

        // 非同步嵌入（fire-and-forget，不阻塞回覆）
        if (_embeddingService is { IsEnabled: true })
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var hash = EmbeddingService.ComputeHash(value);

                    // 檢查是否已有相同 hash 的嵌入
                    var existingVec = _db!.GetAll<VectorEntry>()
                        .FirstOrDefault(v => v.ContentHash == hash && v.TaskId == taskId);
                    if (existingVec != null)
                    {
                        // 更新 source_key 即可
                        existingVec.SourceKey = key;
                        _db.Update(existingVec);
                        return;
                    }

                    var vector = await _embeddingService.EmbedAsync($"{key}: {value}");
                    if (vector == null) return;

                    // 刪除該 key 的舊向量
                    _db.Execute("DELETE FROM vector_entries WHERE source_key = @key AND task_id = @taskId",
                        new { key, taskId });

                    _db.Insert(new VectorEntry
                    {
                        EntryId = BrokerCore.IdGen.New("vec"),
                        SourceKey = key,
                        TaskId = taskId,
                        TextPreview = value.Length > 500 ? value[..500] : value,
                        ContentHash = hash,
                        Embedding = EmbeddingService.VectorToBytes(vector),
                        EmbeddingModel = _embeddingService.ModelName,
                        Dimension = vector.Length,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Async embedding failed for key {Key}", key);
                }
            });
        }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { key, version, stored = true,
                embedding_queued = _embeddingService is { IsEnabled: true } }));
    }

    private ExecutionResult ExecuteMemoryRetrieve(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var key = TryGetString(args, "key");
        var search = TryGetString(args, "search");
        var limitStr = TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? l : 20;

        var taskId = request.TaskId ?? "global";
        var entries = _db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId);

        if (!string.IsNullOrEmpty(key))
        {
            // 精確 key 查詢 — 回傳最新版本
            var latest = entries.Where(e => e.Key == key)
                .OrderByDescending(e => e.Version)
                .FirstOrDefault();

            if (latest == null)
                return ExecutionResult.Ok(request.RequestId,
                    JsonSerializer.Serialize(new { key, found = false }));

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new
                {
                    key = latest.Key,
                    value = latest.ContentRef,
                    version = latest.Version,
                    content_type = latest.ContentType,
                    created_at = latest.CreatedAt.ToString("o"),
                    found = true
                }));
        }

        if (!string.IsNullOrEmpty(search))
        {
            // 模糊搜尋 key 和 value
            var results = entries
                .Where(e => e.Key.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            e.ContentRef.Contains(search, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.Key)
                .Select(g => g.OrderByDescending(e => e.Version).First())
                .Take(limit)
                .Select(e => new
                {
                    key = e.Key,
                    value = e.ContentRef.Length > 200 ? e.ContentRef[..200] + "..." : e.ContentRef,
                    version = e.Version
                })
                .ToList();

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { search, matches = results, total = results.Count }));
        }

        // 列出所有 keys
        var allKeys = entries
            .GroupBy(e => e.Key)
            .Select(g =>
            {
                var latest = g.OrderByDescending(e => e.Version).First();
                return new { key = latest.Key, version = latest.Version, content_type = latest.ContentType };
            })
            .Take(limit)
            .ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { keys = allKeys, total = allKeys.Count }));
    }

    private ExecutionResult ExecuteMemoryDelete(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var key = TryGetString(args, "key") ?? "";

        if (string.IsNullOrEmpty(key))
            return ExecutionResult.Fail(request.RequestId, "key is required.");

        var taskId = request.TaskId ?? "global";
        var deleted = _db.Execute(
            "DELETE FROM shared_context_entries WHERE task_id = @taskId AND key = @key",
            new { taskId, key });

        // 清除 FTS5 + 向量
        try { _db.Execute("DELETE FROM memory_fts WHERE source_key = @key AND task_id = @taskId", new { key, taskId }); } catch { }
        try { _db.Execute("DELETE FROM vector_entries WHERE source_key = @key AND task_id = @taskId", new { key, taskId }); } catch { }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { key, deleted_count = deleted }));
    }

    // ═══════════════════════════════════════════
    // 搜尋路由（BM25 + Vector + RAG）
    // ═══════════════════════════════════════════

    /// <summary>BM25 全文檢索</summary>
    private ExecutionResult ExecuteFulltextSearch(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var scope = TryGetString(args, "scope") ?? "memory";
        var limitStr = TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 50) : 10;

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        var taskId = request.TaskId ?? "global";
        var results = new List<object>();

        // 搜尋智慧記憶
        if (scope is "memory" or "all")
        {
            try
            {
                var ftsResults = _db.Query<FtsResult>(
                    "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @limit",
                    new { q = PrepareFts5Query(query), taskId, limit });

                foreach (var r in ftsResults)
                {
                    results.Add(new
                    {
                        source = "memory",
                        key = r.SourceKey,
                        content = r.Content?.Length > 300 ? r.Content[..300] + "..." : r.Content,
                        bm25_score = r.Rank
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FTS5 memory search failed");
            }
        }

        // 搜尋對話日誌
        if (scope is "convlog" or "all")
        {
            try
            {
                var ftsResults = _db.Query<ConvlogFtsResult>(
                    "SELECT user_id, role, content, rank FROM convlog_fts WHERE convlog_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @limit",
                    new { q = PrepareFts5Query(query), taskId, limit });

                foreach (var r in ftsResults)
                {
                    results.Add(new
                    {
                        source = "convlog",
                        user_id = r.UserId,
                        role = r.Role,
                        content = r.Content?.Length > 300 ? r.Content[..300] + "..." : r.Content,
                        bm25_score = r.Rank
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FTS5 convlog search failed");
            }
        }

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, scope, results, total = results.Count }));
    }

    /// <summary>向量語意搜尋（cosine similarity）</summary>
    private async Task<ExecutionResult> ExecuteSemanticSearchAsync(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");
        if (_embeddingService is not { IsEnabled: true })
            return ExecutionResult.Fail(request.RequestId, "Embedding service not enabled.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var locale = TryGetString(args, "locale") ?? "zh-TW";
        var safeMode = TryGetString(args, "safe_mode") ?? "moderate";
        var limitStr = TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 20) : 5;
        var thresholdStr = TryGetString(args, "threshold");
        var threshold = float.TryParse(thresholdStr, out var t) ? t : 0.3f;

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        var taskId = request.TaskId ?? "global";

        // 嵌入查詢文字（帶快取）
        float[]? queryVector;
        if (_ragPipeline is { CacheEnabled: true })
            queryVector = await _ragPipeline.GetCachedEmbeddingAsync(query, _embeddingService);
        else
            queryVector = await _embeddingService.EmbedAsync(query);

        if (queryVector == null)
            return ExecutionResult.Fail(request.RequestId, "Failed to embed query.");

        // 載入所有向量（小資料量，in-memory cosine）
        var vectors = _db.GetAll<VectorEntry>()
            .Where(v => v.TaskId == taskId && v.Embedding.Length > 0)
            .ToList();

        // 計算相似度
        var scored = vectors.Select(v =>
        {
            var vec = EmbeddingService.BytesToVector(v.Embedding);
            var similarity = EmbeddingService.CosineSimilarity(queryVector, vec);
            return new { v.SourceKey, v.TextPreview, similarity };
        })
        .Where(x => x.similarity >= threshold)
        .OrderByDescending(x => x.similarity)
        .Take(limit)
        .ToList();

        // 附帶原始記憶內容
        var results = scored.Select(s =>
        {
            var entry = _db.GetAll<SharedContextEntry>()
                .Where(e => e.TaskId == taskId && e.Key == s.SourceKey)
                .OrderByDescending(e => e.Version)
                .FirstOrDefault();

            return new
            {
                key = s.SourceKey,
                content = entry?.ContentRef ?? s.TextPreview,
                similarity = MathF.Round(s.similarity, 4),
                version = entry?.Version ?? 0
            };
        }).ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new { query, results, total = results.Count, vector_count = vectors.Count }));
    }

    /// <summary>
    /// RAG 檢索（混合 BM25 + 向量，含 reciprocal rank fusion）
    ///
    /// 完整管線：
    /// 1. Query Rewriting — LLM 改寫口語查詢為精準搜尋詞
    /// 2. Tag Pre-filter — 按標籤縮小搜尋範圍
    /// 3. BM25 + Vector 雙通道檢索
    /// 4. Reciprocal Rank Fusion 混合排序
    /// 5. Re-ranking — LLM 對 top-N 重新評分
    /// </summary>
    private async Task<ExecutionResult> ExecuteRagRetrieveAsync(ApprovedRequest request)
    {
        if (_db == null) return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var mode = TryGetString(args, "mode") ?? "hybrid";
        var limitStr = TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 20) : 5;
        var thresholdStr = TryGetString(args, "threshold");
        var threshold = float.TryParse(thresholdStr, out var t) ? t : 0.2f;
        var includeConvlog = args.TryGetProperty("include_convlog", out var icl) &&
                             icl.ValueKind == JsonValueKind.True;
        var rewrite = !args.TryGetProperty("rewrite", out var rw) || rw.ValueKind != JsonValueKind.False; // 預設開啟
        var rerank = !args.TryGetProperty("rerank", out var rr) || rr.ValueKind != JsonValueKind.False; // 預設開啟

        // 新增：標籤過濾
        List<string>? filterTags = null;
        if (args.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Array)
        {
            filterTags = new List<string>();
            foreach (var tagItem in tagsEl.EnumerateArray())
            {
                var tagStr = tagItem.GetString();
                if (!string.IsNullOrWhiteSpace(tagStr)) filterTags.Add(tagStr);
            }
            if (filterTags.Count == 0) filterTags = null;
        }

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        var taskId = request.TaskId ?? "global";
        var k = 60; // RRF constant

        // ── Step 1: Query Rewriting ──
        string searchQuery = query;
        string[]? expandedTerms = null;
        if (rewrite && _ragPipeline is { QueryRewriteEnabled: true })
        {
            var rewriteResult = await _ragPipeline.RewriteQueryAsync(query);
            searchQuery = rewriteResult.RewrittenQuery;
            expandedTerms = rewriteResult.ExpandedTerms;
        }

        // ── Step 2: Tag Pre-filter（取得符合標籤的 source_key 集合） ──
        HashSet<string>? allowedKeys = null;
        if (filterTags != null)
        {
            allowedKeys = new HashSet<string>();
            var allEntries = _db.GetAll<SharedContextEntry>()
                .Where(e => e.TaskId == taskId);

            foreach (var entry in allEntries)
            {
                try
                {
                    var entryTags = JsonSerializer.Deserialize<string[]>(entry.Tags ?? "[]");
                    if (entryTags != null && entryTags.Any(et => filterTags.Any(ft =>
                        et.Contains(ft, StringComparison.OrdinalIgnoreCase))))
                    {
                        allowedKeys.Add(entry.Key);
                    }
                }
                catch { }
            }
        }

        // ── Step 3: BM25 分支 ──
        var bm25Scores = new Dictionary<string, float>();
        var bm25Contents = new Dictionary<string, string>();

        if (mode is "fulltext" or "hybrid")
        {
            try
            {
                // 用改寫後的多個搜尋詞分別查詢，合併結果
                var searchTerms = expandedTerms ?? new[] { query };
                foreach (var term in searchTerms.Take(5))
                {
                    var ftsResults = _db.Query<FtsResult>(
                        "SELECT source_key, content, rank FROM memory_fts WHERE memory_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                        new { q = PrepareFts5Query(term), taskId, fetchLimit = limit * 3 });

                    int rank = 1;
                    foreach (var r in ftsResults)
                    {
                        if (r.SourceKey == null) continue;
                        if (allowedKeys != null && !allowedKeys.Contains(r.SourceKey)) continue;

                        var score = 1.0f / (k + rank);
                        // 合併分數（多個搜尋詞命中同一文件，分數累加）
                        bm25Scores[r.SourceKey] = bm25Scores.GetValueOrDefault(r.SourceKey, 0f) + score;
                        if (!bm25Contents.ContainsKey(r.SourceKey))
                            bm25Contents[r.SourceKey] = r.Content ?? "";
                        rank++;
                    }
                }

                // 也搜尋對話日誌
                if (includeConvlog)
                {
                    var convResults = _db.Query<ConvlogFtsResult>(
                        "SELECT user_id, role, content, rank FROM convlog_fts WHERE convlog_fts MATCH @q AND task_id = @taskId ORDER BY rank LIMIT @fetchLimit",
                        new { q = PrepareFts5Query(query), taskId, fetchLimit = limit * 2 });

                    int convRank = 1;
                    foreach (var r in convResults)
                    {
                        var convKey = $"__convlog__{r.UserId}_{convRank}";
                        bm25Scores[convKey] = 1.0f / (k + convRank) * 0.5f;
                        bm25Contents[convKey] = $"[{r.Role}] {r.Content}";
                        convRank++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG BM25 branch failed");
            }
        }

        // ── Step 4: 向量分支（帶快取） ──
        var vecScores = new Dictionary<string, float>();

        if ((mode is "semantic" or "hybrid") && _embeddingService is { IsEnabled: true })
        {
            try
            {
                // 使用快取取得嵌入
                float[]? queryVector;
                if (_ragPipeline is { CacheEnabled: true })
                    queryVector = await _ragPipeline.GetCachedEmbeddingAsync(query, _embeddingService);
                else
                    queryVector = await _embeddingService.EmbedAsync(query);

                if (queryVector != null)
                {
                    var vectors = _db.GetAll<VectorEntry>()
                        .Where(v => v.TaskId == taskId && v.Embedding.Length > 0)
                        .ToList();

                    // 標籤過濾
                    if (allowedKeys != null)
                        vectors = vectors.Where(v => allowedKeys.Contains(v.SourceKey)).ToList();

                    var scored = vectors.Select(v =>
                    {
                        var vec = EmbeddingService.BytesToVector(v.Embedding);
                        var sim = EmbeddingService.CosineSimilarity(queryVector, vec);
                        return new { v.SourceKey, sim };
                    })
                    .Where(x => x.sim >= threshold)
                    .OrderByDescending(x => x.sim)
                    .Take(limit * 3)
                    .ToList();

                    int rank = 1;
                    foreach (var s in scored)
                    {
                        vecScores[s.SourceKey] = 1.0f / (k + rank);
                        rank++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG vector branch failed");
            }
        }

        // ── Step 5: Reciprocal Rank Fusion ──
        var allKeys = bm25Scores.Keys.Union(vecScores.Keys).Distinct();
        var fused = allKeys.Select(key =>
        {
            var bm25 = bm25Scores.GetValueOrDefault(key, 0f);
            var vec = vecScores.GetValueOrDefault(key, 0f);
            return new { key, score = bm25 + vec, bm25, vec };
        })
        .OrderByDescending(x => x.score)
        .Take(limit * 2) // 取多一些給 re-rank
        .ToList();

        // 組裝候選結果（附帶原始內容）
        var candidates = fused.Select(f =>
        {
            string content;
            string source;

            if (f.key.StartsWith("__convlog__"))
            {
                content = bm25Contents.GetValueOrDefault(f.key, "");
                source = "convlog";
            }
            else
            {
                if (bm25Contents.TryGetValue(f.key, out var cached))
                {
                    content = cached;
                }
                else
                {
                    var entry = _db.GetAll<SharedContextEntry>()
                        .Where(e => e.TaskId == taskId && e.Key == f.key)
                        .OrderByDescending(e => e.Version)
                        .FirstOrDefault();
                    content = entry?.ContentRef ?? "";
                }
                source = "memory";
            }

            return new RerankItem
            {
                Key = f.key,
                Content = content,
                Source = source,
                OriginalScore = f.score,
                Bm25Score = f.bm25,
                VectorScore = f.vec
            };
        }).ToList();

        // ── Step 6: Re-ranking ──
        List<RerankItem> finalResults;
        bool reranked = false;

        if (rerank && _ragPipeline is { RerankEnabled: true } && candidates.Count > 1)
        {
            finalResults = await _ragPipeline.RerankAsync(query, candidates, limit);
            reranked = true;
        }
        else
        {
            finalResults = candidates.Take(limit).ToList();
        }

        // 組裝最終結果
        var results = finalResults.Select(r => new
        {
            key = r.Key,
            content = r.Content.Length > 1000 ? r.Content[..1000] + "..." : r.Content,
            score = MathF.Round(reranked ? r.RerankScore : r.OriginalScore, 4),
            bm25_score = MathF.Round(r.Bm25Score, 4),
            vector_score = MathF.Round(r.VectorScore, 4),
            rerank_score = reranked ? MathF.Round(r.RerankScore, 4) : (float?)null,
            source = r.Source
        }).ToList();

        return ExecutionResult.Ok(request.RequestId,
            JsonSerializer.Serialize(new
            {
                query,
                mode,
                results,
                total = results.Count,
                bm25_candidates = bm25Scores.Count,
                vector_candidates = vecScores.Count,
                pipeline = new
                {
                    query_rewrite = expandedTerms != null,
                    expanded_terms = expandedTerms,
                    reranked,
                    tag_filter = filterTags
                }
            }));
    }

    // FTS5 結果 DTO
    private class FtsResult
    {
        [BaseOrm.Column("source_key")]
        public string? SourceKey { get; set; }
        [BaseOrm.Column("content")]
        public string? Content { get; set; }
        [BaseOrm.Column("rank")]
        public double Rank { get; set; }
    }

    private class ConvlogFtsResult
    {
        [BaseOrm.Column("user_id")]
        public string? UserId { get; set; }
        [BaseOrm.Column("role")]
        public string? Role { get; set; }
        [BaseOrm.Column("content")]
        public string? Content { get; set; }
        [BaseOrm.Column("rank")]
        public double Rank { get; set; }
    }

    // ═══════════════════════════════════════════
    // Web 路由
    // ═══════════════════════════════════════════

    private async Task<ExecutionResult> ExecuteWebSearchAsync(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var limitStr = TryGetString(args, "limit");
        var limit = int.TryParse(limitStr, out var l) ? Math.Min(l, 10) : 5;

        if (string.IsNullOrEmpty(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            // DuckDuckGo Lite HTML 搜尋（無 API key）
            var url = $"https://lite.duckduckgo.com/lite/?q={Uri.EscapeDataString(query)}";
            var html = await _httpClient.GetStringAsync(url);

            // 解析結果：提取連結和摘要
            var results = ParseDuckDuckGoLite(html, limit);

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Web search failed: {ex.Message}");
        }
    }

    private async Task<ExecutionResult> ExecuteWebSearchGoogleAsync(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var locale = TryGetString(args, "locale") ?? "zh-TW";
        var safeMode = TryGetString(args, "safe_mode") ?? "moderate";
        var limit = TryGetInt(args, "limit") ?? 5;
        limit = Math.Clamp(limit, 1, 10);

        if (string.IsNullOrWhiteSpace(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            var results = await SearchGoogleAsync(query, limit, locale, safeMode);
            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { engine = "google", query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Google search failed: {ex.Message}");
        }
    }

    private async Task<ExecutionResult> ExecuteWebSearchDuckDuckGoAsync(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var locale = TryGetString(args, "locale") ?? "zh-TW";
        var limit = TryGetInt(args, "limit") ?? 5;
        limit = Math.Clamp(limit, 1, 10);

        if (string.IsNullOrWhiteSpace(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            var results = await SearchDuckDuckGoAsync(query, limit, locale);
            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { engine = "duckduckgo", query, results, total = results.Count }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"DuckDuckGo search failed: {ex.Message}");
        }
    }

    private async Task<ExecutionResult> ExecuteWebFetchAsync(ApprovedRequest request)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var urlStr = TryGetString(args, "url") ?? "";
        var maxLenStr = TryGetString(args, "max_length");
        var maxLen = int.TryParse(maxLenStr, out var ml) ? ml : 50000;

        if (string.IsNullOrEmpty(urlStr))
            return ExecutionResult.Fail(request.RequestId, "url is required.");

        if (!Uri.TryCreate(urlStr, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return ExecutionResult.Fail(request.RequestId, "Invalid URL. Only http/https allowed.");

        try
        {
            var html = await _httpClient.GetStringAsync(uri);

            // HTML → 純文字
            var text = HtmlToText(html);
            if (text.Length > maxLen)
                text = text[..maxLen] + "\n... [truncated]";

            // 提取 title
            var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success ? System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value).Trim() : "";

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new { url = urlStr, title, content = text, length = text.Length }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"Web fetch failed: {ex.Message}");
        }
    }

    private Task<ExecutionResult> ExecuteTravelRailSearchAsync(ApprovedRequest request)
        => ExecuteTravelSearchAsync(
            request,
            mode: "rail",
            sourceLabel: "DuckDuckGo / railway.gov.tw / thsrc.com.tw",
            queryDecorator: query => $"{query} site:railway.gov.tw OR site:thsrc.com.tw 火車 台鐵 高鐵 時刻表 班次");

    private Task<ExecutionResult> ExecuteTravelBusSearchAsync(ApprovedRequest request)
        => ExecuteTravelSearchAsync(
            request,
            mode: "bus",
            sourceLabel: "DuckDuckGo / public transport web",
            queryDecorator: query => $"{query} 公車 OR 客運 時刻表 班次");

    private Task<ExecutionResult> ExecuteTravelFlightSearchAsync(ApprovedRequest request)
        => ExecuteTravelSearchAsync(
            request,
            mode: "flight",
            sourceLabel: "DuckDuckGo / public travel web",
            queryDecorator: query => $"{query} 航班 班次 時刻表");

    private async Task<ExecutionResult> ExecuteTravelSearchAsync(
        ApprovedRequest request,
        string mode,
        string sourceLabel,
        Func<string, string> queryDecorator)
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);
        var query = TryGetString(args, "query") ?? "";
        var locale = TryGetString(args, "locale") ?? "zh-TW";
        var limit = TryGetInt(args, "limit") ?? 5;
        limit = Math.Clamp(limit, 1, 5);

        if (string.IsNullOrWhiteSpace(query))
            return ExecutionResult.Fail(request.RequestId, "query is required.");

        try
        {
            var searchResults = await SearchDuckDuckGoAsync(queryDecorator(query), limit, locale);
            var normalizedResults = new List<object>();
            foreach (var searchResult in searchResults.Take(limit))
            {
                var resultJson = JsonSerializer.Serialize(searchResult);
                using var resultDoc = JsonDocument.Parse(resultJson);
                var root = resultDoc.RootElement;
                var url = TryGetString(root, "url") ?? string.Empty;
                var snippet = TryGetString(root, "snippet") ?? string.Empty;
                var title = TryGetString(root, "title") ?? string.Empty;
                var rank = TryGetInt(root, "rank") ?? (normalizedResults.Count + 1);
                var timeCandidates = new List<string>();

                if (!string.IsNullOrWhiteSpace(snippet))
                    timeCandidates.AddRange(ExtractTimeCandidates(snippet));

                if (timeCandidates.Count < 4 && !string.IsNullOrWhiteSpace(url))
                {
                    try
                    {
                        var html = await _httpClient.GetStringAsync(url);
                        var content = HtmlToText(html);
                        timeCandidates.AddRange(ExtractTimeCandidates(content));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Skipping follow fetch for travel result {Url}", url);
                    }
                }

                normalizedResults.Add(new
                {
                    rank,
                    title,
                    url,
                    snippet,
                    time_candidates = timeCandidates
                        .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(6)
                        .ToArray()
                });
            }

            return ExecutionResult.Ok(request.RequestId,
                JsonSerializer.Serialize(new
                {
                    mode,
                    query,
                    retrieved_at = DateTimeOffset.UtcNow.ToString("O"),
                    results = normalizedResults,
                    sources_used = new[] { sourceLabel }
                }));
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(request.RequestId, $"{mode} travel search failed: {ex.Message}");
        }
    }

    private static async Task<List<object>> SearchDuckDuckGoAsync(string query, int limit, string locale)
    {
        var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(query)}&kl={Uri.EscapeDataString(locale)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", $"{locale},en;q=0.8");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        return ParseDuckDuckGoLite(html, limit);
    }

    private static async Task<List<object>> SearchGoogleAsync(string query, int limit, string locale, string safeMode)
    {
        var safe = safeMode switch
        {
            "strict" => "active",
            "off" => "off",
            _ => "active"
        };

        var url = $"https://www.google.com/search?gbv=1&q={Uri.EscapeDataString(query)}&num={limit}&hl={Uri.EscapeDataString(locale)}&safe={safe}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html");
        request.Headers.Add("Accept-Language", $"{locale},en;q=0.8");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        return ParseGoogleResults(html, limit);
    }

    private static List<object> ParseDuckDuckGoLite(string html, int limit)
    {
        var results = new List<object>();
        // 匹配 DuckDuckGo Lite 結果格式：<a> 標籤 + 後續摘要文字
        var linkPattern = new Regex(
            @"<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        // 備用：簡單提取所有外部連結
        if (!linkPattern.IsMatch(html))
        {
            linkPattern = new Regex(
                @"<a[^>]+href=""(https?://[^""]+)""[^>]*class=""result-link""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        var snippetPattern = new Regex(
            @"<a[^>]+class=""result__snippet""[^>]*>(.*?)</a>|<td[^>]*class=""result-snippet""[^>]*>(.*?)</td>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var links = linkPattern.Matches(html);
        var snippets = snippetPattern.Matches(html);

        for (int i = 0; i < Math.Min(links.Count, limit); i++)
        {
            var link = links[i];
            var href = System.Net.WebUtility.HtmlDecode(link.Groups[1].Value);
            if (href.Contains("uddg=", StringComparison.OrdinalIgnoreCase))
            {
                var uddg = Regex.Match(href, @"uddg=([^&]+)", RegexOptions.IgnoreCase);
                if (uddg.Success)
                    href = Uri.UnescapeDataString(uddg.Groups[1].Value);
            }

            var snippet = i < snippets.Count
                ? HtmlToText(snippets[i].Groups[1].Success ? snippets[i].Groups[1].Value : snippets[i].Groups[2].Value).Trim()
                : "";

            results.Add(new
            {
                rank = i + 1,
                url = href,
                title = HtmlToText(link.Groups[2].Value).Trim(),
                snippet
            });
        }

        return results;
    }

    private static List<object> ParseGoogleResults(string html, int limit)
    {
        var results = new List<object>();
        var linkPattern = new Regex(
            @"<a[^>]+href=""/url\?q=([^&""]+)[^""]*""[^>]*>(.*?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in linkPattern.Matches(html))
        {
            if (results.Count >= limit)
                break;

            var href = Uri.UnescapeDataString(match.Groups[1].Value);
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
                continue;

            var anchorHtml = match.Groups[2].Value;
            var titleMatch = Regex.Match(anchorHtml, @"<h3[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var title = titleMatch.Success
                ? HtmlToText(titleMatch.Groups[1].Value).Trim()
                : HtmlToText(anchorHtml).Trim();

            if (string.IsNullOrWhiteSpace(title))
                title = href;

            results.Add(new
            {
                rank = results.Count + 1,
                title,
                url = href,
                snippet = string.Empty
            });
        }

        return results;
    }

    private static IEnumerable<string> ExtractTimeCandidates(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (Match match in Regex.Matches(text, @"(?<!\d)([01]?\d|2[0-3]):[0-5]\d(?!\d)"))
            yield return match.Value;
    }

    private static string HtmlToText(string html)
    {
        // 移除 script/style
        var text = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // <br> / <p> / <div> → 換行
        text = Regex.Replace(text, @"<(br|/p|/div|/tr|/li)[^>]*>", "\n", RegexOptions.IgnoreCase);
        // 移除所有 HTML 標籤
        text = Regex.Replace(text, @"<[^>]+>", "");
        // 解碼 HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);
        // 壓縮空白
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    // ── RAG Import（CSV/JSON） ──

    private async Task<ExecutionResult> ExecuteRagImportAsync(ApprovedRequest request)
    {
        if (_db == null)
            return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);

        var format = TryGetString(args, "format") ?? "json";
        var tag = TryGetString(args, "tag") ?? "";
        var data = TryGetString(args, "data") ?? "";
        var taskId = TryGetString(args, "task_id") ?? request.TaskId ?? "global";

        if (string.IsNullOrWhiteSpace(tag))
            return ExecutionResult.Fail(request.RequestId, "tag is required.");
        if (string.IsNullOrWhiteSpace(data))
            return ExecutionResult.Fail(request.RequestId, "data is required.");

        Scripts.RagIngestService.IngestResult result;
        switch (format.ToLowerInvariant())
        {
            case "json":
                result = await Scripts.RagIngestService.ImportJsonAsync(
                    data, tag, taskId, _db, _embeddingService,
                    _logger as ILogger);
                break;
            case "csv":
                result = await Scripts.RagIngestService.ImportCsvAsync(
                    data, tag, taskId, _db, _embeddingService,
                    _logger as ILogger);
                break;
            default:
                return ExecutionResult.Fail(request.RequestId, $"Unsupported format: {format}. Use 'json' or 'csv'.");
        }

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
        {
            inserted = result.Inserted,
            skipped = result.Skipped,
            embedded = result.Embedded,
            errors = result.Errors
        }));
    }

    // ── RAG Import from Web ──

    private async Task<ExecutionResult> ExecuteRagImportWebAsync(ApprovedRequest request)
    {
        if (_db == null)
            return ExecutionResult.Fail(request.RequestId, "Database not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        var args = GetArgsElement(doc.RootElement);

        var query = TryGetString(args, "query") ?? "";
        var tag = TryGetString(args, "tag") ?? query;
        var taskId = TryGetString(args, "task_id") ?? request.TaskId ?? "global";
        var maxPages = 5;
        var chunkSize = 1000;
        var chunkOverlap = 100;

        if (args.TryGetProperty("max_pages", out var mp) && mp.ValueKind == JsonValueKind.Number)
            maxPages = mp.GetInt32();
        if (args.TryGetProperty("chunk_size", out var cs) && cs.ValueKind == JsonValueKind.Number)
            chunkSize = cs.GetInt32();
        if (args.TryGetProperty("chunk_overlap", out var co) && co.ValueKind == JsonValueKind.Number)
            chunkOverlap = co.GetInt32();

        List<string>? urls = null;
        if (args.TryGetProperty("urls", out var urlsEl) && urlsEl.ValueKind == JsonValueKind.Array)
        {
            urls = new List<string>();
            foreach (var u in urlsEl.EnumerateArray())
            {
                var s = u.GetString();
                if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
            }
        }

        if (string.IsNullOrWhiteSpace(query) && (urls == null || urls.Count == 0))
            return ExecutionResult.Fail(request.RequestId, "query or urls is required.");

        var webRequest = new Scripts.RagIngestService.WebSearchRequest
        {
            Query = query,
            Tag = tag,
            MaxPages = maxPages,
            Urls = urls,
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap
        };

        var result = await Scripts.RagIngestService.ImportFromWebAsync(
            webRequest, taskId, _db, _embeddingService,
            _logger as ILogger);

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(new
        {
            pages_fetched = result.PagesFetched,
            inserted = result.Inserted,
            skipped = result.Skipped,
            embedded = result.Embedded,
            errors = result.Errors
        }));
    }

    /// <summary>此分派器是否支援指定路由（供 FallbackDispatcher 判斷降級）</summary>
    public bool CanHandle(string route) => route switch
    {
        "read_file" or "list_directory" or "search_files" or "search_content"
            or "list_agents" or "create_agent" or "stop_agent"
            or "conv_log_append" or "conv_log_read"
            or "memory_store" or "memory_retrieve" or "memory_delete"
            or "memory_semantic_search" or "memory_fulltext_search" or "rag_retrieve"
            or "rag_import" or "rag_import_web"
            or "web_search" or "web_search_google" or "web_search_duckduckgo" or "web_fetch"
            or "deploy_azure_vm_iis" => true,
        _ => false
    };

    private async Task<ExecutionResult> ExecuteAzureIisDeploymentAsync(ApprovedRequest request)
    {
        if (_azureIisDeploymentExecutionService == null)
            return ExecutionResult.Fail(request.RequestId, "AzureIisDeploymentExecutionService not available.");

        using var doc = JsonDocument.Parse(request.Payload);
        if (!IsPayloadRouteValid(doc.RootElement, request.Route))
            return ExecutionResult.Fail(request.RequestId, "Payload route does not match approved route.");

        var args = GetArgsElement(doc.RootElement);
        var targetId = TryGetString(args, "target_id") ?? "";
        var projectPath = TryGetString(args, "project_path") ?? "";
        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(projectPath))
            return ExecutionResult.Fail(request.RequestId, "target_id and project_path are required.");

        var input = new AzureIisDeploymentBuildInput
        {
            RequestId = request.RequestId,
            CapabilityId = request.CapabilityId,
            Route = request.Route,
            PrincipalId = request.PrincipalId,
            TaskId = request.TaskId,
            SessionId = request.SessionId,
            TargetId = targetId,
            ProjectPath = projectPath,
            Configuration = TryGetString(args, "configuration") ?? "Release",
            RuntimeIdentifier = TryGetString(args, "runtime_identifier"),
            SelfContained = args.TryGetProperty("self_contained", out var selfContainedProp) && selfContainedProp.ValueKind == JsonValueKind.True,
            CleanupTarget = !args.TryGetProperty("cleanup_target", out var cleanupProp) || cleanupProp.ValueKind == JsonValueKind.True,
            RestartSite = !args.TryGetProperty("restart_site", out var restartProp) || restartProp.ValueKind == JsonValueKind.True,
            ScopeJson = request.Scope
        };

        var dryRun = args.TryGetProperty("dry_run", out var dryRunProp) && dryRunProp.ValueKind == JsonValueKind.True;
        var result = await _azureIisDeploymentExecutionService.ExecuteAsync("deploy.azure-vm-iis", input, dryRun);
        if (!result.Success || result.Result == null)
            return ExecutionResult.Fail(request.RequestId, result.Result?.Message ?? result.Error ?? "deployment_failed");

        return ExecutionResult.Ok(request.RequestId, JsonSerializer.Serialize(result.Result));
    }

    private static JsonElement GetArgsElement(JsonElement root)
    {
        if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
            return args;
        if (root.TryGetProperty("tool_args", out var legacyArgs) && legacyArgs.ValueKind == JsonValueKind.Object)
            return legacyArgs;
        return root;
    }

    private static bool IsPayloadRouteValid(JsonElement root, string approvedRoute)
    {
        var payloadRoute = TryGetString(root, "route", "tool_name");
        return string.IsNullOrWhiteSpace(payloadRoute) ||
               payloadRoute.Equals(approvedRoute, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsedNumber))
                return parsedNumber;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsedString))
                return parsedString;
        }

        return null;
    }

    /// <summary>
    /// 將中文文字轉為 FTS5 查詢語法
    /// unicode61 tokenizer 將 CJK 字元逐字切割，所以 "退貨" → "退 貨"
    /// 用空格隔開 → FTS5 預設 AND 語意
    /// </summary>
    public static string PrepareFts5Query(string query)
    {
        var sb = new System.Text.StringBuilder(query.Length * 2);
        bool prevIsCjk = false;

        foreach (var ch in query)
        {
            bool isCjk = char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.OtherLetter;

            if (isCjk)
            {
                if (sb.Length > 0 && !prevIsCjk)
                    sb.Append(' ');
                else if (prevIsCjk)
                    sb.Append(' ');
                sb.Append(ch);
            }
            else if (ch == ' ' || ch == '\t')
            {
                if (sb.Length > 0) sb.Append(' ');
            }
            else
            {
                if (sb.Length > 0 && prevIsCjk)
                    sb.Append(' ');
                sb.Append(ch);
            }

            prevIsCjk = isCjk;
        }

        var result = sb.ToString().Trim();
        // 移除 FTS5 特殊運算符（防止語法錯誤）
        result = result.Replace("\"", "");
        result = result.Replace("*", "");
        result = result.Replace("(", "");
        result = result.Replace(")", "");
        result = result.Replace("^", "");
        // 將 - 替換為空格（FTS5 中 - 是 NOT 運算符）
        result = result.Replace("-", " ");
        // 壓縮多餘空格
        while (result.Contains("  "))
            result = result.Replace("  ", " ");
        result = result.Trim();
        return string.IsNullOrEmpty(result) ? query : result;
    }

    /// <summary>解析路徑，確保在沙箱範圍內</summary>
    private string? ResolveSandboxedPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(_sandboxRoot, path));

            // 確保結果路徑在沙箱根目錄下
            if (!fullPath.StartsWith(_sandboxRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            return fullPath;
        }
        catch
        {
            return null;
        }
    }
}
