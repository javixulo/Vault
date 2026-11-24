using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_users_and_groups</c>.</summary>
public sealed class ListUsersAndGroupsContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-13.AC1, UC-13.AC4")]
    public async Task Returns_a_system_and_a_custom_block_each_with_users_and_groups_in_snake_case()
    {
        var result = await _server.CallAsync("list_users_and_groups");

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        var custom = content.GetProperty("custom");
        var users = custom.GetProperty("users").EnumerateArray().ToList();
        Assert.Equal(["ana", "kid"], users.Select(u => u.GetProperty("username").GetString()));
        Assert.Equal("Ana Lopez", users[0].GetProperty("full_name").GetString());
        Assert.Equal(["family"], users[0].GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
        var groups = custom.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(["family", "admins"], groups.Select(g => g.GetProperty("name").GetString()));
        Assert.Equal(["ana", "kid"], groups[0].GetProperty("members").EnumerateArray().Select(m => m.GetString()));
        var system = content.GetProperty("system");
        Assert.Equal(["root"], system.GetProperty("users").EnumerateArray().Select(u => u.GetProperty("username").GetString()));
        Assert.Equal(["root"], system.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
        Assert.Equal(["UserMgmt.enumerateAllUsers", "UserMgmt.enumerateAllGroups"], _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-13.AC5")]
    public async Task The_name_argument_filters_both_blocks()
    {
        var result = await _server.CallAsync("list_users_and_groups", new() { ["name"] = "FAM" });

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        Assert.Equal(0, content.GetProperty("custom").GetProperty("users").GetArrayLength());
        Assert.Equal(["family"], content.GetProperty("custom").GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("name").GetString()));
        Assert.Equal(0, content.GetProperty("system").GetProperty("groups").GetArrayLength());
    }

    [Fact]
    [Trait("Criteria", "UC-13.AC6")]
    public async Task A_name_nothing_matches_is_a_normal_result_with_a_message()
    {
        var result = await _server.CallAsync("list_users_and_groups", new() { ["name"] = "zzz" });

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("'zzz'", result.StructuredContent!.Value.GetProperty("message").GetString(), StringComparison.Ordinal);
    }
}
