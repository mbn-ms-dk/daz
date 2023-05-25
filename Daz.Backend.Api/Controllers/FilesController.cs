using Microsoft.AspNetCore.Mvc;
using Daz.Backend.Api.Models;
using Daz.Backend.Api.Services;
using System.Text;

namespace Daz.Backend.Api.Controllers;

[Route("api/files")]
[ApiController]
public class FilesController : ControllerBase
{
    private readonly ILogger<FilesController> logger;
    private readonly IFilesManager filesManager;



    public FilesController(ILogger<FilesController> logger, IFilesManager filesManager)
    {
        this.logger = logger;
        this.filesManager = filesManager;
    }

    [HttpGet]
    public async Task<IEnumerable<FileModel>> Get()
    {
        return await filesManager.GetFilesAsync();
    }

    [HttpGet("{fileId}")]
    public async Task<FileModel> Get(Guid fileId)
    {
        return await filesManager.GetFileByIdAsync(fileId);
    }

    [HttpGet("{fileName}")]
    public async Task<FileModel?> Get(string fileName)
    {
        return await filesManager.GetFileByNameAsync(fileName);
    }

    [HttpGet("{createdBy}")]
    public async Task<IEnumerable<FileModel>> GetFilesByCreator(string createdBy)
    {
        return await filesManager.GetFilesByCreatorAsync(createdBy);
    }

    [HttpPost]
    public async Task<IActionResult> Post(IFormFile file, string createdBy = "Anonymous")
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var content = await reader.ReadToEndAsync();
        var fileId = await filesManager.AddFileAsync(file.FileName, createdBy, content);
        return CreatedAtAction($"/api/files/fileId", fileId);
    }

    [HttpPost("{batch}")]
    public async Task<IActionResult> Post(List<IFormFile> files, string createdBy = "Anonymous")
    {

        if (files.Count > 0)
        {
            var size = files.Sum(f => f.Length);
            List<string> fileNames = new List<string>();
            foreach (var file in files)
            {
                if (file.Length > 0)
                {
                    using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                    var content = await reader.ReadToEndAsync();
                    var fileCreated = await filesManager.AddFileAsync(file.FileName, createdBy, content);
                    fileNames.Add($"{file.FileName} saved with id {fileCreated}");
                }
            }
            return Ok(new { count = files.Count, size });
        }
        return BadRequest();
    }

    [HttpDelete("{fileId}")]
    public async Task<IActionResult> Delete(Guid fileId)
    {
        var deleted = await filesManager.DeleteFileAsync(fileId);
        if (deleted)
        {
            return Ok();
        }
        return NotFound();
    }
}

