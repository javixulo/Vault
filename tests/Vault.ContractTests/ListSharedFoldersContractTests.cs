using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_shared_folders</c>.</summary>
public sealed class ListSharedFoldersContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Exposes_list_shared_folders_with_a_snake_case_schema_and_two_optional_arguments()
    {
        var tool = Assert.Single(await _server.ToolsAsync(), t => t.Name == "list_shared_folders");

        var input = tool.JsonSchema.GetRawText();
        Assert.Contains("\"limit\"", input, StringComparison.Ordinal);
        Assert.Contains("\"offset\"", input, StringComparison.Ordinal);
        Assert.False(tool.JsonSchema.TryGetProperty("required", out var required) && required.GetArrayLength() > 0, "No argument is required.");
        var output = tool.ReturnJsonSchema!.Value.GetRawText();
        Assert.Contains("relative_path", output, StringComparison.Ordinal);
        Assert.Contains("has_more", output, StringComparison.Ordinal);
        Assert.DoesNotContain("hasMore", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-03.AC1")]
    public async Task Lists_the_folders_with_their_names_and_paths()
    {
        var result = await _server.CallAsync("list_shared_folders");

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        var folders = content.GetProperty("folders").EnumerateArray().ToList();
        Assert.Equal(["media", "photos", "backups"], folders.Select(f => f.GetProperty("name").GetString()));
        Assert.Equal("media/", folders[0].GetProperty("relative_path").GetString());
        Assert.Equal("Movies", folders[0].GetProperty("description").GetString());
        Assert.Equal(3, content.GetProperty("total").GetInt32());
        Assert.False(content.GetProperty("has_more").GetBoolean());
        Assert.Equal(["ShareMgmt.getList"], _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-03.AC2")]
    public async Task Accepts_limit_and_offset_as_numbers()
    {
        var result = await _server.CallAsync("list_shared_folders", new() { ["limit"] = 2, ["offset"] = 1 });

        Assert.NotEqual(true, result.IsError);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(201, null)]
    [InlineData(null, -1)]
    [Trait("Criteria", "UC-05.AC1, UC-05.AC2, UC-05.AC3")]
    public async Task Invalid_arguments_are_a_tool_error_that_says_what_to_fix_and_OMV_is_not_called(int? limit, int? offset)
    {
        var arguments = new Dictionary<string, object?>();
        if (limit is not null)
        {
            arguments["limit"] = limit;
        }

        if (offset is not null)
        {
            arguments["offset"] = offset;
        }

        var result = await _server.CallAsync("list_shared_folders", arguments);

        Assert.True(result.IsError);
        var error = result.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("invalid_arguments", error.GetProperty("code").GetString());
        Assert.Contains("Call the tool again", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(_server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-04.AC1")]
    public async Task No_folders_is_a_normal_result_with_a_message()
    {
        await using var server = VaultTestServer.Create(new StubOmv { Answer = _ => StubOmv.Parse("""{"total":0,"data":[]}""") });

        var result = await server.CallAsync("list_shared_folders");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(0, result.StructuredContent!.Value.GetProperty("folders").GetArrayLength());
        Assert.Contains("no shared folders", result.StructuredContent!.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
