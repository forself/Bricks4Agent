using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Bricks4Agent.Api.FileHandling.Models;
using Bricks4Agent.Database.Repository;

namespace Bricks4Agent.Api.FileHandling.Services
{
    /// <summary>
    /// Service for handling file uploads
    /// </summary>
    public class FileUploadService : IFileUploadService
    {
        private readonly IGenericRepository<FileRecord> _fileRepository;
        private readonly IGenericRepository<UploadPathRule> _ruleRepository;
        private readonly IPathTemplateParser _pathParser;
        private readonly ILogger<FileUploadService> _logger;

        /// <summary>
        /// Default upload rule when no rule is specified
        /// </summary>
        private static readonly UploadPathRule DefaultRule = new UploadPathRule
        {
            Id = 0,
            RuleName = "Default",
            PathTemplate = "{PathRoot}/{Date:yyyy/MM/dd}/{GUID}_{FileName}",
            MaxFileSize = 10485760, // 10MB
            IsActive = true
        };

        public FileUploadService(
            IGenericRepository<FileRecord> fileRepository,
            IGenericRepository<UploadPathRule> ruleRepository,
            IPathTemplateParser pathParser,
            ILogger<FileUploadService> logger)
        {
            _fileRepository = fileRepository ?? throw new ArgumentNullException(nameof(fileRepository));
            _ruleRepository = ruleRepository ?? throw new ArgumentNullException(nameof(ruleRepository));
            _pathParser = pathParser ?? throw new ArgumentNullException(nameof(pathParser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task<FileUploadResult> UploadFileAsync(
            IFormFile file,
            int? ruleId,
            string tableName,
            string tablePk,
            string identify,
            int userId)
        {
            if (file == null || file.Length == 0)
            {
                return FileUploadResult.ErrorResult(null, "File is empty or not provided");
            }

            try
            {
                // Get the upload rule
                UploadPathRule rule = DefaultRule;
                if (ruleId.HasValue)
                {
                    var foundRule = await _ruleRepository.GetByIdAsync(ruleId.Value);
                    if (foundRule == null || !foundRule.IsActive)
                    {
                        return FileUploadResult.ErrorResult(file.FileName, "Upload rule not found or inactive");
                    }
                    rule = foundRule;
                }

                // Validate file against rule
                var validation = ValidateFile(file, rule);
                if (!validation.IsValid)
                {
                    return FileUploadResult.ErrorResult(file.FileName, validation.ErrorMessage);
                }

                // Parse path template
                var context = new PathTemplateContext
                {
                    TableName = tableName,
                    TablePk = tablePk,
                    Identify = identify,
                    FileName = file.FileName,
                    UserId = userId
                };

                string storedPath = _pathParser.Parse(rule.PathTemplate, context);

                // Ensure directory exists
                string directory = Path.GetDirectoryName(storedPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Save the file
                using (var stream = new FileStream(storedPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Create file record
                var fileRecord = new FileRecord
                {
                    OriginalName = file.FileName,
                    StoredPath = storedPath,
                    FileSize = file.Length,
                    ContentType = file.ContentType ?? "application/octet-stream",
                    UploadRuleId = ruleId,
                    TableName = tableName,
                    TablePk = tablePk,
                    Identify = identify,
                    UploadedBy = userId,
                    UploadedAt = DateTime.UtcNow
                };

                await _fileRepository.AddAsync(fileRecord);

                _logger.LogInformation(
                    "File uploaded successfully: {OriginalName} -> {StoredPath} by user {UserId}",
                    file.FileName, storedPath, userId);

                return FileUploadResult.SuccessResult(fileRecord);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file: {FileName}", file.FileName);
                // Don't expose internal error details to client
                return FileUploadResult.ErrorResult(file.FileName, "Upload failed due to an internal error");
            }
        }

        /// <inheritdoc />
        public async Task<BatchUploadResult> UploadFilesAsync(
            List<IFormFile> files,
            int? ruleId,
            string tableName,
            string tablePk,
            string identify,
            int userId)
        {
            var result = new BatchUploadResult();

            if (files == null || files.Count == 0)
            {
                return result;
            }

            // Get the upload rule once for all files
            UploadPathRule rule = DefaultRule;
            if (ruleId.HasValue)
            {
                var foundRule = await _ruleRepository.GetByIdAsync(ruleId.Value);
                if (foundRule != null && foundRule.IsActive)
                {
                    rule = foundRule;
                }
            }

            foreach (var file in files)
            {
                try
                {
                    var uploadResult = await UploadFileAsync(
                        file, ruleId, tableName, tablePk, identify, userId);
                    result.AddResult(uploadResult);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch upload for file: {FileName}", file.FileName);
                    result.AddResult(FileUploadResult.ErrorResult(file.FileName, ex.Message));
                }
            }

            _logger.LogInformation(
                "Batch upload completed: {SuccessCount}/{TotalFiles} files uploaded successfully",
                result.SuccessCount, result.TotalFiles);

            return result;
        }

        /// <inheritdoc />
        public async Task<IEnumerable<FileRecordDto>> GetFilesAsync(
            string tableName = null,
            string tablePk = null,
            string identify = null,
            int? uploadedBy = null)
        {
            var query = _fileRepository.Query().Where(f => !f.IsDeleted);

            if (!string.IsNullOrEmpty(tableName))
            {
                query = query.Where(f => f.TableName == tableName);
            }

            if (!string.IsNullOrEmpty(tablePk))
            {
                query = query.Where(f => f.TablePk == tablePk);
            }

            if (!string.IsNullOrEmpty(identify))
            {
                query = query.Where(f => f.Identify == identify);
            }

            if (uploadedBy.HasValue)
            {
                query = query.Where(f => f.UploadedBy == uploadedBy.Value);
            }

            var files = query
                .OrderByDescending(f => f.UploadedAt)
                .ToList();

            return files.Select(FileRecordDto.FromEntity);
        }

        /// <inheritdoc />
        public async Task<FileRecordDto> GetFileByIdAsync(int id)
        {
            var file = await _fileRepository.GetFirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted);
            return FileRecordDto.FromEntity(file);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteFileAsync(int id, int userId, bool physicalDelete = false)
        {
            var file = await _fileRepository.GetByIdAsync(id);

            if (file == null || file.IsDeleted)
            {
                return false;
            }

            try
            {
                // Soft delete
                file.IsDeleted = true;
                file.DeletedAt = DateTime.UtcNow;
                _fileRepository.Update(file);

                // Physical delete if requested
                if (physicalDelete && File.Exists(file.StoredPath))
                {
                    File.Delete(file.StoredPath);
                    _logger.LogInformation(
                        "Physical file deleted: {StoredPath} by user {UserId}",
                        file.StoredPath, userId);
                }

                _logger.LogInformation(
                    "File record deleted: {Id} ({OriginalName}) by user {UserId}",
                    id, file.OriginalName, userId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file: {Id}", id);
                return false;
            }
        }

        /// <inheritdoc />
        public (bool IsValid, string ErrorMessage) ValidateFile(IFormFile file, UploadPathRule rule)
        {
            if (file == null)
            {
                return (false, "File is required");
            }

            // Check file size
            if (file.Length > rule.MaxFileSize)
            {
                var maxSizeMB = rule.MaxFileSize / (1024.0 * 1024.0);
                var fileSizeMB = file.Length / (1024.0 * 1024.0);
                return (false, $"File size ({fileSizeMB:F2}MB) exceeds maximum allowed size ({maxSizeMB:F2}MB)");
            }

            // Check file extension
            if (!string.IsNullOrEmpty(rule.AllowedExtensions))
            {
                var allowedExtensions = rule.AllowedExtensions
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim().ToLowerInvariant())
                    .ToList();

                var fileExtension = Path.GetExtension(file.FileName)?.ToLowerInvariant();

                if (string.IsNullOrEmpty(fileExtension) || !allowedExtensions.Contains(fileExtension))
                {
                    return (false, $"File type '{fileExtension}' is not allowed. Allowed types: {rule.AllowedExtensions}");
                }
            }

            // Check for dangerous file types (additional security)
            // Includes: executables, scripts, server-side code, and other potentially harmful files
            var dangerousExtensions = new[]
            {
                // Executables
                ".exe", ".dll", ".com", ".scr", ".msi", ".msp",
                // Windows scripts
                ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".wsf", ".wsh",
                // Unix scripts
                ".sh", ".bash", ".zsh", ".csh",
                // Server-side code (can be executed if uploaded to web root)
                ".aspx", ".asp", ".php", ".php3", ".php4", ".php5", ".phtml",
                ".jsp", ".jspx", ".cfm", ".cgi", ".pl",
                // JavaScript/TypeScript (can be dangerous in web context)
                ".js", ".mjs", ".ts",
                // Java
                ".jar", ".war", ".class",
                // Python
                ".py", ".pyc", ".pyw",
                // Other dangerous types
                ".reg", ".inf", ".scf", ".lnk", ".pif", ".hta", ".htaccess"
            };
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (dangerousExtensions.Contains(ext))
            {
                return (false, $"File type '{ext}' is not allowed for security reasons");
            }

            // Check for double extensions (e.g., file.php.jpg) which can bypass filters
            var fileName = Path.GetFileNameWithoutExtension(file.FileName);
            var innerExt = Path.GetExtension(fileName)?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(innerExt) && dangerousExtensions.Contains(innerExt))
            {
                return (false, "Files with double extensions containing executable types are not allowed");
            }

            return (true, null);
        }
    }
}
