using BaseOrm;

namespace BrokerCore.Data;

/// <summary>
/// Broker 資料庫封裝 —— BaseDb 的薄包裝
/// 透過 BaseOrm 透明支援 SQLite / PostgreSQL / SQL Server / MySQL
/// </summary>
public class BrokerDb : IDisposable
{
    private readonly BaseDb _db;

    /// <summary>SQLite（開發/單節點）</summary>
    public BrokerDb(string connectionString)
    {
        _db = new BaseDb(connectionString);
    }

    /// <summary>指定資料庫類型（叢集化部署）</summary>
    public BrokerDb(BaseDb db)
    {
        _db = db;
    }

    /// <summary>內部 BaseDb 實例（供進階操作）</summary>
    public BaseDb Db => _db;

    // ── 轉發 BaseDb 核心方法 ──

    public void EnsureTable<T>() => _db.EnsureTable<T>();

    public T? Get<T>(object id) where T : class, new() => _db.Get<T>(id);

    public List<T> GetAll<T>() where T : new() => _db.GetAll<T>();

    public long Insert<T>(T entity) where T : class => _db.Insert(entity);

    public int Update<T>(T entity) where T : class => _db.Update(entity);

    public int Delete<T>(object id) => _db.Delete<T>(id);

    public List<T> Query<T>(string sql, object? parameters = null) where T : new()
        => _db.Query<T>(sql, parameters);

    public T? QueryFirst<T>(string sql, object? parameters = null) where T : class, new()
        => _db.QueryFirst<T>(sql, parameters);

    public T? Scalar<T>(string sql, object? parameters = null)
        => _db.Scalar<T>(sql, parameters);

    public int Execute(string sql, object? parameters = null)
        => _db.Execute(sql, parameters);

    public void BeginTransaction() => _db.BeginTransaction();
    public void Commit() => _db.Commit();
    public void Rollback() => _db.Rollback();

    public void InTransaction(Action action) => _db.InTransaction(action);
    public T InTransaction<T>(Func<T> action) => _db.InTransaction(action);

    public void Dispose() => _db.Dispose();

    // ── 工廠方法 ──

    public static BrokerDb UseSqlite(string connectionString)
        => new(connectionString);

    public static BrokerDb UsePostgreSql(string connectionString)
        => new(BaseDb.UsePostgreSql(connectionString));

    public static BrokerDb UseSqlServer(string connectionString)
        => new(BaseDb.UseSqlServer(connectionString));
}
