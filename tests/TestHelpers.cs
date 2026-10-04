using System.Net.Http.Headers;
using System.Text;
using Dapper;
using Npgsql;

namespace RaspiStorage.Tests;

public static class TestHelpers
{
    public static MultipartFormDataContent FileForm(string fileName, string content)
    {
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { fileContent, "file", fileName } };
    }

    public static string UniqueContent() => $"test content {Guid.NewGuid()}";

    public static Task<long> GetUsedBytesAsync(NpgsqlConnection db) =>
        db.QuerySingleAsync<long>("SELECT used_bytes FROM users WHERE id = @Id", new { Id = TestConstants.OwnerId });

    public static async Task ResetStateAsync(NpgsqlConnection db)
    {
        await db.ExecuteAsync("DELETE FROM activity");
        await db.ExecuteAsync("DELETE FROM files");
        await db.ExecuteAsync("DELETE FROM folders");
        await db.ExecuteAsync(
            "UPDATE users SET quota_bytes = 10737418240, used_bytes = 0 WHERE id = @Id",
            new { Id = TestConstants.OwnerId }
        );

        if (Directory.Exists(TestConstants.TestStoragePath))
            Directory.Delete(TestConstants.TestStoragePath, recursive: true);

        Directory.CreateDirectory(TestConstants.TestStoragePath);
    }
}
