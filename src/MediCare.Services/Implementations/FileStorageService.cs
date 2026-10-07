using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class FileStorageService : IFileStorageService
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB per FR-15

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".pdf"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "application/pdf"
    };

    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(ILogger<FileStorageService> logger)
    {
        _logger = logger;
    }

    public async Task<Result<string>> SaveMedicalAttachmentAsync(IFormFile file, string webRootPath)
    {
        if (file == null || file.Length == 0)
        {
            return Result<string>.Failure("The selected attachment is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return Result<string>.Failure("File size exceeds the 5 MB limit.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
        {
            return Result<string>.Failure("Invalid file type. Only JPG, PNG, and PDF documents are allowed.");
        }

        // Validate content-type
        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return Result<string>.Failure("Invalid file content type.");
        }

        try
        {
            var uploadsFolder = Path.Combine(webRootPath, "uploads", "records");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            // Generate safe non-guessable GUID filename, preventing path traversal
            var safeFileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadsFolder, safeFileName);

            // Double check for directory traversal
            var fullNormalizedPath = Path.GetFullPath(fullPath);
            var normalizedUploadsFolder = Path.GetFullPath(uploadsFolder);
            if (!fullNormalizedPath.StartsWith(normalizedUploadsFolder, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Security: Path traversal attempt detected during file upload: {FileName}", file.FileName);
                return Result<string>.Failure("Invalid file destination path.");
            }

            await using var stream = new FileStream(fullNormalizedPath, FileMode.Create, FileAccess.Write);
            await file.CopyToAsync(stream);

            var relativePath = $"uploads/records/{safeFileName}";
            _logger.LogInformation("Diagnostic attachment securely stored at: {RelativePath}", relativePath);

            return Result<string>.Success(relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error saving diagnostic file attachment.");
            return Result<string>.Failure("Failed to save the diagnostic attachment to the server.");
        }
    }

    public void DeleteAttachment(string relativePath, string webRootPath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;

        try
        {
            var fullPath = Path.Combine(webRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted attachment file: {FullPath}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete attachment file: {RelativePath}", relativePath);
        }
    }
}
