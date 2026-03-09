using System;
using System.Collections.Generic;
using System.Linq;

namespace Bricks4Agent.Utils.Pagination
{
    /// <summary>
    /// Pagination helper for handling paged results
    /// </summary>
    public static class PaginationHelper
    {
        /// <summary>
        /// Create paginated result from source
        /// </summary>
        public static PagedResult<T> CreatePagedResult<T>(
            IEnumerable<T> source,
            int page,
            int pageSize)
        {
            var totalCount = source.Count();
            var items = source
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new PagedResult<T>(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// Create paginated result with total count already known
        /// </summary>
        public static PagedResult<T> CreatePagedResult<T>(
            IEnumerable<T> items,
            int totalCount,
            int page,
            int pageSize)
        {
            return new PagedResult<T>(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// Calculate total pages
        /// </summary>
        public static int CalculateTotalPages(int totalCount, int pageSize)
        {
            return (int)Math.Ceiling(totalCount / (double)pageSize);
        }

        /// <summary>
        /// Validate pagination parameters
        /// </summary>
        public static (int page, int pageSize) ValidatePaginationParams(int page, int pageSize, int maxPageSize = 100)
        {
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(pageSize, maxPageSize));
            return (page, pageSize);
        }

        /// <summary>
        /// Get skip count for SQL queries
        /// </summary>
        public static int GetSkipCount(int page, int pageSize)
        {
            return (page - 1) * pageSize;
        }
    }

    /// <summary>
    /// Paged result container
    /// </summary>
    /// <typeparam name="T">Item type</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// Items in current page
        /// </summary>
        public IEnumerable<T> Items { get; set; }

        /// <summary>
        /// Total number of items across all pages
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Current page number (1-based)
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// Number of items per page
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Total number of pages
        /// </summary>
        public int TotalPages { get; set; }

        /// <summary>
        /// Whether there is a previous page
        /// </summary>
        public bool HasPreviousPage => Page > 1;

        /// <summary>
        /// Whether there is a next page
        /// </summary>
        public bool HasNextPage => Page < TotalPages;

        /// <summary>
        /// First item index in current page (1-based)
        /// </summary>
        public int FirstItemIndex => (Page - 1) * PageSize + 1;

        /// <summary>
        /// Last item index in current page (1-based)
        /// </summary>
        public int LastItemIndex => Math.Min(Page * PageSize, TotalCount);

        /// <summary>
        /// Constructor
        /// </summary>
        public PagedResult()
        {
            Items = new List<T>();
        }

        /// <summary>
        /// Constructor with data
        /// </summary>
        public PagedResult(IEnumerable<T> items, int totalCount, int page, int pageSize)
        {
            Items = items ?? new List<T>();
            TotalCount = totalCount;
            Page = page;
            PageSize = pageSize;
            TotalPages = PaginationHelper.CalculateTotalPages(totalCount, pageSize);
        }

        /// <summary>
        /// Map items to different type
        /// </summary>
        public PagedResult<TResult> Map<TResult>(Func<T, TResult> mapper)
        {
            var mappedItems = Items.Select(mapper);
            return new PagedResult<TResult>(mappedItems, TotalCount, Page, PageSize);
        }
    }

    /// <summary>
    /// Pagination metadata for API responses
    /// </summary>
    public class PaginationMetadata
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
        public int? PreviousPage { get; set; }
        public int? NextPage { get; set; }

        public static PaginationMetadata Create(int page, int pageSize, int totalCount)
        {
            var totalPages = PaginationHelper.CalculateTotalPages(totalCount, pageSize);

            return new PaginationMetadata
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasPreviousPage = page > 1,
                HasNextPage = page < totalPages,
                PreviousPage = page > 1 ? page - 1 : null,
                NextPage = page < totalPages ? page + 1 : null
            };
        }
    }

    /// <summary>
    /// Extension methods for IQueryable pagination
    /// </summary>
    public static class PaginationExtensions
    {
        /// <summary>
        /// Apply pagination to IQueryable
        /// </summary>
        public static IQueryable<T> Paginate<T>(this IQueryable<T> query, int page, int pageSize)
        {
            return query
                .Skip((page - 1) * pageSize)
                .Take(pageSize);
        }

        /// <summary>
        /// Convert IQueryable to PagedResult (executes query)
        /// </summary>
        public static PagedResult<T> ToPagedResult<T>(this IQueryable<T> query, int page, int pageSize)
        {
            var totalCount = query.Count();
            var items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return new PagedResult<T>(items, totalCount, page, pageSize);
        }

        /// <summary>
        /// Convert IEnumerable to PagedResult
        /// </summary>
        public static PagedResult<T> ToPagedResult<T>(this IEnumerable<T> source, int page, int pageSize)
        {
            return PaginationHelper.CreatePagedResult(source, page, pageSize);
        }
    }

    /// <summary>
    /// Pagination query parameters for API requests
    /// </summary>
    public class PaginationParams
    {
        private const int MaxPageSize = 100;
        private int _pageSize = 10;
        private int _page = 1;

        /// <summary>
        /// Page number (1-based, default: 1)
        /// </summary>
        public int Page
        {
            get => _page;
            set => _page = Math.Max(1, value);
        }

        /// <summary>
        /// Page size (default: 10, max: 100)
        /// </summary>
        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = Math.Max(1, Math.Min(value, MaxPageSize));
        }

        /// <summary>
        /// Sort field
        /// </summary>
        public string SortBy { get; set; }

        /// <summary>
        /// Sort direction (asc/desc)
        /// </summary>
        public string SortDirection { get; set; } = "asc";

        /// <summary>
        /// Whether to sort descending
        /// </summary>
        public bool IsDescending => SortDirection?.ToLower() == "desc";
    }

    /// <summary>
    /// Cursor-based pagination helper for large datasets
    /// </summary>
    public class CursorPaginationHelper
    {
        /// <summary>
        /// Create cursor-based paginated result
        /// </summary>
        public static CursorPagedResult<T> CreateCursorPagedResult<T>(
            IEnumerable<T> items,
            int pageSize,
            Func<T, string> getCursor,
            bool hasNextPage)
        {
            var itemList = items.ToList();
            var nextCursor = hasNextPage && itemList.Any()
                ? getCursor(itemList.Last())
                : null;

            return new CursorPagedResult<T>
            {
                Items = itemList,
                NextCursor = nextCursor,
                HasNextPage = hasNextPage,
                PageSize = pageSize
            };
        }
    }

    /// <summary>
    /// Cursor-based paged result
    /// </summary>
    public class CursorPagedResult<T>
    {
        public IEnumerable<T> Items { get; set; }
        public string NextCursor { get; set; }
        public bool HasNextPage { get; set; }
        public int PageSize { get; set; }
    }
}
