using MediCare.Services.Common;
using Microsoft.AspNetCore.Http;

namespace MediCare.Services.Contracts;

public interface IFileStorageService
{
    Task<Result<string>> SaveMedicalAttachmentAsync(IFormFile file, string webRootPath);
    void DeleteAttachment(string relativePath, string webRootPath);
}
