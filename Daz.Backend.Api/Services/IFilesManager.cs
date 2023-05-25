using Daz.Backend.Api.Models;
namespace Daz.Backend.Api.Services
{
    public interface IFilesManager
    {
        Task<FileModel> GetFileByIdAsync(Guid fileId);
        Task<FileModel?> GetFileByNameAsync(string fileName);
        Task<IEnumerable<FileModel>> GetFilesAsync();
        Task<IEnumerable<FileModel>> GetFilesByCreatorAsync(string createdBy);
        Task<Guid> AddFileAsync(string fileName, string createdBy,string content);
        Task<bool> DeleteFileAsync(Guid fileId);
    }
}