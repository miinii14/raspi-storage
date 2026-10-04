namespace RaspiStorage.Models;

public class Activity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public Guid TargetId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public long? Bytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
