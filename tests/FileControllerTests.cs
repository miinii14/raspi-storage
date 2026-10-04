using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using RaspiStorage.DTO;
using static RaspiStorage.Tests.TestHelpers;

namespace RaspiStorage.Tests;

[Collection("Sequential")]
public class FileControllerTests : IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly NpgsqlConnection _dbConnection;

    public FileControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
        _dbConnection = new NpgsqlConnection(factory.ConnectionString);
    }

    public Task InitializeAsync() => ResetStateAsync(_dbConnection);

    public async Task DisposeAsync() => await _dbConnection.DisposeAsync();

    private async Task<FileDto> UploadAsync(string fileName = "test.txt", Guid? folderId = null, string? content = null)
    {
        var url = folderId is null ? "/api/files" : $"/api/files?folderId={folderId}";
        var response = await _client.PostAsync(url, FileForm(fileName, content ?? UniqueContent()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FileDto>())!;
    }

    private async Task<FolderDto> CreateFolderAsync(string name, Guid? parentId = null)
    {
        var response = await _client.PostAsync("/api/folders", JsonContent.Create(new { Name = name, ParentId = parentId }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    [Fact]
    public async Task Request_WithoutClientCertificate_ReturnsUnauthorized()
    {
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync("/api/files");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFiles_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/files");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_ReturnsCreatedAndSavesFileToDisk()
    {
        var content = UniqueContent();
        var response = await _client.PostAsync("/api/files", FileForm("test.txt", content));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(Directory.GetFiles(TestConstants.TestStoragePath), path => File.ReadAllText(path) == content);
    }

    [Fact]
    public async Task UploadFile_QuotaExceeded_ReturnsBadRequest()
    {
        await _dbConnection.ExecuteAsync("UPDATE users SET quota_bytes = 5 WHERE id = @Id", new { Id = TestConstants.OwnerId });

        var response = await _client.PostAsync("/api/files", FileForm("test.txt", UniqueContent()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await GetUsedBytesAsync(_dbConnection));
        Assert.Empty(Directory.GetFiles(TestConstants.TestStoragePath));
    }

    [Fact]
    public async Task UploadFile_DuplicateName_ReturnsConflict()
    {
        await UploadAsync("test.txt");

        var response = await _client.PostAsync("/api/files", FileForm("test.txt", UniqueContent()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(Directory.GetFiles(TestConstants.TestStoragePath));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a/b.txt")]
    [InlineData(" leading-space.txt")]
    public async Task UploadFile_InvalidName_ReturnsBadRequest(string fileName)
    {
        var response = await _client.PostAsync("/api/files", FileForm(fileName, UniqueContent()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(TestConstants.TestStoragePath));
    }

    [Fact]
    public async Task UploadFile_TooLongName_ReturnsBadRequest()
    {
        var response = await _client.PostAsync("/api/files", FileForm(new string('a', 252) + ".txt", UniqueContent()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_WithFolderId_ReturnsCreated()
    {
        var folder = await CreateFolderAsync("test-folder");

        var file = await UploadAsync(folderId: folder.Id);

        Assert.Equal(folder.Id, file.FolderId);
    }

    [Fact]
    public async Task UploadFile_WithUnknownFolderId_ReturnsNotFound()
    {
        var response = await _client.PostAsync($"/api/files?folderId={Guid.NewGuid()}", FileForm("test.txt", UniqueContent()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_UpdatesUsedBytes()
    {
        var content = UniqueContent();
        var file = await UploadAsync(content: content);

        Assert.Equal(file.SizeBytes, await GetUsedBytesAsync(_dbConnection));
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(content), file.SizeBytes);
    }

    [Fact]
    public async Task DownloadFile_ReturnsContent()
    {
        var content = UniqueContent();
        var file = await UploadAsync(content: content);

        var response = await _client.GetAsync($"/api/files/download/{file.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DownloadFile_Unknown_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/files/download/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_ReturnsNoContentAndRemovesBlob()
    {
        var content = UniqueContent();
        var file = await UploadAsync(content: content);

        var blob = Directory.GetFiles(TestConstants.TestStoragePath).Single(path => File.ReadAllText(path) == content);

        var response = await _client.DeleteAsync($"/api/files/{file.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(blob));
    }

    [Fact]
    public async Task DeleteFile_Unknown_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/files/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFile_UpdatesUsedBytes()
    {
        var file = await UploadAsync();
        Assert.Equal(file.SizeBytes, await GetUsedBytesAsync(_dbConnection));

        await _client.DeleteAsync($"/api/files/{file.Id}");

        Assert.Equal(0, await GetUsedBytesAsync(_dbConnection));
    }

    [Fact]
    public async Task DeleteFile_BlobMissingOnDisk_StillDeletesRecord()
    {
        var file = await UploadAsync();
        foreach (var path in Directory.GetFiles(TestConstants.TestStoragePath))
            File.Delete(path);

        var response = await _client.DeleteAsync($"/api/files/{file.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await GetUsedBytesAsync(_dbConnection));
    }

    [Fact]
    public async Task RenameFile_ReturnsOk()
    {
        var file = await UploadAsync();

        var response = await _client.PatchAsync($"/api/files/rename/{file.Id}", JsonContent.Create(new { Name = "test-new.txt" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.Equal("test-new.txt", renamed!.Name);
    }

    [Fact]
    public async Task RenameFile_Unknown_ReturnsNotFound()
    {
        var response = await _client.PatchAsync($"/api/files/rename/{Guid.NewGuid()}", JsonContent.Create(new { Name = "test-new.txt" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenameFile_ToTakenName_ReturnsConflict()
    {
        await UploadAsync("test1.txt");
        var second = await UploadAsync("test2.txt");

        var response = await _client.PatchAsync($"/api/files/rename/{second.Id}", JsonContent.Create(new { Name = "test1.txt" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RenameFile_InvalidName_ReturnsBadRequest()
    {
        var file = await UploadAsync();

        var response = await _client.PatchAsync($"/api/files/rename/{file.Id}", JsonContent.Create(new { Name = "a/b.txt" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveFile_ToFolder_ReturnsOk()
    {
        var file = await UploadAsync();
        var folder = await CreateFolderAsync("test-folder");

        var response = await _client.PatchAsync($"/api/files/move/{file.Id}", JsonContent.Create(new { FolderId = folder.Id }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.Equal(folder.Id, moved!.FolderId);
    }

    [Fact]
    public async Task MoveFile_ToRoot_ReturnsOk()
    {
        var folder = await CreateFolderAsync("test-folder");
        var file = await UploadAsync(folderId: folder.Id);

        var response = await _client.PatchAsync($"/api/files/move/{file.Id}", JsonContent.Create(new { FolderId = (Guid?)null }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.Null(moved!.FolderId);
    }

    [Fact]
    public async Task MoveFile_ToUnknownFolder_ReturnsNotFound()
    {
        var file = await UploadAsync();

        var response = await _client.PatchAsync($"/api/files/move/{file.Id}", JsonContent.Create(new { FolderId = Guid.NewGuid() }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetByFolderId_ReturnsOnlyThatFolder()
    {
        var folder = await CreateFolderAsync("test-folder");
        await UploadAsync("root.txt");
        for (int i = 0; i < 3; ++i)
            await UploadAsync($"test{i}.txt", folder.Id);

        var response = await _client.GetAsync($"/api/files?folderId={folder.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var files = await response.Content.ReadFromJsonAsync<List<FileDto>>();
        Assert.Equal(3, files!.Count);
        Assert.All(files, f => Assert.Equal(folder.Id, f.FolderId));
    }

    [Fact]
    public async Task GetById_ReturnsOk()
    {
        var file = await UploadAsync();

        var response = await _client.GetAsync($"/api/files/{file.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<FileDto>();
        Assert.Equal(file.Id, data!.Id);
    }

    [Fact]
    public async Task GetById_Unknown_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/files/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
