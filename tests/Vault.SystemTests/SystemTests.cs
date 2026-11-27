using Vault.TestSupport;
using Xunit;

namespace Vault.SystemTests;

/// <summary>
/// What only the whole image can show, spoken to as a real client would. The OMV is a stand-in inside the test process.
/// Skipped unless <c>VAULT_SYSTEM_IMAGE</c> names the image (for example <c>docker build -t vault-mcp . &amp;&amp;
/// VAULT_SYSTEM_IMAGE=vault-mcp dotnet test --project tests/Vault.SystemTests</c>).
/// </summary>
public sealed class SystemTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private static string UrlForContainer(FakeOmvServer omv) =>
        $"http://{VaultContainer.HostForContainer}:{new Uri(omv.Url).Port}";

    [Fact]
    [Trait("Criteria", "UC-01.AC1, UC-03.AC1, UC-07.AC1, UC-08.AC1")]
    public async Task Both_tools_answer_from_OMV_and_only_the_documented_calls_reach_it()
    {
        VaultContainer.RequireImage();
        await using var omv = await FakeOmvServer.StartAsync(host: "0.0.0.0");
        await using var vault = await VaultContainer.StartAsync(UrlForContainer(omv), omv.Username, omv.Password);

        var tools = await vault.Client.ListToolsAsync(cancellationToken: Token);
        var status = await vault.Client.CallToolAsync("system_status", cancellationToken: Token);
        var folders = await vault.Client.CallToolAsync("list_shared_folders", cancellationToken: Token);

        var permissions = await vault.Client.CallToolAsync("get_folder_permissions", new Dictionary<string, object?> { ["folder"] = "media" }, cancellationToken: Token);

        var acl = await vault.Client.CallToolAsync("get_folder_acl", new Dictionary<string, object?> { ["folder"] = "media" }, cancellationToken: Token);

        Assert.Equal(["system_status", "list_shared_folders", "get_folder_permissions", "get_folder_acl", "list_users_and_groups", "list_services", "list_disks", "list_filesystems", "list_scheduled_tasks", "list_pending_updates", "list_docker_containers"], tools.Select(t => t.Name));
        Assert.NotEqual(true, acl.IsError);
        Assert.Equal(2, acl.StructuredContent!.Value.GetProperty("entries").GetArrayLength());
        Assert.NotEqual(true, permissions.IsError);
        Assert.Equal(3, permissions.StructuredContent!.Value.GetProperty("privileges").GetArrayLength());
        Assert.NotEqual(true, status.IsError);
        Assert.Equal("nas-test", status.StructuredContent!.Value.GetProperty("status").GetProperty("hostname").GetString());
        Assert.NotEqual(true, folders.IsError);
        Assert.Equal(3, folders.StructuredContent!.Value.GetProperty("folders").GetArrayLength());
        Assert.All(omv.Calls, call => Assert.Contains(call, new[] { "Session.login", "System.getInformation", "ShareMgmt.getList", "ShareMgmt.getPrivileges", "ShareMgmt.getFileACL", "FolderBrowser.get" }));
    }

    [Fact]
    [Trait("Criteria", "UC-08.AC2, UC-02.AC1")]
    public async Task An_OMV_that_is_down_does_not_stop_the_container_and_the_tools_say_so()
    {
        VaultContainer.RequireImage();
        await using var vault = await VaultContainer.StartAsync($"http://{VaultContainer.HostForContainer}:1", "reader", "p");

        var result = await vault.Client.CallToolAsync("system_status", cancellationToken: Token);

        Assert.True(result.IsError);
        Assert.Equal("omv_unreachable", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC3")]
    public async Task Without_the_secret_the_container_stops_with_a_message_that_says_how_to_fix_it()
    {
        VaultContainer.RequireImage();

        var (exitCode, output) = await VaultContainer.RunWithoutSecretAsync("http://omv.invalid");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("password file", output, StringComparison.Ordinal);
        Assert.Contains("Docker secret", output, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC2, UC-02.AC6")]
    public async Task A_wrong_password_is_an_error_and_it_is_in_neither_the_result_nor_the_logs()
    {
        VaultContainer.RequireImage();
        await using var omv = await FakeOmvServer.StartAsync(host: "0.0.0.0");
        await using var vault = await VaultContainer.StartAsync(UrlForContainer(omv), omv.Username, "a-wrong-password-for-the-test");

        var result = await vault.Client.CallToolAsync("system_status", cancellationToken: Token);
        var logs = await vault.LogsAsync();

        Assert.True(result.IsError);
        Assert.Equal("omv_auth_failed", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("a-wrong-password-for-the-test", result.StructuredContent!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("a-wrong-password-for-the-test", logs, StringComparison.Ordinal);
    }
}
