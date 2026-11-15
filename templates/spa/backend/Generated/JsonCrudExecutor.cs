using System.Data;
using System.Data.Common;
using System.Text.Json;

namespace SpaApi.Generated;

public sealed record JsonCrudQueryResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Records,
    IReadOnlyList<string> IgnoredUserFilters);

/// <summary>
/// Runs a reviewed resource mapping against an injected database connection. The template never
/// registers this executor or exposes a route automatically. All grants must be built server-side.
/// </summary>
public sealed class JsonCrudExecutor(JsonCrudKernel kernel, DbConnection connection)
{
    private bool _schemaValidated;

    public async Task ValidateSchemaAsync(CancellationToken cancellationToken = default)
    {
        await kernel.ValidateDatabaseSchemaAsync(connection, cancellationToken);
        _schemaValidated = true;
    }

    public async Task<JsonCrudQueryResult> QueryAsync(
        JsonCrudCaller caller, JsonElement request, CancellationToken cancellationToken = default)
    {
        RequireValidated();
        var parsed = kernel.ParseUserFilters(request);
        var sql = kernel.CompileQuery(caller, parsed.Predicate);
        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<IReadOnlyDictionary<string, object?>>();
        var fields = kernel.Resource.Fields.Where(field => field.Readable)
            .ToDictionary(field => field.Code, StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var code = reader.GetName(index);
                if (!fields.TryGetValue(code, out var field))
                    throw new InvalidOperationException("Query returned a field outside the reviewed mapping.");
                record.Add(code, reader.IsDBNull(index) ? null : NormalizeValue(reader.GetValue(index), field.Type));
            }
            records.Add(record);
        }
        return new JsonCrudQueryResult(records, parsed.IgnoredFields);
    }

    public async Task<int> CreateAsync(
        JsonCrudCaller caller, JsonElement fields, JsonElement key, CancellationToken cancellationToken = default)
    {
        RequireValidated();
        var values = kernel.ParseFields(fields, JsonCrudAction.Create);
        var sql = kernel.CompileCreate(caller, values, kernel.ParseKey(key));
        await using var command = CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> UpdateAsync(
        JsonCrudCaller caller, JsonElement key, JsonElement fields, CancellationToken cancellationToken = default)
    {
        RequireValidated();
        var values = kernel.ParseFields(fields, JsonCrudAction.Update);
        var sql = kernel.CompileUpdate(caller, kernel.ParseKey(key), values);
        await using var command = CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> DeleteAsync(
        JsonCrudCaller caller, JsonElement key, CancellationToken cancellationToken = default)
    {
        RequireValidated();
        var sql = kernel.CompileDelete(caller, kernel.ParseKey(key));
        await using var command = CreateCommand(sql);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(JsonCrudSql sql)
    {
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("The validated database connection is closed.");
        var command = connection.CreateCommand();
        command.CommandText = sql.Text;
        foreach (var pair in sql.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + pair.Key;
            parameter.Value = pair.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static object NormalizeValue(object value, JsonCrudType type) => type switch
    {
        JsonCrudType.Text => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        JsonCrudType.Integer => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture),
        JsonCrudType.Number => Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture),
        JsonCrudType.Boolean => Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("Unsupported mapped type.")
    };

    private void RequireValidated()
    {
        if (!_schemaValidated)
            throw new InvalidOperationException("Validate the reviewed field mapping against the database before execution.");
    }
}
