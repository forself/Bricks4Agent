using BaseOrm;
using SpaGenerator.Models;

namespace SpaGenerator.Data;

/**
 * 應用程式資料庫
 * 使用 BaseOrm 輕量級 ORM
 */
public class AppDb : BaseDb
{
    public AppDb(string connectionString) : base(connectionString) { }

    /// <summary>
    /// 初始化資料表結構
    /// </summary>
    public void EnsureCreated()
    {
        // 建立 Users 資料表
        Execute(@"
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Email TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL DEFAULT 'user',
                Status TEXT NOT NULL DEFAULT 'active',
                Department TEXT,
                Phone TEXT,
                CreatedAt TEXT NOT NULL,
                LastLoginAt TEXT
            )
        ");

        // 建立 Email 唯一索引
        Execute(@"
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email)
        ");

        // --- BRICKS:TABLE_SQL ---
    }

    #region User Operations

    public List<User> GetAllUsers()
    {
        return Query<User>("SELECT * FROM Users ORDER BY CreatedAt DESC");
    }

    public User? GetUserById(int id)
    {
        return QueryFirst<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = id });
    }

    public User? GetUserByEmail(string email)
    {
        return QueryFirst<User>("SELECT * FROM Users WHERE Email = @Email", new { Email = email.ToLowerInvariant() });
    }

    public bool EmailExists(string email)
    {
        var count = Scalar<int>("SELECT COUNT(*) FROM Users WHERE Email = @Email", new { Email = email.ToLowerInvariant() });
        return count > 0;
    }

    public int GetUserCount()
    {
        return Scalar<int>("SELECT COUNT(*) FROM Users");
    }

    public long CreateUser(User user)
    {
        return Insert(user);
    }

    public int UpdateUser(User user)
    {
        return Update(user);
    }

    public int DeleteUser(int id)
    {
        return Delete<User>(id);
    }

    public void UpdateLastLogin(int userId)
    {
        Execute("UPDATE Users SET LastLoginAt = @Now WHERE Id = @Id",
            new { Now = DateTime.UtcNow.ToString("o"), Id = userId });
    }

    // --- BRICKS:DB_METHODS ---

    #endregion
}

/**
 * 密碼雜湊輔助類別
 * 使用 PBKDF2 with HMAC-SHA256 (100,000 iterations)
 * 每個密碼使用獨立的隨機 Salt
 */
public static class BCryptHelper
{
    private const int SaltSize = 16; // 128 bits
    private const int HashSize = 32; // 256 bits
    private const int Iterations = 100000; // OWASP 建議值

    public static string HashPassword(string password)
    {
        // 產生隨機 Salt
        var salt = new byte[SaltSize];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // 使用 PBKDF2 雜湊
        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var hash = pbkdf2.GetBytes(HashSize);

        // 格式: iterations.salt.hash (Base64)
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3) return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var hash = Convert.FromBase64String(parts[2]);

            using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password,
                salt,
                iterations,
                System.Security.Cryptography.HashAlgorithmName.SHA256);

            var computedHash = pbkdf2.GetBytes(hash.Length);

            // 常數時間比較，防止計時攻擊
            return CryptographicEquals(hash, computedHash);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 常數時間比較，防止計時攻擊
    /// </summary>
    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;

        var result = 0;
        for (int i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
