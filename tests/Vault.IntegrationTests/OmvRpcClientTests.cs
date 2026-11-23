using Vault.Core.Rpc;
using Xunit;

namespace Vault.IntegrationTests;

/// <summary>The RPC client against a stand-in of OMV over real HTTP: login, the session cookie, renewal and each way OMV can fail.</summary>
public sealed class OmvRpcClientTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Criteria", "UC-06.AC1")]
    public async Task It_logs_in_once_and_reuses_the_session_for_the_next_calls()
    {
        await using var env = await OmvTestEnvironment.StartAsync();

        await env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token);
        await env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token);

        Assert.Equal(["Session.login", "System.getInformation", "System.getInformation"], env.Server.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-06.AC2")]
    public async Task An_expired_session_is_renewed_and_the_call_still_works()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        await env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token);
        env.Server.ExpireSessions();

        var response = await env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token);

        Assert.Equal("nas-test", response.GetProperty("hostname").GetString());
        Assert.Equal(
            ["Session.login", "System.getInformation", "System.getInformation", "Session.login", "System.getInformation"],
            env.Server.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-06.AC3")]
    public async Task Calls_that_find_the_session_expired_together_cause_a_single_new_login()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        await env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token);
        env.Server.ExpireSessions();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token)));

        Assert.Equal(2, env.Server.Calls.Count(c => c == "Session.login"));
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC2, UC-02.AC6")]
    public async Task A_refused_login_is_AuthFailed_and_the_password_is_not_in_the_error()
    {
        await using var env = await OmvTestEnvironment.StartAsync(passwordOverride: "the-wrong-password");

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token));

        Assert.Equal(OmvRpcErrorKind.AuthFailed, error.Kind);
        Assert.DoesNotContain("the-wrong-password", error.Message, StringComparison.Ordinal);
        Assert.Equal(["Session.login"], env.Server.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1")]
    public async Task A_server_that_is_not_there_is_Unreachable()
    {
        OmvTestEnvironment env;
        await using (env = await OmvTestEnvironment.StartAsync())
        {
            await env.Server.DisposeAsync();

            var error = await Assert.ThrowsAsync<OmvRpcException>(() => env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token));

            Assert.Equal(OmvRpcErrorKind.Unreachable, error.Kind);
        }
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC3")]
    public async Task An_RPC_error_of_OMV_carries_its_code()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        env.Server.FailWithRpcCode = 3000;

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => env.Client.CallAsync(OmvRpcCall.SharedFolders, new { start = 0, limit = 5 }, Token));

        Assert.Equal(OmvRpcErrorKind.RpcError, error.Kind);
        Assert.Equal(3000, error.RpcCode);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC4")]
    public async Task An_answer_that_is_not_JSON_is_a_BadResponse()
    {
        await using var env = await OmvTestEnvironment.StartAsync();
        env.Server.RawAnswer = "<html>502 Bad Gateway</html>";

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => env.Client.CallAsync(OmvRpcCall.SystemInformation, null, Token));

        Assert.Equal(OmvRpcErrorKind.BadResponse, error.Kind);
    }

    [Fact]
    public async Task VerifyAccess_logs_in_and_calls_nothing_else()
    {
        await using var env = await OmvTestEnvironment.StartAsync();

        await env.Client.VerifyAccessAsync(Token);

        Assert.Equal(["Session.login"], env.Server.Calls);
    }
}
