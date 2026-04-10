using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SpaApi.Data;
using System.Text;
using Xunit;

namespace SpaApi.Template.Tests;

public sealed class CommerceApiTests
{
    [Fact]
    public async Task Shop_categories_endpoint_returns_seeded_categories()
    {
        await using var factory = new CommerceAppFactory();
        using var client = factory.CreateClient();

        var categories = await client.GetFromJsonAsync<List<CategoryPayload>>("/api/shop/categories");

        categories.Should().NotBeNull();
        categories!.Should().NotBeEmpty();
        categories.Should().Contain(category => category.Name == "Digital Goods");
        categories.Should().Contain(category => category.Name == "Member Services");
    }

    [Fact]
    public async Task Member_can_register_login_place_order_and_see_history()
    {
        await using var factory = new CommerceAppFactory();
        using var client = factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Member One",
            email = "member@example.com",
            password = "MemberPass123!"
        });

        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created, registerBody);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "member@example.com",
            password = "MemberPass123!"
        });

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, loginBody);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<LoginPayload>();
        loginPayload.Should().NotBeNull();
        loginPayload!.AccessToken.Should().NotBeNullOrWhiteSpace();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginPayload.AccessToken);

        var products = await client.GetFromJsonAsync<List<ProductPayload>>("/api/shop/products");
        products.Should().NotBeNull();
        var productList = products!;
        productList.Should().NotBeEmpty();

        var createOrderResponse = await client.PostAsJsonAsync("/api/shop/orders", new
        {
            productId = productList[0].Id,
            quantity = 2,
            shippingAddress = "Taipei Test Address 1",
            note = "Please ring the bell."
        });

        createOrderResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderPayload = await createOrderResponse.Content.ReadFromJsonAsync<OrderPayload>();
        orderPayload.Should().NotBeNull();
        orderPayload!.OrderNumber.Should().NotBeNullOrWhiteSpace();
        orderPayload.TotalAmount.Should().BeGreaterThan(0);
        orderPayload.Items.Should().ContainSingle();
        orderPayload.Items[0].ProductName.Should().NotBeNullOrWhiteSpace();

        var orderHistory = await client.GetFromJsonAsync<List<OrderPayload>>("/api/shop/orders");
        orderHistory.Should().NotBeNull();
        var orderHistoryList = orderHistory!;
        orderHistoryList.Should().ContainSingle(x => x.OrderNumber == orderPayload.OrderNumber);
        orderHistoryList[0].Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Admin_can_create_update_and_delete_products()
    {
        await using var factory = new CommerceAppFactory();
        using var client = factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@example.com",
            password = CommerceAppFactory.AdminPassword
        });

        var loginBody = await loginResponse.Content.ReadAsStringAsync();
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, loginBody);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<LoginPayload>();
        loginPayload.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginPayload!.AccessToken);

        var createdResponse = await client.PostAsJsonAsync("/api/admin/products", new
        {
            name = "Proof Product",
            description = "Created from test",
            price = 1280m,
            stock = 12,
            categoryId = 1,
            images = "",
            status = "active"
        });

        var createdBody = await createdResponse.Content.ReadAsStringAsync();
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created, createdBody);
        var created = await createdResponse.Content.ReadFromJsonAsync<ProductPayload>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Proof Product");

        var updatedResponse = await client.PutAsJsonAsync($"/api/admin/products/{created.Id}", new
        {
            name = "Proof Product Updated",
            price = 1440m,
            stock = 8
        });

        updatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<ProductPayload>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Proof Product Updated");
        updated.Price.Should().Be(1440m);

        var list = await client.GetFromJsonAsync<List<ProductPayload>>("/api/admin/products");
        list.Should().NotBeNull();
        list!.Should().Contain(x => x.Id == created.Id && x.Name == "Proof Product Updated");

        var deleteResponse = await client.DeleteAsync($"/api/admin/products/{created.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterDelete = await client.GetAsync($"/api/admin/products/{created.Id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

internal sealed class CommerceAppFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    public const string AdminPassword = "AdminPass123!";
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"spa-commerce-proof-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}",
                ["SeedData:AdminEmail"] = "admin@example.com",
                ["SeedData:AdminName"] = "Admin",
                ["SeedData:AdminPassword"] = AdminPassword,
                ["Jwt:Key"] = "ProofJwtKey_012345678901234567890123456789",
                ["Jwt:Issuer"] = "SpaApi.Template.Tests"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDb>();
            services.AddSingleton(new AppDb($"Data Source={_dbPath}"));
            services.PostConfigureAll<JwtBearerOptions>(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = "SpaApi.Template.Tests",
                    ValidAudience = "SpaApi.Template.Tests",
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes("ProofJwtKey_012345678901234567890123456789")),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
        });
    }

    public new async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
        TryDelete(_dbPath);
        TryDelete($"{_dbPath}-shm");
        TryDelete($"{_dbPath}-wal");
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

internal sealed record LoginPayload(string AccessToken, string RefreshToken, UserPayload User);
internal sealed record UserPayload(int Id, string Name, string Email, string Role);
internal sealed record ProductPayload(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int Stock,
    int CategoryId,
    string Images,
    string Status,
    DateTime CreatedAt);
internal sealed record CategoryPayload(
    int Id,
    string Name,
    int ParentId,
    int SortOrder,
    string Icon,
    string Status,
    DateTime CreatedAt);
internal sealed record OrderPayload(
    int Id,
    int UserId,
    string OrderNumber,
    decimal TotalAmount,
    string Status,
    string ShippingAddress,
    string Note,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemPayload> Items);

internal sealed record OrderItemPayload(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal);
