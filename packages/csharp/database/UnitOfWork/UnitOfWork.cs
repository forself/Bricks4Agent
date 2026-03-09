using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Bricks4Agent.Database.Repository;

namespace Bricks4Agent.Database.UnitOfWork
{
    /// <summary>
    /// Unit of Work interface
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        // Repository access
        IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class;

        // Transaction management
        Task<int> SaveChangesAsync();
        int SaveChanges();
        Task<IDbContextTransaction> BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();

        // State management
        void DetachAll();
        void DetachEntity<TEntity>(TEntity entity) where TEntity : class;
    }

    /// <summary>
    /// Unit of Work implementation for Entity Framework Core
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        private readonly DbContext _context;
        private IDbContextTransaction _currentTransaction;
        private bool _disposed = false;

        /// <summary>
        /// Constructor
        /// </summary>
        public UnitOfWork(DbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #region Repository Access

        /// <summary>
        /// Get repository for entity type
        /// </summary>
        public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
        {
            return new GenericRepository<TEntity>(_context);
        }

        #endregion

        #region Save Changes

        /// <summary>
        /// Save all changes to database (async)
        /// </summary>
        public async Task<int> SaveChangesAsync()
        {
            try
            {
                return await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new Exception("Concurrency conflict occurred while saving changes", ex);
            }
            catch (DbUpdateException ex)
            {
                throw new Exception("Database update error occurred", ex);
            }
        }

        /// <summary>
        /// Save all changes to database (sync)
        /// </summary>
        public int SaveChanges()
        {
            try
            {
                return _context.SaveChanges();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new Exception("Concurrency conflict occurred while saving changes", ex);
            }
            catch (DbUpdateException ex)
            {
                throw new Exception("Database update error occurred", ex);
            }
        }

        #endregion

        #region Transaction Management

        /// <summary>
        /// Begin database transaction
        /// </summary>
        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress");
            }

            _currentTransaction = await _context.Database.BeginTransactionAsync();
            return _currentTransaction;
        }

        /// <summary>
        /// Commit current transaction
        /// </summary>
        public async Task CommitTransactionAsync()
        {
            try
            {
                if (_currentTransaction == null)
                {
                    throw new InvalidOperationException("No transaction in progress");
                }

                await _context.SaveChangesAsync();
                await _currentTransaction.CommitAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }
            }
        }

        /// <summary>
        /// Rollback current transaction
        /// </summary>
        public async Task RollbackTransactionAsync()
        {
            try
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.RollbackAsync();
                }
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }
            }
        }

        #endregion

        #region State Management

        /// <summary>
        /// Detach all tracked entities
        /// </summary>
        public void DetachAll()
        {
            foreach (var entry in _context.ChangeTracker.Entries().ToArray())
            {
                entry.State = EntityState.Detached;
            }
        }

        /// <summary>
        /// Detach specific entity
        /// </summary>
        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        #endregion

        #region Dispose

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _currentTransaction?.Dispose();
                    _context?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Extended Unit of Work with repository caching (thread-safe)
    /// </summary>
    public class CachedUnitOfWork : IUnitOfWork
    {
        private readonly DbContext _context;
        private readonly ConcurrentDictionary<Type, object> _repositories;
        private IDbContextTransaction _currentTransaction;
        private readonly object _transactionLock = new object();
        private bool _disposed = false;

        /// <summary>
        /// Constructor
        /// </summary>
        public CachedUnitOfWork(DbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _repositories = new ConcurrentDictionary<Type, object>();
        }

        #region Repository Access with Caching

        /// <summary>
        /// Get repository for entity type (cached, thread-safe)
        /// </summary>
        public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class
        {
            var type = typeof(TEntity);
            return (IGenericRepository<TEntity>)_repositories.GetOrAdd(type, _ => new GenericRepository<TEntity>(_context));
        }

        #endregion

        #region Save Changes

        public async Task<int> SaveChangesAsync()
        {
            try
            {
                return await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new Exception("Concurrency conflict occurred while saving changes", ex);
            }
            catch (DbUpdateException ex)
            {
                throw new Exception("Database update error occurred", ex);
            }
        }

        public int SaveChanges()
        {
            try
            {
                return _context.SaveChanges();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new Exception("Concurrency conflict occurred while saving changes", ex);
            }
            catch (DbUpdateException ex)
            {
                throw new Exception("Database update error occurred", ex);
            }
        }

        #endregion

        #region Transaction Management

        public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            if (_currentTransaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress");
            }

            _currentTransaction = await _context.Database.BeginTransactionAsync();
            return _currentTransaction;
        }

        public async Task CommitTransactionAsync()
        {
            try
            {
                if (_currentTransaction == null)
                {
                    throw new InvalidOperationException("No transaction in progress");
                }

                await _context.SaveChangesAsync();
                await _currentTransaction.CommitAsync();
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }
            }
        }

        public async Task RollbackTransactionAsync()
        {
            try
            {
                if (_currentTransaction != null)
                {
                    await _currentTransaction.RollbackAsync();
                }
            }
            finally
            {
                if (_currentTransaction != null)
                {
                    _currentTransaction.Dispose();
                    _currentTransaction = null;
                }
            }
        }

        #endregion

        #region State Management

        public void DetachAll()
        {
            foreach (var entry in _context.ChangeTracker.Entries().ToArray())
            {
                entry.State = EntityState.Detached;
            }
        }

        public void DetachEntity<TEntity>(TEntity entity) where TEntity : class
        {
            _context.Entry(entity).State = EntityState.Detached;
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _currentTransaction?.Dispose();
                    _repositories.Clear();
                    _context?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion
    }
}
