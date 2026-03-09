using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Bricks4Agent.Database.Repository
{
    /// <summary>
    /// Generic repository interface
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    public interface IGenericRepository<TEntity> where TEntity : class
    {
        // Query
        Task<TEntity> GetByIdAsync(int id);
        Task<TEntity> GetByIdAsync(string id);
        Task<TEntity> GetFirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate);
        Task<IEnumerable<TEntity>> GetAllAsync();
        Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);
        IQueryable<TEntity> Query();
        Task<int> CountAsync();
        Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate);
        Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);

        // Command
        Task<TEntity> AddAsync(TEntity entity);
        Task AddRangeAsync(IEnumerable<TEntity> entities);
        void Update(TEntity entity);
        void UpdateRange(IEnumerable<TEntity> entities);
        void Remove(TEntity entity);
        void RemoveRange(IEnumerable<TEntity> entities);
    }

    /// <summary>
    /// Generic repository implementation for Entity Framework Core
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    public class GenericRepository<TEntity> : IGenericRepository<TEntity> where TEntity : class
    {
        protected readonly DbContext _context;
        protected readonly DbSet<TEntity> _dbSet;

        /// <summary>
        /// Constructor
        /// </summary>
        public GenericRepository(DbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dbSet = _context.Set<TEntity>();
        }

        #region Query Methods

        /// <summary>
        /// Get entity by integer ID
        /// </summary>
        public virtual async Task<TEntity> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        /// <summary>
        /// Get entity by string ID
        /// </summary>
        public virtual async Task<TEntity> GetByIdAsync(string id)
        {
            return await _dbSet.FindAsync(id);
        }

        /// <summary>
        /// Get first entity matching predicate or null
        /// </summary>
        public virtual async Task<TEntity> GetFirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _dbSet.FirstOrDefaultAsync(predicate);
        }

        /// <summary>
        /// Get all entities
        /// </summary>
        public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        {
            return await _dbSet.ToListAsync();
        }

        /// <summary>
        /// Find entities matching predicate
        /// </summary>
        public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _dbSet.Where(predicate).ToListAsync();
        }

        /// <summary>
        /// Get queryable for advanced queries
        /// </summary>
        public virtual IQueryable<TEntity> Query()
        {
            return _dbSet.AsQueryable();
        }

        /// <summary>
        /// Count all entities
        /// </summary>
        public virtual async Task<int> CountAsync()
        {
            return await _dbSet.CountAsync();
        }

        /// <summary>
        /// Count entities matching predicate
        /// </summary>
        public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _dbSet.CountAsync(predicate);
        }

        /// <summary>
        /// Check if any entity matches predicate
        /// </summary>
        public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate)
        {
            return await _dbSet.AnyAsync(predicate);
        }

        #endregion

        #region Command Methods

        /// <summary>
        /// Add new entity
        /// </summary>
        public virtual async Task<TEntity> AddAsync(TEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            await _dbSet.AddAsync(entity);
            return entity;
        }

        /// <summary>
        /// Add multiple entities
        /// </summary>
        public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            await _dbSet.AddRangeAsync(entities);
        }

        /// <summary>
        /// Update entity
        /// </summary>
        public virtual void Update(TEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            _dbSet.Update(entity);
        }

        /// <summary>
        /// Update multiple entities
        /// </summary>
        public virtual void UpdateRange(IEnumerable<TEntity> entities)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            _dbSet.UpdateRange(entities);
        }

        /// <summary>
        /// Remove entity
        /// </summary>
        public virtual void Remove(TEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            _dbSet.Remove(entity);
        }

        /// <summary>
        /// Remove multiple entities
        /// </summary>
        public virtual void RemoveRange(IEnumerable<TEntity> entities)
        {
            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            _dbSet.RemoveRange(entities);
        }

        #endregion
    }

    /// <summary>
    /// Extended generic repository with common query helpers
    /// </summary>
    /// <typeparam name="TEntity">Entity type</typeparam>
    public class ExtendedRepository<TEntity> : GenericRepository<TEntity> where TEntity : class
    {
        public ExtendedRepository(DbContext context) : base(context)
        {
        }

        /// <summary>
        /// Get entities with pagination
        /// </summary>
        public async Task<(IEnumerable<TEntity> Items, int TotalCount)> GetPagedAsync(
            int page,
            int pageSize,
            Expression<Func<TEntity, bool>> predicate = null,
            Expression<Func<TEntity, object>> orderBy = null,
            bool descending = false)
        {
            var query = _dbSet.AsQueryable();

            if (predicate != null)
                query = query.Where(predicate);

            var totalCount = await query.CountAsync();

            if (orderBy != null)
            {
                query = descending
                    ? query.OrderByDescending(orderBy)
                    : query.OrderBy(orderBy);
            }

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        /// <summary>
        /// Get entities with includes (eager loading)
        /// </summary>
        public async Task<IEnumerable<TEntity>> GetWithIncludesAsync(
            Expression<Func<TEntity, bool>> predicate = null,
            params Expression<Func<TEntity, object>>[] includes)
        {
            var query = _dbSet.AsQueryable();

            if (includes != null)
            {
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
            }

            if (predicate != null)
                query = query.Where(predicate);

            return await query.ToListAsync();
        }

        /// <summary>
        /// Get single entity with includes
        /// </summary>
        public async Task<TEntity> GetByIdWithIncludesAsync(
            int id,
            params Expression<Func<TEntity, object>>[] includes)
        {
            var query = _dbSet.AsQueryable();

            if (includes != null)
            {
                foreach (var include in includes)
                {
                    query = query.Include(include);
                }
            }

            // Assumes entity has an "Id" property
            var parameter = Expression.Parameter(typeof(TEntity), "e");
            var property = Expression.Property(parameter, "Id");
            var constant = Expression.Constant(id);
            var equality = Expression.Equal(property, constant);
            var lambda = Expression.Lambda<Func<TEntity, bool>>(equality, parameter);

            return await query.FirstOrDefaultAsync(lambda);
        }

        /// <summary>
        /// Bulk delete entities matching predicate
        /// </summary>
        public async Task<int> BulkDeleteAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var entities = await _dbSet.Where(predicate).ToListAsync();
            _dbSet.RemoveRange(entities);
            return entities.Count;
        }

        /// <summary>
        /// Execute raw SQL query
        /// </summary>
        /// <param name="sql">The raw SQL query. MUST use parameterized queries to prevent SQL injection.</param>
        /// <param name="parameters">Parameters for the SQL query. Always use parameters instead of string concatenation.</param>
        /// <returns>List of entities matching the query</returns>
        /// <remarks>
        /// SECURITY WARNING: This method executes raw SQL. To prevent SQL injection:
        /// 1. NEVER concatenate user input directly into the SQL string
        /// 2. ALWAYS use parameterized queries with placeholders (@p0, @p1, etc.)
        /// 3. Validate and sanitize all input before using in queries
        ///
        /// SAFE example:
        ///   await repo.FromSqlRawAsync("SELECT * FROM Users WHERE Email = {0}", email);
        ///
        /// UNSAFE (SQL Injection vulnerable):
        ///   await repo.FromSqlRawAsync($"SELECT * FROM Users WHERE Email = '{email}'");
        /// </remarks>
        public async Task<IEnumerable<TEntity>> FromSqlRawAsync(string sql, params object[] parameters)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL query cannot be null or empty", nameof(sql));

            return await _dbSet.FromSqlRaw(sql, parameters).ToListAsync();
        }
    }
}
