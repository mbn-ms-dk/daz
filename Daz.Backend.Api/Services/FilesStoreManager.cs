using Dapr.Client;
using Daz.Backend.Api.Models;

namespace Daz.Backend.Api.Services;

public class FilesStoreManager : IFilesManager
{
    private static string STORE_NAME = "statestore";

    private const string OUTPUT_BINDING_NAME = "externalfilesblobstore";
    private const string OUTPUT_BINDING_OPERATION = "create";
    private readonly DaprClient daprClient;
    private readonly IConfiguration config;

    private readonly ILogger<FilesStoreManager> logger;

    public FilesStoreManager(ILogger<FilesStoreManager> logger, DaprClient daprClient, IConfiguration config)
    {
        this.logger = logger;
        this.daprClient = daprClient;
        this.config = config;
    }

    public async Task<Guid> AddFileAsync(string fileName, string createdBy, string content)
    {
        var exists = await GetFileByNameAsync(fileName);
        if (exists != null)
        {
            logger.LogInformation($"File {fileName} already exists");
            return exists.FileId;
        }

        IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>()
        {
            {"blobName", $"{fileName}"}
        };
        await daprClient.InvokeBindingAsync(OUTPUT_BINDING_NAME, OUTPUT_BINDING_OPERATION, content, metadata);
        logger.LogInformation("Invoked output binding '{0}' for file. filename: '{1}'", OUTPUT_BINDING_NAME, fileName);

        var fileModel = new FileModel
        {
            FileId = Guid.NewGuid(),
            FileName = fileName,
            FileCreatedBy = createdBy,
            FileCreatedDate = DateTime.UtcNow
        };

        logger.LogInformation($"Save a new file {fileModel.FileName} by {createdBy} to state store");
        await daprClient.SaveStateAsync<FileModel>(STORE_NAME, fileModel.FileId.ToString(), fileModel);
        return fileModel.FileId;
    }

    public async Task<bool> DeleteFileAsync(Guid fileId)
    {
        logger.LogInformation($"Delete file {fileId} from state store");
        await daprClient.DeleteStateAsync(STORE_NAME, fileId.ToString());
        return true;
    }

    public Task<FileModel> GetFileByIdAsync(Guid fileId)
    {
        logger.LogInformation($"Get file {fileId} from state store");
        return daprClient.GetStateAsync<FileModel>(STORE_NAME, fileId.ToString());
    }

    public async Task<FileModel?> GetFileByNameAsync(string fileName)
    {
        logger.LogInformation($"Get file {fileName} from state store");
        var query = "{" +
                    "\"filter\": {" +
                        "\"EQ\": { \"fileName\": \"" + fileName + "\" }" +
                    "}}";

        var queryResponse = await daprClient.QueryStateAsync<FileModel>(STORE_NAME, query);

        var model = queryResponse.Results.Select(q => q.Data).FirstOrDefault(f => f.FileName == fileName);
        if (model != null)
            return model;
        return null;

    }

    public async Task<IEnumerable<FileModel>> GetFilesAsync()
    {
        return new List<FileModel>();
    }

    public async Task<IEnumerable<FileModel>> GetFilesByCreatorAsync(string createdBy)
    {
        var query = "{" +
                    "\"filter\": {" +
                        "\"EQ\": { \"fileCreatedBy\": \"" + createdBy + "\" }" +
                    "}}";

        var queryResponse = await daprClient.QueryStateAsync<FileModel>(STORE_NAME, query);

        var fileList = queryResponse.Results.Select(q => q.Data).OrderByDescending(o => o.FileCreatedDate);
        return fileList.ToList();
    }
}