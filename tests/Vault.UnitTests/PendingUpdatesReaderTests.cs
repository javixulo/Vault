using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.Updates;
using Xunit;

namespace Vault.UnitTests;

public sealed class PendingUpdatesReaderTests
{
    private static PendingUpdatesReader Reader(FakeRpcClient omv) => new(omv, NullLogger<PendingUpdatesReader>.Instance);

    private static FakeRpcClient Omv(string answer) => new() { Handler = _ => FakeRpcClient.Json(answer) };

    // Shaped like the real OMV 8.5.12 answer (Apt.enumerateUpgraded): a plain list of APT records, with oldversion empty for a new package.
    private const string Packages = """
        [{"name":"libparted2t64","package":"libparted2t64","version":"3.6-5+deb13u1","oldversion":"3.6-5","size":300276,"description":"disk partition manipulator"},
         {"name":"openmediavault-compose","package":"openmediavault-compose","version":"8.2.7","oldversion":"8.2.5"},
         {"name":"linux-image-7.3.4+deb13-amd64","package":"linux-image-7.3.4+deb13-amd64","version":"7.3.4-1~bpo13+1","oldversion":""}]
        """;

    [Fact]
    [Trait("Criteria", "UC-18.AC1, UC-07.AC1")]
    public async Task By_default_only_the_count_is_reported_and_only_that_call_is_made()
    {
        var omv = Omv(Packages);

        var result = await Reader(omv).ReadAsync(null);

        Assert.True(result.Ok);
        Assert.Equal(3, result.Count);
        Assert.Empty(result.Updates);
        Assert.Null(result.Message);
        Assert.Equal([OmvRpcCall.PendingUpdates], omv.Calls.Select(c => c.Call));
    }

    [Fact]
    [Trait("Criteria", "UC-18.AC2")]
    public async Task Detailed_lists_every_package_with_its_versions_and_a_new_package_has_no_current_version()
    {
        var result = await Reader(Omv(Packages)).ReadAsync(true);

        Assert.True(result.Ok);
        Assert.Equal(3, result.Count);
        Assert.Equal(["libparted2t64", "openmediavault-compose", "linux-image-7.3.4+deb13-amd64"], result.Updates.Select(u => u.Package));
        Assert.Equal(["3.6-5", "8.2.5", null], result.Updates.Select(u => u.CurrentVersion));
        Assert.Equal(["3.6-5+deb13u1", "8.2.7", "7.3.4-1~bpo13+1"], result.Updates.Select(u => u.CandidateVersion));
    }

    [Fact]
    [Trait("Criteria", "UC-18.AC2")]
    public async Task A_package_without_the_package_field_falls_back_to_its_name()
    {
        var result = await Reader(Omv("""{"total":1,"data":[{"name":"curl","version":"2","oldversion":"1"}]}""")).ReadAsync(true);

        Assert.Equal(["curl"], result.Updates.Select(u => u.Package));
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("\"text\"")]
    [InlineData("""[{"name":"curl","version":"2"},{"version":"3"}]""")]
    [InlineData("""[{"name":" ","package":""}]""")]
    [Trait("Criteria", "UC-18.AC3")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_with_no_partial_result(string answer)
    {
        var result = await Reader(Omv(answer)).ReadAsync(true);

        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Updates);
    }

    [Theory]
    [InlineData("[]", null)]
    [InlineData("[]", true)]
    [InlineData("""{"total":0,"data":[]}""", false)]
    [Trait("Criteria", "UC-19.AC1")]
    public async Task Nothing_pending_is_a_normal_result_with_a_message(string answer, bool? detailed)
    {
        var result = await Reader(Omv(answer)).ReadAsync(detailed);

        Assert.True(result.Ok);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Updates);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1")]
    public async Task OMV_being_unreachable_is_an_omv_unreachable_error()
    {
        var omv = new FakeRpcClient { Handler = _ => throw new OmvRpcException(OmvRpcErrorKind.Unreachable, "detail only for the logs") };

        var result = await Reader(omv).ReadAsync(null);

        Assert.Equal(ErrorCodes.OmvUnreachable, result.Error!.Code);
        Assert.DoesNotContain("detail only for the logs", result.Error.Message, StringComparison.Ordinal);
    }
}
