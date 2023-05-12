using Dapr.Client;
using System.Text;
using System.Text.Json;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//Add Dapr
builder.Services.AddDaprClient();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

///
app.MapGet("/listfiles", async (DaprClient client) =>
{
    var response = await client.InvokeBindingAsync(new BindingRequest("files", "list"));
    var result = JsonSerializer.Deserialize<IEnumerable<string>>(response.Data.Span);
    if (result == null)
        return Results.StatusCode(500);
    //publish event
    await client.PublishEventAsync<FilesEvent>("pubsub", "filetopic", new FilesEvent($"Retrieved file list with {result.Count()} items"));
    return Results.Ok(result);
    
})
.WithName("ListFiles")
.WithOpenApi(operation => new(operation)
{
    Summary = "List all files",
    Description = "List all files",
    Tags = new List<OpenApiTag> { new() { Name = "Files" } }
});
 //Get file by id
app.MapGet("/files/{fileId}", async (string fileId, DaprClient client, bool? isLocal) =>
{
    var fileNameFormat = isLocal.GetValueOrDefault() ? "fileName" : "blobName";
    //Get from to storage
    var req = new BindingRequest("files", "get");
    req.Metadata.Add(fileNameFormat, fileId);
    var response = await client.InvokeBindingAsync(req); 
    if (response.Data.IsEmpty)
    {
        //publish event
        await client.PublishEventAsync<FilesEvent>("pubsub", "filetopic", new FilesEvent($"Could not get file with if: {fileId}"));
        return Results.NotFound($"Could not locate file with id: {fileId}");
    }
    //publish event
    await client.PublishEventAsync<FilesEvent>("pubsub", "filetopic", new FilesEvent($"Retrieved {fileId}"));
    return Results.Ok(Encoding.UTF8.GetString(response.Data.Span));
})
.WithName("GetFileById")
.WithOpenApi(operation => 
{
    operation.Summary = "Get file by id";
    operation.Description = "Get file by id";
    operation.Tags = new List<OpenApiTag> { new() { Name = "Files" } };
    var fileIdParam = operation.Parameters[0];
    fileIdParam.Description = "The file id (name of file)";
    fileIdParam.Schema = new OpenApiSchema
    {
        Type = "string"
    };
    fileIdParam.Required = true;
    var isLocalParam = operation.Parameters.FirstOrDefault(p => p.Name == "isLocal");
    if(isLocalParam != null)
    {
    isLocalParam.Description = "Is using local storage";
    isLocalParam.Schema = new OpenApiSchema
    {
        Type = "boolean"
    };
    isLocalParam.Required = false;
    }

    return operation;
});

//Add new file
app.MapPost("/addfile", async (IFormFile file, DaprClient client,bool? isLocal) =>
{
    var fileNameFormat = isLocal.GetValueOrDefault() ? "fileName" : "blobName";
    using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
    var content = await reader.ReadToEndAsync();
    var req = new BindingRequest("files", "create");
    req.Metadata.Add(fileNameFormat, file.FileName);
    //save
    await client.InvokeBindingAsync("files", "create", content, req.Metadata);
    //publish
    await client.PublishEventAsync<FilesEvent>("pubsub", "filetopic", new FilesEvent($"Added file {file.FileName}"));
    return Results.Created($"/files/{file.FileName}", file); 
  
})
.WithName("AddNewFile")
.WithOpenApi(operation => 
{
    operation.Summary = "Add new file";
    operation.Description = "Add new file";
    operation.Tags = new List<OpenApiTag> { new() { Name = "Files" } };
    var fileIdParam = operation.Parameters[0];
    fileIdParam.Description = "The file to add";
    fileIdParam.Schema = new OpenApiSchema
    {
        Type = "file",
        Format = "binary"
    };
    fileIdParam.Required = true;
    var isLocalParam = operation.Parameters.FirstOrDefault(p => p.Name == "isLocal");
    if(isLocalParam != null)
    {
    isLocalParam.Description = "Is using local storage";
    isLocalParam.Schema = new OpenApiSchema
    {
        Type = "boolean"
    };
    isLocalParam.Required = false;
    }

    return operation;
});

//Delete file by id
app.MapDelete("/files/{fileId}", async (string fileId, DaprClient client, bool? isLocal) =>
{
    //use 'fleName' for local storage and 'blobName' for Azure Blob storage
    var fileNameFormat = isLocal.GetValueOrDefault() ? "fileName" : "blobName";
    await client.InvokeBindingAsync("files", "delete", $"{fileNameFormat}: {fileId}");
    //pub
    await client.PublishEventAsync<FilesEvent>("pubsub", "filetopic", new FilesEvent($"Deleted file {fileId}"));
    return Results.Ok($"File {fileId} is deleted");
    
})
.WithName("DeleteFileById")
.WithOpenApi(operation => 
{
    operation.Summary = "Delete file by id";
    operation.Description = "Delete file by id";
    operation.Tags = new List<OpenApiTag> { new() { Name = "Files" } };
    var fileIdParam = operation.Parameters[0];
    fileIdParam.Description = "The file id (name of file)";
    fileIdParam.Schema = new OpenApiSchema
    {
        Type = "string"
    };
    fileIdParam.Required = true;
    var isLocalParam = operation.Parameters.FirstOrDefault(p => p.Name == "isLocal");
    if(isLocalParam != null)
    {
    isLocalParam.Description = "Is using local storage";
    isLocalParam.Schema = new OpenApiSchema
    {
        Type = "boolean"
    };
    isLocalParam.Required = false;
    }
    return operation;
});

app.Run();

public record FilesEvent(string Content);