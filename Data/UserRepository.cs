using System.Data;
using Dapper;
using RaspiStorage.Models;

namespace RaspiStorage.Data;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);
    Task<User?> GetByUsernameAsync(string username);
    Task<long?> UpdateUsedBytesAsync(Guid id, long delta, IDbConnection? connection = null, IDbTransaction? transaction = null);
}

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionHandler _db;

    public UserRepository(IDbConnectionHandler db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var connection = _db.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE id = @Id",
            new { Id = id }
        );
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var connection = _db.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<User>(
            "SELECT * FROM users WHERE username = @Username",
            new { Username = username }
        );
    }

    public async Task<long?> UpdateUsedBytesAsync(Guid id, long delta, IDbConnection? connection = null, IDbTransaction? transaction = null)
    {
        var conn = connection ?? _db.CreateConnection();

        try
        {
            return await conn.QuerySingleOrDefaultAsync<long?>(
                "UPDATE users SET used_bytes = GREATEST(used_bytes+@Delta, 0) WHERE id = @Id AND used_bytes + @Delta <= quota_bytes RETURNING used_bytes",
                new { Delta = delta, Id = id },
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
