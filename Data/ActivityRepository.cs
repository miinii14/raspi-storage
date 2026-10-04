using System.Data;
using Dapper;
using RaspiStorage.Models;

namespace RaspiStorage.Data;

public interface IActivityRepository
{
    Task CreateAsync(Guid ownerId, string action, string targetType, Guid targetId, string targetName, Guid? folderId, long? bytes, IDbConnection? connection = null, IDbTransaction? transaction = null);
    Task<IEnumerable<Activity>> GetByOwnerAsync(Guid ownerId, int limit);
}

public class ActivityRepository : IActivityRepository
{
    private readonly IDbConnectionHandler _db;

    public ActivityRepository(IDbConnectionHandler db)
    {
        _db = db;
    }

    public async Task CreateAsync(Guid ownerId, string action, string targetType, Guid targetId, string targetName, Guid? folderId, long? bytes, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conn = connection ?? _db.CreateConnection();

        try
        {
            await conn.ExecuteAsync(
                "INSERT INTO activity(owner_id, action, target_type, target_id, target_name, folder_id, bytes) VALUES(@OwnerId, @Action, @TargetType, @TargetId, @TargetName, @FolderId, @Bytes)",
                new { OwnerId = ownerId, Action = action, TargetType = targetType, TargetId = targetId, TargetName = targetName, FolderId = folderId, Bytes = bytes },
                transaction
            );
        }
        finally
        {
            if (connection is null)
                conn.Dispose();
        }
    }

    public async Task<IEnumerable<Activity>> GetByOwnerAsync(Guid ownerId, int limit)
    {
        using var connection = _db.CreateConnection();
        return await connection.QueryAsync<Activity>(
            "SELECT * FROM activity WHERE owner_id = @OwnerId ORDER BY created_at DESC LIMIT @Limit",
            new { OwnerId = ownerId, Limit = limit }
        );
    }
}
