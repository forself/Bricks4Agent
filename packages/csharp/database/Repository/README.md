# GenericRepository - Repository Pattern Implementation

Generic repository pattern implementation for Entity Framework Core with extended query capabilities.

## Features

- Generic repository interface and implementation
- CRUD operations (Create, Read, Update, Delete)
- Async/await support
- Query filtering with expressions
- Pagination support
- Eager loading (Include)
- Bulk operations
- Raw SQL query support
- Count and existence checking

## Setup

### 1. Install Entity Framework Core

```bash
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

### 2. Create Your DbContext

```csharp
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<Order> Orders { get; set; }
}
```

### 3. Register in Program.cs

```csharp
// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register repositories
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped(typeof(ExtendedRepository<>));
```

## Usage

### Basic Repository Operations

```csharp
public class UserService
{
    private readonly IGenericRepository<User> _userRepository;

    public UserService(IGenericRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }

    // Get by ID
    public async Task<User> GetUserById(int id)
    {
        return await _userRepository.GetByIdAsync(id);
    }

    // Get all
    public async Task<IEnumerable<User>> GetAllUsers()
    {
        return await _userRepository.GetAllAsync();
    }

    // Find with condition
    public async Task<IEnumerable<User>> GetActiveUsers()
    {
        return await _userRepository.FindAsync(u => u.IsActive);
    }

    // Get first or default
    public async Task<User> GetUserByEmail(string email)
    {
        return await _userRepository.GetFirstOrDefaultAsync(u => u.Email == email);
    }

    // Add
    public async Task<User> CreateUser(User user)
    {
        return await _userRepository.AddAsync(user);
        // Don't forget to call SaveChanges on DbContext or UnitOfWork
    }

    // Update
    public void UpdateUser(User user)
    {
        _userRepository.Update(user);
        // Don't forget to call SaveChanges
    }

    // Delete
    public void DeleteUser(User user)
    {
        _userRepository.Remove(user);
        // Don't forget to call SaveChanges
    }
}
```

### Advanced Queries with IQueryable

```csharp
public async Task<IEnumerable<User>> GetFilteredUsers(string searchTerm)
{
    var query = _userRepository.Query();

    if (!string.IsNullOrEmpty(searchTerm))
    {
        query = query.Where(u =>
            u.Username.Contains(searchTerm) ||
            u.Email.Contains(searchTerm));
    }

    query = query
        .Where(u => u.IsActive)
        .OrderBy(u => u.Username)
        .Take(100);

    return await query.ToListAsync();
}
```

### Using Extended Repository

```csharp
public class ProductService
{
    private readonly ExtendedRepository<Product> _productRepository;

    public ProductService(ExtendedRepository<Product> productRepository)
    {
        _productRepository = productRepository;
    }

    // Pagination
    public async Task<(IEnumerable<Product> Products, int TotalCount)> GetProducts(
        int page, int pageSize, string category = null)
    {
        Expression<Func<Product, bool>> predicate = null;
        if (!string.IsNullOrEmpty(category))
        {
            predicate = p => p.Category == category;
        }

        return await _productRepository.GetPagedAsync(
            page: page,
            pageSize: pageSize,
            predicate: predicate,
            orderBy: p => p.Name,
            descending: false
        );
    }

    // Eager loading with includes
    public async Task<Product> GetProductWithDetails(int id)
    {
        return await _productRepository.GetByIdWithIncludesAsync(
            id,
            p => p.Category,
            p => p.Supplier,
            p => p.Reviews
        );
    }

    // Get with multiple includes
    public async Task<IEnumerable<Product>> GetProductsWithDetails(string category)
    {
        return await _productRepository.GetWithIncludesAsync(
            predicate: p => p.Category.Name == category,
            includes: new Expression<Func<Product, object>>[]
            {
                p => p.Category,
                p => p.Supplier
            }
        );
    }

    // Bulk delete
    public async Task<int> DeleteDiscontinuedProducts()
    {
        return await _productRepository.BulkDeleteAsync(p => p.IsDiscontinued);
        // Don't forget to call SaveChanges
    }
}
```

### Existence and Count Checks

```csharp
// Check if exists
public async Task<bool> UsernameExists(string username)
{
    return await _userRepository.ExistsAsync(u => u.Username == username);
}

// Count all
public async Task<int> GetTotalUsers()
{
    return await _userRepository.CountAsync();
}

// Count with condition
public async Task<int> GetActiveUserCount()
{
    return await _userRepository.CountAsync(u => u.IsActive);
}
```

### Bulk Operations

```csharp
// Add multiple entities
public async Task ImportUsers(List<User> users)
{
    await _userRepository.AddRangeAsync(users);
    // Don't forget to call SaveChanges
}

// Update multiple entities
public void DeactivateUsers(List<User> users)
{
    foreach (var user in users)
    {
        user.IsActive = false;
    }
    _userRepository.UpdateRange(users);
    // Don't forget to call SaveChanges
}

// Delete multiple entities
public void DeleteUsers(List<User> users)
{
    _userRepository.RemoveRange(users);
    // Don't forget to call SaveChanges
}
```

### Raw SQL Queries

```csharp
public async Task<IEnumerable<User>> GetUsersByRawSql(int minAge)
{
    var sql = "SELECT * FROM Users WHERE Age >= {0}";
    return await _extendedRepository.FromSqlRawAsync(sql, minAge);
}
```

### Creating Custom Repositories

```csharp
public interface IUserRepository : IGenericRepository<User>
{
    Task<User> GetByUsernameAsync(string username);
    Task<IEnumerable<User>> GetAdminsAsync();
}

public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<User> GetByUsernameAsync(string username)
    {
        return await _dbSet
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<IEnumerable<User>> GetAdminsAsync()
    {
        return await _dbSet
            .Where(u => u.Roles.Any(r => r.Name == "Admin"))
            .ToListAsync();
    }
}

// Register custom repository
builder.Services.AddScoped<IUserRepository, UserRepository>();
```

## Complete Example with Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController : BaseController
{
    private readonly IGenericRepository<User> _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UsersController(
        IGenericRepository<User> userRepository,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var extRepo = new ExtendedRepository<User>((DbContext)_unitOfWork);
        var (users, total) = await extRepo.GetPagedAsync(page, pageSize);

        return Success(new
        {
            Items = users,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound("User not found");

        return Success(user);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
    {
        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            CreatedAt = DateTime.UtcNow
        };

        await _userRepository.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        return Created(user, "User created successfully");
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserDto dto)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound("User not found");

        user.Username = dto.Username;
        user.Email = dto.Email;

        _userRepository.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return Success(user, "User updated successfully");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            return NotFound("User not found");

        _userRepository.Remove(user);
        await _unitOfWork.SaveChangesAsync();

        return Success("User deleted successfully");
    }
}
```

## Important Notes

1. **SaveChanges**: Repository methods don't call `SaveChanges()`. Use Unit of Work pattern or call `DbContext.SaveChangesAsync()` manually.

2. **Tracking**: All queries are tracked by default. Use `.AsNoTracking()` for read-only queries to improve performance.

3. **Transactions**: Use Unit of Work or `DbContext.Database.BeginTransaction()` for transactional operations.

## Benefits

1. **DRY Principle** - Eliminate repetitive data access code
2. **Testability** - Easy to mock for unit testing
3. **Abstraction** - Decouple business logic from data access
4. **Maintainability** - Centralized data access logic
5. **Flexibility** - Easy to switch data access implementations
6. **Type Safety** - Strongly-typed queries
7. **Async/Await** - Non-blocking database operations

## Dependencies

- Microsoft.EntityFrameworkCore
- .NET 6.0 or higher

## Recommended Usage with Unit of Work

For better transaction management, use GenericRepository with UnitOfWork pattern (see UnitOfWork component).
