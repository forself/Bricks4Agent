using SpaApi.Generated;
using SpaApi.Models;

namespace SpaApi.Data;

public static class DbInitializer
{
    public static void Initialize(
        AppDb db,
        IConfiguration configuration,
        DefinitionBackendModel? backendDefinition = null)
    {
        db.EnsureCreated();
        var definition = backendDefinition ?? new DefinitionBackendModel(
            Tier: "N2",
            Template: "base_n2_commerce",
            Orm: "BaseOrm",
            Database: "sqlite",
            AuthenticationEnabled: true,
            RequireAdminSeed: true,
            SeedSampleProducts: true,
            SeedSampleCategories: true,
            SecurityBaseline: "authentication",
            Entities: ["User", "Category", "Product", "Order", "OrderItem"],
            Modules: ["authentication", "commerce"]);

        SeedAdminUser(db, configuration, definition);
        SeedCommerceCatalog(db, definition);
    }

    private static void SeedAdminUser(
        AppDb db,
        IConfiguration configuration,
        DefinitionBackendModel definition)
    {
        if (!definition.RequireAdminSeed)
        {
            return;
        }

        if (db.GetUserByEmail(configuration["SeedData:AdminEmail"] ?? "admin@example.com") != null)
        {
            return;
        }

        var adminEmail = configuration["SeedData:AdminEmail"] ?? "admin@example.com";
        var adminPassword = configuration["SeedData:AdminPassword"];
        var adminName = configuration["SeedData:AdminName"] ?? "Admin";

        if (string.IsNullOrEmpty(adminPassword))
        {
            adminPassword = $"Dev_{Guid.NewGuid():N}"[..20];
            Console.WriteLine($"[DbInitializer] generated development admin password: {adminPassword}");
        }

        if (adminPassword.Length < 8)
        {
            throw new InvalidOperationException(
                "SeedData:AdminPassword must be at least 8 characters.");
        }

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
    }

    private static void SeedCommerceCatalog(AppDb db, DefinitionBackendModel definition)
    {
        if (!definition.Modules.Contains("commerce", StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        if (!definition.SeedSampleProducts && !definition.SeedSampleCategories)
        {
            return;
        }

        var digitalGoodsCategoryId = EnsureCategory(
            db,
            name: "Digital Goods",
            sortOrder: 1,
            icon: "package",
            enabled: definition.SeedSampleCategories || definition.SeedSampleProducts);
        var memberServicesCategoryId = EnsureCategory(
            db,
            name: "Member Services",
            sortOrder: 2,
            icon: "users",
            enabled: definition.SeedSampleCategories || definition.SeedSampleProducts);

        if (!definition.SeedSampleProducts)
        {
            return;
        }

        if (db.GetProductCount() > 0)
        {
            return;
        }

        db.CreateProduct(new Product
        {
            Name = "Starter Membership",
            Description = "Starter member plan for proof commerce flow.",
            Price = 499m,
            Stock = 100,
            CategoryId = memberServicesCategoryId,
            Images = "",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });

        db.CreateProduct(new Product
        {
            Name = "Commerce Toolkit",
            Description = "Digital toolkit seeded for template proof.",
            Price = 1280m,
            Stock = 50,
            CategoryId = digitalGoodsCategoryId,
            Images = "",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static int EnsureCategory(
        AppDb db,
        string name,
        int sortOrder,
        string icon,
        bool enabled)
    {
        var existing = db.GetCategoryByName(name);
        if (existing != null)
        {
            return existing.Id;
        }

        if (!enabled)
        {
            throw new InvalidOperationException($"Required category '{name}' was not found for seeded commerce products.");
        }

        var id = db.CreateCategory(new Category
        {
            Name = name,
            ParentId = 0,
            SortOrder = sortOrder,
            Icon = icon,
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });

        return (int)id;
    }
}
