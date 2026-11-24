using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.Services;
using Xunit;

namespace Vault.UnitTests;

public sealed class ServicesReaderTests
{
    private static ServicesReader Reader(FakeRpcClient omv) => new(omv, NullLogger<ServicesReader>.Instance);

    private static FakeRpcClient Omv(string answer) => new() { Handler = _ => FakeRpcClient.Json(answer) };

    [Fact]
    [Trait("Criteria", "UC-14.AC1, UC-07.AC1")]
    public async Task Every_service_OMV_reports_is_listed_in_its_order_and_only_that_call_is_made()
    {
        var omv = Omv("""[{"name":"ssh","title":"SSH","enabled":true,"running":true},{"name":"nfs","title":"NFS","enabled":false,"running":false},{"name":"smb","enabled":1,"running":"0"}]""");

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Equal(["ssh", "nfs", "smb"], result.Services.Select(s => s.Name));
        Assert.Equal([true, false, true], result.Services.Select(s => s.Enabled));
        Assert.Equal([true, false, false], result.Services.Select(s => s.Running));
        Assert.Equal([OmvRpcCall.ServicesStatus], omv.Calls.Select(c => c.Call));
    }

    [Fact]
    [Trait("Criteria", "UC-14.AC2")]
    public async Task A_state_OMV_does_not_report_is_null_and_the_rest_is_reported()
    {
        var result = await Reader(Omv("""[{"name":"ssh","enabled":true},{"name":"smb","running":false}]""")).ReadAsync();

        Assert.True(result.Ok);
        Assert.Equal([true, null], result.Services.Select(s => s.Enabled));
        Assert.Equal([null, false], result.Services.Select(s => s.Running));
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("\"text\"")]
    [InlineData("""[{"name":"ssh","enabled":true},{"enabled":true}]""")]
    [InlineData("""[{"name":" ","enabled":true}]""")]
    [Trait("Criteria", "UC-14.AC3")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_with_no_partial_result(string answer)
    {
        var result = await Reader(Omv(answer)).ReadAsync();

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Empty(result.Services);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"data":[]}""")]
    [Trait("Criteria", "UC-14.AC4")]
    public async Task No_services_is_a_normal_result_with_a_message(string answer)
    {
        var result = await Reader(Omv(answer)).ReadAsync();

        Assert.True(result.Ok);
        Assert.Empty(result.Services);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC1")]
    public async Task OMV_being_unreachable_is_an_omv_unreachable_error()
    {
        var omv = new FakeRpcClient { Handler = _ => throw new OmvRpcException(OmvRpcErrorKind.Unreachable, "detail only for the logs") };

        var result = await Reader(omv).ReadAsync();

        Assert.Equal(ErrorCodes.OmvUnreachable, result.Error!.Code);
        Assert.DoesNotContain("detail only for the logs", result.Error.Message, StringComparison.Ordinal);
    }
}
