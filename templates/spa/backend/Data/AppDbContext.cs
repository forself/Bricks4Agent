using BaseOrm;
using SpaApi.Models;

namespace SpaApi.Data;

public class AppDb : BaseDb
{
    public AppDb(string connectionString) : base(connectionString) { }

    public void EnsureCreated()
    {
        Execute("""
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
            """);

        Execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Email ON Users(Email)
            """);

        Execute("""
            CREATE TABLE IF NOT EXISTS Categories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                ParentId INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                Icon TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'active',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT
            )
            """);

        Execute("""
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Price REAL NOT NULL,
                Stock INTEGER NOT NULL DEFAULT 0,
                CategoryId INTEGER NOT NULL,
                Images TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'active',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY(CategoryId) REFERENCES Categories(Id)
            )
            """);

        Execute("""
            CREATE INDEX IF NOT EXISTS IX_Products_CategoryId ON Products(CategoryId)
            """);

        Execute("""
            CREATE INDEX IF NOT EXISTS IX_Products_Status ON Products(Status)
            """);

        Execute("""
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                OrderNumber TEXT NOT NULL,
                TotalAmount REAL NOT NULL,
                Status TEXT NOT NULL DEFAULT 'pending',
                ShippingAddress TEXT NOT NULL,
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY(UserId) REFERENCES Users(Id)
            )
            """);

        Execute("""
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Orders_OrderNumber ON Orders(OrderNumber)
            """);

        Execute("""
            CREATE INDEX IF NOT EXISTS IX_Orders_UserId ON Orders(UserId)
            """);

        Execute("""
            CREATE TABLE IF NOT EXISTS OrderItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                ProductName TEXT NOT NULL,
                UnitPrice REAL NOT NULL,
                Quantity INTEGER NOT NULL,
                Subtotal REAL NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                FOREIGN KEY(OrderId) REFERENCES Orders(Id),
                FOREIGN KEY(ProductId) REFERENCES Products(Id)
            )
            """);

        Execute("""
            CREATE INDEX IF NOT EXISTS IX_OrderItems_OrderId ON OrderItems(OrderId)
            """);
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
        return QueryFirst<User>(
            "SELECT * FROM Users WHERE Email = @Email",
            new { Email = email.ToLowerInvariant() });
    }

    public bool EmailExists(string email)
    {
        var count = Scalar<int>(
            "SELECT COUNT(*) FROM Users WHERE Email = @Email",
            new { Email = email.ToLowerInvariant() });
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
        Execute(
            "UPDATE Users SET LastLoginAt = @Now WHERE Id = @Id",
            new { Now = DateTime.UtcNow.ToString("o"), Id = userId });
    }

    #endregion

    #region Category Operations

    public int GetCategoryCount()
    {
        return Scalar<int>("SELECT COUNT(*) FROM Categories");
    }

    public List<Category> GetAllCategories()
    {
        return Query<Category>("SELECT * FROM Categories ORDER BY SortOrder ASC, Id ASC");
    }

    public Category? GetCategoryByName(string name)
    {
        return QueryFirst<Category>(
            "SELECT * FROM Categories WHERE Name = @Name",
            new { Name = name });
    }

    public long CreateCategory(Category category)
    {
        return Insert(category);
    }

    #endregion

    #region Product Operations

    public int GetProductCount()
    {
        return Scalar<int>("SELECT COUNT(*) FROM Products");
    }

    public List<Product> GetAllProducts(bool activeOnly = false)
    {
        return activeOnly
            ? Query<Product>(
                "SELECT * FROM Products WHERE Status = @Status ORDER BY CreatedAt DESC",
                new { Status = "active" })
            : Query<Product>("SELECT * FROM Products ORDER BY CreatedAt DESC");
    }

    public Product? GetProductById(int id)
    {
        return QueryFirst<Product>("SELECT * FROM Products WHERE Id = @Id", new { Id = id });
    }

    public long CreateProduct(Product product)
    {
        return Insert(product);
    }

    public int UpdateProduct(Product product)
    {
        return Update(product);
    }

    public int DeleteProduct(int id)
    {
        return Delete<Product>(id);
    }

    #endregion

    #region Order Operations

    public List<Order> GetOrdersByUser(int userId)
    {
        return Query<Order>(
            "SELECT * FROM Orders WHERE UserId = @UserId ORDER BY CreatedAt DESC",
            new { UserId = userId });
    }

    public List<OrderItem> GetOrderItemsByOrderId(int orderId)
    {
        return Query<OrderItem>(
            "SELECT * FROM OrderItems WHERE OrderId = @OrderId ORDER BY Id ASC",
            new { OrderId = orderId });
    }

    public Order? CreateOrderWithSingleItem(
        int userId,
        int productId,
        int quantity,
        string shippingAddress,
        string note,
        string orderNumber)
    {
        return InTransaction(() =>
        {
            var product = QueryFirst<Product>(
                "SELECT * FROM Products WHERE Id = @Id",
                new { Id = productId });

            if (product == null || !string.Equals(product.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (quantity <= 0 || product.Stock < quantity)
            {
                return null;
            }

            var subtotal = product.Price * quantity;
            var now = DateTime.UtcNow;
            var order = new Order
            {
                UserId = userId,
                OrderNumber = orderNumber,
                TotalAmount = subtotal,
                Status = "placed",
                ShippingAddress = shippingAddress,
                Note = note,
                CreatedAt = now
            };

            var orderId = (int)CreateOrder(order);
            var orderItem = new OrderItem
            {
                OrderId = orderId,
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = quantity,
                Subtotal = subtotal,
                CreatedAt = now
            };

            CreateOrderItem(orderItem);

            product.Stock -= quantity;
            product.UpdatedAt = now;
            UpdateProduct(product);

            order.Id = orderId;
            return order;
        });
    }

    public long CreateOrder(Order order)
    {
        return Insert(order);
    }

    public long CreateOrderItem(OrderItem item)
    {
        return Insert(item);
    }

    #endregion
}

public static class BCryptHelper
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100000;

    public static string HashPassword(string password)
    {
        var salt = new byte[SaltSize];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            System.Security.Cryptography.HashAlgorithmName.SHA256);

        var hash = pbkdf2.GetBytes(HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3)
            {
                return false;
            }

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var hash = Convert.FromBase64String(parts[2]);

            using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password,
                salt,
                iterations,
                System.Security.Cryptography.HashAlgorithmName.SHA256);

            var computedHash = pbkdf2.GetBytes(hash.Length);
            return CryptographicEquals(hash, computedHash);
        }
        catch
        {
            return false;
        }
    }

    private static bool CryptographicEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }

        return result == 0;
    }
}
