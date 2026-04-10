using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SpaApi.Data;
using SpaApi.Generated;
using SpaApi.Models;
using Xunit;

namespace SpaApi.Template.Tests;

public sealed class DbInitializerTests : IDisposable
{
    private readonly List<string> _paths = [];

    [Fact]
    public void Does_not_seed_commerce_catalog_when_sample_flags_are_disabled()
    {
        var db = CreateDb();
        var configuration = CreateConfiguration();
        var definition = CreateDefinition(seedSampleProducts: false, seedSampleCategories: false);

        DbInitializer.Initialize(db, configuration, definition);

        db.GetUserCount().Should().Be(1);
        db.GetCategoryCount().Should().Be(0);
        db.GetProductCount().Should().Be(0);
    }

    [Fact]
    public void Seeds_products_using_category_lookup_instead_of_fixed_ids()
    {
        var db = CreateDb();
        var configuration = CreateConfiguration();

        db.EnsureCreated();
        db.CreateCategory(new Category
        {
            Name = "Existing Category",
            ParentId = 0,
            SortOrder = 0,
            Icon = "folder",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });
        db.CreateCategory(new Category
        {
            Name = "Digital Goods",
            ParentId = 0,
            SortOrder = 1,
            Icon = "package",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });
        db.CreateCategory(new Category
        {
            Name = "Member Services",
            ParentId = 0,
            SortOrder = 2,
            Icon = "users",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });

        var memberServices = db.GetCategoryByName("Member Services");
        var digitalGoods = db.GetCategoryByName("Digital Goods");

        memberServices.Should().NotBeNull();
        digitalGoods.Should().NotBeNull();
        memberServices!.Id.Should().NotBe(2); // old code hard-coded Starter Membership to category 2
        digitalGoods!.Id.Should().NotBe(1);

        var definition = CreateDefinition(seedSampleProducts: true, seedSampleCategories: false);

        DbInitializer.Initialize(db, configuration, definition);

        var products = db.GetAllProducts();
        products.Should().ContainSingle(product => product.Name == "Starter Membership" && product.CategoryId == memberServices.Id);
        products.Should().ContainSingle(product => product.Name == "Commerce Toolkit" && product.CategoryId == digitalGoods.Id);
    }

    public void Dispose()
    {
        foreach (var path in _paths)
        {
            TryDelete(path);
            TryDelete($"{path}-shm");
            TryDelete($"{path}-wal");
        }
    }

    private AppDb CreateDb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spa-commerce-proof-dbinit-{Guid.NewGuid():N}.db");
        _paths.Add(path);
        return new AppDb($"Data Source={path}");
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SeedData:AdminEmail"] = "admin@example.com",
                ["SeedData:AdminName"] = "Admin",
                ["SeedData:AdminPassword"] = "AdminPass123!"
            })
            .Build();
    }

    private static DefinitionBackendModel CreateDefinition(bool seedSampleProducts, bool seedSampleCategories)
    {
        return new DefinitionBackendModel(
            Tier: "N2",
            Template: "base_n2_commerce",
            Orm: "BaseOrm",
            Database: "sqlite",
            AuthenticationEnabled: true,
            RequireAdminSeed: true,
            SeedSampleProducts: seedSampleProducts,
            SeedSampleCategories: seedSampleCategories,
            SecurityBaseline: "authentication",
            Entities: ["User", "Category", "Product", "Order", "OrderItem"],
            Modules: ["authentication", "commerce"]);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
