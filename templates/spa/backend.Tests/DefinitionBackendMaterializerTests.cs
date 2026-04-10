using FluentAssertions;
using SpaApi.Generated;
using Xunit;

namespace SpaApi.Template.Tests;

public sealed class DefinitionBackendMaterializerTests
{
    [Fact]
    public void Materializes_N2_baseline_with_security_and_authentication()
    {
        const string json = """
        {
          "schema_version": 1,
          "enabled": true,
          "tier": "N2",
          "template": "base_n2_commerce",
          "persistence": {
            "enabled": true,
            "orm": "BaseOrm",
            "database": "sqlite"
          },
          "security": {
            "auth_required": true,
            "auth_mode": "local_jwt",
            "role_model": ["member", "admin"]
          },
          "entities": ["User", "Category", "Product", "Order", "OrderItem"],
          "modules": ["authentication", "commerce"],
          "seed": {
            "admin_account": true,
            "sample_products": true,
            "sample_categories": true
          }
        }
        """;

        var materializer = new DefinitionBackendMaterializer();

        var definition = materializer.Materialize(json);

        definition.Tier.Should().Be("N2");
        definition.Template.Should().Be("base_n2_commerce");
        definition.Orm.Should().Be("BaseOrm");
        definition.Database.Should().Be("sqlite");
        definition.AuthenticationEnabled.Should().BeTrue();
        definition.RequireAdminSeed.Should().BeTrue();
        definition.SeedSampleProducts.Should().BeTrue();
        definition.SeedSampleCategories.Should().BeTrue();
        definition.SecurityBaseline.Should().Be("authentication");
        definition.Entities.Should().Contain(["User", "Category", "Product", "Order", "OrderItem"]);
        definition.Modules.Should().ContainInOrder("authentication", "commerce");
    }

    [Fact]
    public void Throws_for_unsupported_tier()
    {
        const string json = """
        {
          "schema_version": 1,
          "enabled": true,
          "tier": "N4",
          "template": "unknown",
          "persistence": {
            "enabled": true,
            "orm": "BaseOrm",
            "database": "sqlite"
          },
          "security": {
            "auth_required": true,
            "auth_mode": "local_jwt",
            "role_model": ["member", "admin"]
          },
          "entities": [],
          "modules": [],
          "seed": {
            "admin_account": true,
            "sample_products": false,
            "sample_categories": false
          }
        }
        """;

        var materializer = new DefinitionBackendMaterializer();

        Action act = () => materializer.Materialize(json);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*N4*");
    }

    [Fact]
    public void Throws_for_authenticated_tier_without_required_authentication()
    {
        const string json = """
        {
          "schema_version": 1,
          "enabled": true,
          "tier": "N2",
          "template": "base_n2_commerce",
          "persistence": {
            "enabled": true,
            "orm": "BaseOrm",
            "database": "sqlite"
          },
          "security": {
            "auth_required": false,
            "auth_mode": "local_jwt",
            "role_model": ["member", "admin"]
          },
          "entities": ["User"],
          "modules": ["authentication"],
          "seed": {
            "admin_account": true,
            "sample_products": false,
            "sample_categories": false
          }
        }
        """;

        var materializer = new DefinitionBackendMaterializer();

        Action act = () => materializer.Materialize(json);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires security.auth_required to be true*");
    }
}
