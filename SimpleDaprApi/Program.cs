using Dapr.Client;
using System.Text;
using System.Text.Json;

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

app.MapGet("/listfiles", async (DaprClient client) =>
{
    var response = await client.InvokeBindingAsync(new BindingRequest("files", "list"));
    var result = JsonSerializer.Deserialize<IEnumerable<FileItem>>(response.Data.Span);
    if (result == null)
        return Results.StatusCode(500);
    //publish event
    await client.PublishEventAsync<string>("pubsub", "filetopic", $"Retrieved file list with {result.Count()} items");
    return Results.Ok(result);
    
})
.WithName("ListFiles");

app.MapGet("/files/{fileId}", async (string fileId, DaprClient client) =>
{
    //Get from to statestore
    var fi = await client.GetStateAsync<FileItem>("statestore", fileId); 
    if (fi == null)
    {
        //publish event
        await client.PublishEventAsync<string>("pubsub", "filetopic", $"Could not get file with if: {fileId}");
        return Results.NotFound($"Could not locate file with id: {fileId}");
    }
    //publish event
    await client.PublishEventAsync<string>("pubsub", "filetopic", $"Retrieved {fileId}");
    return Results.Ok(fi);
})
.WithName("GetFileById");

app.MapPost("/addfile", async (HttpRequest request, DaprClient client) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var content = await reader.ReadToEndAsync();
    var fi = new FileItem(Guid.NewGuid().ToString(), content);
    //save
    await client.SaveStateAsync<FileItem>("statestore", fi.fileId, fi);
    //publish
    await client.PublishEventAsync<string>("pubsub", "filetopic", $"Added file {fi.fileId}");
    return Results.Created($"/files/{fi.fileId}", fi); 
})
.WithName("AddNewFile");

app.MapDelete("/files/{fileId}", async (string fileId, DaprClient client, bool? isLocal) =>
{
    //use 'fleName' for local storage and 'blobName' for Azure Blob storage
    var fileNameFormat = isLocal.GetValueOrDefault() ? "fileName" : "blobName";
    await client.InvokeBindingAsync("files", "delete", $"{fileNameFormat}: {fileId}");
    //pub
    await client.PublishEventAsync<string>("pubsub", "filetopic", $"Deleted file {fileId}");
    return Results.Ok($"File {fileId} is deleted");
    
})
.WithName("DeleteFileById");

app.Run();

internal record FileItem(string fileId, string context);