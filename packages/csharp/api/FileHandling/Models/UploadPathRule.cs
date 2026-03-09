using System;
using System.ComponentModel.DataAnnotations;

namespace Bricks4Agent.Api.FileHandling.Models
{
    /// <summary>
    /// Upload path rule configuration entity
    /// </summary>
    public class UploadPathRule
    {
        /// <summary>
        /// Primary key
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Rule name for identification
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string RuleName { get; set; }

        /// <summary>
        /// Path template with variables (e.g., {PathRoot}/{TableName}/{Date:yyyy/MM/dd}/{GUID}_{FileName})
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string PathTemplate { get; set; }

        /// <summary>
        /// Description of the rule
        /// </summary>
        [MaxLength(500)]
        public string Description { get; set; }

        /// <summary>
        /// Comma-separated list of allowed file extensions (e.g., ".jpg,.png,.pdf")
        /// Empty or null means all extensions are allowed
        /// </summary>
        [MaxLength(200)]
        public string AllowedExtensions { get; set; }

        /// <summary>
        /// Maximum file size in bytes (default: 10MB = 10485760)
        /// </summary>
        public long MaxFileSize { get; set; } = 10485760;

        /// <summary>
        /// Whether this rule is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last modified timestamp
        /// </summary>
        public DateTime? ModifiedAt { get; set; }
    }
}
