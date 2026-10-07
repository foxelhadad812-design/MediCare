using System.Text;
using FluentAssertions;
using MediCare.Services.Implementations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempWebRoot;
    private readonly Mock<ILogger<FileStorageService>> _loggerMock;
    private readonly FileStorageService _service;

    public FileStorageServiceTests()
    {
        _tempWebRoot = Path.Combine(Path.GetTempPath(), $"medicare_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempWebRoot);

        _loggerMock = new Mock<ILogger<FileStorageService>>();
        _service = new FileStorageService(_loggerMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWebRoot))
        {
            try
            {
                Directory.Delete(_tempWebRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    private static IFormFile CreateMockFormFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_ValidJpgFile_SavesFileAndReturnsRelativePath()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("fake jpg image binary content");
        var file = CreateMockFormFile("scan_result.jpg", "image/jpeg", content);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempWebRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
        result.Value.Should().StartWith("uploads/records/");
        result.Value.Should().EndWith(".jpg");

        var physicalPath = Path.Combine(_tempWebRoot, result.Value!.Replace('/', Path.DirectorySeparatorChar));
        File.Exists(physicalPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_ValidPdfFile_SavesFileAndReturnsRelativePath()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 mock pdf data");
        var file = CreateMockFormFile("lab_report.pdf", "application/pdf", content);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempWebRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_FileExceeds5Mb_ReturnsFailure()
    {
        // Arrange (5MB + 1 byte)
        var largeContent = new byte[5 * 1024 * 1024 + 1];
        var file = CreateMockFormFile("too_large.pdf", "application/pdf", largeContent);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempWebRoot);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("5 MB");
    }

    [Theory]
    [InlineData("script.exe", "application/x-msdownload")]
    [InlineData("malware.bat", "application/x-bat")]
    [InlineData("notes.txt", "text/plain")]
    [InlineData("doc.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task SaveMedicalAttachmentAsync_DisallowedExtension_ReturnsFailure(string fileName, string contentType)
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("sample content");
        var file = CreateMockFormFile(fileName, contentType, content);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempWebRoot);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only JPG, PNG, and PDF");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_PathTraversalAttemptInFileName_SafelyGeneratesGuidFilename()
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("sample payload");
        var file = CreateMockFormFile("../../../evil.png", "image/png", content);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempWebRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("uploads/records/");
        result.Value.Should().NotContain("evil");
        result.Value.Should().NotContain("..");
    }

    [Fact]
    public void DeleteAttachment_ExistingFile_DeletesPhysicalFile()
    {
        // Arrange
        var folder = Path.Combine(_tempWebRoot, "uploads", "records");
        Directory.CreateDirectory(folder);
        var testFile = Path.Combine(folder, "test_to_delete.pdf");
        File.WriteAllText(testFile, "test delete");

        // Act
        _service.DeleteAttachment("uploads/records/test_to_delete.pdf", _tempWebRoot);

        // Assert
        File.Exists(testFile).Should().BeFalse();
    }
}
