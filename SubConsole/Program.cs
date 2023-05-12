using Dapr.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();

// Subscribe to the topic
app.MapPost("/fileinfo", (FilesEvent filesEvent, DaprClient client) =>
{
    Console.WriteLine(filesEvent.Content);
    return Results.Ok(filesEvent.Content);
})
.WithTopic("pubsub", "filetopic");

app.Run();

public record FilesEvent(string Content);