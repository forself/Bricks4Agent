using SpaApi.Models;

namespace SpaApi.Data;

/**
 * 資料庫初始化器
 * 負責在應用程式啟動時建立種子資料
 */
public static class DbInitializer
{
    /// <summary>
    /// 初始化資料庫並建立種子資料
    /// </summary>
    public static void Initialize(AppDb db, IConfiguration configuration)
    {
        // 確保資料表已建立
        db.EnsureCreated();

        // 如果已有使用者，跳過種子資料
        if (db.GetUserCount() > 0)
        {
            return;
        }

        // 從設定讀取初始管理員資訊，或使用預設值
        var adminEmail = configuration["SeedData:AdminEmail"] ?? "admin@example.com";
        var adminPassword = configuration["SeedData:AdminPassword"] ?? "Admin@123";
        var adminName = configuration["SeedData:AdminName"] ?? "Admin";

        // 驗證密碼強度
        if (adminPassword.Length < 8)
        {
            throw new InvalidOperationException(
                "初始管理員密碼長度必須至少 8 個字元。請在 appsettings.json 中設定 SeedData:AdminPassword");
        }

        // 建立管理員帳號
        var adminUser = new User
        {
            Name = adminName,
            Email = adminEmail.ToLowerInvariant(),
            PasswordHash = BCryptHelper.HashPassword(adminPassword),
            Role = "admin",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        };

        db.CreateUser(adminUser);

        Console.WriteLine($"[DbInitializer] 已建立初始管理員帳號: {adminEmail}");
        Console.WriteLine("[DbInitializer] 警告: 請在首次登入後立即更改預設密碼!");
    }
}
