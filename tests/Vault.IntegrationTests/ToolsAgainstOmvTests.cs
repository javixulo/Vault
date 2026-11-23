using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Shares;
using Vault.Core.SystemInfo;
using Xunit;

namespace Vault.IntegrationTests;

/// <summary>The two services with the real client and a stand-in of OMV: what OMV sends is what the client of the MCP gets.</summary>
public sealed class ToolsAgainstOmvTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Criteria", "UC-01.AC1, UC-03.AC1, UC-07.AC1")]
    public async Task Both_tools_work_and_only_the_documented_calls_reach_OMV()
    {
        await using var env = await OmvTestEnvironment.StartAsync();

        var status = await new SystemStatusReader(env.Client, NullLogger<SystemStatusReader>.Instance).ReadAsync(Token);
        var folders = await new SharedFolderLister(env.Client, NullLogger<SharedFolderLister>.Instance).ListAsync(null, null, Token);

        Assert.True(status.Ok);
        Assert.Equal("nas-test", status.Status!.Hostname);
        Assert.Equal(2_000_000_000, status.Status.MemoryUsedBytes);
        Assert.Equal(86_400, status.Status.UptimeSeconds);
        Assert.True(folders.Ok);
        Assert.Equal(["media", "photos", "backups"], folders.Folders.Select(f => f.Name));
        Assert.Equal(["Session.login", "System.getInformation", "ShareMgmt.getList"], env.Server.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-10.AC1, UC-10.AC2, UC-11.AC1, UC-07.AC1")]
    public async Task Folder_permissions_are_read_by_name_and_only_the_documented_calls_reach_OMV()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        var reader = new FolderPermissionsReader(env.Client, NullLogger<FolderPermissionsReader>.Instance);

        var media = await reader.ReadAsync("media", Token);
        var photos = await reader.ReadAsync("photos", Token);
        var missing = await reader.ReadAsync("nope", Token);

        // OMV lists users first, then groups, and the fake leaves out those with perms null.
        Assert.Equal(["read_only", "deny", "read_write"], media.Privileges.Select(p => p.Access));
        Assert.Equal(["kid", "guest", "family"], media.Privileges.Select(p => p.SubjectName));
        Assert.True(photos.Ok);
        Assert.Empty(photos.Privileges);
        Assert.NotNull(photos.Message);
        Assert.Equal("not_found", missing.Error!.Code);
        Assert.Equal(
            ["Session.login", "ShareMgmt.getList", "ShareMgmt.getPrivileges", "ShareMgmt.getList", "ShareMgmt.getPrivileges", "ShareMgmt.getList"],
            env.Server.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-03.AC2, UC-03.AC3")]
    public async Task A_page_of_the_list_says_whether_there_is_more()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        var lister = new SharedFolderLister(env.Client, NullLogger<SharedFolderLister>.Instance);

        var first = await lister.ListAsync(2, 0, Token);
        var last = await lister.ListAsync(2, 2, Token);

        Assert.Equal(["media", "photos"], first.Folders.Select(f => f.Name));
        Assert.True(first.HasMore);
        Assert.Equal(["backups"], last.Folders.Select(f => f.Name));
        Assert.False(last.HasMore);
        Assert.Equal(3, last.Total);
    }

    [Fact]
    [Trait("Criteria", "UC-04.AC1")]
    public async Task A_NAS_with_no_shared_folders_gives_an_empty_result()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        env.Server.SharedFolders.Clear();

        var result = await new SharedFolderLister(env.Client, NullLogger<SharedFolderLister>.Instance).ListAsync(null, null, Token);

        Assert.True(result.Ok);
        Assert.Empty(result.Folders);
        Assert.NotNull(result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC2")]
    public async Task An_account_that_OMV_forbids_a_call_is_told_so_and_the_session_is_not_renewed_for_nothing()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        env.Server.ForbiddenCalls.Add("ShareMgmt.getList");

        var result = await new SharedFolderLister(env.Client, NullLogger<SharedFolderLister>.Instance).ListAsync(null, null, Token);

        Assert.Equal("omv_auth_failed", result.Error!.Code);
        Assert.Contains("does not allow it this call", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(1, env.Server.Calls.Count(c => c == "Session.login"));
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC2, UC-02.AC6")]
    public async Task Wrong_credentials_are_an_error_that_does_not_show_them()
    {
        await using var env = await OmvTestEnvironment.StartAsync(passwordOverride: "the-wrong-password");

        var result = await new SystemStatusReader(env.Client, NullLogger<SystemStatusReader>.Instance).ReadAsync(Token);

        Assert.Equal("omv_auth_failed", result.Error!.Code);
        Assert.DoesNotContain("the-wrong-password", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(env.Server.Username, result.Error.Message, StringComparison.Ordinal);
    }
}
