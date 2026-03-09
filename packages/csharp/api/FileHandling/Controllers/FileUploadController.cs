using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Bricks4Agent.Api.Controllers;
using Bricks4Agent.Api.FileHandling.Models;
using Bricks4Agent.Api.FileHandling.Services;

namespace Bricks4Agent.Api.FileHandling.Controllers
{
    /// <summary>
    /// API controller for file upload operations
    /// </summary>
    [Route("api/files")]
    public class FileUploadController : BaseController
    {
        private readonly IFileUploadService _fileUploadService;

        public FileUploadController(IFileUploadService fileUploadService)
        {
            _fileUploadService = fileUploadService;
        }

        /// <summary>
        /// Upload a single file
        /// </summary>
        /// <param name="file">The file to upload</param>
        /// <param name="ruleId">Upload rule ID (optional)</param>
        /// <param name="tableName">Associated table name (optional)</param>
        /// <param name="tablePk">Table primary key (optional)</param>
        /// <param name="identify">Custom identifier (optional)</param>
        /// <returns>Upload result</returns>
        [HttpPost("upload")]
        [Authorize]
        [RequestSizeLimit(104857600)] // 100MB max
        public async Task<IActionResult> UploadFile(
            IFormFile file,
            [FromForm] int? ruleId = null,
            [FromForm] string tableName = null,
            [FromForm] string tablePk = null,
            [FromForm] string identify = null)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required");
            }

            var userId = CurrentUserId ?? 0;

            var result = await _fileUploadService.UploadFileAsync(
                file, ruleId, tableName, tablePk, identify, userId);

            if (result.Success)
            {
                return Created(result, "File uploaded successfully");
            }

            return BadRequest(result.ErrorMessage);
        }

        /// <summary>
        /// Upload a single file (alternative endpoint with request body)
        /// </summary>
        /// <param name="request">Upload request</param>
        /// <returns>Upload result</returns>
        [HttpPost("upload-single")]
        [Authorize]
        [RequestSizeLimit(104857600)] // 100MB max
        public async Task<IActionResult> UploadSingleFile([FromForm] FileUploadRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequestWithModelState();
            }

            var userId = CurrentUserId ?? 0;

            var result = await _fileUploadService.UploadFileAsync(
                request.File,
                request.RuleId,
                request.TableName,
                request.TablePk,
                request.Identify,
                userId);

            if (result.Success)
            {
                return Created(result, "File uploaded successfully");
            }

            return BadRequest(result.ErrorMessage);
        }

        /// <summary>
        /// Upload multiple files in batch
        /// </summary>
        /// <param name="files">List of files to upload</param>
        /// <param name="ruleId">Upload rule ID (optional)</param>
        /// <param name="tableName">Associated table name (optional)</param>
        /// <param name="tablePk">Table primary key (optional)</param>
        /// <param name="identify">Custom identifier (optional)</param>
        /// <returns>Batch upload result</returns>
        [HttpPost("upload-batch")]
        [Authorize]
        [RequestSizeLimit(524288000)] // 500MB max for batch
        public async Task<IActionResult> UploadBatch(
            List<IFormFile> files,
            [FromForm] int? ruleId = null,
            [FromForm] string tableName = null,
            [FromForm] string tablePk = null,
            [FromForm] string identify = null)
        {
            if (files == null || files.Count == 0)
            {
                return BadRequest("At least one file is required");
            }

            var userId = CurrentUserId ?? 0;

            var result = await _fileUploadService.UploadFilesAsync(
                files, ruleId, tableName, tablePk, identify, userId);

            if (result.AllSuccessful)
            {
                return Created(result, $"All {result.TotalFiles} files uploaded successfully");
            }

            // Partial success
            return Success(result, $"{result.SuccessCount}/{result.TotalFiles} files uploaded successfully");
        }

        /// <summary>
        /// Upload multiple files in batch (alternative endpoint with request body)
        /// </summary>
        /// <param name="request">Batch upload request</param>
        /// <returns>Batch upload result</returns>
        [HttpPost("upload-batch-form")]
        [Authorize]
        [RequestSizeLimit(524288000)] // 500MB max for batch
        public async Task<IActionResult> UploadBatchForm([FromForm] BatchUploadRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequestWithModelState();
            }

            var userId = CurrentUserId ?? 0;

            var result = await _fileUploadService.UploadFilesAsync(
                request.Files,
                request.RuleId,
                request.TableName,
                request.TablePk,
                request.Identify,
                userId);

            if (result.AllSuccessful)
            {
                return Created(result, $"All {result.TotalFiles} files uploaded successfully");
            }

            return Success(result, $"{result.SuccessCount}/{result.TotalFiles} files uploaded successfully");
        }

        /// <summary>
        /// Get files with optional filters
        /// </summary>
        /// <param name="tableName">Filter by table name</param>
        /// <param name="tablePk">Filter by table primary key</param>
        /// <param name="identify">Filter by custom identifier</param>
        /// <param name="uploadedBy">Filter by uploader user ID</param>
        /// <returns>List of file records</returns>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetFiles(
            [FromQuery] string tableName = null,
            [FromQuery] string tablePk = null,
            [FromQuery] string identify = null,
            [FromQuery] int? uploadedBy = null)
        {
            var files = await _fileUploadService.GetFilesAsync(
                tableName, tablePk, identify, uploadedBy);

            return Success(files);
        }

        /// <summary>
        /// Get a specific file by ID
        /// </summary>
        /// <param name="id">File record ID</param>
        /// <returns>File record</returns>
        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetFileById(int id)
        {
            var file = await _fileUploadService.GetFileByIdAsync(id);

            if (file == null)
            {
                return NotFound($"File with ID {id} not found");
            }

            return Success(file);
        }

        /// <summary>
        /// Delete a file
        /// </summary>
        /// <param name="id">File record ID</param>
        /// <param name="physicalDelete">Whether to also delete the physical file (default: false)</param>
        /// <returns>Success status</returns>
        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> DeleteFile(int id, [FromQuery] bool physicalDelete = false)
        {
            var userId = CurrentUserId ?? 0;

            var result = await _fileUploadService.DeleteFileAsync(id, userId, physicalDelete);

            if (result)
            {
                return Success("File deleted successfully");
            }

            return NotFound($"File with ID {id} not found or already deleted");
        }

        /// <summary>
        /// Get my uploaded files (current user)
        /// </summary>
        /// <returns>List of user's file records</returns>
        [HttpGet("my-files")]
        [Authorize]
        public async Task<IActionResult> GetMyFiles()
        {
            var userId = CurrentUserId;

            if (!userId.HasValue)
            {
                return Unauthorized("User not authenticated");
            }

            var files = await _fileUploadService.GetFilesAsync(uploadedBy: userId.Value);

            return Success(files);
        }
    }
}
