using Microsoft.Extensions.Options;
using RaspiStorage.Data;
using RaspiStorage.DTO;
using RaspiStorage.Exceptions;
using RaspiStorage.Models;

namespace RaspiStorage.Services;

public interface IFileService
{
    Task<FileDto> UploadAsync(Stream stream, string name, Guid? folderId, Guid ownerId, long sizeBytes, string? mimeType);
    Task<(Stream stream, string? mimeType, string fileName)> DownloadAsync(Guid id, Guid ownerId);
    Task DeleteAsync(Guid id, Guid ownerId);
    Task<FileDto> RenameAsync(Guid id, string name, Guid ownerId);
    Task<FileDto> MoveAsync(Guid id, Guid? folderId, Guid ownerId);
    Task<FileDto> GetByIdAsync(Guid id, Guid ownerId);
    Task<IEnumerable<FileDto>> GetByFolderIdAsync(Guid? folderId, Guid ownerId);
}

public class FileService : IFileService
{
    private readonly IFileRepository _fileRepository;
    private readonly IUserRepository _userRepository;
    private readonly IActivityRepository _activityRepository;
    private readonly IFolderRepository _folderRepository;
    private readonly IDbConnectionHandler _db;
    private readonly string _storagePath;

    public FileService(IFileRepository fileRepository, IUserRepository userRepository, IActivityRepository activityRepository, IFolderRepository folderRepository, IDbConnectionHandler db, IOptions<StorageOptions> storageOptions)
    {
        _fileRepository = fileRepository;
        _userRepository = userRepository;
        _activityRepository = activityRepository;
        _folderRepository = folderRepository;
        _db = db;
        _storagePath = storageOptions.Value.BasePath;
    }

    public async Task<FileDto> UploadAsync(Stream stream, string name, Guid? folderId, Guid ownerId, long sizeBytes, string? mimeType)
    {
        var nameError = ValidNameAttribute.Validate(name);
        if (nameError is not null)
        {
            throw new InvalidNameException(nameError);
        }

        var user = await _userRepository.GetByIdAsync(ownerId);
        if ((user!.UsedBytes + sizeBytes) > user.QuotaBytes)
            throw new QuotaExceededException("Bytes quota exceeded");

        if (folderId.HasValue)
        {
            var folder = await _folderRepository.GetByIdAsync(folderId.Value, ownerId);
            if (folder is null)
                throw new NotFoundException($"Folder {folderId} not found");
        }

        Directory.CreateDirectory(_storagePath);

        var extension = Path.GetExtension(name);
        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(_storagePath, fileName);

        using (var fileStream = File.Create(filePath))
        {
            await stream.CopyToAsync(fileStream);
        }

        using var connection = _db.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        FileEntry file;

        try
        {
            file = await _fileRepository.CreateAsync(name, folderId, ownerId, sizeBytes, mimeType, filePath, connection, transaction);

            var response = await _userRepository.UpdateUsedBytesAsync(ownerId, sizeBytes, connection, transaction);

            if (response == null)
                throw new QuotaExceededException("User's byte quota exceeded");

            await _activityRepository.CreateAsync(ownerId, "UPLOAD", "file", file.Id, file.Name, folderId, sizeBytes, connection, transaction);

            transaction.Commit();
        }
        catch
        {
            File.Delete(filePath);
            throw;
        }

        return FileDto.FromModel(file);
    }

    public async Task<(Stream stream, string? mimeType, string fileName)> DownloadAsync(Guid id, Guid ownerId)
    {
        var file = await _fileRepository.GetByIdAsync(id, ownerId);

        if (file is null || !File.Exists(file.StoragePath))
            throw new NotFoundException($"File {id} not found");

        var stream = File.OpenRead(file.StoragePath);

        await _activityRepository.CreateAsync(ownerId, "DOWNLOAD", "file", file.Id, file.Name, file.FolderId, file.SizeBytes);

        return (stream, file.MimeType, file.Name);
    }

    public async Task DeleteAsync(Guid id, Guid ownerId)
    {
        var file = await _fileRepository.GetByIdAsync(id, ownerId);

        if (file is null)
            throw new NotFoundException($"File {id} not found");

        using var connection = _db.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        await _fileRepository.DeleteAsync(id, ownerId, connection, transaction);

        await _userRepository.UpdateUsedBytesAsync(ownerId, -file.SizeBytes, connection, transaction);

        await _activityRepository.CreateAsync(ownerId, "DELETE", "file", file.Id, file.Name, file.FolderId, file.SizeBytes, connection, transaction);

        transaction.Commit();

        File.Delete(file.StoragePath);
    }

    public async Task<FileDto> RenameAsync(Guid id, string name, Guid ownerId)
    {
        var file = await _fileRepository.GetByIdAsync(id, ownerId);

        if (file is null)
            throw new NotFoundException($"File {id} not found");

        await _fileRepository.RenameAsync(id, ownerId, name);

        file = await _fileRepository.GetByIdAsync(id, ownerId);

        await _activityRepository.CreateAsync(ownerId, "RENAME", "file", file!.Id, file.Name, file.FolderId, file.SizeBytes);

        return FileDto.FromModel(file!);
    }
    public async Task<FileDto> MoveAsync(Guid id, Guid? folderId, Guid ownerId)
    {
        var file = await _fileRepository.GetByIdAsync(id, ownerId);

        if (file is null)
            throw new NotFoundException($"File {id} not found");

        if (folderId.HasValue)
        {
            var folder = await _folderRepository.GetByIdAsync(folderId.Value, ownerId);
            if (folder is null)
                throw new NotFoundException($"Folder {folderId} not found");
        }

        await _fileRepository.MoveAsync(id, ownerId, folderId);

        file = await _fileRepository.GetByIdAsync(id, ownerId);

        await _activityRepository.CreateAsync(ownerId, "MOVE", "file", file!.Id, file.Name, file.FolderId, file.SizeBytes);

        return FileDto.FromModel(file!);
    }

    public async Task<FileDto> GetByIdAsync(Guid id, Guid ownerId)
    {
        var file = await _fileRepository.GetByIdAsync(id, ownerId);
        if (file is null)
            throw new NotFoundException($"File {id} not found");

        return FileDto.FromModel(file);
    }

    public async Task<IEnumerable<FileDto>> GetByFolderIdAsync(Guid? folderId, Guid ownerId)
    {
        var files = await _fileRepository.GetByFolderIdAsync(folderId, ownerId);
        return files.Select(FileDto.FromModel);
    }
}
