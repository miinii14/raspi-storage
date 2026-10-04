using System.Data;
using Dapper;
using Npgsql;
using RaspiStorage.Exceptions;
using RaspiStorage.Models;

namespace RaspiStorage.Data;

public interface IFolderRepository
{
    Task<Folder?> GetByIdAsync(Guid id, Guid ownerId);
    Task<IEnumerable<Folder>> GetByParentIdAsync(Guid? parentId, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
    Task<Folder> CreateAsync(string name, Guid ownerId, Guid? parentId);
    Task RenameAsync(Guid id, Guid ownerId, string name);
    Task MoveAsync(Guid id, Guid ownerId, Guid? parentId);
    Task DeleteAsync(Guid id, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null);
}

public class FolderRepository : IFolderRepository
{
    private readonly IDbConnectionHandler _db;

    public FolderRepository(IDbConnectionHandler db)
    {
        _db = db;
    }

    public async Task<Folder?> GetByIdAsync(Guid id, Guid ownerId)
    {
        using var connection = _db.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Folder>(
            "SELECT * FROM folders WHERE id = @Id AND owner_id = @OwnerId",
            new { Id = id, OwnerId = ownerId }
        );
    }

    public async Task<IEnumerable<Folder>> GetByParentIdAsync(Guid? parentId, Guid ownerId, IDbConnection? connection, IDbTransaction? transaction)
    {
        var conn = connection ?? _db.CreateConnection();
        try
        {
            return await conn.QueryAsync<Folder>(
                "SELECT * FROM folders WHERE owner_id = @OwnerId AND (@ParentId IS NULL AND parent_id IS NULL OR parent_id = @ParentId)",
                new { ParentId = parentId, OwnerId = ownerId },
                transaction
            );
        }
        finally
        {
            if (connection is null)
                conn.Dispose();
        }
    }

    public async Task<Folder> CreateAsync(string name, Guid ownerId, Guid? parentId)
    {
        using var connection = _db.CreateConnection();

        try
        {
            return await connection.QuerySingleAsync<Folder>(
                "INSERT INTO folders(name, owner_id, parent_id) VALUES (@Name, @OwnerId, @ParentId) RETURNING *",
                new { Name = name, OwnerId = ownerId, ParentId = parentId }
            );
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            if (ex.ConstraintName == "folders_unique_name_per_parent")
                throw new ConflictException($"Folder named '{name}' already exists in this location");

            throw;
        }
    }

    public async Task RenameAsync(Guid id, Guid ownerId, string name)
    {
        using var connection = _db.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE folders SET name = @Name WHERE id = @Id AND owner_id = @OwnerId",
            new { Name = name, Id = id, OwnerId = ownerId }
        );
    }

    public async Task MoveAsync(Guid id, Guid ownerId, Guid? parentId)
    {
        using var connection = _db.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE folders SET parent_id = @ParentId WHERE id = @Id AND owner_id = @OwnerId",
            new { ParentId = parentId, Id = id, OwnerId = ownerId }
        );
    }

    public async Task DeleteAsync(Guid id, Guid ownerId, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conn = connection ?? _db.CreateConnection();
        try
        {
            await conn.ExecuteAsync(
                "DELETE FROM folders WHERE id = @Id AND owner_id = @OwnerId",
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
