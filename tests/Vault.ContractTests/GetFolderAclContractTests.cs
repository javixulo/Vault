using Vault.Core.Rpc;
using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>get_folder_acl</c>.</summary>
public sealed class GetFolderAclContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Exposes_get_folder_acl_with_one_argument_named_folder_and_a_snake_case_output()
    {
        var tool = Assert.Single(await _server.ToolsAsync(), t => t.Name == "get_folder_acl");

        Assert.Contains("\"folder\"", tool.JsonSchema.GetRawText(), StringComparison.Ordinal);
        var output = tool.ReturnJsonSchema!.Value.GetRawText();
        Assert.Contains("owner_permissions", output, StringComparison.Ordinal);
        Assert.Contains("subject_name", output, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerPermissions", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-20.AC1")]
    public async Task Returns_the_base_permissions_and_each_named_entry()
    {
        var result = await _server.CallAsync("get_folder_acl", new() { ["folder"] = "media" });

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        Assert.Equal("media", content.GetProperty("folder").GetString());
        Assert.Equal("root", content.GetProperty("owner").GetString());
        Assert.Equal("rwx", content.GetProperty("owner_permissions").GetString());
        Assert.Equal("r-x", content.GetProperty("group_permissions").GetString());
        Assert.Equal("---", content.GetProperty("other_permissions").GetString());
        var entries = content.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["kid", "family"], entries.Select(e => e.GetProperty("subject_name").GetString()));
        Assert.Equal(["user", "group"], entries.Select(e => e.GetProperty("subject_type").GetString()));
        Assert.Equal(["r-x", "rwx"], entries.Select(e => e.GetProperty("permissions").GetString()));
        Assert.Equal(["ShareMgmt.getList", "ShareMgmt.getFileACL"], _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-20.AC2")]
    public async Task No_named_entry_is_a_normal_result_with_a_message()
    {
        await using var server = VaultTestServer.Create(new StubOmv
        {
            Answer = call => call == OmvRpcCall.SharedFolderAcl
                ? StubOmv.Parse("""{"owner":"root","group":"users","acl":{"user":7,"group":0,"other":0,"users":[],"groups":[]}}""")
                : new StubOmv().Answer(call),
        });

        var result = await server.CallAsync("get_folder_acl", new() { ["folder"] = "media" });

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(0, result.StructuredContent!.Value.GetProperty("entries").GetArrayLength());
        Assert.Contains("no extended ACL entry", result.StructuredContent!.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-21.AC1")]
    public async Task An_unknown_folder_is_a_not_found_tool_error_that_says_what_to_do()
    {
        var result = await _server.CallAsync("get_folder_acl", new() { ["folder"] = "nope" });

        Assert.True(result.IsError);
        var error = result.StructuredContent!.Value.GetProperty("error");
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.Contains("list_shared_folders", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [Trait("Criteria", "UC-22.AC1")]
    public async Task A_missing_or_blank_folder_is_an_invalid_arguments_tool_error_and_OMV_is_not_called(string? folder)
    {
        var arguments = new Dictionary<string, object?>();
        if (folder is not null)
        {
            arguments["folder"] = folder;
        }

        var result = await _server.CallAsync("get_folder_acl", arguments);

        Assert.True(result.IsError);
        Assert.Equal("invalid_arguments", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_server.Omv.Calls);
    }
}
