using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpaApi.Generated;

// This is an opt-in kernel. No endpoint or resource is registered by the SPA template.
// A project must supply reviewed mappings and server-created grants before using it.
public enum JsonCrudType { Text, Integer, Number, Boolean }
public enum JsonCrudAction { Create, Read, Update, Delete }
public enum JsonCrudJoin { All, Any }
public enum JsonCrudOperator { Equal, In, Contains }

public sealed record JsonCrudField(
    string Code,
    string Column,
    JsonCrudType Type,
    bool Readable,
    bool CreateWritable,
    bool UpdateWritable,
    bool Queryable,
    bool RequiredOnCreate = false,
    bool Nullable = false);

public sealed record JsonCrudResource(
    string Code,
    string Table,
    string KeyCode,
    IReadOnlyList<JsonCrudField> Fields,
    bool IgnoreInvalidUserFilters = false);

public abstract record JsonCrudPredicate;
public sealed record JsonCrudClause(string FieldCode, JsonCrudOperator Operator, IReadOnlyList<object?> Values)
    : JsonCrudPredicate;
public sealed record JsonCrudGroup(JsonCrudJoin Join, IReadOnlyList<JsonCrudPredicate> Children)
    : JsonCrudPredicate;
public sealed record JsonCrudGrant(string Role, JsonCrudAction Action, JsonCrudPredicate Scope);
public sealed record JsonCrudCaller(IReadOnlySet<string> Roles);

public sealed record JsonCrudFilterResult(JsonCrudPredicate? Predicate, IReadOnlyList<string> IgnoredFields);
public sealed record JsonCrudSql(string Text, IReadOnlyDictionary<string, object?> Parameters);

public sealed class JsonCrudKernel
{
    private static readonly Regex IdentifierPattern = new("^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled);
    private static readonly Regex CodePattern = new("^[A-Za-z][A-Za-z0-9_]{0,79}$", RegexOptions.Compiled);
    private readonly JsonCrudResource _resource;
    private readonly IReadOnlyList<JsonCrudGrant> _grants;
    private readonly Dictionary<string, JsonCrudField> _fields;

    public JsonCrudKernel(JsonCrudResource resource, IReadOnlyList<JsonCrudGrant> grants)
    {
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        if (!CodePattern.IsMatch(resource.Code) || !IdentifierPattern.IsMatch(resource.Table))
            throw new ArgumentException("Invalid resource code or table name.", nameof(resource));
        if (resource.Fields.Count == 0 || resource.Fields.Count > 128)
            throw new ArgumentException("A resource needs 1–128 mapped fields.", nameof(resource));
        _fields = new Dictionary<string, JsonCrudField>(StringComparer.Ordinal);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in resource.Fields)
        {
            if (!CodePattern.IsMatch(field.Code) || !IdentifierPattern.IsMatch(field.Column) ||
                !_fields.TryAdd(field.Code, field) || !columns.Add(field.Column))
                throw new ArgumentException("Invalid or duplicate field mapping.", nameof(resource));
        }
        if (!_fields.TryGetValue(resource.KeyCode, out var key) || key.Type is not (JsonCrudType.Text or JsonCrudType.Integer))
            throw new ArgumentException("The unique key must map to a text or integer field.", nameof(resource));
        if (key.CreateWritable || key.UpdateWritable)
            throw new ArgumentException("The unique key is assigned by the trusted backend and cannot be changed by a request.", nameof(resource));
        foreach (var grant in grants)
        {
            if (!CodePattern.IsMatch(grant.Role)) throw new ArgumentException("Invalid role code.", nameof(grants));
            ValidateScope(grant.Scope, 0);
        }
    }

    public JsonCrudResource Resource => _resource;

    public object ParseKey(JsonElement value)
        => ReadValue(value, _fields[_resource.KeyCode])
           ?? throw new ArgumentException("The unique key cannot be null.", nameof(value));

    // Reflection is a deployment/startup check, not the authority for write permission.
    public async Task ValidateDatabaseSchemaAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        await EnsureOpenAsync(connection, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {Quote(_resource.Table)} WHERE 1 = 0";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = reader.GetColumnSchema().ToDictionary(
            column => column.ColumnName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var field in _resource.Fields)
        {
            if (!columns.TryGetValue(field.Column, out var column))
                throw new InvalidOperationException($"Mapped column missing: {field.Code}.");
            if (!MatchesDatabaseType(field.Type, column.DataTypeName ?? string.Empty))
                throw new InvalidOperationException($"Mapped column type differs: {field.Code}.");
        }
    }

    public JsonCrudPredicate AuthorizedScope(JsonCrudCaller caller, JsonCrudAction action)
    {
        var scopes = _grants
            .Where(grant => grant.Action == action && caller.Roles.Contains(grant.Role))
            .Select(grant => grant.Scope)
            .ToArray();
        if (scopes.Length == 0) throw new UnauthorizedAccessException("No matching grant.");
        return scopes.Length == 1 ? scopes[0] : new JsonCrudGroup(JsonCrudJoin.Any, scopes);
    }

    public IReadOnlyDictionary<string, object?> ParseFields(JsonElement fields, JsonCrudAction action)
    {
        if (action is not (JsonCrudAction.Create or JsonCrudAction.Update) || fields.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("The operation requires a JSON fields object.", nameof(fields));
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in fields.EnumerateObject())
        {
            if (!_fields.TryGetValue(property.Name, out var field) ||
                !(action == JsonCrudAction.Create ? field.CreateWritable : field.UpdateWritable) ||
                !result.TryAdd(property.Name, ReadValue(property.Value, field)))
                throw new ArgumentException($"Field is unknown, repeated, or not writable: {property.Name}.", nameof(fields));
        }
        if (action == JsonCrudAction.Create)
        {
            foreach (var field in _resource.Fields.Where(field => field.RequiredOnCreate))
                if (!result.TryGetValue(field.Code, out var value) || value is null)
                    throw new ArgumentException($"Required field is missing: {field.Code}.", nameof(fields));
        }
        if (result.Count == 0) throw new ArgumentException("No writable fields supplied.", nameof(fields));
        return result;
    }

    // Only optional user filters may be ignored. The trusted authorization scope is built separately.
    public JsonCrudFilterResult ParseUserFilters(JsonElement request)
    {
        if (request.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Query must be a JSON object.", nameof(request));
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "join", "filters" };
        if (request.EnumerateObject().Any(property => !allowed.Contains(property.Name)) ||
            !request.TryGetProperty("filters", out var array) || array.ValueKind != JsonValueKind.Array ||
            array.GetArrayLength() > 32)
            throw new ArgumentException("Query shape is invalid.", nameof(request));
        var join = JsonCrudJoin.All;
        if (request.TryGetProperty("join", out var joinNode))
        {
            if (joinNode.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Query join must be all or any.", nameof(request));
            join = joinNode.GetString() switch
            {
                "all" => JsonCrudJoin.All,
                "any" => JsonCrudJoin.Any,
                _ => throw new ArgumentException("Query join must be all or any.", nameof(request))
            };
        }
        var clauses = new List<JsonCrudPredicate>();
        var ignored = new List<string>();
        foreach (var node in array.EnumerateArray())
        {
            var code = node.ValueKind == JsonValueKind.Object && node.TryGetProperty("field", out var idNode) &&
                       idNode.ValueKind == JsonValueKind.String ? idNode.GetString() ?? string.Empty : string.Empty;
            try { clauses.Add(ParseUserClause(node)); }
            catch (ArgumentException) when (_resource.IgnoreInvalidUserFilters)
            {
                ignored.Add(code);
            }
        }
        JsonCrudPredicate? predicate = clauses.Count switch
        {
            0 => null,
            1 => clauses[0],
            _ => new JsonCrudGroup(join, clauses)
        };
        return new JsonCrudFilterResult(predicate, ignored);
    }

    public JsonCrudSql CompileQuery(JsonCrudCaller caller, JsonCrudPredicate? userFilter)
    {
        var scope = AuthorizedScope(caller, JsonCrudAction.Read);
        var predicate = userFilter is null ? scope : new JsonCrudGroup(JsonCrudJoin.All, [scope, userFilter]);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var condition = CompilePredicate(predicate, parameters, false);
        var readable = _resource.Fields.Where(field => field.Readable).ToArray();
        if (readable.Length == 0) throw new InvalidOperationException("Resource has no readable fields.");
        var projection = string.Join(", ", readable.Select(field => $"{Quote(field.Column)} AS {Quote(field.Code)}"));
        var key = _fields[_resource.KeyCode];
        return new JsonCrudSql(
            $"SELECT {projection} FROM {Quote(_resource.Table)} WHERE {condition} ORDER BY {Quote(key.Column)}",
            parameters);
    }

    public JsonCrudSql CompileDelete(JsonCrudCaller caller, object keyValue)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["p0"] = keyValue };
        var scope = CompilePredicate(AuthorizedScope(caller, JsonCrudAction.Delete), parameters, true);
        var key = _fields[_resource.KeyCode];
        return new JsonCrudSql(
            $"DELETE FROM {Quote(_resource.Table)} WHERE {Quote(key.Column)} = @p0 AND ({scope})",
            parameters);
    }

    public JsonCrudSql CompileUpdate(JsonCrudCaller caller, object keyValue, IReadOnlyDictionary<string, object?> values)
    {
        if (values.Count == 0) throw new ArgumentException("No fields supplied.", nameof(values));
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) { ["p0"] = keyValue };
        var sets = new List<string>();
        foreach (var pair in values)
        {
            if (!_fields.TryGetValue(pair.Key, out var field) || !field.UpdateWritable)
                throw new ArgumentException($"Field is not updateable: {pair.Key}.", nameof(values));
            var name = $"p{parameters.Count}";
            parameters.Add(name, pair.Value);
            sets.Add($"{Quote(field.Column)} = @{name}");
        }
        var scope = CompilePredicate(AuthorizedScope(caller, JsonCrudAction.Update), parameters, true);
        var key = _fields[_resource.KeyCode];
        return new JsonCrudSql(
            $"UPDATE {Quote(_resource.Table)} SET {string.Join(", ", sets)} WHERE {Quote(key.Column)} = @p0 AND ({scope})",
            parameters);
    }

    public JsonCrudSql CompileCreate(JsonCrudCaller caller, IReadOnlyDictionary<string, object?> values, object keyValue)
    {
        var proposed = new Dictionary<string, object?>(values, StringComparer.Ordinal) { [_resource.KeyCode] = keyValue };
        if (!Evaluate(AuthorizedScope(caller, JsonCrudAction.Create), proposed))
            throw new UnauthorizedAccessException("Proposed record is outside the create scope.");
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        var columns = new List<string>();
        foreach (var pair in proposed)
        {
            if (!_fields.TryGetValue(pair.Key, out var field) ||
                (pair.Key != _resource.KeyCode && !field.CreateWritable))
                throw new ArgumentException($"Field is not creatable: {pair.Key}.", nameof(values));
            var name = $"p{parameters.Count}";
            parameters.Add(name, pair.Value);
            columns.Add(Quote(field.Column));
        }
        var placeholders = Enumerable.Range(0, parameters.Count).Select(index => $"@p{index}");
        return new JsonCrudSql(
            $"INSERT INTO {Quote(_resource.Table)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", placeholders)})",
            parameters);
    }

    private JsonCrudClause ParseUserClause(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Count() != 3 ||
            !node.TryGetProperty("field", out var id) || id.ValueKind != JsonValueKind.String ||
            !node.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.String ||
            !node.TryGetProperty("value", out var value) ||
            !_fields.TryGetValue(id.GetString() ?? string.Empty, out var field) || !field.Queryable)
            throw new ArgumentException("Filter shape or field is invalid.");
        var kind = op.GetString() switch
        {
            "eq" => JsonCrudOperator.Equal,
            "in" => JsonCrudOperator.In,
            "contains" when field.Type == JsonCrudType.Text => JsonCrudOperator.Contains,
            _ => throw new ArgumentException("Filter operator is invalid.")
        };
        var values = kind == JsonCrudOperator.In
            ? value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is > 0 and <= 32
                ? value.EnumerateArray().Select(item => ReadValue(item, field)).ToArray()
                : throw new ArgumentException("IN requires 1–32 values.")
            : [ReadValue(value, field)];
        return new JsonCrudClause(field.Code, kind, values);
    }

    private void ValidateScope(JsonCrudPredicate predicate, int depth)
    {
        if (depth > 8) throw new ArgumentException("Scope is too deep.");
        switch (predicate)
        {
            case JsonCrudClause clause when _fields.TryGetValue(clause.FieldCode, out var field) &&
                                            field.Queryable && !field.UpdateWritable &&
                                            clause.Operator is JsonCrudOperator.Equal or JsonCrudOperator.In &&
                                            clause.Values.Count is > 0 and <= 32:
                break;
            case JsonCrudGroup group when group.Children.Count is > 0 and <= 32:
                foreach (var child in group.Children) ValidateScope(child, depth + 1);
                break;
            default:
                throw new ArgumentException("Scope must use mapped, immutable, queryable fields and equality or set membership.");
        }
    }

    private string CompilePredicate(JsonCrudPredicate predicate, Dictionary<string, object?> parameters, bool trustedScope)
    {
        switch (predicate)
        {
            case JsonCrudGroup group:
                return "(" + string.Join(group.Join == JsonCrudJoin.All ? " AND " : " OR ",
                    group.Children.Select(child => CompilePredicate(child, parameters, trustedScope))) + ")";
            case JsonCrudClause clause:
            {
                if (!_fields.TryGetValue(clause.FieldCode, out var field) || !field.Queryable ||
                    clause.Values.Count == 0 || clause.Values.Count > 32 ||
                    (trustedScope && clause.Operator == JsonCrudOperator.Contains))
                    throw new ArgumentException("Predicate is invalid.");
                var placeholders = new List<string>();
                foreach (var value in clause.Values)
                {
                    var name = $"p{parameters.Count}";
                    parameters.Add(name, clause.Operator == JsonCrudOperator.Contains ? $"%{EscapeLike(value?.ToString() ?? string.Empty)}%" : value);
                    placeholders.Add($"@{name}");
                }
                var column = Quote(field.Column);
                return clause.Operator switch
                {
                    JsonCrudOperator.Equal when clause.Values[0] is null => $"{column} IS NULL",
                    JsonCrudOperator.Equal => $"{column} = {placeholders[0]}",
                    JsonCrudOperator.In => $"{column} IN ({string.Join(", ", placeholders)})",
                    JsonCrudOperator.Contains => $"{column} LIKE {placeholders[0]} ESCAPE '\\'",
                    _ => throw new ArgumentException("Predicate operator is invalid.")
                };
            }
            default:
                throw new ArgumentException("Predicate is invalid.");
        }
    }

    private bool Evaluate(JsonCrudPredicate predicate, IReadOnlyDictionary<string, object?> values) => predicate switch
    {
        JsonCrudGroup group when group.Join == JsonCrudJoin.All => group.Children.All(child => Evaluate(child, values)),
        JsonCrudGroup group => group.Children.Any(child => Evaluate(child, values)),
        JsonCrudClause clause when values.TryGetValue(clause.FieldCode, out var value) => clause.Operator switch
        {
            JsonCrudOperator.Equal => Equals(value, clause.Values[0]),
            JsonCrudOperator.In => clause.Values.Any(candidate => Equals(value, candidate)),
            _ => false
        },
        _ => false
    };

    private static object? ReadValue(JsonElement value, JsonCrudField field)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return field.Nullable ? null : throw new ArgumentException($"Null is not allowed for {field.Code}.");
        return field.Type switch
        {
            JsonCrudType.Text when value.ValueKind == JsonValueKind.String => value.GetString(),
            JsonCrudType.Integer when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) => number,
            JsonCrudType.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) => number,
            JsonCrudType.Boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            _ => throw new ArgumentException($"JSON value does not match {field.Code}.")
        };
    }

    private static bool MatchesDatabaseType(JsonCrudType type, string dataType)
    {
        var name = dataType.ToUpperInvariant();
        return type switch
        {
            JsonCrudType.Text => name.Contains("CHAR", StringComparison.Ordinal) || name.Contains("TEXT", StringComparison.Ordinal) || name.Contains("CLOB", StringComparison.Ordinal),
            JsonCrudType.Integer => name.Contains("INT", StringComparison.Ordinal),
            JsonCrudType.Number => name.Contains("REAL", StringComparison.Ordinal) || name.Contains("FLOAT", StringComparison.Ordinal) || name.Contains("DOUBLE", StringComparison.Ordinal) || name.Contains("DECIMAL", StringComparison.Ordinal) || name.Contains("NUMERIC", StringComparison.Ordinal),
            JsonCrudType.Boolean => name.Contains("BOOL", StringComparison.Ordinal) || name.Contains("INT", StringComparison.Ordinal),
            _ => false
        };
    }

    private static string Quote(string identifier) => $"\"{identifier}\"";
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static async Task EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
    }
}
