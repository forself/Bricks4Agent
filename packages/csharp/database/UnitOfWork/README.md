# UnitOfWork - Unit of Work Pattern

Unit of Work pattern implementation for managing database transactions and coordinating repository operations.

## Features

- Centralized transaction management
- Repository coordination
- Automatic SaveChanges handling
- Transaction commit/rollback support
- Entity state management
- Repository caching (CachedUnitOfWork)
- Exception handling and wrapping

## Setup

### Register in Program.cs

```csharp
// Register DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Unit of Work
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Or use cached version (recommended for better performance)
builder.Services.AddScoped<IUnitOfWork, CachedUnitOfWork>();

// Ensure DbContext is also registered for UnitOfWork
builder.Services.AddScoped<DbContext>(provider => provider.GetService<ApplicationDbContext>());
```

## Usage

### Basic Save Operations

```csharp
public class UserService
{
    private readonly IUnitOfWork _unitOfWork;

    public UserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<User> CreateUser(CreateUserDto dto)
    {
        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Repository<User>().AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        return user;
    }

    public async Task<User> UpdateUser(int id, UpdateUserDto dto)
    {
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(id);
        if (user == null)
            throw new NotFoundException("User", id);

        user.Username = dto.Username;
        user.Email = dto.Email;

        _unitOfWork.Repository<User>().Update(user);
        await _unitOfWork.SaveChangesAsync();

        return user;
    }

    public async Task DeleteUser(int id)
    {
        var user = await _unitOfWork.Repository<User>().GetByIdAsync(id);
        if (user == null)
            throw new NotFoundException("User", id);

        _unitOfWork.Repository<User>().Remove(user);
        await _unitOfWork.SaveChangesAsync();
    }
}
```

### Multiple Repository Operations

```csharp
public async Task<Order> CreateOrderWithItems(CreateOrderDto dto)
{
    var userRepo = _unitOfWork.Repository<User>();
    var orderRepo = _unitOfWork.Repository<Order>();
    var orderItemRepo = _unitOfWork.Repository<OrderItem>();

    // Verify user exists
    var user = await userRepo.GetByIdAsync(dto.UserId);
    if (user == null)
        throw new NotFoundException("User", dto.UserId);

    // Create order
    var order = new Order
    {
        UserId = dto.UserId,
        OrderDate = DateTime.UtcNow,
        TotalAmount = dto.Items.Sum(i => i.Price * i.Quantity)
    };

    await orderRepo.AddAsync(order);

    // Create order items
    var orderItems = dto.Items.Select(i => new OrderItem
    {
        Order = order,
        ProductId = i.ProductId,
        Quantity = i.Quantity,
        Price = i.Price
    }).ToList();

    await orderItemRepo.AddRangeAsync(orderItems);

    // Save all changes in one transaction
    await _unitOfWork.SaveChangesAsync();

    return order;
}
```

### Manual Transaction Management

```csharp
public async Task<bool> ProcessPayment(int orderId, PaymentDto payment)
{
    var orderRepo = _unitOfWork.Repository<Order>();
    var paymentRepo = _unitOfWork.Repository<Payment>();

    // Begin transaction
    await _unitOfWork.BeginTransactionAsync();

    try
    {
        // Get order
        var order = await orderRepo.GetByIdAsync(orderId);
        if (order == null)
            throw new NotFoundException("Order", orderId);

        // Create payment record
        var paymentRecord = new Payment
        {
            OrderId = orderId,
            Amount = payment.Amount,
            PaymentMethod = payment.Method,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        await paymentRepo.AddAsync(paymentRecord);

        // Process payment (external API call)
        var result = await _paymentGateway.ProcessAsync(payment);

        if (!result.Success)
        {
            paymentRecord.Status = "Failed";
            paymentRecord.ErrorMessage = result.ErrorMessage;

            // Rollback transaction
            await _unitOfWork.RollbackTransactionAsync();
            return false;
        }

        // Update payment and order status
        paymentRecord.Status = "Completed";
        paymentRecord.TransactionId = result.TransactionId;

        order.PaymentStatus = "Paid";
        order.Status = "Processing";

        orderRepo.Update(order);
        paymentRepo.Update(paymentRecord);

        // Commit transaction
        await _unitOfWork.CommitTransactionAsync();

        return true;
    }
    catch (Exception)
    {
        // Rollback on any error
        await _unitOfWork.RollbackTransactionAsync();
        throw;
    }
}
```

### Complex Multi-Step Operation

```csharp
public async Task<User> TransferUserData(int sourceUserId, int targetUserId)
{
    await _unitOfWork.BeginTransactionAsync();

    try
    {
        var userRepo = _unitOfWork.Repository<User>();
        var orderRepo = _unitOfWork.Repository<Order>();
        var commentRepo = _unitOfWork.Repository<Comment>();

        // Get both users
        var sourceUser = await userRepo.GetByIdAsync(sourceUserId);
        var targetUser = await userRepo.GetByIdAsync(targetUserId);

        if (sourceUser == null || targetUser == null)
            throw new NotFoundException("User not found");

        // Transfer orders
        var orders = await orderRepo.FindAsync(o => o.UserId == sourceUserId);
        foreach (var order in orders)
        {
            order.UserId = targetUserId;
        }
        orderRepo.UpdateRange(orders);

        // Transfer comments
        var comments = await commentRepo.FindAsync(c => c.UserId == sourceUserId);
        foreach (var comment in comments)
        {
            comment.UserId = targetUserId;
        }
        commentRepo.UpdateRange(comments);

        // Deactivate source user
        sourceUser.IsActive = false;
        sourceUser.DeactivatedAt = DateTime.UtcNow;
        userRepo.Update(sourceUser);

        // Commit all changes
        await _unitOfWork.CommitTransactionAsync();

        return targetUser;
    }
    catch (Exception)
    {
        await _unitOfWork.RollbackTransactionAsync();
        throw;
    }
}
```

### Using in Controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : BaseController
{
    private readonly IUnitOfWork _unitOfWork;

    public OrdersController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
    {
        try
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var itemRepo = _unitOfWork.Repository<OrderItem>();

            var order = new Order
            {
                UserId = CurrentUserId.Value,
                OrderDate = DateTime.UtcNow,
                Status = "Pending"
            };

            await orderRepo.AddAsync(order);

            var items = dto.Items.Select(i => new OrderItem
            {
                Order = order,
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                Price = i.Price
            }).ToList();

            await itemRepo.AddRangeAsync(items);

            // Save all changes
            await _unitOfWork.SaveChangesAsync();

            return Created(order, "Order created successfully");
        }
        catch (Exception ex)
        {
            return InternalServerError(ex);
        }
    }

    [HttpPut("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(int id)
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            var orderRepo = _unitOfWork.Repository<Order>();
            var paymentRepo = _unitOfWork.Repository<Payment>();

            var order = await orderRepo.GetByIdAsync(id);
            if (order == null)
                return NotFound("Order not found");

            if (order.Status == "Shipped")
                return BadRequest("Cannot cancel shipped order");

            // Cancel order
            order.Status = "Cancelled";
            order.CancelledAt = DateTime.UtcNow;
            orderRepo.Update(order);

            // Refund payment if exists
            var payment = await paymentRepo.GetFirstOrDefaultAsync(p => p.OrderId == id);
            if (payment != null && payment.Status == "Completed")
            {
                // Process refund
                var refundResult = await _paymentGateway.RefundAsync(payment.TransactionId);

                if (!refundResult.Success)
                {
                    await _unitOfWork.RollbackTransactionAsync();
                    return BadRequest("Refund failed");
                }

                payment.Status = "Refunded";
                paymentRepo.Update(payment);
            }

            await _unitOfWork.CommitTransactionAsync();

            return Success("Order cancelled successfully");
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return InternalServerError(ex);
        }
    }
}
```

### State Management

```csharp
// Detach all tracked entities
_unitOfWork.DetachAll();

// Detach specific entity
var user = await _unitOfWork.Repository<User>().GetByIdAsync(1);
_unitOfWork.DetachEntity(user);

// Now you can modify user without affecting database
user.Email = "new@email.com";
// Changes won't be saved unless you explicitly update and save
```

## CachedUnitOfWork vs UnitOfWork

### UnitOfWork (Standard)
- Creates new repository instance each time
- Lighter memory footprint
- Good for simple operations

### CachedUnitOfWork (Recommended)
- Caches repository instances per entity type
- Better performance for multiple repository accesses
- Reduces object creation overhead

```csharp
// With standard UnitOfWork - creates two instances
var userRepo1 = _unitOfWork.Repository<User>();
var userRepo2 = _unitOfWork.Repository<User>(); // New instance

// With CachedUnitOfWork - reuses instance
var userRepo1 = _unitOfWork.Repository<User>();
var userRepo2 = _unitOfWork.Repository<User>(); // Same instance
```

## Error Handling

The Unit of Work wraps database exceptions with meaningful messages:

```csharp
try
{
    await _unitOfWork.SaveChangesAsync();
}
catch (Exception ex)
{
    // Catches:
    // - DbUpdateConcurrencyException -> "Concurrency conflict occurred"
    // - DbUpdateException -> "Database update error occurred"

    _logger.LogError(ex, "Failed to save changes");
    throw;
}
```

## Best Practices

1. **One Unit of Work per Request** - Use scoped lifetime in DI
2. **Manual Transactions for Complex Operations** - Use BeginTransaction/Commit for multi-step operations
3. **Always Rollback on Error** - Use try/catch with rollback
4. **Dispose Properly** - Unit of Work implements IDisposable
5. **Save Changes Explicitly** - Unit of Work doesn't auto-save
6. **Use CachedUnitOfWork** - Better performance for most scenarios

## Benefits

1. **Transaction Coordination** - Single transaction across multiple repositories
2. **Consistent State** - All-or-nothing saves
3. **Simplified Code** - No need to pass DbContext around
4. **Testability** - Easy to mock for unit tests
5. **Performance** - Repository caching (CachedUnitOfWork)
6. **Error Handling** - Centralized exception handling

## Dependencies

- Microsoft.EntityFrameworkCore
- GenericRepository component
- .NET 6.0 or higher
