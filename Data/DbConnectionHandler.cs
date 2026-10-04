using System.Data;
using Npgsql;

namespace RaspiStorage.Data;

public interface IDbConnectionHandler
{
    IDbConnection CreateConnection();
}

public class DbConnectionHandler : IDbConnectionHandler
{
    private readonly string _connectionString;

    public DbConnectionHandler(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IDbConnection CreateConnection()
    {
        return new NpgsqlConnection(_connectionString);
    }
}
