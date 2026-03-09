using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bricks4Agent.Api.FileHandling.Models
{
    /// <summary>
    /// Request model for batch file upload
    /// </summary>
    public class BatchUploadRequest
    {
        /// <summary>
        /// List of files to upload
        /// </summary>
        [Required(ErrorMessage = "At least one file is required")]
        public List<IFormFile> Files { get; set; }

        /// <summary>
        /// Upload rule ID to determine the storage path
        /// </summary>
        public int? RuleId { get; set; }

        /// <summary>
        /// Associated table name (optional)
        /// </summary>
        [MaxLength(100)]
        public string TableName { get; set; }

        /// <summary>
        /// Primary key of the associated record (optional)
        /// </summary>
        [MaxLength(100)]
        public string TablePk { get; set; }

        /// <summary>
        /// Custom identifier for grouping (optional)
        /// </summary>
        [MaxLength(100)]
        public string Identify { get; set; }
    }
}
