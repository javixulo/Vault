using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Vault.Core.SystemInfo;
using Xunit;

namespace Vault.UnitTests;

public sealed class SystemStatusReaderTests
{
    private static SystemStatusReader Reader(FakeRpcClient omv) => new(omv, NullLogger<SystemStatusReader>.Instance);

    [Fact]
    [Trait("Criteria", "UC-01.AC1, UC-07.AC1")]
    public async Task Reports_what_OMV_says_and_calls_only_System_getInformation()
    {
        var omv = FakeRpcClient.Answering("""
            {"hostname":"nas","version":"7.4.0","kernel":"Linux 6.1","cpuModelName":"N100","cpuUtilization":12.5,"memTotal":8000,"memUsed":2000,"uptime":3600}
            """);

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Equal(new SystemStatus("nas", "7.4.0", "Linux 6.1", "N100", 12.5, 8000, 2000, 3600), result.Status);
        Assert.Equal([OmvRpcCall.SystemInformation], omv.Calls.Select(c => c.Call));
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC2")]
    public async Task A_field_OMV_does_not_report_is_null_and_numbers_sent_as_text_are_read()
    {
        var omv = FakeRpcClient.Answering("""{"hostname":"nas","memUsed":"2000","uptime":"oops"}""");

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Equal("nas", result.Status!.Hostname);
        Assert.Equal(2000, result.Status.MemoryUsedBytes);
        Assert.Null(result.Status.UptimeSeconds);
        Assert.Null(result.Status.Kernel);
        Assert.Null(result.Status.CpuUsagePercent);
    }

    [Theory]
    [InlineData(OmvRpcErrorKind.Unreachable, "omv_unreachable")]
    [InlineData(OmvRpcErrorKind.AuthFailed, "omv_auth_failed")]
    [InlineData(OmvRpcErrorKind.RpcError, "omv_rpc_error")]
    [InlineData(OmvRpcErrorKind.BadResponse, "omv_bad_response")]
    [Trait("Criteria", "UC-02.AC1, UC-02.AC2, UC-02.AC3, UC-02.AC4")]
    public async Task Each_way_OMV_can_fail_is_its_own_error(OmvRpcErrorKind kind, string code)
    {
        var omv = FakeRpcClient.Failing(new OmvRpcException(kind, "detail for the logs only: http://nas.local/rpc.php", 42));

        var result = await Reader(omv).ReadAsync();

        Assert.False(result.Ok);
        Assert.Null(result.Status);
        Assert.Equal(code, result.Error!.Code);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC5, UC-02.AC6")]
    public async Task An_unexpected_failure_is_failed_and_does_not_tell_its_cause()
    {
        var omv = FakeRpcClient.Failing(new InvalidOperationException("secret-detail C:\\internal\\path"));

        var result = await Reader(omv).ReadAsync();

        Assert.Equal("failed", result.Error!.Code);
        Assert.DoesNotContain("secret-detail", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("internal", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Do not retry", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC6")]
    public async Task What_OMV_said_never_reaches_the_client()
    {
        var omv = FakeRpcClient.Failing(new OmvRpcException(OmvRpcErrorKind.RpcError, "OMV said: password hunter2 is wrong at /var/www/openmediavault", 4711));

        var result = await Reader(omv).ReadAsync();

        Assert.DoesNotContain("hunter2", result.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("/var/www", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("4711", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancellation_is_not_turned_into_an_error()
    {
        var omv = FakeRpcClient.Failing(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => Reader(omv).ReadAsync());
    }
}
