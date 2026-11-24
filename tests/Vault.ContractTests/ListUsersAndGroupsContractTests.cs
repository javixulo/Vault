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
    [Trait("Criteria", "UC-13.AC1")]
    public async Task Returns_the_users_with_their_groups_and_the_groups_with_their_members_in_snake_case()
    {
        var result = await _server.CallAsync("list_users_and_groups");

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        var users = content.GetProperty("users").EnumerateArray().ToList();
        Assert.Equal(["ana", "kid"], users.Select(u => u.GetProperty("username").GetString()));
        Assert.Equal("Ana Lopez", users[0].GetProperty("full_name").GetString());
        Assert.False(users[0].GetProperty("system").GetBoolean());
        Assert.Equal(["family"], users[0].GetProperty("groups").EnumerateArray().Select(g => g.GetString()));
        var groups = content.GetProperty("groups").EnumerateArray().ToList();
        Assert.Equal(["family", "admins"], groups.Select(g => g.GetProperty("name").GetString()));
        Assert.Equal(["ana", "kid"], groups[0].GetProperty("members").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(["UserMgmt.enumerateAllUsers", "UserMgmt.enumerateAllGroups"], _server.Omv.Calls);
    }
}
