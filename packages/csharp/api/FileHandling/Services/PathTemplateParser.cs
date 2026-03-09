using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Bricks4Agent.Api.FileHandling.Services
{
    /// <summary>
    /// Context data for path template parsing
    /// </summary>
    public class PathTemplateContext
    {
        /// <summary>
        /// Table name for {TableName} variable
        /// </summary>
        public string TableName { get; set; }

        /// <summary>
        /// Table primary key for {TablePk} variable
        /// </summary>
        public string TablePk { get; set; }

        /// <summary>
        /// Custom identifier for {Identify} variable
        /// </summary>
        public string Identify { get; set; }

        /// <summary>
        /// Original file name for {FileName} variable
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Current user ID for {UserId} variable
        /// </summary>
        public int? UserId { get; set; }
    }

    /// <summary>
    /// Interface for path template parser
    /// </summary>
    public interface IPathTemplateParser
    {
        /// <summary>
        /// Parse a path template and replace variables with actual values
        /// </summary>
        /// <param name="template">Path template string</param>
        /// <param name="context">Context data for variable replacement</param>
        /// <returns>Parsed path string</returns>
        string Parse(string template, PathTemplateContext context);

        /// <summary>
        /// Validate a path template
        /// </summary>
        /// <param name="template">Path template to validate</param>
        /// <returns>Tuple of (isValid, errorMessage)</returns>
        (bool IsValid, string ErrorMessage) Validate(string template);
    }

    /// <summary>
    /// Service to parse path templates with variable substitution
    /// Supported variables:
    /// - {PathRoot} - Root directory from configuration
    /// - {TableName} - Associated table name
    /// - {TablePk} - Table primary key value
    /// - {Identify} - Custom identifier
    /// - {FileName} - Original file name
    /// - {FileNameWithoutExt} - File name without extension
    /// - {FileExt} - File extension (with dot)
    /// - {UserId} - Current user ID
    /// - {Date:format} - Date with custom format (e.g., {Date:yyyy/MM/dd})
    /// - {GUID} - Unique identifier (GUID without dashes)
    /// </summary>
    public class PathTemplateParser : IPathTemplateParser
    {
        private readonly string _pathRoot;
        // Regex timeout to prevent ReDoS attacks
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
        private static readonly Regex DateFormatRegex = new Regex(@"\{Date:([^}]+)\}", RegexOptions.Compiled, RegexTimeout);
        private static readonly Regex VariableRegex = new Regex(@"\{([^}:]+)\}", RegexOptions.Compiled, RegexTimeout);

        /// <summary>
        /// Supported variable names
        /// </summary>
        private static readonly string[] SupportedVariables = new[]
        {
            "PathRoot", "TableName", "TablePk", "Identify",
            "FileName", "FileNameWithoutExt", "FileExt",
            "UserId", "GUID"
        };

        public PathTemplateParser(IConfiguration configuration)
        {
            // Read PathRoot from configuration, default to "uploads" if not configured
            _pathRoot = configuration["FileUpload:PathRoot"] ?? "uploads";
        }

        /// <summary>
        /// Constructor for testing with explicit path root
        /// </summary>
        public PathTemplateParser(string pathRoot)
        {
            _pathRoot = pathRoot ?? "uploads";
        }

        /// <inheritdoc />
        public string Parse(string template, PathTemplateContext context)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                throw new ArgumentException("Template cannot be null or empty", nameof(template));
            }

            context ??= new PathTemplateContext();

            string result = template;

            // Replace {Date:format} patterns first (special handling for format specifier)
            result = DateFormatRegex.Replace(result, match =>
            {
                string format = match.Groups[1].Value;
                try
                {
                    return DateTime.Now.ToString(format);
                }
                catch
                {
                    // Invalid format, return current date with default format
                    return DateTime.Now.ToString("yyyy-MM-dd");
                }
            });

            // Replace standard variables
            result = ReplaceVariable(result, "PathRoot", _pathRoot);
            result = ReplaceVariable(result, "TableName", SanitizePath(context.TableName) ?? "default");
            result = ReplaceVariable(result, "TablePk", SanitizePath(context.TablePk) ?? "0");
            result = ReplaceVariable(result, "Identify", SanitizePath(context.Identify) ?? "general");
            result = ReplaceVariable(result, "UserId", context.UserId?.ToString() ?? "anonymous");
            result = ReplaceVariable(result, "GUID", Guid.NewGuid().ToString("N")); // N = no dashes

            // Handle file name related variables
            if (!string.IsNullOrEmpty(context.FileName))
            {
                string sanitizedFileName = SanitizeFileName(context.FileName);
                string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(sanitizedFileName);
                string fileExt = System.IO.Path.GetExtension(sanitizedFileName);

                result = ReplaceVariable(result, "FileName", sanitizedFileName);
                result = ReplaceVariable(result, "FileNameWithoutExt", fileNameWithoutExt);
                result = ReplaceVariable(result, "FileExt", fileExt);
            }
            else
            {
                result = ReplaceVariable(result, "FileName", "file");
                result = ReplaceVariable(result, "FileNameWithoutExt", "file");
                result = ReplaceVariable(result, "FileExt", "");
            }

            // Normalize path separators
            result = NormalizePath(result);

            return result;
        }

        /// <inheritdoc />
        public (bool IsValid, string ErrorMessage) Validate(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return (false, "Template cannot be null or empty");
            }

            // Check for unclosed braces
            int openBraces = 0;
            foreach (char c in template)
            {
                if (c == '{') openBraces++;
                if (c == '}') openBraces--;
                if (openBraces < 0)
                {
                    return (false, "Unmatched closing brace found");
                }
            }
            if (openBraces != 0)
            {
                return (false, "Unmatched opening brace found");
            }

            // Check for unknown variables (excluding Date:format patterns)
            string withoutDatePatterns = DateFormatRegex.Replace(template, "");
            var matches = VariableRegex.Matches(withoutDatePatterns);

            foreach (Match match in matches)
            {
                string varName = match.Groups[1].Value;
                if (!Array.Exists(SupportedVariables, v => v.Equals(varName, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, $"Unknown variable: {{{varName}}}");
                }
            }

            // Check for path traversal attempts
            if (template.Contains(".."))
            {
                return (false, "Path traversal patterns (..) are not allowed");
            }

            return (true, null);
        }

        /// <summary>
        /// Replace a variable in the template
        /// </summary>
        private static string ReplaceVariable(string template, string variableName, string value)
        {
            return template.Replace($"{{{variableName}}}", value ?? "", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sanitize a path component to prevent path traversal
        /// </summary>
        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // Remove dangerous characters and patterns
            string sanitized = path
                .Replace("..", "")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_");

            return sanitized.Trim();
        }

        /// <summary>
        /// Sanitize a file name to prevent path traversal and remove invalid characters
        /// </summary>
        private static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "file";

            // Get just the file name, removing any path information
            string name = System.IO.Path.GetFileName(fileName);

            // Remove or replace invalid characters
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            // Remove path traversal patterns
            name = name.Replace("..", "_");

            return string.IsNullOrWhiteSpace(name) ? "file" : name.Trim();
        }

        /// <summary>
        /// Normalize path separators to the platform default
        /// </summary>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Convert all separators to forward slash, then normalize
            path = path.Replace("\\", "/");

            // Remove double slashes
            while (path.Contains("//"))
            {
                path = path.Replace("//", "/");
            }

            // Convert to platform-specific separator
            return path.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString());
        }
    }
}
