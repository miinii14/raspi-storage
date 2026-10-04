

using System.Data;
using RaspiStorage.Data;
using RaspiStorage.DTO;
using RaspiStorage.Exceptions;
using RaspiStorage.Models;

namespace RaspiStorage.Services;

public interface IFolderService
{
    Task<FolderDto> CreateAsync(string name, Guid ownerId, Guid? parentId);
    Task<FolderDto> GetByIdAsync(Guid id, Guid ownerId);
    Task<IEnumerable<FolderDto>> GetByParentIdAsync (Guid? parentId, Guid ownerId);
    Task<FolderDto> RenameAsync(Guid id, Guid ownerId, string name);
    Task<FolderDto> MoveAsync(Guid id, Guid ownerId, Guid? parentId);
    Task DeleteAsync(Guid id, Guid ownerId);
}

public class FolderService : IFolderService
{
    private readonly IFolderRepository _folderRepository;
    private readonly IFileRepository _fileRepository;
    private readonly IUserRepository _userRepository;
    private readonly IActivityRepository _activityRepository;
    private readonly IDbConnectionHandler _db;
    private readonly ILogger<FolderService> _logger;

    public FolderService(IFolderRepository folderRepository, IFileRepository fileRepository, IUserRepository userRepository, IActivityRepository activityRepository, IDbConnectionHandler db, ILogger<FolderService> logger)
    {
        _folderRepository = folderRepository;
        _fileRepository = fileRepository;
        _userRepository = userRepository;
        _activityRepository = activityRepository;
        _db = db;
        _logger = logger;
    }

    public async Task<FolderDto> CreateAsync(string name, Guid ownerId, Guid? parentId)
    {
        var folder = await _folderRepository.CreateAsync(name, ownerId, parentId);

        await _activityRepository.CreateAsync(ownerId, "CREATE", "folder", folder.Id, folder.Name, folder.ParentId, null);

        return FolderDto.FromModel(folder);
    }

    public async Task<FolderDto> GetByIdAsync(Guid id, Guid ownerId)
    {
        var folder = await _folderRepository.GetByIdAsync(id, ownerId);
        if (folder is null)
        {
            throw new NotFoundException($"Folder {id} not found");
        }

        return FolderDto.FromModel(folder);
    }

    public async Task<IEnumerable<FolderDto>> GetByParentIdAsync(Guid? parentId, Guid ownerId)
    {
        var folders = await _folderRepository.GetByParentIdAsync(parentId, ownerId);
        return folders.Select(FolderDto.FromModel);
    }

    public async Task<FolderDto> RenameAsync(Guid id, Guid ownerId, string name)
    {
        var folder = await _folderRepository.GetByIdAsync(id, ownerId);

        if (folder is null)
            throw new NotFoundException($"Folder {id} not found");

        await _folderRepository.RenameAsync(id, ownerId, name);

        folder = await _folderRepository.GetByIdAsync(id, ownerId);

        await _activityRepository.CreateAsync(ownerId, "RENAME", "folder", folder!.Id, folder.Name, folder.ParentId, null);

        return FolderDto.FromModel(folder!);
    }

    public async Task<FolderDto> MoveAsync(Guid id, Guid ownerId, Guid? parentId)
    {
        var folder = await _folderRepository.GetByIdAsync(id, ownerId);
        if (folder is null)
            throw new NotFoundException($"Folder {id} not found");

        if (parentId.HasValue)
        {
            var parentFolder = await _folderRepository.GetByIdAsync(parentId.Value, ownerId);
            if (parentFolder is null)
                throw new NotFoundException($"Folder {parentId} not found");

            if (!await CheckIfFolderIsMovable(id, ownerId, parentId, 0))
                throw new ConflictException($"Folder {id} can't be moved into its own subdirectory {parentId}");
        }

        await _folderRepository.MoveAsync(id, ownerId, parentId);
        folder = await _folderRepository.GetByIdAsync(id, ownerId);

        await _activityRepository.CreateAsync(ownerId, "MOVE", "folder", folder!.Id, folder.Name, folder.ParentId, null);

        return FolderDto.FromModel(folder!);
    }

    private async Task<bool> CheckIfFolderIsMovable(Guid id, Guid ownerId, Guid? parentId, int counter)
    {
        if (counter > 100 || parentId == id)
            return false;

        if (parentId == null)
            return true;

        var folder = await _folderRepository.GetByIdAsync(parentId.Value, ownerId);

        if (folder == null)
            throw new ConflictException($"Error during moving folder {id}");

        return await CheckIfFolderIsMovable(id, ownerId, folder.ParentId, ++counter);
    }

    public async Task DeleteAsync(Guid id, Guid ownerId)
    {
        var folder = await _folderRepository.GetByIdAsync(id, ownerId);
        if (folder is null)
            throw new NotFoundException($"Folder {id} not found");

        using var connection = _db.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var files = new List<FileEntry>();
        await CollectFilesRecursivelyAsync(id, ownerId, files, connection, transaction);

        var totalSize = files.Sum(f => f.SizeBytes);

        if (totalSize > 0)
        {
            var usedBytes = await _userRepository.UpdateUsedBytesAsync(ownerId, -totalSize, connection, transaction);

            if (usedBytes is null)
                _logger.LogWarning("Quota not credited back for user {OwnerId} while deleting folder {FolderId}", ownerId, id);
        }

        await _folderRepository.DeleteAsync(id, ownerId, connection, transaction);

        await _activityRepository.CreateAsync(ownerId, "DELETE", "folder", folder.Id, folder.Name, folder.ParentId, totalSize, connection, transaction);

        transaction.Commit();

        DeleteFilesOnDisk(files);
    }

    private async Task CollectFilesRecursivelyAsync(Guid folderId, Guid ownerId, List<FileEntry> files, IDbConnection connection, IDbTransaction transaction)
    {
        var currentFiles = await _fileRepository.GetByFolderIdAsync(folderId, ownerId, connection, transaction);
        files.AddRange(currentFiles);

        var subfolders = await _folderRepository.GetByParentIdAsync(folderId, ownerId, connection, transaction);
        foreach (var subfolder in subfolders)
        {
            await CollectFilesRecursivelyAsync(subfolder.Id, ownerId, files, connection, transaction);
        }
    }

    private void DeleteFilesOnDisk(IEnumerable<FileEntry> files)
    {
        foreach (var file in files)
        {
            try
            {
                File.Delete(file.StoragePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete {StoragePath} of file {FileId}", file.StoragePath, file.Id);
            }
        }
    }
}
