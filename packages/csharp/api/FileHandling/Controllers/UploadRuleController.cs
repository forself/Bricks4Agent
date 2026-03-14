using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Bricks4Agent.Api.Controllers;
using Bricks4Agent.Api.FileHandling.Models;
using Bricks4Agent.Api.FileHandling.Services;
using Bricks4Agent.Database.Repository;

namespace Bricks4Agent.Api.FileHandling.Controllers
{
    /// <summary>
    /// DTO for creating/updating upload rules
    /// </summary>
    public class UploadRuleDto
    {
        [Required(ErrorMessage = "Rule name is required")]
        [MaxLength(100)]
        public string RuleName { get; set; }

        [Required(ErrorMessage = "Path template is required")]
        [MaxLength(500)]
        public string PathTemplate { get; set; }

        [MaxLength(500)]
        public string Description { get; set; }

        [MaxLength(200)]
        public string AllowedExtensions { get; set; }

        public long MaxFileSize { get; set; } = 10485760;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Map to entity
        /// </summary>
        public UploadPathRule ToEntity()
        {
            return new UploadPathRule
            {
                RuleName = RuleName,
                PathTemplate = PathTemplate,
                Description = Description,
                AllowedExtensions = AllowedExtensions,
                MaxFileSize = MaxFileSize,
                IsActive = IsActive,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Apply updates to existing entity
        /// </summary>
        public void ApplyTo(UploadPathRule entity)
        {
            entity.RuleName = RuleName;
            entity.PathTemplate = PathTemplate;
            entity.Description = Description;
            entity.AllowedExtensions = AllowedExtensions;
            entity.MaxFileSize = MaxFileSize;
            entity.IsActive = IsActive;
            entity.ModifiedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Response DTO for upload rules
    /// </summary>
    public class UploadRuleResponseDto
    {
        public int Id { get; set; }
        public string RuleName { get; set; }
        public string PathTemplate { get; set; }
        public string Description { get; set; }
        public string AllowedExtensions { get; set; }
        public long MaxFileSize { get; set; }
        public string MaxFileSizeDisplay { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ModifiedAt { get; set; }

        public static UploadRuleResponseDto FromEntity(UploadPathRule entity)
        {
            if (entity == null) return null;

            return new UploadRuleResponseDto
            {
                Id = entity.Id,
                RuleName = entity.RuleName,
                PathTemplate = entity.PathTemplate,
                Description = entity.Description,
                AllowedExtensions = entity.AllowedExtensions,
                MaxFileSize = entity.MaxFileSize,
                MaxFileSizeDisplay = FormatFileSize(entity.MaxFileSize),
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt,
                ModifiedAt = entity.ModifiedAt
            };
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// API controller for upload rule management (admin only)
    /// </summary>
    [Route("api/upload-rules")]
    public class UploadRuleController : BaseController
    {
        private readonly IGenericRepository<UploadPathRule> _ruleRepository;
        private readonly IPathTemplateParser _pathParser;

        public UploadRuleController(
            IGenericRepository<UploadPathRule> ruleRepository,
            IPathTemplateParser pathParser)
        {
            _ruleRepository = ruleRepository;
            _pathParser = pathParser;
        }

        /// <summary>
        /// Get all upload rules
        /// </summary>
        /// <param name="activeOnly">Filter to active rules only (default: false)</param>
        /// <returns>List of upload rules</returns>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetRules([FromQuery] bool activeOnly = false)
        {
            IEnumerable<UploadPathRule> rules;

            if (activeOnly)
            {
                rules = await _ruleRepository.FindAsync(r => r.IsActive);
            }
            else
            {
                rules = await _ruleRepository.GetAllAsync();
            }

            var response = rules
                .OrderBy(r => r.RuleName)
                .Select(UploadRuleResponseDto.FromEntity);

            return Success(response);
        }

        /// <summary>
        /// Get a specific upload rule by ID
        /// </summary>
        /// <param name="id">Rule ID</param>
        /// <returns>Upload rule</returns>
        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetRuleById(int id)
        {
            var rule = await _ruleRepository.GetByIdAsync(id);

            if (rule == null)
            {
                return NotFound($"Upload rule with ID {id} not found");
            }

            return Success(UploadRuleResponseDto.FromEntity(rule));
        }

        /// <summary>
        /// Create a new upload rule (admin only)
        /// </summary>
        /// <param name="dto">Rule data</param>
        /// <returns>Created rule</returns>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateRule([FromBody] UploadRuleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequestWithModelState();
            }

            // Validate path template
            var validation = _pathParser.Validate(dto.PathTemplate);
            if (!validation.IsValid)
            {
                return BadRequest($"Invalid path template: {validation.ErrorMessage}");
            }

            // Check for duplicate rule name
            var existing = await _ruleRepository.GetFirstOrDefaultAsync(r => r.RuleName == dto.RuleName);
            if (existing != null)
            {
                return Conflict($"A rule with name '{dto.RuleName}' already exists");
            }

            var rule = dto.ToEntity();
            await _ruleRepository.AddAsync(rule);

            return Created(UploadRuleResponseDto.FromEntity(rule), "Upload rule created successfully");
        }

        /// <summary>
        /// Update an existing upload rule (admin only)
        /// </summary>
        /// <param name="id">Rule ID</param>
        /// <param name="dto">Updated rule data</param>
        /// <returns>Updated rule</returns>
        [HttpPut("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateRule(int id, [FromBody] UploadRuleDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequestWithModelState();
            }

            var rule = await _ruleRepository.GetByIdAsync(id);
            if (rule == null)
            {
                return NotFound($"Upload rule with ID {id} not found");
            }

            // Validate path template
            var validation = _pathParser.Validate(dto.PathTemplate);
            if (!validation.IsValid)
            {
                return BadRequest($"Invalid path template: {validation.ErrorMessage}");
            }

            // Check for duplicate rule name (excluding current rule)
            var existing = await _ruleRepository.GetFirstOrDefaultAsync(
                r => r.RuleName == dto.RuleName && r.Id != id);
            if (existing != null)
            {
                return Conflict($"A rule with name '{dto.RuleName}' already exists");
            }

            dto.ApplyTo(rule);
            _ruleRepository.Update(rule);

            return Success(UploadRuleResponseDto.FromEntity(rule), "Upload rule updated successfully");
        }

        /// <summary>
        /// Delete an upload rule (admin only)
        /// </summary>
        /// <param name="id">Rule ID</param>
        /// <returns>Success status</returns>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteRule(int id)
        {
            var rule = await _ruleRepository.GetByIdAsync(id);
            if (rule == null)
            {
                return NotFound($"Upload rule with ID {id} not found");
            }

            _ruleRepository.Remove(rule);

            return Success("Upload rule deleted successfully");
        }

        /// <summary>
        /// Toggle rule active status (admin only)
        /// </summary>
        /// <param name="id">Rule ID</param>
        /// <returns>Updated rule</returns>
        [HttpPatch("{id:int}/toggle-active")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ToggleRuleActive(int id)
        {
            var rule = await _ruleRepository.GetByIdAsync(id);
            if (rule == null)
            {
                return NotFound($"Upload rule with ID {id} not found");
            }

            rule.IsActive = !rule.IsActive;
            rule.ModifiedAt = DateTime.UtcNow;
            _ruleRepository.Update(rule);

            return Success(UploadRuleResponseDto.FromEntity(rule),
                $"Rule {(rule.IsActive ? "activated" : "deactivated")} successfully");
        }

        /// <summary>
        /// Validate a path template without creating a rule
        /// </summary>
        /// <param name="template">Path template to validate</param>
        /// <returns>Validation result</returns>
        [HttpPost("validate-template")]
        [Authorize]
        public IActionResult ValidateTemplate([FromBody] string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return BadRequest("Template is required");
            }

            var validation = _pathParser.Validate(template);

            if (validation.IsValid)
            {
                // Also show a preview with sample data
                var context = new PathTemplateContext
                {
                    TableName = "SampleTable",
                    TablePk = "123",
                    Identify = "test",
                    FileName = "example.jpg",
                    UserId = 1
                };

                var preview = _pathParser.Parse(template, context);

                return Success(new
                {
                    IsValid = true,
                    Preview = preview
                }, "Template is valid");
            }

            return BadRequest(new
            {
                IsValid = false,
                ErrorMessage = validation.ErrorMessage
            });
        }

        /// <summary>
        /// Get supported template variables
        /// </summary>
        /// <returns>List of supported variables</returns>
        [HttpGet("template-variables")]
        [Authorize]
        public IActionResult GetTemplateVariables()
        {
            var variables = new[]
            {
                new { Name = "{PathRoot}", Description = "Root directory (from configuration)" },
                new { Name = "{TableName}", Description = "Associated table name" },
                new { Name = "{TablePk}", Description = "Table primary key value" },
                new { Name = "{Identify}", Description = "Custom identifier" },
                new { Name = "{FileName}", Description = "Original file name" },
                new { Name = "{FileNameWithoutExt}", Description = "File name without extension" },
                new { Name = "{FileExt}", Description = "File extension (with dot)" },
                new { Name = "{UserId}", Description = "Current user ID" },
                new { Name = "{Date:format}", Description = "Date with format (e.g., {Date:yyyy/MM/dd})" },
                new { Name = "{GUID}", Description = "Unique identifier (32 characters)" }
            };

            return Success(variables);
        }
    }
}
