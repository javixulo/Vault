using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.Users;
using Xunit;

namespace Vault.UnitTests;

public sealed class UsersAndGroupsReaderTests
{
    // The shape a real OMV 8.5.12 sends (checked 2026-11-24): users have comment, groups (names) and system; groups have members and system.
    private const string Users = """[{"name":"ana","uid":1000,"comment":"Ana Lopez","shell":"/bin/sh","groups":["family"],"system":false},{"name":"kid","comment":"","groups":[],"system":false},{"name":"bot","groups":["robots"],"system":true}]""";
    private const string Groups = """[{"name":"family","gid":1000,"members":["ana","kid"],"system":false},{"name":"admins","members":[]},{"name":"robots","system":true}]""";

    private static UsersAndGroupsReader Reader(FakeRpcClient omv) => new(omv, NullLogger<UsersAndGroupsReader>.Instance);

    private static FakeRpcClient Omv(string users, string groups) => new()
    {
        Handler = call => FakeRpcClient.Json(call == OmvRpcCall.Users ? users : groups),
    };

    [Fact]
    [Trait("Criteria", "UC-13.AC1, UC-07.AC1")]
    public async Task Every_user_and_group_is_mapped_with_the_groups_of_each_user_and_only_the_two_calls_are_made()
    {
        var omv = Omv(Users, Groups);

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Equal(["ana", "kid", "bot"], result.Users.Select(u => u.Username));
        Assert.Equal(["Ana Lopez", null, null], result.Users.Select(u => u.FullName));
        Assert.Equal([false, false, true], result.Users.Select(u => u.System));
        Assert.Equal([false, null, true], result.Groups.Select(g => g.System));
        Assert.Equal([["family"], ["family"], ["robots"]], result.Users.Select(u => u.Groups));
        Assert.Equal(["family", "admins", "robots"], result.Groups.Select(g => g.Name));
        Assert.Equal(["ana", "kid"], result.Groups[0].Members);
        Assert.Empty(result.Groups[2].Members);
        Assert.Equal([OmvRpcCall.Users, OmvRpcCall.Groups], omv.Calls.Select(c => c.Call));
        Assert.Equal("basic", omv.Calls[0].Parameters.GetProperty("detail").GetString());
    }

    [Fact]
    [Trait("Criteria", "UC-13.AC2")]
    public async Task A_field_OMV_does_not_send_is_empty_and_the_call_is_not_an_error()
    {
        var result = await Reader(Omv("""[{"name":"ana"}]""", """{"total":1,"data":[{"name":"family"}]}""")).ReadAsync();

        Assert.True(result.Ok);
        var user = Assert.Single(result.Users);
        Assert.Equal(("ana", null, null), (user.Username, user.FullName, user.System));
        Assert.Empty(user.Groups);
        Assert.Equal("family", Assert.Single(result.Groups).Name);
    }

    [Theory]
    [InlineData("""{"name":"x"}""", "[]")]
    [InlineData("""[{"comment":"no name"}]""", "[]")]
    [InlineData("[]", """[{"name":" "}]""")]
    [InlineData("[]", "\"text\"")]
    [Trait("Criteria", "UC-13.AC3, UC-02.AC4")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_not_a_hidden_entry(string users, string groups)
    {
        var result = await Reader(Omv(users, groups)).ReadAsync();

        Assert.Equal("omv_bad_response", result.Error!.Code);
        Assert.Empty(result.Users);
        Assert.Empty(result.Groups);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1, UC-02.AC5, UC-02.AC6")]
    public async Task OMV_failures_and_unexpected_failures_become_errors_without_their_cause()
    {
        var unreachable = await Reader(FakeRpcClient.Failing(new OmvRpcException(OmvRpcErrorKind.Unreachable, "down"))).ReadAsync();
        var unexpected = await Reader(FakeRpcClient.Failing(new InvalidOperationException("boom"))).ReadAsync();

        Assert.Equal("omv_unreachable", unreachable.Error!.Code);
        Assert.Equal("failed", unexpected.Error!.Code);
        Assert.DoesNotContain("boom", unexpected.Error.Message, StringComparison.Ordinal);
    }
}
