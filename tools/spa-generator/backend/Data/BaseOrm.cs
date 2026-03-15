using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BaseOrm;

public enum DbType
{
    SQLite,
    SqlServer,
    MySql,
    PostgreSql,
    Unknown
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class TableAttribute : Attribute
{
    public TableAttribute(string name) => Name = name;
    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class KeyAttribute : Attribute
{
    public bool AutoIncrement { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ColumnAttribute : Attribute
{
    public ColumnAttribute(string name) => Name = name;
    public string Name { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class IgnoreAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxLengthAttribute : Attribute
{
    public MaxLengthAttribute(int length) => Length = length;
    public int Length { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiredAttribute : Attribute
{
}

public class BaseDb : IDisposable, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly DbProviderFactory _factory;
    private readonly DbType _dbType;
    private readonly DbDialect _dialect;
    private DbConnection? _transactionConnection;
    private DbTransaction? _transaction;

    public BaseDb(string connectionString)
        : this(connectionString, GetSqliteFactory(), DbType.SQLite)
    {
    }

    public BaseDb(string connectionString, DbProviderFactory factory)
        : this(connectionString, factory, DetectDbType(factory))
    {
    }

    public BaseDb(string connectionString, DbProviderFactory factory, DbType dbType)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _dbType = dbType;
        _dialect = DbDialect.Create(dbType);
    }

    public DbType DatabaseType => _dbType;

    public static BaseDb UseSqlServer(string connectionString)
    {
        var factory = RequireProviderFactory("Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");
        return new BaseDb(connectionString, factory, DbType.SqlServer);
    }

    public static BaseDb UseMySql(string connectionString)
    {
        var factory = RequireProviderFactory(
            "MySqlConnector.MySqlConnectorFactory, MySqlConnector",
            "MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data");
        return new BaseDb(connectionString, factory, DbType.MySql);
    }

    public static BaseDb UsePostgreSql(string connectionString)
    {
        var factory = RequireProviderFactory("Npgsql.NpgsqlFactory, Npgsql");
        return new BaseDb(connectionString, factory, DbType.PostgreSql);
    }

    public static BaseDb UseSqlite(string connectionString)
    {
        return new BaseDb(connectionString, GetSqliteFactory(), DbType.SQLite);
    }

    private static DbProviderFactory GetSqliteFactory()
    {
        return RequireProviderFactory("Microsoft.Data.Sqlite.SqliteFactory, Microsoft.Data.Sqlite");
    }

    private static DbProviderFactory? GetProviderFactory(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type == null)
        {
            return null;
        }

        var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null) as DbProviderFactory;
    }

    private static DbProviderFactory RequireProviderFactory(params string[] typeNames)
    {
        foreach (var typeName in typeNames)
        {
            var factory = GetProviderFactory(typeName);
            if (factory != null)
            {
                return factory;
            }
        }

        throw new InvalidOperationException(
            $"Unable to resolve ADO.NET provider factory: {string.Join(" | ", typeNames)}");
    }

    private static DbType DetectDbType(DbProviderFactory factory)
    {
        var typeName = factory.GetType().FullName ?? string.Empty;
        if (typeName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return DbType.SQLite;
        }

        if (typeName.Contains("SqlClient", StringComparison.OrdinalIgnoreCase))
        {
            return DbType.SqlServer;
        }

        if (typeName.Contains("MySql", StringComparison.OrdinalIgnoreCase))
        {
            return DbType.MySql;
        }

        if (typeName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return DbType.PostgreSql;
        }

        return DbType.Unknown;
    }

    private DbConnection CreateDbConnection()
    {
        var connection = _factory.CreateConnection()
            ?? throw new InvalidOperationException("Provider factory returned null connection.");
        connection.ConnectionString = _connectionString;
        return connection;
    }

    private DbConnection CreateConnection()
    {
        var connection = CreateDbConnection();
        connection.Open();
        _dialect.ConfigureConnection(connection);
        return connection;
    }

    private async Task<DbConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateDbConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await _dialect.ConfigureConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static void ExecutePragma(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecutePragmaAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ApplySqlitePragmas(DbConnection connection)
    {
        ExecutePragma(connection, "PRAGMA busy_timeout=5000;");
        ExecutePragma(connection, "PRAGMA foreign_keys=ON;");

        if (!connection.ConnectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            ExecutePragma(connection, "PRAGMA journal_mode=WAL;");
        }
    }

    private static async Task ApplySqlitePragmasAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecutePragmaAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken).ConfigureAwait(false);
        await ExecutePragmaAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);

        if (!connection.ConnectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            await ExecutePragmaAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
        }
    }

    private DbConnection GetConnection(out bool ownsConnection)
    {
        if (_transaction != null && _transactionConnection != null)
        {
            ownsConnection = false;
            return _transactionConnection;
        }

        ownsConnection = true;
        return CreateConnection();
    }

    private async Task<(DbConnection Connection, bool OwnsConnection)> GetConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (_transaction != null && _transactionConnection != null)
        {
            return (_transactionConnection, false);
        }

        return (await CreateConnectionAsync(cancellationToken).ConfigureAwait(false), true);
    }

    private DbCommand CreateCommand(
        string sql,
        object? parameters,
        out DbConnection connection,
        out bool ownsConnection)
    {
        connection = GetConnection(out ownsConnection);
        var command = connection.CreateCommand();
        command.CommandText = sql;

        if (_transaction != null)
        {
            command.Transaction = _transaction;
        }

        if (parameters != null)
        {
            AddParameters(command, parameters);
        }

        return command;
    }

    private async Task<(DbCommand Command, DbConnection Connection, bool OwnsConnection)> CreateCommandAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var (connection, ownsConnection) = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = sql;

        if (_transaction != null)
        {
            command.Transaction = _transaction;
        }

        if (parameters != null)
        {
            AddParameters(command, parameters);
        }

        return (command, connection, ownsConnection);
    }

    private void AddParameters(DbCommand command, object parameters)
    {
        if (parameters is DynamicParameters dynamicParameters)
        {
            foreach (var pair in dynamicParameters.GetValues())
            {
                AddParameter(command, pair.Key, pair.Value);
            }

            return;
        }

        if (parameters is IDictionary<string, object?> dictionary)
        {
            foreach (var pair in dictionary)
            {
                AddParameter(command, pair.Key, pair.Value);
            }

            return;
        }

        foreach (var property in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            AddParameter(command, property.Name, property.GetValue(parameters));
        }
    }

    private void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = GetParameterPrefix() + name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private void AddEntityParameters<T>(DbCommand command, T entity, TypeMeta meta, bool excludeKey)
        where T : class
    {
        foreach (var property in meta.Properties)
        {
            if (excludeKey && property.IsKey && property.AutoIncrement)
            {
                continue;
            }

            AddParameter(command, property.PropertyName, property.Property.GetValue(entity));
        }
    }

    private string GetParameterPrefix() => _dialect.ParameterPrefix;

    private string NormalizeParameterNames(string sql) => _dialect.NormalizeParameterNames(sql);

    internal string BuildPagedSql(string sql) => _dialect.BuildPagedSql(sql);

    internal string BuildCountSql(string sql) => _dialect.BuildCountSql(sql);

    private string GetLastInsertIdSql() => _dialect.GetLastInsertIdSql();

    private string QuoteIdentifier(string name)
    {
        ThrowIfInvalidIdentifier(name);
        return _dialect.QuoteIdentifier(name);
    }

    private static void ThrowIfInvalidIdentifier(string name)
    {
        if (!IsValidIdentifier(name))
        {
            throw new ArgumentException($"Invalid identifier: {name}", nameof(name));
        }
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        {
            return false;
        }

        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static string EscapeSqlString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private string BuildInsertColumns(TypeMeta meta)
    {
        return string.Join(", ",
            meta.Properties
                .Where(static property => !(property.IsKey && property.AutoIncrement))
                .Select(property => QuoteIdentifier(property.ColumnName)));
    }

    private string BuildInsertValues(TypeMeta meta)
    {
        return string.Join(", ",
            meta.Properties
                .Where(static property => !(property.IsKey && property.AutoIncrement))
                .Select(property => GetParameterPrefix() + property.PropertyName));
    }

    private string BuildUpdateSetClause(TypeMeta meta)
    {
        return string.Join(", ",
            meta.Properties
                .Where(static property => !property.IsKey)
                .Select(property => $"{QuoteIdentifier(property.ColumnName)} = {GetParameterPrefix()}{property.PropertyName}"));
    }

    private string GetAutoIncrementColumn(PropertyMeta property)
    {
        return _dialect.GetAutoIncrementColumn(QuoteIdentifier(property.ColumnName));
    }

    private string GetSqlType(PropertyMeta property)
    {
        var type = Nullable.GetUnderlyingType(property.Property.PropertyType) ?? property.Property.PropertyType;
        return _dialect.GetSqlType(type, property.MaxLength);
    }

    private string GenerateCreateTableSql(TypeMeta meta)
    {
        ThrowIfInvalidIdentifier(meta.TableName);

        var builder = new StringBuilder();
        var createPrefix = _dialect.GetCreateTablePrefix(EscapeSqlString(meta.TableName));
        builder.Append(createPrefix);
        builder.Append(' ');
        builder.Append(QuoteIdentifier(meta.TableName));
        builder.Append(" (");

        var columns = new List<string>(meta.Properties.Count);
        foreach (var property in meta.Properties)
        {
            ThrowIfInvalidIdentifier(property.ColumnName);

            string columnDefinition;
            if (property.IsKey && property.AutoIncrement)
            {
                columnDefinition = GetAutoIncrementColumn(property);
            }
            else
            {
                columnDefinition = $"{QuoteIdentifier(property.ColumnName)} {GetSqlType(property)}";
                if (property.IsRequired)
                {
                    columnDefinition += " NOT NULL";
                }

                if (property.IsKey)
                {
                    columnDefinition += " PRIMARY KEY";
                }
            }

            columns.Add(columnDefinition);
        }

        builder.Append(string.Join(", ", columns));
        builder.Append(')');
        return builder.ToString();
    }

    private static long ConvertToInt64OrDefault(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return 0;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static T? ConvertScalar<T>(object? value)
    {
        if (value == null || value == DBNull.Value)
        {
            return default;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (targetType.IsEnum)
        {
            if (value is string enumName)
            {
                return (T)Enum.Parse(targetType, enumName, ignoreCase: true);
            }

            return (T)Enum.ToObject(targetType, value);
        }

        if (targetType == typeof(Guid))
        {
            if (value is Guid guid)
            {
                return (T)(object)guid;
            }

            return (T)(object)Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            if (value is DateTimeOffset dto)
            {
                return (T)(object)dto;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return (T)(object)DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(TimeSpan))
        {
            if (value is TimeSpan span)
            {
                return (T)(object)span;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return (T)(object)TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        }

        if (value.GetType() == targetType)
        {
            return (T)value;
        }

        return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
    }

    public List<T> Query<T>(string sql, object? parameters = null) where T : new()
    {
        var command = CreateCommand(NormalizeParameterNames(sql), parameters, out var connection, out var ownsConnection);
        try
        {
            using var reader = command.ExecuteReader();
            return MapToList<T>(reader);
        }
        finally
        {
            command.Dispose();
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        var (command, connection, ownsConnection) =
            await CreateCommandAsync(NormalizeParameterNames(sql), parameters, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await MapToListAsync<T>(reader, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public T? QueryFirst<T>(string sql, object? parameters = null) where T : class, new()
    {
        var command = CreateCommand(NormalizeParameterNames(sql), parameters, out var connection, out var ownsConnection);
        try
        {
            using var reader = command.ExecuteReader();
            return reader.Read() ? MapToObject<T>(reader) : null;
        }
        finally
        {
            command.Dispose();
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    public async Task<T?> QueryFirstAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var (command, connection, ownsConnection) =
            await CreateCommandAsync(NormalizeParameterNames(sql), parameters, cancellationToken).ConfigureAwait(false);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? MapToObject<T>(reader) : null;
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public T? Scalar<T>(string sql, object? parameters = null)
    {
        var command = CreateCommand(NormalizeParameterNames(sql), parameters, out var connection, out var ownsConnection);
        try
        {
            return ConvertScalar<T>(command.ExecuteScalar());
        }
        finally
        {
            command.Dispose();
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    public async Task<T?> ScalarAsync<T>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var (command, connection, ownsConnection) =
            await CreateCommandAsync(NormalizeParameterNames(sql), parameters, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return ConvertScalar<T>(result);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public int Execute(string sql, object? parameters = null)
    {
        var command = CreateCommand(NormalizeParameterNames(sql), parameters, out var connection, out var ownsConnection);
        try
        {
            return command.ExecuteNonQuery();
        }
        finally
        {
            command.Dispose();
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var (command, connection, ownsConnection) =
            await CreateCommandAsync(NormalizeParameterNames(sql), parameters, cancellationToken).ConfigureAwait(false);
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public T? Get<T>(object id) where T : class, new()
    {
        var meta = TypeMeta.Get<T>();
        var sql = $"SELECT * FROM {QuoteIdentifier(meta.TableName)} WHERE {QuoteIdentifier(meta.KeyColumn)} = @Id";
        return QueryFirst<T>(sql, new { Id = id });
    }

    public Task<T?> GetAsync<T>(object id, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        var meta = TypeMeta.Get<T>();
        var sql = $"SELECT * FROM {QuoteIdentifier(meta.TableName)} WHERE {QuoteIdentifier(meta.KeyColumn)} = @Id";
        return QueryFirstAsync<T>(sql, new { Id = id }, cancellationToken);
    }

    public List<T> GetAll<T>() where T : new()
    {
        var meta = TypeMeta.Get<T>();
        return Query<T>($"SELECT * FROM {QuoteIdentifier(meta.TableName)}");
    }

    public Task<List<T>> GetAllAsync<T>(CancellationToken cancellationToken = default) where T : new()
    {
        var meta = TypeMeta.Get<T>();
        return QueryAsync<T>($"SELECT * FROM {QuoteIdentifier(meta.TableName)}", cancellationToken: cancellationToken);
    }

    public long Insert<T>(T entity) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var tableName = QuoteIdentifier(meta.TableName);
        var columns = BuildInsertColumns(meta);
        var values = BuildInsertValues(meta);
        var insertAndReturnIdSql = meta.HasAutoIncrementKey
            ? _dialect.BuildInsertAndReturnIdSql(tableName, columns, values, QuoteIdentifier(meta.KeyColumn))
            : null;

        if (!string.IsNullOrWhiteSpace(insertAndReturnIdSql))
        {
            var returningCommand = CreateCommand(insertAndReturnIdSql, null, out var returningConnection, out var returningOwnsConnection);
            try
            {
                AddEntityParameters(returningCommand, entity, meta, excludeKey: true);
                return ConvertToInt64OrDefault(returningCommand.ExecuteScalar());
            }
            finally
            {
                returningCommand.Dispose();
                if (returningOwnsConnection)
                {
                    returningConnection.Dispose();
                }
            }
        }

        var sql = $"INSERT INTO {tableName} ({columns}) VALUES ({values})";
        var command = CreateCommand(sql, null, out var connection, out var ownsConnection);
        try
        {
            AddEntityParameters(command, entity, meta, excludeKey: meta.HasAutoIncrementKey);
            command.ExecuteNonQuery();

            if (!meta.HasAutoIncrementKey)
            {
                return ConvertToInt64OrDefault(meta.GetKeyValue(entity));
            }

            command.CommandText = GetLastInsertIdSql();
            command.Parameters.Clear();
            return ConvertToInt64OrDefault(command.ExecuteScalar());
        }
        finally
        {
            command.Dispose();
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    public async Task<long> InsertAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var meta = TypeMeta.Get<T>();
        var tableName = QuoteIdentifier(meta.TableName);
        var columns = BuildInsertColumns(meta);
        var values = BuildInsertValues(meta);
        var insertAndReturnIdSql = meta.HasAutoIncrementKey
            ? _dialect.BuildInsertAndReturnIdSql(tableName, columns, values, QuoteIdentifier(meta.KeyColumn))
            : null;

        if (!string.IsNullOrWhiteSpace(insertAndReturnIdSql))
        {
            var (returningCommand, returningConnection, returningOwnsConnection) =
                await CreateCommandAsync(insertAndReturnIdSql, cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                AddEntityParameters(returningCommand, entity, meta, excludeKey: true);
                var result = await returningCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return ConvertToInt64OrDefault(result);
            }
            finally
            {
                await returningCommand.DisposeAsync().ConfigureAwait(false);
                if (returningOwnsConnection)
                {
                    await returningConnection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        var sql = $"INSERT INTO {tableName} ({columns}) VALUES ({values})";
        var (command, connection, ownsConnection) =
            await CreateCommandAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            AddEntityParameters(command, entity, meta, excludeKey: meta.HasAutoIncrementKey);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (!meta.HasAutoIncrementKey)
            {
                return ConvertToInt64OrDefault(meta.GetKeyValue(entity));
            }

            command.CommandText = GetLastInsertIdSql();
            command.Parameters.Clear();
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return ConvertToInt64OrDefault(result);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public int Update<T>(T entity) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var sql =
            $"UPDATE {QuoteIdentifier(meta.TableName)} SET {BuildUpdateSetClause(meta)} WHERE {QuoteIdentifier(meta.KeyColumn)} = {GetParameterPrefix()}{meta.KeyProperty}";

        var command = CreateCommand(sql, null, out var connection, out var ownsConnection);
        try
        {
            AddEntityParameters(command, entity, meta, excludeKey: false);
            return command.ExecuteNonQuery();
        }
        finally
        {
            command.Dispose();
            if (ownsConnection)
            {
                connection.Dispose();
            }
        }
    }

    public async Task<int> UpdateAsync<T>(
        T entity,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var meta = TypeMeta.Get<T>();
        var sql =
            $"UPDATE {QuoteIdentifier(meta.TableName)} SET {BuildUpdateSetClause(meta)} WHERE {QuoteIdentifier(meta.KeyColumn)} = {GetParameterPrefix()}{meta.KeyProperty}";

        var (command, connection, ownsConnection) =
            await CreateCommandAsync(sql, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            AddEntityParameters(command, entity, meta, excludeKey: false);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await command.DisposeAsync().ConfigureAwait(false);
            if (ownsConnection)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public int Delete<T>(object id)
    {
        var meta = TypeMeta.Get<T>();
        var sql = $"DELETE FROM {QuoteIdentifier(meta.TableName)} WHERE {QuoteIdentifier(meta.KeyColumn)} = @Id";
        return Execute(sql, new { Id = id });
    }

    public Task<int> DeleteAsync<T>(object id, CancellationToken cancellationToken = default)
    {
        var meta = TypeMeta.Get<T>();
        var sql = $"DELETE FROM {QuoteIdentifier(meta.TableName)} WHERE {QuoteIdentifier(meta.KeyColumn)} = @Id";
        return ExecuteAsync(sql, new { Id = id }, cancellationToken);
    }

    public int Delete<T>(T entity) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var keyValue = meta.GetKeyValue(entity) ?? throw new InvalidOperationException("Entity key cannot be null.");
        return Delete<T>(keyValue);
    }

    public Task<int> DeleteAsync<T>(T entity, CancellationToken cancellationToken = default) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var keyValue = meta.GetKeyValue(entity) ?? throw new InvalidOperationException("Entity key cannot be null.");
        return DeleteAsync<T>(keyValue, cancellationToken);
    }

    public void EnsureTable<T>()
    {
        var meta = TypeMeta.Get<T>();
        Execute(GenerateCreateTableSql(meta));
    }

    public Task<int> EnsureTableAsync<T>(CancellationToken cancellationToken = default)
    {
        var meta = TypeMeta.Get<T>();
        return ExecuteAsync(GenerateCreateTableSql(meta), cancellationToken: cancellationToken);
    }

    public void BeginTransaction()
    {
        if (_transaction != null || _transactionConnection != null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        var connection = CreateConnection();
        try
        {
            _transactionConnection = connection;
            _transaction = connection.BeginTransaction();
        }
        catch
        {
            connection.Dispose();
            _transactionConnection = null;
            _transaction = null;
            throw;
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null || _transactionConnection != null)
        {
            throw new InvalidOperationException("A transaction is already active.");
        }

        var connection = await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _transactionConnection = connection;
            _transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            _transactionConnection = null;
            _transaction = null;
            throw;
        }
    }

    public void Commit()
    {
        if (_transaction == null)
        {
            return;
        }

        try
        {
            _transaction.Commit();
        }
        finally
        {
            CleanupTransaction();
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            return;
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CleanupTransactionAsync().ConfigureAwait(false);
        }
    }

    public void Rollback()
    {
        if (_transaction == null)
        {
            return;
        }

        try
        {
            _transaction.Rollback();
        }
        finally
        {
            CleanupTransaction();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await CleanupTransactionAsync().ConfigureAwait(false);
        }
    }

    private void CleanupTransaction()
    {
        _transaction?.Dispose();
        _transaction = null;
        _transactionConnection?.Dispose();
        _transactionConnection = null;
    }

    private async Task CleanupTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }

        if (_transactionConnection != null)
        {
            await _transactionConnection.DisposeAsync().ConfigureAwait(false);
            _transactionConnection = null;
        }
    }

    public void InTransaction(Action action)
    {
        BeginTransaction();
        try
        {
            action();
            Commit();
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public T InTransaction<T>(Func<T> action)
    {
        BeginTransaction();
        try
        {
            var result = action();
            Commit();
            return result;
        }
        catch
        {
            Rollback();
            throw;
        }
    }

    public async Task InTransactionAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default)
    {
        await BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
            await CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<T> InTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        await BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await action().ConfigureAwait(false);
            await CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private List<T> MapToList<T>(DbDataReader reader) where T : new()
    {
        var list = new List<T>();
        var meta = TypeMeta.Get<T>();
        while (reader.Read())
        {
            list.Add(MapToObject<T>(reader, meta));
        }

        return list;
    }

    private async Task<List<T>> MapToListAsync<T>(DbDataReader reader, CancellationToken cancellationToken) where T : new()
    {
        var list = new List<T>();
        var meta = TypeMeta.Get<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(MapToObject<T>(reader, meta));
        }

        return list;
    }

    private T MapToObject<T>(DbDataReader reader, TypeMeta? meta = null) where T : new()
    {
        meta ??= TypeMeta.Get<T>();
        var instance = new T();

        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (reader.IsDBNull(index))
            {
                continue;
            }

            var columnName = reader.GetName(index);
            var property = meta.GetPropertyByColumn(columnName);
            if (property == null)
            {
                continue;
            }

            var value = reader.GetValue(index);
            var targetType = Nullable.GetUnderlyingType(property.Property.PropertyType) ?? property.Property.PropertyType;

            if (targetType == typeof(Guid))
            {
                value = value is Guid guid
                    ? guid
                    : Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }
            else if (targetType == typeof(DateTime))
            {
                value = value is DateTime dateTime
                    ? dateTime
                    : DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
            }
            else if (targetType == typeof(DateTimeOffset))
            {
                value = value is DateTimeOffset offset
                    ? offset
                    : DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
            }
            else if (targetType == typeof(TimeSpan))
            {
                value = value is TimeSpan span
                    ? span
                    : TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, CultureInfo.InvariantCulture);
            }
            else if (targetType.IsEnum)
            {
                value = value is string enumName
                    ? Enum.Parse(targetType, enumName, ignoreCase: true)
                    : Enum.ToObject(targetType, value);
            }
            else if (value.GetType() != targetType)
            {
                value = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }

            property.Property.SetValue(instance, value);
        }

        return instance;
    }

    public void Dispose()
    {
        if (_transaction != null)
        {
            try
            {
                _transaction.Rollback();
            }
            catch
            {
            }
        }

        CleanupTransaction();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction != null)
        {
            try
            {
                await _transaction.RollbackAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

        await CleanupTransactionAsync().ConfigureAwait(false);
    }

    private abstract class DbDialect
    {
        private static readonly Regex MySqlParameterRegex = new(
            @"@(\w+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

        public abstract string ParameterPrefix { get; }

        public abstract string QuoteIdentifier(string name);

        public abstract string GetLastInsertIdSql();

        public abstract string GetCreateTablePrefix(string escapedTableName);

        public abstract string GetAutoIncrementColumn(string quotedColumnName);

        public abstract string BuildPagedSql(string sql);

        public virtual string BuildCountSql(string sql) => $"SELECT COUNT(*) FROM ({sql}) AS CountQuery";

        public virtual string? BuildInsertAndReturnIdSql(
            string quotedTableName,
            string insertColumns,
            string insertValues,
            string quotedKeyColumn)
        {
            return null;
        }

        public virtual string NormalizeParameterNames(string sql) => sql;

        public virtual void ConfigureConnection(DbConnection connection)
        {
        }

        public virtual Task ConfigureConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            ConfigureConnection(connection);
            return Task.CompletedTask;
        }

        public string GetSqlType(Type type, int maxLength)
        {
            if (type == typeof(int))
            {
                return Int32Type;
            }

            if (type == typeof(long))
            {
                return Int64Type;
            }

            if (type == typeof(short))
            {
                return Int16Type;
            }

            if (type == typeof(byte))
            {
                return ByteType;
            }

            if (type == typeof(bool))
            {
                return BooleanType;
            }

            if (type == typeof(float))
            {
                return SingleType;
            }

            if (type == typeof(double))
            {
                return DoubleType;
            }

            if (type == typeof(decimal))
            {
                return DecimalType;
            }

            if (type == typeof(string))
            {
                return GetStringType(maxLength);
            }

            if (type == typeof(DateTime))
            {
                return DateTimeType;
            }

            if (type == typeof(DateTimeOffset))
            {
                return DateTimeOffsetType;
            }

            if (type == typeof(TimeSpan))
            {
                return TimeSpanType;
            }

            if (type == typeof(Guid))
            {
                return GuidType;
            }

            if (type == typeof(byte[]))
            {
                return BinaryType;
            }

            if (type.IsEnum)
            {
                return EnumType;
            }

            return FallbackType;
        }

        protected abstract string Int32Type { get; }
        protected abstract string Int64Type { get; }
        protected abstract string Int16Type { get; }
        protected abstract string ByteType { get; }
        protected abstract string BooleanType { get; }
        protected abstract string SingleType { get; }
        protected abstract string DoubleType { get; }
        protected abstract string DecimalType { get; }
        protected abstract string DateTimeType { get; }
        protected abstract string DateTimeOffsetType { get; }
        protected abstract string TimeSpanType { get; }
        protected abstract string GuidType { get; }
        protected abstract string BinaryType { get; }
        protected abstract string EnumType { get; }
        protected abstract string FallbackType { get; }

        protected abstract string GetStringType(int maxLength);

        protected static string NormalizeMySqlParameterNames(string sql)
        {
            return MySqlParameterRegex.Replace(sql, "?$1");
        }

        protected static string StripTrailingTopLevelOrderBy(string sql)
        {
            var orderByIndex = FindTopLevelOrderByIndex(sql);
            return orderByIndex >= 0 ? sql[..orderByIndex].TrimEnd() : sql;
        }

        private static int FindTopLevelOrderByIndex(string sql)
        {
            var depth = 0;
            var inSingleQuote = false;
            var lastOrderByIndex = -1;

            for (var index = 0; index < sql.Length; index++)
            {
                var current = sql[index];

                if (inSingleQuote)
                {
                    if (current == '\'')
                    {
                        if (index + 1 < sql.Length && sql[index + 1] == '\'')
                        {
                            index++;
                        }
                        else
                        {
                            inSingleQuote = false;
                        }
                    }

                    continue;
                }

                if (current == '\'')
                {
                    inSingleQuote = true;
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                    continue;
                }

                if (current == ')' && depth > 0)
                {
                    depth--;
                    continue;
                }

                if (depth == 0 && IsKeywordAt(sql, index, "ORDER"))
                {
                    var probe = index + 5;
                    while (probe < sql.Length && char.IsWhiteSpace(sql[probe]))
                    {
                        probe++;
                    }

                    if (IsKeywordAt(sql, probe, "BY"))
                    {
                        lastOrderByIndex = index;
                    }
                }
            }

            return lastOrderByIndex;
        }

        private static bool IsKeywordAt(string sql, int index, string keyword)
        {
            if (index < 0 || index + keyword.Length > sql.Length)
            {
                return false;
            }

            if (!sql.AsSpan(index, keyword.Length).Equals(keyword.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (index > 0)
            {
                var previous = sql[index - 1];
                if (char.IsLetterOrDigit(previous) || previous == '_')
                {
                    return false;
                }
            }

            var end = index + keyword.Length;
            if (end < sql.Length)
            {
                var next = sql[end];
                if (char.IsLetterOrDigit(next) || next == '_')
                {
                    return false;
                }
            }

            return true;
        }

        public static DbDialect Create(DbType dbType)
        {
            return dbType switch
            {
                DbType.SQLite => new SqliteDialect(),
                DbType.SqlServer => new SqlServerDialect(),
                DbType.MySql => new MySqlDialect(),
                DbType.PostgreSql => new PostgreSqlDialect(),
                _ => new GenericDialect()
            };
        }
    }

    private sealed class SqliteDialect : DbDialect
    {
        public override string ParameterPrefix => "@";
        public override string QuoteIdentifier(string name) => $"\"{name}\"";
        public override string GetLastInsertIdSql() => "SELECT last_insert_rowid()";
        public override string GetCreateTablePrefix(string escapedTableName) => "CREATE TABLE IF NOT EXISTS";
        public override string GetAutoIncrementColumn(string quotedColumnName) => $"{quotedColumnName} INTEGER PRIMARY KEY AUTOINCREMENT";
        public override string BuildPagedSql(string sql) => $"{sql} LIMIT @PageSize OFFSET @Offset";

        protected override string Int32Type => "INTEGER";
        protected override string Int64Type => "INTEGER";
        protected override string Int16Type => "INTEGER";
        protected override string ByteType => "INTEGER";
        protected override string BooleanType => "INTEGER";
        protected override string SingleType => "REAL";
        protected override string DoubleType => "REAL";
        protected override string DecimalType => "REAL";
        protected override string DateTimeType => "TEXT";
        protected override string DateTimeOffsetType => "TEXT";
        protected override string TimeSpanType => "TEXT";
        protected override string GuidType => "TEXT";
        protected override string BinaryType => "BLOB";
        protected override string EnumType => "INTEGER";
        protected override string FallbackType => "TEXT";

        protected override string GetStringType(int maxLength) => "TEXT";

        public override void ConfigureConnection(DbConnection connection)
        {
            ApplySqlitePragmas(connection);
        }

        public override Task ConfigureConnectionAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            return ApplySqlitePragmasAsync(connection, cancellationToken);
        }
    }

    private sealed class SqlServerDialect : DbDialect
    {
        public override string ParameterPrefix => "@";
        public override string QuoteIdentifier(string name) => $"[{name}]";
        public override string GetLastInsertIdSql() => "SELECT SCOPE_IDENTITY()";
        public override string GetCreateTablePrefix(string escapedTableName) => $"IF OBJECT_ID(N'{escapedTableName}', N'U') IS NULL CREATE TABLE";
        public override string GetAutoIncrementColumn(string quotedColumnName) => $"{quotedColumnName} INT IDENTITY(1,1) PRIMARY KEY";
        public override string BuildPagedSql(string sql) => $"{sql} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        public override string BuildCountSql(string sql) => $"SELECT COUNT(*) FROM ({StripTrailingTopLevelOrderBy(sql)}) AS CountQuery";
        public override string BuildInsertAndReturnIdSql(
            string quotedTableName,
            string insertColumns,
            string insertValues,
            string quotedKeyColumn)
            => $"INSERT INTO {quotedTableName} ({insertColumns}) OUTPUT INSERTED.{quotedKeyColumn} VALUES ({insertValues})";

        protected override string Int32Type => "INT";
        protected override string Int64Type => "BIGINT";
        protected override string Int16Type => "SMALLINT";
        protected override string ByteType => "TINYINT";
        protected override string BooleanType => "BIT";
        protected override string SingleType => "REAL";
        protected override string DoubleType => "FLOAT";
        protected override string DecimalType => "DECIMAL(18,2)";
        protected override string DateTimeType => "DATETIME2";
        protected override string DateTimeOffsetType => "DATETIMEOFFSET";
        protected override string TimeSpanType => "TIME";
        protected override string GuidType => "UNIQUEIDENTIFIER";
        protected override string BinaryType => "VARBINARY(MAX)";
        protected override string EnumType => "INT";
        protected override string FallbackType => "NVARCHAR(MAX)";

        protected override string GetStringType(int maxLength)
        {
            return maxLength > 0 && maxLength <= 4000
                ? $"NVARCHAR({maxLength})"
                : "NVARCHAR(MAX)";
        }
    }

    private sealed class MySqlDialect : DbDialect
    {
        public override string ParameterPrefix => "?";
        public override string QuoteIdentifier(string name) => $"`{name}`";
        public override string GetLastInsertIdSql() => "SELECT LAST_INSERT_ID()";
        public override string GetCreateTablePrefix(string escapedTableName) => "CREATE TABLE IF NOT EXISTS";
        public override string GetAutoIncrementColumn(string quotedColumnName) => $"{quotedColumnName} INT AUTO_INCREMENT PRIMARY KEY";
        public override string BuildPagedSql(string sql) => $"{sql} LIMIT @PageSize OFFSET @Offset";

        protected override string Int32Type => "INT";
        protected override string Int64Type => "BIGINT";
        protected override string Int16Type => "SMALLINT";
        protected override string ByteType => "TINYINT";
        protected override string BooleanType => "TINYINT(1)";
        protected override string SingleType => "REAL";
        protected override string DoubleType => "FLOAT";
        protected override string DecimalType => "DECIMAL(18,2)";
        protected override string DateTimeType => "DATETIME";
        protected override string DateTimeOffsetType => "DATETIME(6)";
        protected override string TimeSpanType => "TIME";
        protected override string GuidType => "CHAR(36)";
        protected override string BinaryType => "LONGBLOB";
        protected override string EnumType => "INT";
        protected override string FallbackType => "LONGTEXT";

        protected override string GetStringType(int maxLength)
        {
            return maxLength > 0 && maxLength <= 65535
                ? $"VARCHAR({maxLength})"
                : "LONGTEXT";
        }

        public override string NormalizeParameterNames(string sql) => NormalizeMySqlParameterNames(sql);
    }

    private sealed class PostgreSqlDialect : DbDialect
    {
        public override string ParameterPrefix => "@";
        public override string QuoteIdentifier(string name) => $"\"{name}\"";
        public override string GetLastInsertIdSql() => "SELECT lastval()";
        public override string GetCreateTablePrefix(string escapedTableName) => "CREATE TABLE IF NOT EXISTS";
        public override string GetAutoIncrementColumn(string quotedColumnName) => $"{quotedColumnName} SERIAL PRIMARY KEY";
        public override string BuildPagedSql(string sql) => $"{sql} LIMIT @PageSize OFFSET @Offset";
        public override string BuildInsertAndReturnIdSql(
            string quotedTableName,
            string insertColumns,
            string insertValues,
            string quotedKeyColumn)
            => $"INSERT INTO {quotedTableName} ({insertColumns}) VALUES ({insertValues}) RETURNING {quotedKeyColumn}";

        protected override string Int32Type => "INT";
        protected override string Int64Type => "BIGINT";
        protected override string Int16Type => "SMALLINT";
        protected override string ByteType => "SMALLINT";
        protected override string BooleanType => "BOOLEAN";
        protected override string SingleType => "REAL";
        protected override string DoubleType => "DOUBLE PRECISION";
        protected override string DecimalType => "DECIMAL(18,2)";
        protected override string DateTimeType => "TIMESTAMP";
        protected override string DateTimeOffsetType => "TIMESTAMPTZ";
        protected override string TimeSpanType => "INTERVAL";
        protected override string GuidType => "UUID";
        protected override string BinaryType => "BYTEA";
        protected override string EnumType => "INT";
        protected override string FallbackType => "TEXT";

        protected override string GetStringType(int maxLength)
        {
            return maxLength > 0 && maxLength <= 10485760
                ? $"VARCHAR({maxLength})"
                : "TEXT";
        }
    }

    private sealed class GenericDialect : DbDialect
    {
        public override string ParameterPrefix => "@";
        public override string QuoteIdentifier(string name) => $"\"{name}\"";
        public override string GetLastInsertIdSql() => "SELECT @@IDENTITY";
        public override string GetCreateTablePrefix(string escapedTableName) => "CREATE TABLE IF NOT EXISTS";
        public override string GetAutoIncrementColumn(string quotedColumnName) => $"{quotedColumnName} INTEGER PRIMARY KEY AUTOINCREMENT";
        public override string BuildPagedSql(string sql) => $"{sql} LIMIT @PageSize OFFSET @Offset";

        protected override string Int32Type => "INT";
        protected override string Int64Type => "BIGINT";
        protected override string Int16Type => "SMALLINT";
        protected override string ByteType => "TINYINT";
        protected override string BooleanType => "BIT";
        protected override string SingleType => "REAL";
        protected override string DoubleType => "DOUBLE";
        protected override string DecimalType => "DECIMAL(18,2)";
        protected override string DateTimeType => "DATETIME";
        protected override string DateTimeOffsetType => "DATETIMEOFFSET";
        protected override string TimeSpanType => "TIME";
        protected override string GuidType => "CHAR(36)";
        protected override string BinaryType => "BLOB";
        protected override string EnumType => "INT";
        protected override string FallbackType => "TEXT";

        protected override string GetStringType(int maxLength)
        {
            return maxLength > 0 && maxLength <= 4000
                ? $"VARCHAR({maxLength})"
                : "TEXT";
        }
    }
}

internal sealed class TypeMeta
{
    private static readonly ConcurrentDictionary<Type, TypeMeta> Cache = new();

    private TypeMeta(Type type)
    {
        var tableAttribute = type.GetCustomAttribute<TableAttribute>();
        TableName = tableAttribute?.Name ?? $"{type.Name}s";

        Properties = new List<PropertyMeta>();
        PropertyMeta? keyProperty = null;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<IgnoreAttribute>() != null)
            {
                continue;
            }

            var metadata = new PropertyMeta(property);
            Properties.Add(metadata);

            if (metadata.IsKey)
            {
                keyProperty = metadata;
            }
        }

        if (keyProperty == null)
        {
            keyProperty = Properties.FirstOrDefault(property =>
                property.PropertyName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                property.PropertyName.Equals(type.Name + "Id", StringComparison.OrdinalIgnoreCase));

            if (keyProperty != null)
            {
                keyProperty.IsKey = true;
            }
        }

        KeyColumn = keyProperty?.ColumnName ?? "Id";
        KeyProperty = keyProperty?.PropertyName ?? "Id";
        HasAutoIncrementKey = keyProperty?.AutoIncrement ?? false;
        InsertColumns = string.Join(", ",
            Properties.Where(static property => !(property.IsKey && property.AutoIncrement)).Select(property => property.ColumnName));
        InsertValues = string.Join(", ",
            Properties.Where(static property => !(property.IsKey && property.AutoIncrement)).Select(property => "@" + property.PropertyName));
        UpdateSetClause = string.Join(", ",
            Properties.Where(static property => !property.IsKey).Select(property => $"{property.ColumnName} = @{property.PropertyName}"));
    }

    public string TableName { get; }
    public string KeyColumn { get; }
    public string KeyProperty { get; }
    public bool HasAutoIncrementKey { get; }
    public List<PropertyMeta> Properties { get; }
    public string InsertColumns { get; }
    public string InsertValues { get; }
    public string UpdateSetClause { get; }

    public static TypeMeta Get<T>() => Get(typeof(T));

    public static TypeMeta Get(Type type) => Cache.GetOrAdd(type, static current => new TypeMeta(current));

    public PropertyMeta? GetPropertyByColumn(string columnName)
    {
        return Properties.FirstOrDefault(property =>
            property.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
    }

    public object? GetKeyValue<T>(T entity) where T : class
    {
        var keyProperty = Properties.FirstOrDefault(static property => property.IsKey);
        return keyProperty?.Property.GetValue(entity);
    }
}

internal sealed class PropertyMeta
{
    public PropertyMeta(PropertyInfo property)
    {
        Property = property;
        PropertyName = property.Name;

        var columnAttribute = property.GetCustomAttribute<ColumnAttribute>();
        ColumnName = columnAttribute?.Name ?? property.Name;

        var keyAttribute = property.GetCustomAttribute<KeyAttribute>();
        IsKey = keyAttribute != null;
        AutoIncrement = keyAttribute?.AutoIncrement ?? true;
        IsRequired = property.GetCustomAttribute<RequiredAttribute>() != null;
        MaxLength = property.GetCustomAttribute<MaxLengthAttribute>()?.Length ?? 0;
    }

    public PropertyInfo Property { get; }
    public string PropertyName { get; }
    public string ColumnName { get; }
    public bool IsKey { get; set; }
    public bool AutoIncrement { get; }
    public bool IsRequired { get; }
    public int MaxLength { get; }
}

public static class BaseDbExtensions
{
    public static Dictionary<TKey, TValue> QueryDictionary<TKey, TValue>(
        this BaseDb db,
        string sql,
        Func<TValue, TKey> keySelector,
        object? parameters = null)
        where TKey : notnull
        where TValue : new()
    {
        return db.Query<TValue>(sql, parameters).ToDictionary(keySelector);
    }

    public static async Task<Dictionary<TKey, TValue>> QueryDictionaryAsync<TKey, TValue>(
        this BaseDb db,
        string sql,
        Func<TValue, TKey> keySelector,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        where TKey : notnull
        where TValue : new()
    {
        var items = await db.QueryAsync<TValue>(sql, parameters, cancellationToken).ConfigureAwait(false);
        return items.ToDictionary(keySelector);
    }

    public static PagedResult<T> QueryPaged<T>(
        this BaseDb db,
        string sql,
        int page,
        int pageSize,
        object? parameters = null)
        where T : new()
    {
        var totalCount = db.Scalar<int>(db.BuildCountSql(sql), parameters);
        var pagedSql = db.BuildPagedSql(sql);
        var offset = Math.Max(page - 1, 0) * pageSize;
        var pagedParameters = BuildPagedParameters(parameters, pageSize, offset);
        var items = db.Query<T>(pagedSql, new DynamicParameters(pagedParameters));

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public static async Task<PagedResult<T>> QueryPagedAsync<T>(
        this BaseDb db,
        string sql,
        int page,
        int pageSize,
        object? parameters = null,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        var totalCount = await db.ScalarAsync<int>(
            db.BuildCountSql(sql),
            parameters,
            cancellationToken).ConfigureAwait(false);
        var pagedSql = db.BuildPagedSql(sql);
        var offset = Math.Max(page - 1, 0) * pageSize;
        var pagedParameters = BuildPagedParameters(parameters, pageSize, offset);
        var items = await db.QueryAsync<T>(
            pagedSql,
            new DynamicParameters(pagedParameters),
            cancellationToken).ConfigureAwait(false);

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static Dictionary<string, object?> BuildPagedParameters(object? parameters, int pageSize, int offset)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PageSize"] = pageSize,
            ["Offset"] = offset
        };

        if (parameters is DynamicParameters dynamicParameters)
        {
            foreach (var pair in dynamicParameters.GetValues())
            {
                values[pair.Key] = pair.Value;
            }

            return values;
        }

        if (parameters is IDictionary<string, object?> dictionary)
        {
            foreach (var pair in dictionary)
            {
                values[pair.Key] = pair.Value;
            }

            return values;
        }

        if (parameters != null)
        {
            foreach (var property in parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                values[property.Name] = property.GetValue(parameters);
            }
        }

        return values;
    }
}

public sealed class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

internal sealed class DynamicParameters
{
    private readonly Dictionary<string, object?> _values;

    public DynamicParameters(Dictionary<string, object?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public IReadOnlyDictionary<string, object?> GetValues() => _values;
}
