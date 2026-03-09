# PaginationHelper - Pagination Utilities

Comprehensive pagination utilities for handling paged results in API responses and database queries.

## Features

- Offset-based pagination (traditional page numbers)
- Cursor-based pagination (for large datasets)
- PagedResult wrapper with metadata
- IQueryable extension methods
- Pagination parameter validation
- Automatic total pages calculation
- DTO mapping support

## Usage

### Basic Pagination

```csharp
// In-memory list pagination
var users = GetAllUsers(); // List<User>
var pagedResult = PaginationHelper.CreatePagedResult(users, page: 1, pageSize: 10);

// Access results
var items = pagedResult.Items;
var totalCount = pagedResult.TotalCount;
var totalPages = pagedResult.TotalPages;
var hasNext = pagedResult.HasNextPage;
```

### With IQueryable (Database Queries)

```csharp
public async Task<PagedResult<User>> GetUsers(int page, int pageSize)
{
    var query = _context.Users
        .Where(u => u.IsActive)
        .OrderBy(u => u.Username);

    // Use extension method
    return query.ToPagedResult(page, pageSize);
}
```

### In Controller with Extension Method

```csharp
[HttpGet]
public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
{
    var query = _context.Users
        .Where(u => u.IsActive)
        .OrderBy(u => u.CreatedAt);

    var result = query.ToPagedResult(page, pageSize);

    return Success(result);
}
```

### Using PaginationParams

```csharp
[HttpGet]
public async Task<IActionResult> GetUsers([FromQuery] PaginationParams pagination)
{
    // Validates and limits page size automatically
    var query = _context.Users
        .Where(u => u.IsActive);

    var result = query.ToPagedResult(pagination.Page, pagination.PageSize);

    return Success(result);
}
```

### With Sorting

```csharp
[HttpGet]
public async Task<IActionResult> GetProducts([FromQuery] PaginationParams pagination)
{
    var query = _context.Products.AsQueryable();

    // Apply sorting
    if (!string.IsNullOrEmpty(pagination.SortBy))
    {
        query = pagination.SortBy.ToLower() switch
        {
            "name" => pagination.IsDescending
                ? query.OrderByDescending(p => p.Name)
                : query.OrderBy(p => p.Name),
            "price" => pagination.IsDescending
                ? query.OrderByDescending(p => p.Price)
                : query.OrderBy(p => p.Price),
            _ => query.OrderBy(p => p.Id)
        };
    }

    var result = query.ToPagedResult(pagination.Page, pagination.PageSize);

    return Success(result);
}
```

### Mapping Results to DTOs

```csharp
[HttpGet]
public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
{
    var query = _context.Users
        .Where(u => u.IsActive);

    var pagedUsers = query.ToPagedResult(page, pageSize);

    // Map User entities to UserDto
    var pagedDtos = pagedUsers.Map(user => new UserDto
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email
    });

    return Success(pagedDtos);
}
```

### Separate Pagination Metadata

```csharp
[HttpGet]
public async Task<IActionResult> GetProducts([FromQuery] PaginationParams pagination)
{
    var query = _context.Products;
    var totalCount = await query.CountAsync();

    var items = await query
        .Paginate(pagination.Page, pagination.PageSize)
        .ToListAsync();

    var metadata = PaginationMetadata.Create(
        pagination.Page,
        pagination.PageSize,
        totalCount
    );

    return Ok(new
    {
        Data = items,
        Pagination = metadata
    });
}
```

### Response Format

Standard PagedResult response:

```json
{
  "items": [
    { "id": 1, "name": "Item 1" },
    { "id": 2, "name": "Item 2" }
  ],
  "totalCount": 50,
  "page": 1,
  "pageSize": 10,
  "totalPages": 5,
  "hasPreviousPage": false,
  "hasNextPage": true,
  "firstItemIndex": 1,
  "lastItemIndex": 10
}
```

Metadata-only response:

```json
{
  "data": [...],
  "pagination": {
    "page": 2,
    "pageSize": 10,
    "totalCount": 50,
    "totalPages": 5,
    "hasPreviousPage": true,
    "hasNextPage": true,
    "previousPage": 1,
    "nextPage": 3
  }
}
```

### Cursor-Based Pagination (for Large Datasets)

```csharp
[HttpGet("cursor")]
public async Task<IActionResult> GetProductsCursor(
    [FromQuery] string cursor = null,
    [FromQuery] int pageSize = 20)
{
    var query = _context.Products.OrderBy(p => p.Id);

    // Apply cursor if provided
    if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out int lastId))
    {
        query = query.Where(p => p.Id > lastId).OrderBy(p => p.Id);
    }

    // Fetch one extra to check if there are more pages
    var items = await query.Take(pageSize + 1).ToListAsync();

    var hasNextPage = items.Count > pageSize;
    if (hasNextPage)
    {
        items = items.Take(pageSize).ToList();
    }

    var result = CursorPaginationHelper.CreateCursorPagedResult(
        items,
        pageSize,
        item => item.Id.ToString(),
        hasNextPage
    );

    return Success(result);
}
```

Cursor pagination response:

```json
{
  "items": [...],
  "nextCursor": "123",
  "hasNextPage": true,
  "pageSize": 20
}
```

### Complete Service Example

```csharp
public class ProductService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<ProductDto>> GetProductsAsync(
        string category = null,
        decimal? minPrice = null,
        decimal? maxPrice = null,
        PaginationParams pagination = null)
    {
        pagination ??= new PaginationParams();

        var query = _unitOfWork.Repository<Product>().Query();

        // Apply filters
        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(p => p.Category == category);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p => p.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= maxPrice.Value);
        }

        // Apply sorting
        query = pagination.SortBy?.ToLower() switch
        {
            "name" => pagination.IsDescending
                ? query.OrderByDescending(p => p.Name)
                : query.OrderBy(p => p.Name),
            "price" => pagination.IsDescending
                ? query.OrderByDescending(p => p.Price)
                : query.OrderBy(p => p.Price),
            _ => query.OrderBy(p => p.Id)
        };

        // Get paged results
        var pagedProducts = query.ToPagedResult(pagination.Page, pagination.PageSize);

        // Map to DTOs
        return pagedProducts.Map(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            Category = p.Category
        });
    }
}
```

### Parameter Validation

```csharp
// Manual validation
var (validPage, validPageSize) = PaginationHelper.ValidatePaginationParams(
    page: 0,        // Will be set to 1
    pageSize: 200,  // Will be capped at 100 (default max)
    maxPageSize: 100
);

// Automatic validation with PaginationParams
var pagination = new PaginationParams
{
    Page = -5,      // Automatically set to 1
    PageSize = 500  // Automatically capped at 100
};
```

## PaginationParams Class

Query parameter binding for pagination:

```csharp
public class PaginationParams
{
    public int Page { get; set; } = 1;          // Auto-validates >= 1
    public int PageSize { get; set; } = 10;      // Auto-validates 1-100
    public string SortBy { get; set; }
    public string SortDirection { get; set; } = "asc";
    public bool IsDescending { get; }            // Computed property
}
```

Usage:
```csharp
// Client sends: GET /api/products?page=2&pageSize=20&sortBy=price&sortDirection=desc
[HttpGet]
public IActionResult Get([FromQuery] PaginationParams pagination)
{
    // pagination.Page = 2
    // pagination.PageSize = 20
    // pagination.SortBy = "price"
    // pagination.IsDescending = true
}
```

## Best Practices

1. **Always Validate Parameters** - Use PaginationParams or ValidatePaginationParams
2. **Set Maximum Page Size** - Prevent abuse (default: 100)
3. **Use Cursor Pagination for Large Datasets** - Better performance than offset
4. **Index Sort Columns** - Ensure database indexes on sorted fields
5. **Count Only When Needed** - For very large tables, consider approximations
6. **Cache Total Counts** - For frequently accessed, slow-changing data
7. **Provide Metadata** - Help clients build pagination UI

## Offset vs Cursor Pagination

### Offset (Page Number) - Best for:
- Small to medium datasets
- Random page access needed
- UI shows page numbers
- Total count is useful

### Cursor - Best for:
- Very large datasets (millions of rows)
- Infinite scroll UI
- Real-time data (items being added/removed)
- Better performance with indexes

## Dependencies

- System.Linq
- .NET 6.0 or higher
- No external NuGet packages required

## Benefits

1. **Consistent API Responses** - Uniform pagination format
2. **Performance** - Efficient database queries with Skip/Take
3. **Metadata** - Complete pagination information for clients
4. **Validation** - Built-in parameter validation
5. **Flexibility** - Works with IQueryable, IEnumerable, and lists
6. **Type Safety** - Strongly-typed paged results
7. **DTO Mapping** - Easy transformation to DTOs
