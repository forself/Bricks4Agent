using System;
using System.Collections.Generic;

namespace Bricks4Agent.Api.FileHandling.Models
{
    /// <summary>
    /// Result model for single file upload
    /// </summary>
    public class FileUploadResult
    {
        /// <summary>
        /// Whether the upload was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// File record ID (if successful)
        /// </summary>
        public int? FileId { get; set; }

        /// <summary>
        /// Original file name
        /// </summary>
        public string OriginalName { get; set; }

        /// <summary>
        /// Stored file path
        /// </summary>
        public string StoredPath { get; set; }

        /// <summary>
        /// File size in bytes
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// MIME content type
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Error message (if failed)
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Upload timestamp
        /// </summary>
        public DateTime? UploadedAt { get; set; }

        /// <summary>
        /// Create a success result
        /// </summary>
        public static FileUploadResult SuccessResult(FileRecord record)
        {
            return new FileUploadResult
            {
                Success = true,
                FileId = record.Id,
                OriginalName = record.OriginalName,
                StoredPath = record.StoredPath,
                FileSize = record.FileSize,
                ContentType = record.ContentType,
                UploadedAt = record.UploadedAt
            };
        }

        /// <summary>
        /// Create an error result
        /// </summary>
        public static FileUploadResult ErrorResult(string fileName, string errorMessage)
        {
            return new FileUploadResult
            {
                Success = false,
                OriginalName = fileName,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// Result model for batch file upload
    /// </summary>
    public class BatchUploadResult
    {
        /// <summary>
        /// Total number of files attempted
        /// </summary>
        public int TotalFiles { get; set; }

        /// <summary>
        /// Number of successful uploads
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Number of failed uploads
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Whether all uploads were successful
        /// </summary>
        public bool AllSuccessful => FailedCount == 0;

        /// <summary>
        /// Individual results for each file
        /// </summary>
        public List<FileUploadResult> Results { get; set; } = new List<FileUploadResult>();

        /// <summary>
        /// Add a result to the batch
        /// </summary>
        public void AddResult(FileUploadResult result)
        {
            Results.Add(result);
            TotalFiles++;
            if (result.Success)
                SuccessCount++;
            else
                FailedCount++;
        }
    }

    /// <summary>
    /// DTO for file record query results
    /// </summary>
    public class FileRecordDto
    {
        public int Id { get; set; }
        public string OriginalName { get; set; }
        public string StoredPath { get; set; }
        public long FileSize { get; set; }
        public string ContentType { get; set; }
        public string TableName { get; set; }
        public string TablePk { get; set; }
        public string Identify { get; set; }
        public int UploadedBy { get; set; }
        public DateTime UploadedAt { get; set; }

        /// <summary>
        /// Map from FileRecord entity
        /// </summary>
        public static FileRecordDto FromEntity(FileRecord entity)
        {
            if (entity == null) return null;

            return new FileRecordDto
            {
                Id = entity.Id,
                OriginalName = entity.OriginalName,
                StoredPath = entity.StoredPath,
                FileSize = entity.FileSize,
                ContentType = entity.ContentType,
                TableName = entity.TableName,
                TablePk = entity.TablePk,
                Identify = entity.Identify,
                UploadedBy = entity.UploadedBy,
                UploadedAt = entity.UploadedAt
            };
        }
    }
}
