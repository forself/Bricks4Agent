using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using SpaApi.Data;
using SpaApi.Generated;
using SpaApi.Models;
using SpaApi.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=spa_app.db";
builder.Services.AddSingleton(new AppDb(connectionString));

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("JWT key must be configured with at least 32 characters.");
    }

    jwtKey = "DevOnlyKey_DoNotUseInProduction_32chars!";
    builder.Configuration["Jwt:Key"] = jwtKey;
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "SpaApi";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("register", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromHours(1);
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 2;
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

var backendDefinition = LoadBackendDefinition(builder.Environment.ContentRootPath);
builder.Services.AddSingleton(backendDefinition);
builder.Services.AddSingleton(new DefinitionBackendModuleRegistry(backendDefinition));

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
        }
    });
});

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthService, AuthService>();

DefinitionTemplateGeneratedComposition.BeforeBuild(builder);
DefinitionTemplateGeneratedComposition.ConfigureServices(builder.Services, builder.Configuration);

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

var frontendRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "frontend"));
var packagesRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "packages"));

{
    var db = app.Services.GetRequiredService<AppDb>();
    var config = app.Services.GetRequiredService<IConfiguration>();
    var definition = app.Services.GetRequiredService<DefinitionBackendModel>();
    DbInitializer.Initialize(db, config, definition);
}

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");

    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler(appError =>
    {
        appError.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "Internal server error" });
        });
    });
}

app.UseHttpsRedirection();

if (Directory.Exists(frontendRoot))
{
    var frontendProvider = new PhysicalFileProvider(frontendRoot);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = frontendProvider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = frontendProvider
    });
}

if (Directory.Exists(packagesRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        RequestPath = "/packages",
        FileProvider = new PhysicalFileProvider(packagesRoot)
    });
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

DefinitionTemplateGeneratedComposition.ConfigureMiddleware(app);

app.MapGet("/health", (AppDb db) => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTime.UtcNow,
    database = "SQLite (BaseOrm)",
    users = db.GetUserCount(),
    products = db.GetProductCount()
})).WithName("HealthCheck");

app.MapPost("/api/auth/login", async (LoginRequest request, IAuthService authService) =>
{
    var validation = ValidateLoginRequest(request);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var result = await authService.LoginAsync(request.Email.Trim().ToLowerInvariant(), request.Password);
    return result == null ? Results.Unauthorized() : Results.Ok(result);
})
.WithName("Login")
.AllowAnonymous()
.RequireRateLimiting("login");

app.MapPost("/api/auth/register", async (RegisterRequest request, IAuthService authService) =>
{
    var validation = ValidateRegisterRequest(request);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var result = await authService.RegisterAsync(request with
    {
        Email = request.Email.Trim().ToLowerInvariant(),
        Name = request.Name.Trim()
    });

    return !result.Success
        ? Results.BadRequest(new { error = result.Message })
        : Results.Created($"/api/users/{result.UserId}", result);
})
.WithName("Register")
.AllowAnonymous()
.RequireRateLimiting("register");

app.MapGet("/api/auth/me", (ClaimsPrincipal user, AppDb db) =>
{
    if (!TryGetUserId(user, out var userId))
    {
        return Results.Unauthorized();
    }

    var currentUser = db.GetUserById(userId);
    return currentUser == null
        ? Results.Unauthorized()
        : Results.Ok(UserDto.FromEntity(currentUser));
})
.WithName("GetCurrentUser")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/users", async (ClaimsPrincipal user, IUserService userService) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    var users = await userService.GetAllAsync();
    return Results.Ok(users);
})
.WithName("GetUsers")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/users/{id:int}", async (int id, ClaimsPrincipal user, IUserService userService) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    if (id <= 0)
    {
        return Results.BadRequest(new { error = "Invalid user ID" });
    }

    var found = await userService.GetByIdAsync(id);
    return found == null
        ? Results.NotFound(new { error = "User not found" })
        : Results.Ok(found);
})
.WithName("GetUserById")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/shop/products", (AppDb db) =>
{
    var products = db.GetAllProducts(activeOnly: true)
        .Select(ToProductResponse)
        .ToList();
    return Results.Ok(products);
})
.WithName("GetShopProducts")
.AllowAnonymous()
.RequireRateLimiting("api");

app.MapGet("/api/shop/categories", (AppDb db) =>
{
    var categories = db.GetAllCategories()
        .Where(category => string.Equals(category.Status, "active", StringComparison.OrdinalIgnoreCase))
        .Select(category => new CategoryResponse(
            category.Id,
            category.Name,
            category.ParentId,
            category.SortOrder,
            category.Icon,
            category.Status,
            category.CreatedAt))
        .ToList();
    return Results.Ok(categories);
})
.WithName("GetShopCategories")
.AllowAnonymous()
.RequireRateLimiting("api");

app.MapGet("/api/shop/products/{id:int}", (int id, AppDb db) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "Invalid product ID" });
    }

    var product = db.GetProductById(id);
    return product == null || !string.Equals(product.Status, "active", StringComparison.OrdinalIgnoreCase)
        ? Results.NotFound(new { error = "Product not found" })
        : Results.Ok(ToProductResponse(product));
})
.WithName("GetShopProductById")
.AllowAnonymous()
.RequireRateLimiting("api");

app.MapPost("/api/shop/orders", (CreateShopOrderRequest request, ClaimsPrincipal user, AppDb db) =>
{
    if (!TryGetUserId(user, out var userId))
    {
        return Results.Unauthorized();
    }

    var validation = ValidateCreateShopOrderRequest(request, db);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var orderNumber = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
    var order = db.CreateOrderWithSingleItem(
        userId,
        request.ProductId,
        request.Quantity,
        request.ShippingAddress.Trim(),
        request.Note?.Trim() ?? string.Empty,
        orderNumber);

    return order == null
        ? Results.BadRequest(new { error = "Unable to place order." })
        : Results.Created($"/api/shop/orders/{order.Id}", ToOrderResponse(db, order));
})
.WithName("CreateShopOrder")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/shop/orders", (ClaimsPrincipal user, AppDb db) =>
{
    if (!TryGetUserId(user, out var userId))
    {
        return Results.Unauthorized();
    }

    var orders = db.GetOrdersByUser(userId)
        .Select(order => ToOrderResponse(db, order))
        .ToList();
    return Results.Ok(orders);
})
.WithName("GetShopOrders")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/admin/products", (ClaimsPrincipal user, AppDb db) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    var products = db.GetAllProducts(activeOnly: false)
        .Select(ToProductResponse)
        .ToList();
    return Results.Ok(products);
})
.WithName("GetAdminProducts")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/admin/products/{id:int}", (int id, ClaimsPrincipal user, AppDb db) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    if (id <= 0)
    {
        return Results.BadRequest(new { error = "Invalid product ID" });
    }

    var product = db.GetProductById(id);
    return product == null
        ? Results.NotFound(new { error = "Product not found" })
        : Results.Ok(ToProductResponse(product));
})
.WithName("GetAdminProductById")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapPost("/api/admin/products", (CreateProductRequest request, ClaimsPrincipal user, AppDb db) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    var validation = ValidateCreateProductRequest(request, db);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    var now = DateTime.UtcNow;
    var product = new Product
    {
        Name = request.Name.Trim(),
        Description = request.Description?.Trim() ?? string.Empty,
        Price = request.Price,
        Stock = request.Stock,
        CategoryId = request.CategoryId,
        Images = request.Images ?? string.Empty,
        Status = NormalizeStatus(request.Status),
        CreatedAt = now,
        UpdatedAt = now
    };

    product.Id = (int)db.CreateProduct(product);
    return Results.Created($"/api/admin/products/{product.Id}", ToProductResponse(product));
})
.WithName("CreateAdminProduct")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapPut("/api/admin/products/{id:int}", (int id, UpdateProductRequest request, ClaimsPrincipal user, AppDb db) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    if (id <= 0)
    {
        return Results.BadRequest(new { error = "Invalid product ID" });
    }

    var product = db.GetProductById(id);
    if (product == null)
    {
        return Results.NotFound(new { error = "Product not found" });
    }

    var validation = ValidateUpdateProductRequest(request, db);
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { error = validation.Error });
    }

    if (!string.IsNullOrWhiteSpace(request.Name))
    {
        product.Name = request.Name.Trim();
    }

    if (request.Description != null)
    {
        product.Description = request.Description.Trim();
    }

    if (request.Price.HasValue)
    {
        product.Price = request.Price.Value;
    }

    if (request.Stock.HasValue)
    {
        product.Stock = request.Stock.Value;
    }

    if (request.CategoryId.HasValue)
    {
        product.CategoryId = request.CategoryId.Value;
    }

    if (request.Images != null)
    {
        product.Images = request.Images;
    }

    if (request.Status != null)
    {
        product.Status = NormalizeStatus(request.Status);
    }

    product.UpdatedAt = DateTime.UtcNow;
    db.UpdateProduct(product);
    return Results.Ok(ToProductResponse(product));
})
.WithName("UpdateAdminProduct")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapDelete("/api/admin/products/{id:int}", (int id, ClaimsPrincipal user, AppDb db) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    if (id <= 0)
    {
        return Results.BadRequest(new { error = "Invalid product ID" });
    }

    var affected = db.DeleteProduct(id);
    return affected <= 0
        ? Results.NotFound(new { error = "Product not found" })
        : Results.Ok(new { message = "Product deleted", id });
})
.WithName("DeleteAdminProduct")
.RequireAuthorization()
.RequireRateLimiting("api");

app.MapGet("/api/dashboard", (ClaimsPrincipal user, AppDb db) =>
{
    if (!IsAdmin(user))
    {
        return Results.Forbid();
    }

    var orderCount = db.Scalar<int>("SELECT COUNT(*) FROM Orders");
    var revenue = db.Scalar<decimal>("SELECT COALESCE(SUM(TotalAmount), 0) FROM Orders");
    return Results.Ok(new
    {
        stats = new
        {
            users = db.GetUserCount(),
            products = db.GetProductCount(),
            orders = orderCount,
            revenue
        }
    });
})
.WithName("GetDashboard")
.RequireAuthorization()
.RequireRateLimiting("api");

DefinitionTemplateGeneratedComposition.MapEndpoints(app);
DefinitionTemplateGeneratedComposition.BeforeRun(app);
DefinitionTemplateGeneratedComposition.RegisterLifetimeHooks(app);

app.Run();

static DefinitionBackendModel LoadBackendDefinition(string contentRootPath)
{
    var definitionPath = Path.Combine(contentRootPath, "definition", "backend-definition.json");
    if (!File.Exists(definitionPath))
    {
        throw new FileNotFoundException("Backend definition file was not found.", definitionPath);
    }

    var json = File.ReadAllText(definitionPath);
    return new DefinitionBackendMaterializer().Materialize(json);
}

static ValidationResult ValidateLoginRequest(LoginRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return ValidationResult.Fail("Email is required.");
    }

    if (!IsValidEmail(request.Email))
    {
        return ValidationResult.Fail("Email format is invalid.");
    }

    if (string.IsNullOrWhiteSpace(request.Password))
    {
        return ValidationResult.Fail("Password is required.");
    }

    if (request.Password.Length > 128)
    {
        return ValidationResult.Fail("Password is too long.");
    }

    return ValidationResult.Success();
}

static ValidationResult ValidateRegisterRequest(RegisterRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ValidationResult.Fail("Name is required.");
    }

    if (request.Name.Trim().Length > 100)
    {
        return ValidationResult.Fail("Name is too long.");
    }

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return ValidationResult.Fail("Email is required.");
    }

    if (!IsValidEmail(request.Email))
    {
        return ValidationResult.Fail("Email format is invalid.");
    }

    if (string.IsNullOrWhiteSpace(request.Password))
    {
        return ValidationResult.Fail("Password is required.");
    }

    if (request.Password.Length < 8)
    {
        return ValidationResult.Fail("Password must be at least 8 characters.");
    }

    if (request.Password.Length > 128)
    {
        return ValidationResult.Fail("Password is too long.");
    }

    return ValidationResult.Success();
}

static ValidationResult ValidateCreateProductRequest(CreateProductRequest request, AppDb db)
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return ValidationResult.Fail("Product name is required.");
    }

    if (request.Price <= 0)
    {
        return ValidationResult.Fail("Product price must be greater than zero.");
    }

    if (request.Stock < 0)
    {
        return ValidationResult.Fail("Product stock cannot be negative.");
    }

    if (request.CategoryId <= 0 || !db.GetAllCategories().Any(category => category.Id == request.CategoryId))
    {
        return ValidationResult.Fail("Category is invalid.");
    }

    return ValidationResult.Success();
}

static ValidationResult ValidateUpdateProductRequest(UpdateProductRequest request, AppDb db)
{
    if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
    {
        return ValidationResult.Fail("Product name cannot be empty.");
    }

    if (request.Price.HasValue && request.Price.Value <= 0)
    {
        return ValidationResult.Fail("Product price must be greater than zero.");
    }

    if (request.Stock.HasValue && request.Stock.Value < 0)
    {
        return ValidationResult.Fail("Product stock cannot be negative.");
    }

    if (request.CategoryId.HasValue &&
        (request.CategoryId.Value <= 0 || !db.GetAllCategories().Any(category => category.Id == request.CategoryId.Value)))
    {
        return ValidationResult.Fail("Category is invalid.");
    }

    return ValidationResult.Success();
}

static ValidationResult ValidateCreateShopOrderRequest(CreateShopOrderRequest request, AppDb db)
{
    if (request.ProductId <= 0)
    {
        return ValidationResult.Fail("Product is required.");
    }

    if (request.Quantity <= 0)
    {
        return ValidationResult.Fail("Quantity must be greater than zero.");
    }

    if (string.IsNullOrWhiteSpace(request.ShippingAddress))
    {
        return ValidationResult.Fail("Shipping address is required.");
    }

    var product = db.GetProductById(request.ProductId);
    if (product == null || !string.Equals(product.Status, "active", StringComparison.OrdinalIgnoreCase))
    {
        return ValidationResult.Fail("Product is not available.");
    }

    if (product.Stock < request.Quantity)
    {
        return ValidationResult.Fail("Product stock is insufficient.");
    }

    return ValidationResult.Success();
}

static bool IsValidEmail(string email)
{
    if (string.IsNullOrWhiteSpace(email) || email.Length > 254)
    {
        return false;
    }

    return Regex.IsMatch(
        email.Trim(),
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(250));
}

static bool TryGetUserId(ClaimsPrincipal user, out int userId)
{
    var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    return int.TryParse(raw, out userId);
}

static bool IsAdmin(ClaimsPrincipal user)
{
    return user.IsInRole("admin")
        || string.Equals(user.FindFirstValue(ClaimTypes.Role), "admin", StringComparison.OrdinalIgnoreCase);
}

static string NormalizeStatus(string status)
{
    return string.Equals(status?.Trim(), "inactive", StringComparison.OrdinalIgnoreCase)
        ? "inactive"
        : "active";
}

static ProductResponse ToProductResponse(Product product)
{
    return new ProductResponse(
        product.Id,
        product.Name,
        product.Description,
        product.Price,
        product.Stock,
        product.CategoryId,
        product.Images,
        product.Status,
        product.CreatedAt);
}

static OrderResponse ToOrderResponse(AppDb db, Order order)
{
    var items = db.GetOrderItemsByOrderId(order.Id)
        .Select(item => new OrderItemSummary(
            item.ProductId,
            item.ProductName,
            item.UnitPrice,
            item.Quantity,
            item.Subtotal))
        .ToList();

    return new OrderResponse(
        order.Id,
        order.UserId,
        order.OrderNumber,
        order.TotalAmount,
        order.Status,
        order.ShippingAddress,
        order.Note,
        order.CreatedAt,
        items);
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);
public record CreateUserRequest(string Name, string Email, string? Password, string Role, string? Department, string? Phone);
public record UpdateUserRequest(string? Name, string? Email, string? Role, string? Status, string? Department, string? Phone);

public record ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Success() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);
}

public partial class Program
{
}
