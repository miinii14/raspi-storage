using Microsoft.AspNetCore.Mvc;
using RaspiStorage.DTO;
using RaspiStorage.Models;
using RaspiStorage.Services;

namespace RaspiStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _folderService;

    public FoldersController(IFolderService folderService)
    {
        _folderService = folderService;
    }

    private User GetCurrentUser() => (User)HttpContext.Items["CurrentUser"]!;

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var ownerId = GetCurrentUser().Id;
        var folder = await _folderService.GetByIdAsync(id, ownerId);
        return Ok(folder);
    }

    [HttpGet]
    public async Task<IActionResult> GetByParentId([FromQuery] Guid? parentId)
    {
        var ownerId = GetCurrentUser().Id;
        var folders = await _folderService.GetByParentIdAsync(parentId, ownerId);
        return Ok(folders);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest createFolderRequest)
    {
        var ownerId = GetCurrentUser().Id;
        var folder = await _folderService.CreateAsync(createFolderRequest.Name, ownerId, createFolderRequest.ParentId);
        return CreatedAtAction(nameof(GetById), new { id = folder.Id }, folder);
    }

    [HttpPatch("rename/{id}")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenameRequest renameRequest)
    {
        var ownerId = GetCurrentUser().Id;
        return Ok(await _folderService.RenameAsync(id, ownerId, renameRequest.Name));
    }

    [HttpPatch("move/{id}")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveRequest moveRequest)
    {
        var ownerId = GetCurrentUser().Id;
        return Ok(await _folderService.MoveAsync(id, ownerId, moveRequest.FolderId));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var ownerId = GetCurrentUser().Id;
        await _folderService.DeleteAsync(id, ownerId);
        return NoContent();
    }
}
