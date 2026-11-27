using Vault.Core.Rpc;
using Xunit;

namespace Vault.UnitTests;

/// <summary>The fixed list of RPC calls, the credentials and the endpoint: the pieces that keep Vault read-only and keep its secret.</summary>
public sealed class RpcBoundaryTests
{
    [Fact]
    [Trait("Criteria", "UC-07.AC1")]
    public void The_exposed_calls_are_exactly_the_documented_ones_and_login_is_not_among_them()
    {
        Assert.Equal(["System.getInformation", "ShareMgmt.getList", "ShareMgmt.getPrivileges", "ShareMgmt.getFileACL", "FolderBrowser.get", "UserMgmt.enumerateAllUsers", "UserMgmt.enumerateAllGroups", "Services.getStatus", "DiskMgmt.enumerateDevices", "Smart.getList", "FileSystemMgmt.enumerateFilesystems", "Cron.getList", "Apt.enumerateUpgraded", "Compose.getContainerListBg", "Compose.getServicesListBg", "Exec.getOutput"], OmvRpcCall.Exposed.Select(c => c.Name));
    }

    [Fact]
    [Trait("Criteria", "UC-07.AC1")]
    public void An_RPC_call_cannot_be_built_from_outside_the_class()
    {
        // The constructor is private: the only way to hold a call is one of the static fields.
        var constructors = typeof(OmvRpcCall).GetConstructors();
        Assert.Empty(constructors);
    }

    [Fact]
    [Trait("Criteria", "UC-07.AC1")]
    public void The_port_has_no_method_that_takes_a_service_or_method_name_as_text()
    {
        var texts = typeof(IOmvRpcClient).GetMethods().SelectMany(m => m.GetParameters()).Where(p => p.ParameterType == typeof(string));

        Assert.Empty(texts);
    }

    [Theory]
    [InlineData("p", "p", null)]
    [InlineData("p\n", "p", null)]
    [InlineData("p\r\n", "p", null)]
    [InlineData("p\n\n", "p\n", null)]
    [InlineData("p4ss \"w\rd\" {x} $y", "p4ss \"w\rd\" {x} $y", null)]
    [InlineData("", null, "it is empty")]
    [InlineData("\n", null, "it is empty")]
    [Trait("Criteria", "UC-09.AC4")]
    public void The_password_secret_is_plain_text_and_one_trailing_line_break_is_ignored(string content, string? password, string? problem)
    {
        var (found, foundProblem) = OmvCredentials.ParsePassword(content);

        Assert.Equal(problem, foundProblem);
        Assert.Equal(password, found);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC6")]
    public void The_password_is_not_in_the_text_form_of_the_credentials_nor_in_the_problem_found()
    {
        var credentials = new OmvCredentials("reader", "hunter2");
        var (_, problem) = OmvCredentials.ParsePassword("");

        Assert.DoesNotContain("hunter2", credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", problem, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("https://nas.local", "https://nas.local/rpc.php")]
    [InlineData("https://nas.local/", "https://nas.local/rpc.php")]
    [InlineData(" http://192.168.1.10:8080/omv ", "http://192.168.1.10:8080/omv/rpc.php")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("nas.local", null)]
    [InlineData("ftp://nas.local", null)]
    [InlineData(null, null)]
    [Trait("Criteria", "UC-09.AC1, UC-09.AC2")]
    public void The_RPC_endpoint_is_the_base_url_plus_rpc_php_and_only_http_urls_count(string? baseUrl, string? expected)
    {
        Assert.Equal(expected, OmvEndpoint.RpcUri(baseUrl)?.ToString());
    }
}
