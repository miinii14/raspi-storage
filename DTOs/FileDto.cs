using RaspiStorage.Models;

namespace RaspiStorage.DTO;

public class FileDto
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? MimeType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static FileDto FromModel(FileEntry file) => new()
    {
        Id = file.Id,
        OwnerId = file.OwnerId,
        FolderId = file.FolderId,
        Name = file.Name,
        SizeBytes = file.SizeBytes,
        MimeType = file.MimeType,
        CreatedAt = file.CreatedAt,
        UpdatedAt = file.UpdatedAt
    };
}
