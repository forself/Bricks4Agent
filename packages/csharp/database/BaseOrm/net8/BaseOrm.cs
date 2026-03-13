/**
 * BaseOrm - 極簡 ORM 元件
 *
 * 類似 Dapper 的輕量級 ORM，僅依賴 ADO.NET
 * 支援 SQLite、SQL Server、MySQL、PostgreSQL
 *
 * 功能：
 * - 自動物件映射
 * - 參數化查詢
 * - CRUD 操作
 * - 交易支援
 * - 連線池管理
 * - 多資料庫支援
 *
 * 用法：
 *   var db = new BaseDb("Data Source=app.db");
 *   var db = BaseDb.UseSqlServer("Server=.;Database=App;...");
 *   var db = BaseDb.UseMySql("Server=localhost;Database=App;...");
 *   var db = BaseDb.UsePostgreSql("Host=localhost;Database=App;...");
 */

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Text;

namespace BaseOrm;

#region Enums

/// <summary>
/// 資料庫類型
/// </summary>
public enum DbType
{
    SQLite,
    SqlServer,
    MySql,
    PostgreSql,
    Unknown
}

#endregion

#region Attributes

/// <summary>資料表名稱</summary>
[AttributeUsage(AttributeTargets.Class)]
public class TableAttribute : Attribute
{
    public string Name { get; }
    public TableAttribute(string name) => Name = name;
}

/// <summary>主鍵欄位</summary>
[AttributeUsage(AttributeTargets.Property)]
public class KeyAttribute : Attribute
{
    public bool AutoIncrement { get; set; } = true;
}

/// <summary>欄位名稱對應</summary>
[AttributeUsage(AttributeTargets.Property)]
public class ColumnAttribute : Attribute
{
    public string Name { get; }
    public ColumnAttribute(string name) => Name = name;
}

/// <summary>忽略此屬性</summary>
[AttributeUsage(AttributeTargets.Property)]
public class IgnoreAttribute : Attribute { }

/// <summary>欄位長度 (用於 Schema 生成)</summary>
[AttributeUsage(AttributeTargets.Property)]
public class MaxLengthAttribute : Attribute
{
    public int Length { get; }
    public MaxLengthAttribute(int length) => Length = length;
}

/// <summary>必填欄位</summary>
[AttributeUsage(AttributeTargets.Property)]
public class RequiredAttribute : Attribute { }

#endregion

#region Core Database

/// <summary>
/// 微型資料庫操作類別
/// </summary>
public class BaseDb : IDisposable
{
    private readonly string _connectionString;
    private readonly DbProviderFactory _factory;
    private readonly DbType _dbType;
    private DbConnection? _connection;
    private DbTransaction? _transaction;

    /// <summary>
    /// 資料庫類型
    /// </summary>
    public DbType DatabaseType => _dbType;

    #region Constructors & Factory Methods

    /// <summary>
    /// 建立 SQLite 資料庫連線 (預設)
    /// </summary>
    public BaseDb(string connectionString)
        : this(connectionString, GetSqliteFactory(), DbType.SQLite) { }

    /// <summary>
    /// 建立資料庫連線 (自動偵測類型)
    /// </summary>
    public BaseDb(string connectionString, DbProviderFactory factory)
        : this(connectionString, factory, DetectDbType(factory)) { }

    /// <summary>
    /// 建立資料庫連線 (明確指定類型)
    /// </summary>
    public BaseDb(string connectionString, DbProviderFactory factory, DbType dbType)
    {
        _connectionString = connectionString;
        _factory = factory;
        _dbType = dbType;
    }

    /// <summary>
    /// 建立 SQL Server 連線
    /// </summary>
    public static BaseDb UseSqlServer(string connectionString)
    {
        var factory = GetProviderFactory("Microsoft.Data.SqlClient.SqlClientFactory, Microsoft.Data.SqlClient");
        return new BaseDb(connectionString, factory!, DbType.SqlServer);
    }

    /// <summary>
    /// 建立 MySQL 連線
    /// </summary>
    public static BaseDb UseMySql(string connectionString)
    {
        var factory = GetProviderFactory("MySqlConnector.MySqlConnectorFactory, MySqlConnector")
            ?? GetProviderFactory("MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data");
        return new BaseDb(connectionString, factory!, DbType.MySql);
    }

    /// <summary>
    /// 建立 PostgreSQL 連線
    /// </summary>
    public static BaseDb UsePostgreSql(string connectionString)
    {
        var factory = GetProviderFactory("Npgsql.NpgsqlFactory, Npgsql");
        return new BaseDb(connectionString, factory!, DbType.PostgreSql);
    }

    /// <summary>
    /// 建立 SQLite 連線
    /// </summary>
    public static BaseDb UseSqlite(string connectionString)
    {
        return new BaseDb(connectionString, GetSqliteFactory(), DbType.SQLite);
    }

    private static DbProviderFactory GetSqliteFactory()
    {
        return GetProviderFactory("Microsoft.Data.Sqlite.SqliteFactory, Microsoft.Data.Sqlite")!;
    }

    private static DbProviderFactory? GetProviderFactory(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type == null) return null;
        var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null) as DbProviderFactory;
    }

    private static DbType DetectDbType(DbProviderFactory factory)
    {
        var typeName = factory.GetType().FullName ?? "";
        if (typeName.Contains("Sqlite")) return DbType.SQLite;
        if (typeName.Contains("SqlClient")) return DbType.SqlServer;
        if (typeName.Contains("MySql")) return DbType.MySql;
        if (typeName.Contains("Npgsql")) return DbType.PostgreSql;
        return DbType.Unknown;
    }

    #endregion

    #region Connection Management

    private DbConnection GetConnection()
    {
        if (_connection == null)
        {
            _connection = _factory.CreateConnection()!;
            _connection.ConnectionString = _connectionString;
        }

        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }

        return _connection;
    }

    private DbCommand CreateCommand(string sql, object? parameters = null)
    {
        var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (_transaction != null)
        {
            cmd.Transaction = _transaction;
        }

        if (parameters != null)
        {
            AddParameters(cmd, parameters);
        }

        return cmd;
    }

    private void AddParameters(DbCommand cmd, object parameters)
    {
        // 處理 DynamicParameters 類型
        if (parameters is DynamicParameters dynParams)
        {
            foreach (var kvp in dynParams.GetValues())
            {
                var param = cmd.CreateParameter();
                param.ParameterName = GetParameterPrefix() + kvp.Key;
                param.Value = kvp.Value ?? DBNull.Value;
                cmd.Parameters.Add(param);
            }
            return;
        }

        // 處理 Dictionary 類型
        if (parameters is IDictionary<string, object?> dict)
        {
            foreach (var kvp in dict)
            {
                var param = cmd.CreateParameter();
                param.ParameterName = GetParameterPrefix() + kvp.Key;
                param.Value = kvp.Value ?? DBNull.Value;
                cmd.Parameters.Add(param);
            }
            return;
        }

        // 處理一般物件 (透過反射)
        var props = parameters.GetType().GetProperties();
        foreach (var prop in props)
        {
            var param = cmd.CreateParameter();
            param.ParameterName = GetParameterPrefix() + prop.Name;
            param.Value = prop.GetValue(parameters) ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
    }

    private string GetParameterPrefix() => _dbType == DbType.MySql ? "?" : "@";

    #endregion

    #region Query Methods

    /// <summary>
    /// 查詢多筆資料
    /// </summary>
    public List<T> Query<T>(string sql, object? parameters = null) where T : new()
    {
        using var cmd = CreateCommand(NormalizeParameterNames(sql), parameters);
        using var reader = cmd.ExecuteReader();
        return MapToList<T>(reader);
    }

    /// <summary>
    /// 查詢單筆資料
    /// </summary>
    public T? QueryFirst<T>(string sql, object? parameters = null) where T : class, new()
    {
        using var cmd = CreateCommand(NormalizeParameterNames(sql), parameters);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapToObject<T>(reader) : null;
    }

    /// <summary>
    /// 查詢單一值
    /// </summary>
    public T? Scalar<T>(string sql, object? parameters = null)
    {
        using var cmd = CreateCommand(NormalizeParameterNames(sql), parameters);
        var result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
            return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>
    /// 執行非查詢命令
    /// </summary>
    public int Execute(string sql, object? parameters = null)
    {
        using var cmd = CreateCommand(NormalizeParameterNames(sql), parameters);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 將 @param 格式轉換為對應資料庫的參數格式
    /// </summary>
    private string NormalizeParameterNames(string sql)
    {
        if (_dbType == DbType.MySql)
        {
            // MySQL 使用 ? 作為參數前綴
            // 設定逾時防止 ReDoS 攻擊
            return System.Text.RegularExpressions.Regex.Replace(
                sql, @"@(\w+)", "?$1",
                System.Text.RegularExpressions.RegexOptions.None,
                TimeSpan.FromSeconds(1));
        }
        return sql;
    }

    #endregion

    #region CRUD Operations

    /// <summary>
    /// 依主鍵取得資料
    /// </summary>
    public T? Get<T>(object id) where T : class, new()
    {
        var meta = TypeMeta.Get<T>();
        var sql = $"SELECT * FROM {QuoteIdentifier(meta.TableName)} WHERE {QuoteIdentifier(meta.KeyColumn)} = @Id";
        return QueryFirst<T>(sql, new { Id = id });
    }

    /// <summary>
    /// 取得所有資料
    /// </summary>
    public List<T> GetAll<T>() where T : new()
    {
        var meta = TypeMeta.Get<T>();
        return Query<T>($"SELECT * FROM {QuoteIdentifier(meta.TableName)}");
    }

    /// <summary>
    /// 新增資料
    /// </summary>
    public long Insert<T>(T entity) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var columns = meta.InsertColumns;
        var values = meta.InsertValues;

        string sql;
        if (_dbType == DbType.PostgreSql && meta.HasAutoIncrementKey)
        {
            // PostgreSQL 使用 RETURNING
            sql = $"INSERT INTO {QuoteIdentifier(meta.TableName)} ({columns}) VALUES ({values}) RETURNING {meta.KeyColumn}";
            using var cmd = CreateCommand(NormalizeParameterNames(sql));
            AddEntityParameters(cmd, entity, meta, excludeKey: true);
            var result = cmd.ExecuteScalar();
            return Convert.ToInt64(result);
        }
        else
        {
            sql = $"INSERT INTO {QuoteIdentifier(meta.TableName)} ({columns}) VALUES ({values})";
            using var cmd = CreateCommand(NormalizeParameterNames(sql));
            AddEntityParameters(cmd, entity, meta, excludeKey: true);
            cmd.ExecuteNonQuery();

            // 取得自動產生的 ID
            cmd.CommandText = GetLastInsertIdSql();
            cmd.Parameters.Clear();
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt64(result) : 0;
        }
    }

    /// <summary>
    /// 取得最後插入 ID 的 SQL
    /// </summary>
    private string GetLastInsertIdSql() => _dbType switch
    {
        DbType.SQLite => "SELECT last_insert_rowid()",
        DbType.SqlServer => "SELECT SCOPE_IDENTITY()",
        DbType.MySql => "SELECT LAST_INSERT_ID()",
        DbType.PostgreSql => "SELECT lastval()",
        _ => "SELECT @@IDENTITY"
    };

    /// <summary>
    /// 更新資料
    /// </summary>
    public int Update<T>(T entity) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var setClause = meta.UpdateSetClause;

        var sql = $"UPDATE {QuoteIdentifier(meta.TableName)} SET {setClause} WHERE {QuoteIdentifier(meta.KeyColumn)} = @{meta.KeyProperty}";

        using var cmd = CreateCommand(NormalizeParameterNames(sql));
        AddEntityParameters(cmd, entity, meta, excludeKey: false);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 刪除資料
    /// </summary>
    public int Delete<T>(object id)
    {
        var meta = TypeMeta.Get<T>();
        var sql = $"DELETE FROM {QuoteIdentifier(meta.TableName)} WHERE {QuoteIdentifier(meta.KeyColumn)} = @Id";
        return Execute(sql, new { Id = id });
    }

    /// <summary>
    /// 刪除實體
    /// </summary>
    public int Delete<T>(T entity) where T : class
    {
        var meta = TypeMeta.Get<T>();
        var keyValue = meta.GetKeyValue(entity);
        return Delete<T>(keyValue!);
    }

    private void AddEntityParameters<T>(DbCommand cmd, T entity, TypeMeta meta, bool excludeKey) where T : class
    {
        foreach (var prop in meta.Properties)
        {
            if (excludeKey && prop.IsKey && prop.AutoIncrement)
                continue;

            var param = cmd.CreateParameter();
            param.ParameterName = GetParameterPrefix() + prop.PropertyName;
            param.Value = prop.Property.GetValue(entity) ?? DBNull.Value;
            cmd.Parameters.Add(param);
        }
    }

    /// <summary>
    /// 識別符號引用 (處理保留字)
    /// </summary>
    private string QuoteIdentifier(string name)
    {
        // 先驗證識別符
        if (!IsValidIdentifier(name))
        {
            throw new ArgumentException($"Invalid identifier: {name}");
        }

        return _dbType switch
        {
            DbType.MySql => $"`{EscapeBacktick(name)}`",
            DbType.PostgreSql => $"\"{EscapeDoubleQuote(name)}\"",
            DbType.SqlServer => $"[{EscapeBracket(name)}]",
            _ => $"\"{EscapeDoubleQuote(name)}\"" // SQLite 使用雙引號
        };
    }

    /// <summary>
    /// 驗證識別符是否安全 (只允許字母、數字、底線，最多 128 字元)
    /// </summary>
    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            return false;

        // 第一個字元必須是字母或底線
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        // 其餘字元只能是字母、數字、底線
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    /// <summary>
    /// 跳脫 SQL 字串中的單引號
    /// </summary>
    private static string EscapeSqlString(string value) => value.Replace("'", "''");

    /// <summary>
    /// 跳脫反引號 (MySQL)
    /// </summary>
    private static string EscapeBacktick(string value) => value.Replace("`", "``");

    /// <summary>
    /// 跳脫雙引號 (PostgreSQL, SQLite)
    /// </summary>
    private static string EscapeDoubleQuote(string value) => value.Replace("\"", "\"\"");

    /// <summary>
    /// 跳脫方括號 (SQL Server)
    /// </summary>
    private static string EscapeBracket(string value) => value.Replace("]", "]]");

    #endregion

    #region Transaction

    /// <summary>
    /// 開始交易
    /// </summary>
    public void BeginTransaction()
    {
        var conn = GetConnection();
        _transaction = conn.BeginTransaction();
    }

    /// <summary>
    /// 提交交易
    /// </summary>
    public void Commit()
    {
        _transaction?.Commit();
        _transaction?.Dispose();
        _transaction = null;
    }

    /// <summary>
    /// 回滾交易
    /// </summary>
    public void Rollback()
    {
        _transaction?.Rollback();
        _transaction?.Dispose();
        _transaction = null;
    }

    /// <summary>
    /// 在交易中執行動作
    /// </summary>
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

    /// <summary>
    /// 在交易中執行動作 (有回傳值)
    /// </summary>
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

    #endregion

    #region Object Mapping

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

    private T MapToObject<T>(DbDataReader reader, TypeMeta? meta = null) where T : new()
    {
        meta ??= TypeMeta.Get<T>();
        var obj = new T();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);
            var prop = meta.GetPropertyByColumn(columnName);

            if (prop != null && !reader.IsDBNull(i))
            {
                var value = reader.GetValue(i);
                var targetType = Nullable.GetUnderlyingType(prop.Property.PropertyType)
                    ?? prop.Property.PropertyType;

                if (targetType == typeof(Guid) && value is string strGuid)
                {
                    value = Guid.Parse(strGuid);
                }
                else if (targetType == typeof(DateTime) && value is string strDate)
                {
                    value = DateTime.Parse(strDate);
                }
                else if (targetType.IsEnum)
                {
                    value = Enum.ToObject(targetType, value);
                }
                else if (targetType != value.GetType())
                {
                    value = Convert.ChangeType(value, targetType);
                }

                prop.Property.SetValue(obj, value);
            }
        }

        return obj;
    }

    #endregion

    #region Schema Operations

    /// <summary>
    /// 確保資料表存在 (依據實體類別建立)
    /// </summary>
    public void EnsureTable<T>()
    {
        var meta = TypeMeta.Get<T>();
        var sql = GenerateCreateTableSql(meta);
        Execute(sql);
    }

    private string GenerateCreateTableSql(TypeMeta meta)
    {
        var sb = new StringBuilder();

        // 驗證表名 (只允許字母、數字、底線)
        if (!IsValidIdentifier(meta.TableName))
        {
            throw new ArgumentException($"Invalid table name: {meta.TableName}");
        }

        // CREATE TABLE 語法 - 使用安全的識別符引用
        var quotedTableName = QuoteIdentifier(meta.TableName);
        var createPrefix = _dbType == DbType.SqlServer
            ? $"IF OBJECT_ID(N'{EscapeSqlString(meta.TableName)}', N'U') IS NULL CREATE TABLE"
            : "CREATE TABLE IF NOT EXISTS";

        sb.Append($"{createPrefix} {quotedTableName} (");

        var columns = new List<string>();
        foreach (var prop in meta.Properties)
        {
            // 驗證欄位名稱
            if (!IsValidIdentifier(prop.ColumnName))
            {
                throw new ArgumentException($"Invalid column name: {prop.ColumnName}");
            }

            var colDef = $"{QuoteIdentifier(prop.ColumnName)} {GetSqlType(prop)}";

            if (prop.IsRequired)
            {
                colDef += " NOT NULL";
            }

            if (prop.IsKey)
            {
                if (prop.AutoIncrement)
                {
                    colDef = GetAutoIncrementColumn(prop);
                }
                else
                {
                    colDef += " PRIMARY KEY";
                }
            }

            columns.Add(colDef);
        }

        sb.Append(string.Join(", ", columns));
        sb.Append(")");

        return sb.ToString();
    }

    private string GetAutoIncrementColumn(PropertyMeta prop)
    {
        var colName = QuoteIdentifier(prop.ColumnName);
        return _dbType switch
        {
            DbType.SQLite => $"{colName} INTEGER PRIMARY KEY AUTOINCREMENT",
            DbType.SqlServer => $"{colName} INT IDENTITY(1,1) PRIMARY KEY",
            DbType.MySql => $"{colName} INT AUTO_INCREMENT PRIMARY KEY",
            DbType.PostgreSql => $"{colName} SERIAL PRIMARY KEY",
            _ => $"{colName} INTEGER PRIMARY KEY AUTOINCREMENT"
        };
    }

    private string GetSqlType(PropertyMeta prop)
    {
        var type = Nullable.GetUnderlyingType(prop.Property.PropertyType) ?? prop.Property.PropertyType;
        var maxLength = prop.MaxLength;

        // 整數類型
        if (type == typeof(int))
            return _dbType == DbType.SQLite ? "INTEGER" : "INT";
        if (type == typeof(long))
            return _dbType == DbType.SQLite ? "INTEGER" : "BIGINT";
        if (type == typeof(short))
            return _dbType == DbType.SQLite ? "INTEGER" : "SMALLINT";
        if (type == typeof(byte))
            return _dbType == DbType.SQLite ? "INTEGER" : "TINYINT";
        if (type == typeof(bool))
            return _dbType switch
            {
                DbType.SQLite => "INTEGER",
                DbType.PostgreSql => "BOOLEAN",
                DbType.MySql => "TINYINT(1)",
                _ => "BIT"
            };

        // 浮點類型
        if (type == typeof(float))
            return _dbType == DbType.SQLite ? "REAL" : "REAL";
        if (type == typeof(double))
            return _dbType switch
            {
                DbType.SQLite => "REAL",
                DbType.PostgreSql => "DOUBLE PRECISION",
                _ => "FLOAT"
            };
        if (type == typeof(decimal))
            return _dbType == DbType.SQLite ? "REAL" : "DECIMAL(18,2)";

        // 字串類型
        if (type == typeof(string))
        {
            if (maxLength > 0 && maxLength <= 8000)
            {
                return _dbType switch
                {
                    DbType.SQLite => "TEXT",
                    DbType.PostgreSql => $"VARCHAR({maxLength})",
                    DbType.MySql => $"VARCHAR({maxLength})",
                    _ => $"NVARCHAR({maxLength})"
                };
            }
            return _dbType switch
            {
                DbType.SQLite => "TEXT",
                DbType.PostgreSql => "TEXT",
                DbType.MySql => "LONGTEXT",
                _ => "NVARCHAR(MAX)"
            };
        }

        // 日期時間
        if (type == typeof(DateTime))
            return _dbType switch
            {
                DbType.SQLite => "TEXT",
                DbType.PostgreSql => "TIMESTAMP",
                DbType.MySql => "DATETIME",
                _ => "DATETIME2"
            };
        if (type == typeof(DateTimeOffset))
            return _dbType switch
            {
                DbType.SQLite => "TEXT",
                DbType.PostgreSql => "TIMESTAMPTZ",
                _ => "DATETIMEOFFSET"
            };
        if (type == typeof(TimeSpan))
            return _dbType switch
            {
                DbType.SQLite => "TEXT",
                DbType.PostgreSql => "INTERVAL",
                _ => "TIME"
            };

        // GUID
        if (type == typeof(Guid))
            return _dbType switch
            {
                DbType.SQLite => "TEXT",
                DbType.PostgreSql => "UUID",
                DbType.MySql => "CHAR(36)",
                _ => "UNIQUEIDENTIFIER"
            };

        // 二進位
        if (type == typeof(byte[]))
            return _dbType switch
            {
                DbType.SQLite => "BLOB",
                DbType.PostgreSql => "BYTEA",
                DbType.MySql => "LONGBLOB",
                _ => "VARBINARY(MAX)"
            };

        // 枚舉
        if (type.IsEnum)
            return _dbType == DbType.SQLite ? "INTEGER" : "INT";

        return "TEXT";
    }

    #endregion

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection?.Dispose();
    }
}

#endregion

#region Type Metadata

/// <summary>
/// 類型元資料 (快取)
/// </summary>
internal class TypeMeta
{
    // 使用 ConcurrentDictionary 確保執行緒安全
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, TypeMeta> _cache = new();

    public string TableName { get; }
    public string KeyColumn { get; }
    public string KeyProperty { get; }
    public bool HasAutoIncrementKey { get; }
    public List<PropertyMeta> Properties { get; }

    public string InsertColumns { get; }
    public string InsertValues { get; }
    public string UpdateSetClause { get; }

    private TypeMeta(Type type)
    {
        // 取得資料表名稱
        var tableAttr = type.GetCustomAttribute<TableAttribute>();
        TableName = tableAttr?.Name ?? type.Name + "s";

        // 取得所有屬性
        Properties = new List<PropertyMeta>();
        PropertyMeta? keyProp = null;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<IgnoreAttribute>() != null)
                continue;

            var meta = new PropertyMeta(prop);
            Properties.Add(meta);

            if (meta.IsKey)
                keyProp = meta;
        }

        // 如果沒有標記 Key，使用 Id 或 {Type}Id
        if (keyProp == null)
        {
            keyProp = Properties.FirstOrDefault(p =>
                p.PropertyName.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                p.PropertyName.Equals(type.Name + "Id", StringComparison.OrdinalIgnoreCase));

            if (keyProp != null)
                keyProp.IsKey = true;
        }

        KeyColumn = keyProp?.ColumnName ?? "Id";
        KeyProperty = keyProp?.PropertyName ?? "Id";
        HasAutoIncrementKey = keyProp?.AutoIncrement ?? false;

        // 建立 SQL 片段
        var insertCols = Properties.Where(p => !(p.IsKey && p.AutoIncrement)).Select(p => p.ColumnName);
        var insertVals = Properties.Where(p => !(p.IsKey && p.AutoIncrement)).Select(p => "@" + p.PropertyName);
        var updateSets = Properties.Where(p => !p.IsKey).Select(p => $"{p.ColumnName} = @{p.PropertyName}");

        InsertColumns = string.Join(", ", insertCols);
        InsertValues = string.Join(", ", insertVals);
        UpdateSetClause = string.Join(", ", updateSets);
    }

    public static TypeMeta Get<T>() => Get(typeof(T));

    public static TypeMeta Get(Type type)
    {
        // 使用 GetOrAdd 確保執行緒安全的初始化
        return _cache.GetOrAdd(type, t => new TypeMeta(t));
    }

    public PropertyMeta? GetPropertyByColumn(string columnName)
    {
        return Properties.FirstOrDefault(p =>
            p.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
    }

    public object? GetKeyValue<T>(T entity) where T : class
    {
        var keyProp = Properties.FirstOrDefault(p => p.IsKey);
        return keyProp?.Property.GetValue(entity);
    }
}

/// <summary>
/// 屬性元資料
/// </summary>
internal class PropertyMeta
{
    public PropertyInfo Property { get; }
    public string PropertyName { get; }
    public string ColumnName { get; }
    public bool IsKey { get; set; }
    public bool AutoIncrement { get; }
    public bool IsRequired { get; }
    public int MaxLength { get; }

    public PropertyMeta(PropertyInfo prop)
    {
        Property = prop;
        PropertyName = prop.Name;

        var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
        ColumnName = columnAttr?.Name ?? prop.Name;

        var keyAttr = prop.GetCustomAttribute<KeyAttribute>();
        IsKey = keyAttr != null;
        AutoIncrement = keyAttr?.AutoIncrement ?? true;

        IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null;
        MaxLength = prop.GetCustomAttribute<MaxLengthAttribute>()?.Length ?? 0;
    }
}

#endregion

#region Extensions

/// <summary>
/// 擴充方法
/// </summary>
public static class BaseDbExtensions
{
    /// <summary>
    /// 查詢並轉換為字典
    /// </summary>
    public static Dictionary<TKey, TValue> QueryDictionary<TKey, TValue>(
        this BaseDb db,
        string sql,
        Func<TValue, TKey> keySelector,
        object? parameters = null) where TKey : notnull where TValue : new()
    {
        var list = db.Query<TValue>(sql, parameters);
        return list.ToDictionary(keySelector);
    }

    /// <summary>
    /// 分頁查詢
    /// </summary>
    public static PagedResult<T> QueryPaged<T>(
        this BaseDb db,
        string sql,
        int page,
        int pageSize,
        object? parameters = null) where T : new()
    {
        var countSql = $"SELECT COUNT(*) FROM ({sql}) AS CountQuery";
        var totalCount = db.Scalar<int>(countSql, parameters);

        // 不同資料庫的分頁語法
        var pagedSql = db.DatabaseType switch
        {
            DbType.SqlServer => $"{sql} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY",
            _ => $"{sql} LIMIT @PageSize OFFSET @Offset"
        };

        var offset = (page - 1) * pageSize;

        // 合併參數
        var pagedParams = new Dictionary<string, object?>
        {
            ["PageSize"] = pageSize,
            ["Offset"] = offset
        };

        if (parameters != null)
        {
            foreach (var prop in parameters.GetType().GetProperties())
            {
                pagedParams[prop.Name] = prop.GetValue(parameters);
            }
        }

        var items = db.Query<T>(pagedSql, new DynamicParameters(pagedParams));

        return new PagedResult<T>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}

/// <summary>
/// 分頁結果
/// </summary>
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

/// <summary>
/// 動態參數包裝
/// </summary>
internal class DynamicParameters
{
    private readonly Dictionary<string, object?> _values;

    public DynamicParameters(Dictionary<string, object?> values)
    {
        _values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>
    /// 取得所有參數值
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetValues() => _values;
}

#endregion
