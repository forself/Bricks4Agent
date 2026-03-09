using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Bricks4Agent.Api.FileHandling.Models
{
    /// <summary>
    /// Request model for single file upload
    /// </summary>
    public class FileUploadRequest
    {
        /// <summary>
        /// The file to upload
        /// </summary>
        [Required(ErrorMessage = "File is required")]
        public IFormFile File { get; set; }

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
