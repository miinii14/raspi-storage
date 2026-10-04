using RaspiStorage.Models;

namespace RaspiStorage.DTO;

public class FolderDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public static FolderDto FromModel(Folder folder) => new()
    {
        Id = folder.Id,
        OwnerId = folder.OwnerId,
        ParentId = folder.ParentId,
        Name = folder.Name,
        CreatedAt = folder.CreatedAt
    };
}
