namespace RaspiStorage.DTO;

public record CreateFolderRequest([ValidName] string Name, Guid? ParentId);
