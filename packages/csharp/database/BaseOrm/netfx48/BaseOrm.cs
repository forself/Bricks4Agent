/**
 * BaseOrm - 極簡 ORM 元件 (.NET Framework 4.8 版)
 *
 * 類似 Dapper 的輕量級 ORM，僅依賴 ADO.NET
 * 支援 SQLite、SQL Server、MySQL、PostgreSQL
 *
 * 此版本針對 .NET Framework 4.8 優化：
 * - 使用 System.Data.SqlClient
 * - 支援 Tim.Dto.Attribute 的 DataAttribute 和 DBColNameAttribute
 * - 移除 nullable reference types
 *
 * 用法：
 *   var db = new BaseDb("Data Source=app.db");
 *   var db = BaseDb.UseSqlServer("Server=.;Database=App;...");
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BaseOrm
{
    #region Enums

    /// <summary>
    /// 資料庫類型
    /// </summary>
    public enum DatabaseType
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
        public string Name { get; private set; }
        public TableAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>主鍵欄位</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class KeyAttribute : Attribute
    {
        public bool AutoIncrement { get; set; }
        public KeyAttribute()
        {
            AutoIncrement = true;
        }
    }

    /// <summary>欄位名稱對應</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class ColumnAttribute : Attribute
    {
        public string Name { get; private set; }
        public ColumnAttribute(string name)
        {
            Name = name;
        }
    }

    /// <summary>忽略此屬性</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class IgnoreAttribute : Attribute { }

    /// <summary>欄位長度 (用於 Schema 生成)</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MaxLengthAttribute : Attribute
    {
        public int Length { get; private set; }
        public MaxLengthAttribute(int length)
        {
            Length = length;
        }
    }

    /// <summary>必填欄位</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class RequiredAttribute : Attribute { }

    #endregion

    #region Legacy Attribute Adapter

    /// <summary>
    /// 舊版 Attribute 適配器
    /// 支援 Tim.Dto.Attribute.DataAttribute 和 DBColNameAttribute
    /// </summary>
    public static class LegacyAttributeAdapter
    {
        /// <summary>
        /// 檢查是否為主鍵 (支援 [Key] 或 [Data("PK")])
        /// </summary>
        public static bool IsKey(PropertyInfo prop)
        {
            // 檢查 BaseOrm.KeyAttribute
            if (prop.GetCustomAttribute<KeyAttribute>() != null)
                return true;

            // 檢查 Tim.Dto.Attribute.DataAttribute("PK")
            var dataAttr = GetLegacyDataAttribute(prop);
            if (dataAttr != null)
            {
                var noteValue = GetDataAttributeNote(dataAttr);
                return string.Equals(noteValue, "PK", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// 檢查是否應忽略 (支援 [Ignore] 或 [Data("None")])
        /// </summary>
        public static bool IsIgnored(PropertyInfo prop)
        {
            // 檢查 BaseOrm.IgnoreAttribute
            if (prop.GetCustomAttribute<IgnoreAttribute>() != null)
                return true;

            // 檢查 Tim.Dto.Attribute.DataAttribute("None")
            var dataAttr = GetLegacyDataAttribute(prop);
            if (dataAttr != null)
            {
                var noteValue = GetDataAttributeNote(dataAttr);
                return string.Equals(noteValue, "None", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// 取得欄位名稱 (支援 [Column] 或 [DBColName])
        /// </summary>
        public static string GetColumnName(PropertyInfo prop)
        {
            // 檢查 BaseOrm.ColumnAttribute
            var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (colAttr != null)
                return colAttr.Name;

            // 檢查 Tim.Dto.Attribute.DBColNameAttribute
            var dbColAttr = GetLegacyDBColNameAttribute(prop);
            if (dbColAttr != null)
            {
                var colName = GetDBColNameAttributeValue(dbColAttr);
                if (!string.IsNullOrEmpty(colName))
                    return colName;
            }

            return prop.Name;
        }

        /// <summary>
        /// 取得舊版 DataAttribute (透過反射，避免直接參考)
        /// </summary>
        private static object GetLegacyDataAttribute(PropertyInfo prop)
        {
            foreach (var attr in prop.GetCustomAttributes(false))
            {
                var typeName = attr.GetType().FullName;
                if (typeName != null && typeName.Contains("DataAttribute"))
                    return attr;
            }
            return null;
        }

        /// <summary>
        /// 取得 DataAttribute 的 columnNote 值
        /// </summary>
        private static string GetDataAttributeNote(object dataAttr)
        {
            if (dataAttr == null) return null;
            var noteProp = dataAttr.GetType().GetProperty("columnNote");
            if (noteProp != null)
                return noteProp.GetValue(dataAttr, null) as string;
            return null;
        }

        /// <summary>
        /// 取得舊版 DBColNameAttribute (透過反射)
        /// </summary>
        private static object GetLegacyDBColNameAttribute(PropertyInfo prop)
        {
            foreach (var attr in prop.GetCustomAttributes(false))
            {
                var typeName = attr.GetType().FullName;
                if (typeName != null && typeName.Contains("DBColNameAttribute"))
                    return attr;
            }
            return null;
        }

        /// <summary>
        /// 取得 DBColNameAttribute 的 dbColumnName 值
        /// </summary>
        private static string GetDBColNameAttributeValue(object dbColAttr)
        {
            if (dbColAttr == null) return null;
            var colProp = dbColAttr.GetType().GetProperty("dbColumnName");
            if (colProp != null)
                return colProp.GetValue(dbColAttr, null) as string;
            return null;
        }
    }

    #endregion

    #region Core Database

    /// <summary>
    /// 微型資料庫操作類別
    /// </summary>
    public class BaseDb : IDisposable
    {
        private readonly string _connectionString;
        private readonly DbProviderFactory _factory;
        private readonly DatabaseType _dbType;
        private DbConnection _connection;
        private DbTransaction _transaction;

        /// <summary>
        /// 資料庫類型
        /// </summary>
        public DatabaseType DbType
        {
            get { return _dbType; }
        }

        #region Constructors & Factory Methods

        /// <summary>
        /// 建立 SQL Server 資料庫連線 (預設)
        /// </summary>
        public BaseDb(string connectionString)
            : this(connectionString, SqlClientFactory.Instance, DatabaseType.SqlServer) { }

        /// <summary>
        /// 建立資料庫連線 (自動偵測類型)
        /// </summary>
        public BaseDb(string connectionString, DbProviderFactory factory)
            : this(connectionString, factory, DetectDbType(factory)) { }

        /// <summary>
        /// 建立資料庫連線 (明確指定類型)
        /// </summary>
        public BaseDb(string connectionString, DbProviderFactory factory, DatabaseType dbType)
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
            return new BaseDb(connectionString, SqlClientFactory.Instance, DatabaseType.SqlServer);
        }

        /// <summary>
        /// 建立 SQLite 連線
        /// </summary>
        public static BaseDb UseSqlite(string connectionString)
        {
            var factory = GetProviderFactory("System.Data.SQLite.SQLiteFactory, System.Data.SQLite");
            if (factory == null)
                factory = GetProviderFactory("Microsoft.Data.Sqlite.SqliteFactory, Microsoft.Data.Sqlite");
            if (factory == null)
                throw new InvalidOperationException("找不到 SQLite Provider，請安裝 System.Data.SQLite 或 Microsoft.Data.Sqlite");
            return new BaseDb(connectionString, factory, DatabaseType.SQLite);
        }

        /// <summary>
        /// 建立 MySQL 連線
        /// </summary>
        public static BaseDb UseMySql(string connectionString)
        {
            var factory = GetProviderFactory("MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data");
            if (factory == null)
                throw new InvalidOperationException("找不到 MySQL Provider，請安裝 MySql.Data");
            return new BaseDb(connectionString, factory, DatabaseType.MySql);
        }

        /// <summary>
        /// 建立 PostgreSQL 連線
        /// </summary>
        public static BaseDb UsePostgreSql(string connectionString)
        {
            var factory = GetProviderFactory("Npgsql.NpgsqlFactory, Npgsql");
            if (factory == null)
                throw new InvalidOperationException("找不到 PostgreSQL Provider，請安裝 Npgsql");
            return new BaseDb(connectionString, factory, DatabaseType.PostgreSql);
        }

        private static DbProviderFactory GetProviderFactory(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type == null) return null;
            var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            return field != null ? field.GetValue(null) as DbProviderFactory : null;
        }

        private static DatabaseType DetectDbType(DbProviderFactory factory)
        {
            var typeName = factory.GetType().FullName ?? "";
            if (typeName.Contains("Sqlite") || typeName.Contains("SQLite")) return DatabaseType.SQLite;
            if (typeName.Contains("SqlClient")) return DatabaseType.SqlServer;
            if (typeName.Contains("MySql")) return DatabaseType.MySql;
            if (typeName.Contains("Npgsql")) return DatabaseType.PostgreSql;
            return DatabaseType.Unknown;
        }

        #endregion

        #region Connection Management

        private DbConnection GetConnection()
        {
            if (_connection == null)
            {
                _connection = _factory.CreateConnection();
                _connection.ConnectionString = _connectionString;
            }

            if (_connection.State != ConnectionState.Open)
            {
                _connection.Open();
            }

            return _connection;
        }

        private DbCommand CreateCommand(string sql, object parameters = null)
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
            // 處理 Dictionary 類型
            var dict = parameters as IDictionary<string, object>;
            if (dict != null)
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
                param.Value = prop.GetValue(parameters, null) ?? DBNull.Value;
                cmd.Parameters.Add(param);
            }
        }

        private string GetParameterPrefix()
        {
            return _dbType == DatabaseType.MySql ? "?" : "@";
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// 查詢多筆資料
        /// </summary>
        public List<T> Query<T>(string sql, object parameters = null) where T : new()
        {
            using (var cmd = CreateCommand(NormalizeParameterNames(sql), parameters))
            using (var reader = cmd.ExecuteReader())
            {
                return MapToList<T>(reader);
            }
        }

        /// <summary>
        /// 查詢單筆資料
        /// </summary>
        public T QueryFirst<T>(string sql, object parameters = null) where T : class, new()
        {
            using (var cmd = CreateCommand(NormalizeParameterNames(sql), parameters))
            using (var reader = cmd.ExecuteReader())
            {
                return reader.Read() ? MapToObject<T>(reader) : null;
            }
        }

        /// <summary>
        /// 查詢單一值
        /// </summary>
        public T Scalar<T>(string sql, object parameters = null)
        {
            using (var cmd = CreateCommand(NormalizeParameterNames(sql), parameters))
            {
                var result = cmd.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    return default(T);
                return (T)Convert.ChangeType(result, typeof(T));
            }
        }

        /// <summary>
        /// 執行非查詢命令
        /// </summary>
        public int Execute(string sql, object parameters = null)
        {
            using (var cmd = CreateCommand(NormalizeParameterNames(sql), parameters))
            {
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 將 @param 格式轉換為對應資料庫的參數格式
        /// </summary>
        private string NormalizeParameterNames(string sql)
        {
            if (_dbType == DatabaseType.MySql)
            {
                // MySQL 使用 ? 作為參數前綴
                return Regex.Replace(sql, @"@(\w+)", "?$1",
                    RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            return sql;
        }

        #endregion

        #region CRUD Operations

        /// <summary>
        /// 依主鍵取得資料
        /// </summary>
        public T Get<T>(object id) where T : class, new()
        {
            var meta = TypeMeta.Get<T>();
            var sql = string.Format("SELECT * FROM {0} WHERE {1} = @Id",
                QuoteIdentifier(meta.TableName), QuoteIdentifier(meta.KeyColumn));
            return QueryFirst<T>(sql, new { Id = id });
        }

        /// <summary>
        /// 取得所有資料
        /// </summary>
        public List<T> GetAll<T>() where T : new()
        {
            var meta = TypeMeta.Get<T>();
            return Query<T>(string.Format("SELECT * FROM {0}", QuoteIdentifier(meta.TableName)));
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
            if (_dbType == DatabaseType.PostgreSql && meta.HasAutoIncrementKey)
            {
                sql = string.Format("INSERT INTO {0} ({1}) VALUES ({2}) RETURNING {3}",
                    QuoteIdentifier(meta.TableName), columns, values, meta.KeyColumn);
                using (var cmd = CreateCommand(NormalizeParameterNames(sql)))
                {
                    AddEntityParameters(cmd, entity, meta, true);
                    var result = cmd.ExecuteScalar();
                    return Convert.ToInt64(result);
                }
            }
            else
            {
                sql = string.Format("INSERT INTO {0} ({1}) VALUES ({2})",
                    QuoteIdentifier(meta.TableName), columns, values);
                using (var cmd = CreateCommand(NormalizeParameterNames(sql)))
                {
                    AddEntityParameters(cmd, entity, meta, true);
                    cmd.ExecuteNonQuery();

                    // 取得自動產生的 ID
                    cmd.CommandText = GetLastInsertIdSql();
                    cmd.Parameters.Clear();
                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt64(result) : 0;
                }
            }
        }

        private string GetLastInsertIdSql()
        {
            switch (_dbType)
            {
                case DatabaseType.SQLite: return "SELECT last_insert_rowid()";
                case DatabaseType.SqlServer: return "SELECT SCOPE_IDENTITY()";
                case DatabaseType.MySql: return "SELECT LAST_INSERT_ID()";
                case DatabaseType.PostgreSql: return "SELECT lastval()";
                default: return "SELECT @@IDENTITY";
            }
        }

        /// <summary>
        /// 更新資料
        /// </summary>
        public int Update<T>(T entity) where T : class
        {
            var meta = TypeMeta.Get<T>();
            var setClause = meta.UpdateSetClause;

            var sql = string.Format("UPDATE {0} SET {1} WHERE {2} = @{3}",
                QuoteIdentifier(meta.TableName), setClause, QuoteIdentifier(meta.KeyColumn), meta.KeyProperty);

            using (var cmd = CreateCommand(NormalizeParameterNames(sql)))
            {
                AddEntityParameters(cmd, entity, meta, false);
                return cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 刪除資料
        /// </summary>
        public int Delete<T>(object id)
        {
            var meta = TypeMeta.Get<T>();
            var sql = string.Format("DELETE FROM {0} WHERE {1} = @Id",
                QuoteIdentifier(meta.TableName), QuoteIdentifier(meta.KeyColumn));
            return Execute(sql, new { Id = id });
        }

        /// <summary>
        /// 刪除實體
        /// </summary>
        public int Delete<T>(T entity) where T : class
        {
            var meta = TypeMeta.Get<T>();
            var keyValue = meta.GetKeyValue(entity);
            return Delete<T>(keyValue);
        }

        private void AddEntityParameters<T>(DbCommand cmd, T entity, TypeMeta meta, bool excludeKey) where T : class
        {
            foreach (var prop in meta.Properties)
            {
                if (excludeKey && prop.IsKey && prop.AutoIncrement)
                    continue;

                var param = cmd.CreateParameter();
                param.ParameterName = GetParameterPrefix() + prop.PropertyName;
                param.Value = prop.Property.GetValue(entity, null) ?? DBNull.Value;
                cmd.Parameters.Add(param);
            }
        }

        /// <summary>
        /// 識別符號引用 (處理保留字)
        /// </summary>
        private string QuoteIdentifier(string name)
        {
            if (!IsValidIdentifier(name))
            {
                throw new ArgumentException(string.Format("Invalid identifier: {0}", name));
            }

            switch (_dbType)
            {
                case DatabaseType.MySql: return string.Format("`{0}`", EscapeBacktick(name));
                case DatabaseType.PostgreSql: return string.Format("\"{0}\"", EscapeDoubleQuote(name));
                case DatabaseType.SqlServer: return string.Format("[{0}]", EscapeBracket(name));
                default: return string.Format("\"{0}\"", EscapeDoubleQuote(name));
            }
        }

        private static bool IsValidIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128)
                return false;

            if (!char.IsLetter(name[0]) && name[0] != '_')
                return false;

            foreach (var c in name)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }

            return true;
        }

        private static string EscapeBacktick(string value) { return value.Replace("`", "``"); }
        private static string EscapeDoubleQuote(string value) { return value.Replace("\"", "\"\""); }
        private static string EscapeBracket(string value) { return value.Replace("]", "]]"); }

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
            if (_transaction != null)
            {
                _transaction.Commit();
                _transaction.Dispose();
                _transaction = null;
            }
        }

        /// <summary>
        /// 回滾交易
        /// </summary>
        public void Rollback()
        {
            if (_transaction != null)
            {
                _transaction.Rollback();
                _transaction.Dispose();
                _transaction = null;
            }
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

        private T MapToObject<T>(DbDataReader reader, TypeMeta meta = null) where T : new()
        {
            if (meta == null) meta = TypeMeta.Get<T>();
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

                    if (targetType == typeof(Guid) && value is string)
                    {
                        value = Guid.Parse((string)value);
                    }
                    else if (targetType == typeof(DateTime) && value is string)
                    {
                        value = DateTime.Parse((string)value);
                    }
                    else if (targetType.IsEnum)
                    {
                        value = Enum.ToObject(targetType, value);
                    }
                    else if (targetType != value.GetType())
                    {
                        value = Convert.ChangeType(value, targetType);
                    }

                    prop.Property.SetValue(obj, value, null);
                }
            }

            return obj;
        }

        #endregion

        public void Dispose()
        {
            if (_transaction != null)
            {
                _transaction.Dispose();
                _transaction = null;
            }
            if (_connection != null)
            {
                _connection.Dispose();
                _connection = null;
            }
        }
    }

    #endregion

    #region Type Metadata

    /// <summary>
    /// 類型元資料 (快取)
    /// </summary>
    internal class TypeMeta
    {
        private static readonly ConcurrentDictionary<Type, TypeMeta> _cache = new ConcurrentDictionary<Type, TypeMeta>();

        public string TableName { get; private set; }
        public string KeyColumn { get; private set; }
        public string KeyProperty { get; private set; }
        public bool HasAutoIncrementKey { get; private set; }
        public List<PropertyMeta> Properties { get; private set; }

        public string InsertColumns { get; private set; }
        public string InsertValues { get; private set; }
        public string UpdateSetClause { get; private set; }

        private TypeMeta(Type type)
        {
            // 取得資料表名稱
            var tableAttr = type.GetCustomAttribute<TableAttribute>();
            TableName = tableAttr != null ? tableAttr.Name : type.Name + "s";

            // 取得所有屬性
            Properties = new List<PropertyMeta>();
            PropertyMeta keyProp = null;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // 檢查是否忽略 (支援新舊兩種 Attribute)
                if (LegacyAttributeAdapter.IsIgnored(prop))
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

            KeyColumn = keyProp != null ? keyProp.ColumnName : "Id";
            KeyProperty = keyProp != null ? keyProp.PropertyName : "Id";
            HasAutoIncrementKey = keyProp != null && keyProp.AutoIncrement;

            // 建立 SQL 片段
            var insertCols = Properties.Where(p => !(p.IsKey && p.AutoIncrement)).Select(p => p.ColumnName);
            var insertVals = Properties.Where(p => !(p.IsKey && p.AutoIncrement)).Select(p => "@" + p.PropertyName);
            var updateSets = Properties.Where(p => !p.IsKey).Select(p => string.Format("{0} = @{1}", p.ColumnName, p.PropertyName));

            InsertColumns = string.Join(", ", insertCols);
            InsertValues = string.Join(", ", insertVals);
            UpdateSetClause = string.Join(", ", updateSets);
        }

        public static TypeMeta Get<T>()
        {
            return Get(typeof(T));
        }

        public static TypeMeta Get(Type type)
        {
            return _cache.GetOrAdd(type, t => new TypeMeta(t));
        }

        public PropertyMeta GetPropertyByColumn(string columnName)
        {
            return Properties.FirstOrDefault(p =>
                p.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase));
        }

        public object GetKeyValue<T>(T entity) where T : class
        {
            var keyProp = Properties.FirstOrDefault(p => p.IsKey);
            return keyProp != null ? keyProp.Property.GetValue(entity, null) : null;
        }
    }

    /// <summary>
    /// 屬性元資料
    /// </summary>
    internal class PropertyMeta
    {
        public PropertyInfo Property { get; private set; }
        public string PropertyName { get; private set; }
        public string ColumnName { get; private set; }
        public bool IsKey { get; set; }
        public bool AutoIncrement { get; private set; }
        public bool IsRequired { get; private set; }
        public int MaxLength { get; private set; }

        public PropertyMeta(PropertyInfo prop)
        {
            Property = prop;
            PropertyName = prop.Name;

            // 使用適配器取得欄位名稱 (支援新舊 Attribute)
            ColumnName = LegacyAttributeAdapter.GetColumnName(prop);

            // 使用適配器檢查是否為主鍵 (支援新舊 Attribute)
            IsKey = LegacyAttributeAdapter.IsKey(prop);

            // 取得 AutoIncrement (僅 BaseOrm.KeyAttribute 支援)
            var keyAttr = prop.GetCustomAttribute<KeyAttribute>();
            AutoIncrement = keyAttr != null ? keyAttr.AutoIncrement : true;

            IsRequired = prop.GetCustomAttribute<RequiredAttribute>() != null;
            var maxLenAttr = prop.GetCustomAttribute<MaxLengthAttribute>();
            MaxLength = maxLenAttr != null ? maxLenAttr.Length : 0;
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
        /// 分頁查詢
        /// </summary>
        public static PagedResult<T> QueryPaged<T>(this BaseDb db, string sql, int page, int pageSize, object parameters = null) where T : new()
        {
            var countSql = string.Format("SELECT COUNT(*) FROM ({0}) AS CountQuery", sql);
            var totalCount = db.Scalar<int>(countSql, parameters);

            // 不同資料庫的分頁語法
            string pagedSql;
            switch (db.DbType)
            {
                case DatabaseType.SqlServer:
                    pagedSql = string.Format("{0} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", sql);
                    break;
                default:
                    pagedSql = string.Format("{0} LIMIT @PageSize OFFSET @Offset", sql);
                    break;
            }

            var offset = (page - 1) * pageSize;

            // 合併參數
            var pagedParams = new Dictionary<string, object>
            {
                { "PageSize", pageSize },
                { "Offset", offset }
            };

            if (parameters != null)
            {
                foreach (var prop in parameters.GetType().GetProperties())
                {
                    pagedParams[prop.Name] = prop.GetValue(parameters, null);
                }
            }

            var items = db.Query<T>(pagedSql, pagedParams);

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
        public List<T> Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }

        public bool HasPrevious { get { return Page > 1; } }
        public bool HasNext { get { return Page < TotalPages; } }

        public PagedResult()
        {
            Items = new List<T>();
        }
    }

    #endregion
}
