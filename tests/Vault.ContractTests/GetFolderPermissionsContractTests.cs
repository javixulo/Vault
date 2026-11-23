using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>get_folder_permissions</c>.</summary>
public sealed class GetFolderPermissionsContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Exposes_get_folder_permissions_with_one_argument_named_folder_and_a_snake_case_output()
    {
        var tool = Assert.Single(await _server.ToolsAsync(), t => t.Name == "get_folder_permissions");

        Assert.Contains("\"folder\"", tool.JsonSchema.GetRawText(), StringComparison.Ordinal);
        var output = tool.ReturnJsonSchema!.Value.GetRawText();
        Assert.Contains("subject_type", output, StringComparison.Ordinal);
        Assert.Contains("subject_name", output, StringComparison.Ordinal);
        Assert.DoesNotContain("subjectName", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC1")]
    public async Task Returns_each_privilege_with_its_subject_and_access()
    {
        var result = await _server.CallAsync("get_folder_permissions", new() { ["folder"] = "media" });

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        Assert.Equal("media", content.GetProperty("folder").GetString());
        var privileges = content.GetProperty("privileges").EnumerateArray().ToList();
        Assert.Equal(["family", "kid", "guest"], privileges.Select(p => p.GetProperty("subject_name").GetString()));
        Assert.Equal(["group", "user", "user"], privileges.Select(p => p.GetProperty("subject_type").GetString()));
        Assert.Equal(["read_write", "read_only", "deny"], privileges.Select(p => p.GetProperty("access").GetString()));
        Assert.Equal(["ShareMgmt.getList", "ShareMgmt.getPrivileges"], _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC2")]
    public async Task No_explicit_privilege_is_a_normal_result_with_a_message()
    {
        await using var server = VaultTestServer.Create(new StubOmv { Answer = call => call.Method == "getPrivileges" ? StubOmv.Parse("[]") : new StubOmv().Answer(call) });

        var result = await server.CallAsync("get_folder_permissions", new() { ["folder"] = "media" });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(0, result.StructuredContent!.Value.GetProperty("privileges").GetArrayLength());
        Assert.Contains("default", result.StructuredContent!.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-11.AC1")]
    public async Task An_unknown_folder_is_a_not_found_tool_error_that_says_what_to_do()
    {
        var result = await _server.CallAsync("get_folder_permissions", new() { ["folder"] = "nope" });

        Assert.True(result.IsError);
        var error = result.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.Contains("list_shared_folders", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [Trait("Criteria", "UC-12.AC1")]
    public async Task A_missing_or_blank_folder_is_an_invalid_arguments_tool_error_and_OMV_is_not_called(string? folder)
    {
        var arguments = new Dictionary<string, object?>();
        if (folder is not null)
        {
            arguments["folder"] = folder;
        }

        var result = await _server.CallAsync("get_folder_permissions", arguments);

        Assert.True(result.IsError);
        Assert.Equal("invalid_arguments", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_server.Omv.Calls);
    }
}
