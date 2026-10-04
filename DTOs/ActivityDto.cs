using RaspiStorage.Models;

namespace RaspiStorage.DTO;

public class ActivityDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public long? Bytes { get; set; }
    public DateTime CreatedAt { get; set; }

    public static ActivityDto FromModel(Activity activity) => new()
    {
        Id = activity.Id,
        Action = activity.Action,
        TargetType = activity.TargetType,
        TargetId = activity.TargetId,
        TargetName = activity.TargetName,
        FolderId = activity.FolderId,
        Bytes = activity.Bytes,
        CreatedAt = activity.CreatedAt
    };
}
