using System;
using System.ComponentModel.DataAnnotations;

namespace Bricks4Agent.Api.FileHandling.Models
{
    /// <summary>
    /// File upload record entity for tracking uploaded files
    /// </summary>
    public class FileRecord
    {
        /// <summary>
        /// Primary key
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Original file name as uploaded by user
        /// </summary>
        [Required]
        [MaxLength(255)]
        public string OriginalName { get; set; }

        /// <summary>
        /// Stored file path on the server
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string StoredPath { get; set; }

        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// MIME content type (e.g., "image/jpeg", "application/pdf")
        /// </summary>
        [MaxLength(100)]
        public string ContentType { get; set; }

        /// <summary>
        /// Reference to the upload rule used (nullable)
        /// </summary>
        public int? UploadRuleId { get; set; }

        /// <summary>
        /// Associated table name (for relational tracking)
        /// </summary>
        [MaxLength(100)]
        public string TableName { get; set; }

        /// <summary>
        /// Primary key of the associated table record
        /// </summary>
        [MaxLength(100)]
        public string TablePk { get; set; }

        /// <summary>
        /// Custom identifier for grouping files
        /// </summary>
        [MaxLength(100)]
        public string Identify { get; set; }

        /// <summary>
        /// User ID who uploaded the file
        /// </summary>
        public int UploadedBy { get; set; }

        /// <summary>
        /// Upload timestamp
        /// </summary>
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the file has been deleted (soft delete)
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// Deletion timestamp
        /// </summary>
        public DateTime? DeletedAt { get; set; }

        /// <summary>
        /// Navigation property to UploadPathRule
        /// </summary>
        public virtual UploadPathRule UploadRule { get; set; }
    }
}
