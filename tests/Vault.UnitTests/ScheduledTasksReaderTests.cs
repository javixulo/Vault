using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.ScheduledTasks;
using Xunit;

namespace Vault.UnitTests;

public sealed class ScheduledTasksReaderTests
{
    private static ScheduledTasksReader Reader(FakeRpcClient omv) => new(omv, NullLogger<ScheduledTasksReader>.Instance);

    private static FakeRpcClient Omv(string answer) => new() { Handler = _ => FakeRpcClient.Json(answer) };

    // The envelope is the one a real OMV 8.5.12 answered ({total, data}); the entries use OMV's documented fields,
    // because that NAS has no cron jobs to copy one from.
    private const string Jobs = """
        {"total":4,"data":[
         {"uuid":"a","enable":true,"execution":"exactly","minute":"0","everynminute":false,"hour":"3","everynhour":false,"dayofmonth":"*","everyndayofmonth":false,"month":"*","dayofweek":"*","command":"/usr/local/bin/backup.sh","comment":"Nightly backup"},
         {"uuid":"b","enable":false,"execution":"exactly","minute":"5","everynminute":true,"hour":"*","everynhour":false,"dayofmonth":"*","everyndayofmonth":false,"month":"*","dayofweek":"1","command":"/usr/bin/check","comment":""},
         {"uuid":"c","enable":true,"execution":"daily","command":"/usr/bin/cleanup","comment":"Cleanup"},
         {"uuid":"d","enable":true,"execution":"reboot","command":"/usr/bin/boot","comment":"On boot"}]}
        """;

    [Fact]
    [Trait("Criteria", "UC-16.AC1, UC-07.AC1")]
    public async Task Every_job_is_listed_in_OMVs_order_and_only_that_call_is_made()
    {
        var omv = Omv(Jobs);

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Equal(["Nightly backup", "/usr/bin/check", "Cleanup", "On boot"], result.Tasks.Select(t => t.Name));
        Assert.Equal([true, false, true, true], result.Tasks.Select(t => t.Enabled));
        Assert.Equal([OmvRpcCall.ScheduledTasks], omv.Calls.Select(c => c.Call));
    }

    [Fact]
    [Trait("Criteria", "UC-16.AC1")]
    public async Task The_schedule_is_a_cron_expression_or_one_of_its_at_shortcuts()
    {
        var result = await Reader(Omv(Jobs)).ReadAsync();

        Assert.Equal(["0 3 * * *", "*/5 * * * 1", "@daily", "@reboot"], result.Tasks.Select(t => t.Schedule));
    }

    [Fact]
    [Trait("Criteria", "UC-16.AC2")]
    public async Task A_value_OMV_does_not_report_is_null_and_the_rest_is_reported()
    {
        var result = await Reader(Omv("""[{"command":"/bin/x"},{"comment":"y","execution":"weird","enable":"0"}]""")).ReadAsync();

        Assert.True(result.Ok);
        Assert.Equal(["/bin/x", "y"], result.Tasks.Select(t => t.Name));
        Assert.Equal([null, null], result.Tasks.Select(t => t.Schedule));
        Assert.Equal([null, false], result.Tasks.Select(t => t.Enabled));
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("\"text\"")]
    [InlineData("""[{"command":"/bin/x"},{"enable":true}]""")]
    [InlineData("""[{"comment":" ","command":""}]""")]
    [Trait("Criteria", "UC-16.AC3")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_with_no_partial_result(string answer)
    {
        var result = await Reader(Omv(answer)).ReadAsync();

        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Empty(result.Tasks);
    }

    [Theory]
    [InlineData("""{"total":0,"data":[]}""")]
    [InlineData("[]")]
    [Trait("Criteria", "UC-17.AC1")]
    public async Task No_scheduled_tasks_is_a_normal_result_with_a_message(string answer)
    {
        var result = await Reader(Omv(answer)).ReadAsync();

        Assert.True(result.Ok);
        Assert.Empty(result.Tasks);
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
