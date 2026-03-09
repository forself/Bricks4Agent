using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Bricks4Agent.Api.FileHandling.Models;

namespace Bricks4Agent.Api.FileHandling.Services
{
    /// <summary>
    /// Interface for file upload service
    /// </summary>
    public interface IFileUploadService
    {
        /// <summary>
        /// Upload a single file
        /// </summary>
        /// <param name="file">File to upload</param>
        /// <param name="ruleId">Upload rule ID (optional)</param>
        /// <param name="tableName">Associated table name (optional)</param>
        /// <param name="tablePk">Table primary key (optional)</param>
        /// <param name="identify">Custom identifier (optional)</param>
        /// <param name="userId">Current user ID</param>
        /// <returns>Upload result</returns>
        Task<FileUploadResult> UploadFileAsync(
            IFormFile file,
            int? ruleId,
            string tableName,
            string tablePk,
            string identify,
            int userId);

        /// <summary>
        /// Upload multiple files in batch
        /// </summary>
        /// <param name="files">Files to upload</param>
        /// <param name="ruleId">Upload rule ID (optional)</param>
        /// <param name="tableName">Associated table name (optional)</param>
        /// <param name="tablePk">Table primary key (optional)</param>
        /// <param name="identify">Custom identifier (optional)</param>
        /// <param name="userId">Current user ID</param>
        /// <returns>Batch upload result</returns>
        Task<BatchUploadResult> UploadFilesAsync(
            List<IFormFile> files,
            int? ruleId,
            string tableName,
            string tablePk,
            string identify,
            int userId);

        /// <summary>
        /// Get files by query parameters
        /// </summary>
        /// <param name="tableName">Filter by table name (optional)</param>
        /// <param name="tablePk">Filter by table primary key (optional)</param>
        /// <param name="identify">Filter by custom identifier (optional)</param>
        /// <param name="uploadedBy">Filter by uploader user ID (optional)</param>
        /// <returns>List of file records</returns>
        Task<IEnumerable<FileRecordDto>> GetFilesAsync(
            string tableName = null,
            string tablePk = null,
            string identify = null,
            int? uploadedBy = null);

        /// <summary>
        /// Get a file by ID
        /// </summary>
        /// <param name="id">File record ID</param>
        /// <returns>File record or null</returns>
        Task<FileRecordDto> GetFileByIdAsync(int id);

        /// <summary>
        /// Delete a file (soft delete)
        /// </summary>
        /// <param name="id">File record ID</param>
        /// <param name="userId">User performing the deletion</param>
        /// <param name="physicalDelete">Whether to also delete the physical file</param>
        /// <returns>True if successful</returns>
        Task<bool> DeleteFileAsync(int id, int userId, bool physicalDelete = false);

        /// <summary>
        /// Validate a file against upload rules
        /// </summary>
        /// <param name="file">File to validate</param>
        /// <param name="rule">Upload rule to validate against</param>
        /// <returns>Tuple of (isValid, errorMessage)</returns>
        (bool IsValid, string ErrorMessage) ValidateFile(IFormFile file, UploadPathRule rule);
    }
}
