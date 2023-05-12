using Dapr.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDaprClient();

var app = builder.Build();
app.UseCloudEvents();

app.MapPost("/fileinfo", (string info, DaprClient client) =>
{
    Console.WriteLine(info);
    return Results.Ok(info);
})
.WithTopic("pubsub", "filetopic");