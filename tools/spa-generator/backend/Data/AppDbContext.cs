using Microsoft.EntityFrameworkCore;
using SpaGenerator.Models;

namespace SpaGenerator.Data;

/**
 * 應用程式資料庫上下文
 * 使用 SQLite 作為預設資料庫
 */
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User 配置
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(50).HasDefaultValue("user");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("active");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
        });

        // 種子資料改為在 DbInitializer.Initialize() 中處理
        // 這樣可以使用動態密碼雜湊，更適合生產環境
    }
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
