using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Disks;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.UnitTests;

public sealed class DisksReaderTests
{
    private static DisksReader Reader(FakeRpcClient omv) => new(omv, NullLogger<DisksReader>.Instance);

    private static FakeRpcClient Omv(string devices, string smart) => new()
    {
        Handler = call => FakeRpcClient.Json(call == OmvRpcCall.Disks ? devices : smart),
    };

    // Shapes copied from a real OMV 8.5.12: size and temperature are text, temperature is "" when unknown.
    private const string Devices = """
        [{"devicename":"mmcblk0","canonicaldevicefile":"/dev/mmcblk0","model":"","size":"31331450880","temperature":""},
         {"devicename":"sda","canonicaldevicefile":"/dev/sda","model":"ST4000VN006-3CW104","size":"4000787030016","temperature":"36"},
         {"devicename":"sdb","canonicaldevicefile":"/dev/sdb","model":"BUP BK","size":"5000981077504","temperature":""}]
        """;

    private const string Smart = """
        {"total":2,"data":[{"canonicaldevicefile":"/dev/sda","overallstatus":"GOOD","temperature":"36"},
                           {"canonicaldevicefile":"/dev/sdb","overallstatus":"BAD_STATUS","temperature":"30"}]}
        """;

    [Fact]
    [Trait("Criteria", "UC-15.AC1, UC-07.AC1")]
    public async Task Every_disk_is_listed_in_OMVs_order_with_its_SMART_status_and_only_the_two_calls_are_made()
    {
        var omv = Omv(Devices, Smart);

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Equal(["/dev/mmcblk0", "/dev/sda", "/dev/sdb"], result.Disks.Select(d => d.Device));
        Assert.Equal([null, "ST4000VN006-3CW104", "BUP BK"], result.Disks.Select(d => d.Model));
        Assert.Equal([31331450880L, 4000787030016L, 5000981077504L], result.Disks.Select(d => d.SizeBytes));
        Assert.Equal(["unknown", "passed", "failed"], result.Disks.Select(d => d.SmartStatus));
        Assert.Equal([OmvRpcCall.Disks, OmvRpcCall.DisksSmart], omv.Calls.Select(c => c.Call));
    }

    [Fact]
    [Trait("Criteria", "UC-15.AC2")]
    public async Task A_disk_without_a_SMART_entry_is_unknown_and_the_call_is_not_an_error()
    {
        var result = await Reader(Omv(Devices, """{"total":0,"data":[]}""")).ReadAsync();

        Assert.True(result.Ok);
        Assert.All(result.Disks, d => Assert.Equal(SmartStatuses.Unknown, d.SmartStatus));
    }

    [Theory]
    [InlineData("GOOD", "passed")]
    [InlineData("good", "passed")]
    [InlineData("BAD_STATUS", "failed")]
    [InlineData("BAD_SECTOR", "failed")]
    [InlineData("UNKNOWN", "unknown")]
    [InlineData("", "unknown")]
    [Trait("Criteria", "UC-15.AC1, UC-15.AC2")]
    public async Task The_overall_status_of_OMV_is_turned_into_passed_failed_or_unknown(string overall, string expected)
    {
        var smart = $$"""[{"canonicaldevicefile":"/dev/sda","overallstatus":"{{overall}}"}]""";

        var result = await Reader(Omv("""[{"canonicaldevicefile":"/dev/sda"}]""", smart)).ReadAsync();

        Assert.Equal(expected, result.Disks.Single().SmartStatus);
    }

    [Fact]
    [Trait("Criteria", "UC-15.AC3")]
    public async Task The_temperature_is_a_number_and_null_when_OMV_has_none_or_only_SMART_has_it()
    {
        var result = await Reader(Omv(Devices, Smart)).ReadAsync();

        // mmcblk0: none anywhere. sda: from the device list. sdb: empty there, so the one SMART reports.
        Assert.Equal([null, 36d, 30d], result.Disks.Select(d => d.TemperatureCelsius));
    }

    [Theory]
    [InlineData("""{"unexpected":true}""", "[]")]
    [InlineData("[]", "\"text\"")]
    [InlineData("""[{"model":"no device"}]""", "[]")]
    [Trait("Criteria", "UC-15.AC4")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_with_no_partial_result(string devices, string smart)
    {
        var result = await Reader(Omv(devices, smart)).ReadAsync();

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Empty(result.Disks);
    }

    [Fact]
    [Trait("Criteria", "UC-15.AC5")]
    public async Task No_disks_is_a_normal_result_with_a_message()
    {
        var result = await Reader(Omv("[]", """{"total":0,"data":[]}""")).ReadAsync();

        Assert.True(result.Ok);
        Assert.Empty(result.Disks);
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
