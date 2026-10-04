using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using RaspiStorage.DTO;
using static RaspiStorage.Tests.TestHelpers;

namespace RaspiStorage.Tests;

[Collection("Sequential")]
public class FolderControllerTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly NpgsqlConnection _dbConnection;

    public FolderControllerTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateAuthenticatedClient();
        _dbConnection = new NpgsqlConnection(factory.ConnectionString);
    }

    public Task InitializeAsync() => ResetStateAsync(_dbConnection);

    public async Task DisposeAsync() => await _dbConnection.DisposeAsync();

    private Task<HttpResponseMessage> PostFolderAsync(string? name, Guid? parentId = null) =>
        _client.PostAsync("/api/folders", JsonContent.Create(new { Name = name, ParentId = parentId }));

    private async Task<FolderDto> CreateFolderAsync(string name, Guid? parentId = null)
    {
        var response = await PostFolderAsync(name, parentId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FolderDto>())!;
    }

    private async Task<FileDto> UploadAsync(string fileName, Guid folderId)
    {
        var response = await _client.PostAsync($"/api/files?folderId={folderId}", FileForm(fileName, UniqueContent()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FileDto>())!;
    }

    private Task<HttpResponseMessage> MoveAsync(Guid id, Guid? folderId) =>
        _client.PatchAsync($"/api/folders/move/{id}", JsonContent.Create(new { FolderId = folderId }));

    [Fact]
    public async Task CreateFolder_NoParent_ReturnsCreated()
    {
        var folder = await CreateFolderAsync("test-folder");

        Assert.Equal("test-folder", folder.Name);
        Assert.Null(folder.ParentId);
    }

    [Fact]
    public async Task CreateFolder_WithParent_ReturnsCreated()
    {
        var parent = await CreateFolderAsync("test-folder");

        var child = await CreateFolderAsync("test-folder2", parent.Id);

        Assert.Equal(parent.Id, child.ParentId);
    }

    [Fact]
    public async Task CreateFolder_Duplicate_ReturnsConflictWithMessage()
    {
        await CreateFolderAsync("test-folder");

        var response = await PostFolderAsync("test-folder");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateFolder_SameNameInDifferentParents_ReturnsCreated()
    {
        var a = await CreateFolderAsync("a");
        var b = await CreateFolderAsync("b");

        await CreateFolderAsync("same", a.Id);
        await CreateFolderAsync("same", b.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("tab\tinside")]
    public async Task CreateFolder_InvalidName_ReturnsBadRequest(string? name)
    {
        var response = await PostFolderAsync(name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await _dbConnection.QuerySingleAsync<int>("SELECT COUNT(*) FROM folders"));
    }

    [Fact]
    public async Task CreateFolder_NameAtMaxLength_ReturnsCreated()
    {
        await CreateFolderAsync(new string('a', 255));
    }

    [Fact]
    public async Task CreateFolder_NameOverMaxLength_ReturnsBadRequest()
    {
        var response = await PostFolderAsync(new string('a', 256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRootFolders_ReturnsOnlyRoot()
    {
        var folderNames = new[] { "folder1", "folder2", "folder3" };
        var parent = await CreateFolderAsync(folderNames[0]);
        foreach (var name in folderNames.Skip(1))
            await CreateFolderAsync(name);
        await CreateFolderAsync("nested", parent.Id);

        var response = await _client.GetAsync("/api/folders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var folders = await response.Content.ReadFromJsonAsync<FolderDto[]>();
        Assert.Equal(folderNames.Length, folders!.Length);
        Assert.All(folders, f => Assert.Contains(f.Name, folderNames));
    }

    [Fact]
    public async Task GetByParentId_ReturnsOnlyChildren()
    {
        var parent = await CreateFolderAsync("parent-folder");
        await CreateFolderAsync("test-folder");

        var folderNames = new[] { "folder1", "folder2", "folder3" };
        foreach (var name in folderNames)
            await CreateFolderAsync(name, parent.Id);

        var response = await _client.GetAsync($"/api/folders?parentId={parent.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var folders = await response.Content.ReadFromJsonAsync<FolderDto[]>();
        Assert.Equal(folderNames.Length, folders!.Length);
        Assert.All(folders, f => Assert.Contains(f.Name, folderNames));
    }

    [Fact]
    public async Task GetFolderById_ReturnsOk()
    {
        var folder = await CreateFolderAsync("test-folder");

        var response = await _client.GetAsync($"/api/folders/{folder.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<FolderDto>();
        Assert.Equal("test-folder", data!.Name);
    }

    [Fact]
    public async Task GetFolderById_Unknown_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/folders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveFolder_ToRoot_ReturnsOk()
    {
        var parent = await CreateFolderAsync("test-folder");
        var folder = await CreateFolderAsync("test-folder2", parent.Id);

        var response = await MoveAsync(folder.Id, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await response.Content.ReadFromJsonAsync<FolderDto>();
        Assert.Null(moved!.ParentId);
    }

    [Fact]
    public async Task MoveFolder_ToAnotherFolder_ReturnsOk()
    {
        var parent = await CreateFolderAsync("test-folder");
        var folder = await CreateFolderAsync("test-folder2");

        var response = await MoveAsync(folder.Id, parent.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = await response.Content.ReadFromJsonAsync<FolderDto>();
        Assert.Equal(parent.Id, moved!.ParentId);
    }

    [Fact]
    public async Task MoveFolder_Unknown_ReturnsNotFound()
    {
        var response = await MoveAsync(Guid.NewGuid(), null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveFolder_ToUnknownFolder_ReturnsNotFound()
    {
        var folder = await CreateFolderAsync("test-folder");

        var response = await MoveAsync(folder.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveFolder_IntoItself_ReturnsConflict()
    {
        var folder = await CreateFolderAsync("test-folder");

        var response = await MoveAsync(folder.Id, folder.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task MoveFolder_IntoOwnDescendant_ReturnsConflict()
    {
        var top = await CreateFolderAsync("top");
        var middle = await CreateFolderAsync("middle", top.Id);
        var bottom = await CreateFolderAsync("bottom", middle.Id);

        var response = await MoveAsync(top.Id, bottom.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var unchanged = await _client.GetFromJsonAsync<FolderDto>($"/api/folders/{top.Id}");
        Assert.Null(unchanged!.ParentId);
    }

    [Fact]
    public async Task DeleteFolder_Unknown_ReturnsNotFound()
    {
        var response = await _client.DeleteAsync($"/api/folders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFolder_ReturnsNoContentAndRemovesBlob()
    {
        var folder = await CreateFolderAsync("test-folder");
        await UploadAsync("test.txt", folder.Id);
        Assert.NotEmpty(Directory.GetFiles(TestConstants.TestStoragePath));

        var response = await _client.DeleteAsync($"/api/folders/{folder.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        response = await _client.GetAsync($"/api/folders/{folder.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Directory.GetFiles(TestConstants.TestStoragePath));
    }

    [Fact]
    public async Task DeleteFolder_RecursivelyDeletesSubfoldersFilesAndQuota()
    {
        var parent = await CreateFolderAsync("parent-folder");
        var child = await CreateFolderAsync("child-folder", parent.Id);
        var grandchild = await CreateFolderAsync("grandchild-folder", child.Id);

        var uploaded = new[]
        {
            await UploadAsync("file1.txt", parent.Id),
            await UploadAsync("file2.txt", child.Id),
            await UploadAsync("file3.txt", grandchild.Id)
        };

        var outside = await CreateFolderAsync("outside");
        var kept = await UploadAsync("kept.txt", outside.Id);

        Assert.Equal(4, Directory.GetFiles(TestConstants.TestStoragePath).Length);
        Assert.Equal(uploaded.Sum(f => f.SizeBytes) + kept.SizeBytes, await GetUsedBytesAsync(_dbConnection));

        var response = await _client.DeleteAsync($"/api/folders/{parent.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        foreach (var id in new[] { child.Id, grandchild.Id })
        {
            response = await _client.GetAsync($"/api/folders/{id}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Single(Directory.GetFiles(TestConstants.TestStoragePath));
        Assert.Equal(kept.SizeBytes, await GetUsedBytesAsync(_dbConnection));
        Assert.Equal(1, await _dbConnection.QuerySingleAsync<int>("SELECT COUNT(*) FROM files"));
    }

    [Fact]
    public async Task DeleteFolder_IsRecordedInActivity()
    {
        var folder = await CreateFolderAsync("test-folder");

        await _client.DeleteAsync($"/api/folders/{folder.Id}");

        var activity = await _client.GetFromJsonAsync<ActivityDto[]>("/api/activity");
        Assert.Contains(activity!, a => a.Action == "DELETE" && a.TargetId == folder.Id);
    }

    [Fact]
    public async Task RenameFolder_ReturnsOk()
    {
        var folder = await CreateFolderAsync("test-folder");

        var response = await _client.PatchAsync($"/api/folders/rename/{folder.Id}", JsonContent.Create(new { Name = "renamed-folder" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var renamed = await response.Content.ReadFromJsonAsync<FolderDto>();
        Assert.Equal("renamed-folder", renamed!.Name);
    }

    [Fact]
    public async Task RenameFolder_Unknown_ReturnsNotFound()
    {
        var response = await _client.PatchAsync($"/api/folders/rename/{Guid.NewGuid()}", JsonContent.Create(new { Name = "renamed-folder" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RenameFolder_ToTakenName_ReturnsConflict()
    {
        await CreateFolderAsync("folder-1");
        var folder = await CreateFolderAsync("folder-2");

        var response = await _client.PatchAsync($"/api/folders/rename/{folder.Id}", JsonContent.Create(new { Name = "folder-1" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RenameFolder_InvalidName_ReturnsBadRequest()
    {
        var folder = await CreateFolderAsync("test-folder");

        var response = await _client.PatchAsync($"/api/folders/rename/{folder.Id}", JsonContent.Create(new { Name = "" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
