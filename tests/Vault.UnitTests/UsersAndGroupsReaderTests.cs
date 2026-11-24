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
    [Trait("Criteria", "UC-13.AC1, UC-13.AC4, UC-07.AC1")]
    public async Task Entries_are_split_into_system_and_custom_with_the_groups_of_each_user_and_only_the_two_calls_are_made()
    {
        var omv = Omv(Users, Groups);

        var result = await Reader(omv).ReadAsync(null);

        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Equal(["ana", "kid"], result.Custom.Users.Select(u => u.Username));
        Assert.Equal(["Ana Lopez", null], result.Custom.Users.Select(u => u.FullName));
        Assert.Equal([["family"], ["family"]], result.Custom.Users.Select(u => u.Groups));
        Assert.Equal(["family", "admins"], result.Custom.Groups.Select(g => g.Name));
        Assert.Equal(["ana", "kid"], result.Custom.Groups[0].Members);
        Assert.Equal(["bot"], result.System.Users.Select(u => u.Username));
        Assert.Equal([["robots"]], result.System.Users.Select(u => u.Groups));
        Assert.Equal(["robots"], result.System.Groups.Select(g => g.Name));
        Assert.Equal([OmvRpcCall.Users, OmvRpcCall.Groups], omv.Calls.Select(c => c.Call));
        Assert.Equal("basic", omv.Calls[0].Parameters.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("FAM", new string[0], new[] { "family" }, new string[0], new string[0])]
    [InlineData("an", new[] { "ana" }, new string[0], new string[0], new string[0])]
    [InlineData("  bo ", new string[0], new string[0], new[] { "bot" }, new[] { "robots" })]
    [InlineData("ROBOT", new string[0], new string[0], new string[0], new[] { "robots" })]
    [InlineData("a", new[] { "ana" }, new[] { "family", "admins" }, new string[0], new string[0])]
    [Trait("Criteria", "UC-13.AC5")]
    public async Task A_name_keeps_only_the_users_and_groups_that_contain_it_ignoring_case(
        string name, string[] customUsers, string[] customGroups, string[] systemUsers, string[] systemGroups)
    {
        var result = await Reader(Omv(Users, Groups)).ReadAsync(name);

        Assert.True(result.Ok);
        Assert.Equal(customUsers, result.Custom.Users.Select(u => u.Username));
        Assert.Equal(customGroups, result.Custom.Groups.Select(g => g.Name));
        Assert.Equal(systemUsers, result.System.Users.Select(u => u.Username));
        Assert.Equal(systemGroups, result.System.Groups.Select(g => g.Name));
        Assert.Null(result.Message);
    }

    [Fact]
    [Trait("Criteria", "UC-13.AC5")]
    public async Task A_filtered_user_still_lists_all_its_groups()
    {
        var result = await Reader(Omv(Users, Groups)).ReadAsync("kid");

        var kid = Assert.Single(result.Custom.Users);
        Assert.Equal(["family"], kid.Groups);
        Assert.Empty(result.Custom.Groups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Criteria", "UC-13.AC5")]
    public async Task A_missing_or_blank_name_does_not_filter(string? name)
    {
        var result = await Reader(Omv(Users, Groups)).ReadAsync(name);

        Assert.Equal(2, result.Custom.Users.Count);
        Assert.Single(result.System.Users);
    }

    [Fact]
    [Trait("Criteria", "UC-13.AC6")]
    public async Task A_name_nothing_matches_gives_two_empty_blocks_and_a_message_that_says_what_to_do()
    {
        var result = await Reader(Omv(Users, Groups)).ReadAsync("zzz");

        Assert.True(result.Ok);
        Assert.True(result.System.IsEmpty);
        Assert.True(result.Custom.IsEmpty);
        Assert.Contains("'zzz'", result.Message, StringComparison.Ordinal);
        Assert.Contains("leave the name out", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-13.AC2")]
    public async Task A_field_OMV_does_not_send_is_empty_and_the_entry_is_custom()
    {
        var result = await Reader(Omv("""[{"name":"ana"}]""", """{"total":1,"data":[{"name":"family"}]}""")).ReadAsync(null);

        Assert.True(result.Ok);
        Assert.True(result.System.IsEmpty);
        var user = Assert.Single(result.Custom.Users);
        Assert.Equal(("ana", null), (user.Username, user.FullName));
        Assert.Empty(user.Groups);
        Assert.Equal("family", Assert.Single(result.Custom.Groups).Name);
    }

    [Theory]
    [InlineData("""{"name":"x"}""", "[]")]
    [InlineData("""[{"comment":"no name"}]""", "[]")]
    [InlineData("[]", """[{"name":" "}]""")]
    [InlineData("[]", "\"text\"")]
    [Trait("Criteria", "UC-13.AC3, UC-02.AC4")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_not_a_hidden_entry(string users, string groups)
    {
        var result = await Reader(Omv(users, groups)).ReadAsync(null);

        Assert.Equal("omv_bad_response", result.Error!.Code);
        Assert.True(result.System.IsEmpty);
        Assert.True(result.Custom.IsEmpty);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1, UC-02.AC5, UC-02.AC6")]
    public async Task OMV_failures_and_unexpected_failures_become_errors_without_their_cause()
    {
        var unreachable = await Reader(FakeRpcClient.Failing(new OmvRpcException(OmvRpcErrorKind.Unreachable, "down"))).ReadAsync(null);
        var unexpected = await Reader(FakeRpcClient.Failing(new InvalidOperationException("boom"))).ReadAsync(null);

        Assert.Equal("omv_unreachable", unreachable.Error!.Code);
        Assert.Equal("failed", unexpected.Error!.Code);
        Assert.DoesNotContain("boom", unexpected.Error.Message, StringComparison.Ordinal);
    }
}
