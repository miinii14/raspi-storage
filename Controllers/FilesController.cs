

using Microsoft.AspNetCore.Mvc;
using RaspiStorage.DTO;
using RaspiStorage.Models;
using RaspiStorage.Services;

namespace RaspiStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    private User GetCurrentUser() => (User)HttpContext.Items["CurrentUser"]!;

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var ownerId = GetCurrentUser().Id;
        var file = await _fileService.GetByIdAsync(id, ownerId);
        return Ok(file);
    }

    [HttpGet]
    public async Task<IActionResult> GetByFolderId([FromQuery] Guid? folderId)
    {
        var ownerId = GetCurrentUser().Id;
        var files = await _fileService.GetByFolderIdAsync(folderId, ownerId);
        return Ok(files);
    }

    [HttpPost]
    [RequestSizeLimit(1000000000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1000000000)]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] Guid? folderId)
    {
        var ownerId = GetCurrentUser().Id;
        if (file is null)
            return BadRequest("File is empty");

        var uploadedFile = await _fileService.UploadAsync(file.OpenReadStream(), file.FileName, folderId, ownerId, file.Length, file.ContentType);
        return CreatedAtAction(nameof(GetById), new { id = uploadedFile.Id }, uploadedFile);
    }

    [HttpGet("download/{id}")]
    public async Task<IActionResult> Download(Guid id)
    {
        var ownerId = GetCurrentUser().Id;
        var (stream, mimeType, fileName) = await _fileService.DownloadAsync(id, ownerId);
        return File(stream, mimeType ?? "application/octet-stream", fileName);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = GetCurrentUser().Id;
        await _fileService.DeleteAsync(id, ownerId);
        return NoContent();
    }

    [HttpPatch("rename/{id}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameRequest renameRequest)
    {
        var ownerId = GetCurrentUser().Id;
        return Ok(await _fileService.RenameAsync(id, renameRequest.Name, ownerId));
    }

    [HttpPatch("move/{id}")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveRequest moveRequest)
    {
        var ownerId = GetCurrentUser().Id;
        return Ok(await _fileService.MoveAsync(id, moveRequest.FolderId, ownerId));
    }
}
