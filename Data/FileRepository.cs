using System.Data;
using Dapper;
using Npgsql;
using RaspiStorage.Exceptions;
using RaspiStorage.Models;

namespace RaspiStorage.Data;

public interface IFileRepository
{
    Task<FileEntry> CreateAsync(string name, Guid? folderId, Guid ownerId, long sizeBytes, string? mimeType, string storagePath, IDbConnection? connection = null, IDbTransaction? transaction = null);
    Task<FileEntry?> GetByIdAsync(Guid id, Guid ownerId);
    Task<IEnumerable<FileEntry>> GetByFolderIdAsync(Guid? folderId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
    Task RenameAsync(Guid id, Guid ownerId, string name);
    Task MoveAsync(Guid id, Guid ownerId, Guid? folderId);
    Task DeleteAsync(Guid id, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
}

public class FileRepository : IFileRepository
{
    private readonly IDbConnectionHandler _db;

    public FileRepository(IDbConnectionHandler db)
    {
        _db = db;
    }

    public async Task<FileEntry> CreateAsync(string name, Guid? folderId, Guid ownerId, long sizeBytes, string? mimeType, string storagePath, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conn = connection ?? _db.CreateConnection();

        try
        {
            return await conn.QuerySingleAsync<FileEntry>(
                "INSERT INTO files(name, owner_id, folder_id, size_bytes, mime_type, storage_path) VALUES(@Name, @OwnerId, @FolderId, @SizeBytes, @MimeType, @StoragePath) RETURNING *",
                new { Name = name, OwnerId = ownerId, FolderId = folderId, SizeBytes = sizeBytes, MimeType = mimeType, StoragePath = storagePath },
                transaction
            );
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            if (ex.ConstraintName == "files_unique_name_per_folder")
                throw new ConflictException($"File named '{name}' already exists in this location");

            throw;
        }
        finally
        {
            if (connection is null)
                conn.Dispose();
        }
    }

    public async Task<FileEntry?> GetByIdAsync(Guid id, Guid ownerId)
    {
        using var connection = _db.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<FileEntry>(
            "SELECT * FROM files WHERE id = @Id AND owner_id = @OwnerId",
            new { Id = id, OwnerId = ownerId }
        );
    }

    public async Task<IEnumerable<FileEntry>> GetByFolderIdAsync(Guid? folderId, Guid ownerId, IDbConnection? connection, IDbTransaction? transaction)
    {
        var conn = connection ?? _db.CreateConnection();
        try
        {
            return await conn.QueryAsync<FileEntry>(
                "SELECT * FROM files WHERE owner_id = @OwnerId AND (@FolderId IS NULL AND folder_id IS NULL OR folder_id = @FolderId)",
                new { FolderId = folderId, OwnerId = ownerId },
                transaction
            );
        }
        finally
        {
            if (connection is null)
                conn.Dispose();
        }
    }

    public async Task RenameAsync(Guid id, Guid ownerId, string name)
    {
        using var connection = _db.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE files SET name = @Name WHERE id = @Id AND owner_id = @OwnerId",
            new { Id = id, OwnerId = ownerId, Name = name }
        );
    }

    public async Task MoveAsync(Guid id, Guid ownerId, Guid? folderId)
    {
        using var connection = _db.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE files SET folder_id = @FolderId WHERE id = @Id AND owner_id = @OwnerId",
            new { Id = id, OwnerId = ownerId, FolderId = folderId }
        );
    }

    public async Task DeleteAsync(Guid id, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conn = connection ?? _db.CreateConnection();

        try
        {
            await conn.ExecuteAsync(
                "DELETE FROM files WHERE id = @Id AND owner_id = @OwnerId",
                new { Id = id, OwnerId = ownerId },
                transaction
            );
        }
        finally
        {
            if (connection is null)
                conn.Dispose();
        }
    }
}
