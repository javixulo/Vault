using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Filesystems;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.UnitTests;

public sealed class FilesystemsReaderTests
{
    private static FilesystemsReader Reader(FakeRpcClient omv) => new(omv, NullLogger<FilesystemsReader>.Instance);

    private static FakeRpcClient Omv(string answer) => new() { Handler = _ => FakeRpcClient.Json(answer) };

    // Shapes copied from a real OMV 8.5.12: size and available are bytes as text, used is human text ("22.10 GiB"),
    // and a filesystem that is not mounted has -1 in all of them and an empty mountpoint.
    private const string Real = """
        [{"canonicaldevicefile":"/dev/mmcblk0p7","uuid":"db287638-4c71-42d2-a5c7-fe942847592e","label":"USER-DATA","type":"ext4","mounted":false,"mountpoint":"","used":"-1","available":"-1","size":"-1","percentage":-1},
         {"canonicaldevicefile":"/dev/nvme0n1p2","uuid":"4ab10465-7f32-48d9-927a-7400c08856a4","label":"","type":"ext4","mounted":true,"mountpoint":"/","used":"22.10 GiB","available":"900863815680","size":"974159228928","percentage":3},
         {"canonicaldevicefile":"/dev/sdb1","uuid":"e3e37ad4-7f6a-4140-88a1-145b85cb302a","label":"","type":"ext4","mounted":true,"mountpoint":"/srv/dev-disk-by-uuid-e3e37ad4","used":"4.35 TiB","available":"173097644032","size":"4960385134592","percentage":97}]
        """;

    [Fact]
    [Trait("Criteria", "UC-23.AC1, UC-07.AC1")]
    public async Task Every_filesystem_is_listed_in_OMVs_order_and_only_that_call_is_made()
    {
        var omv = Omv(Real);

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Equal(["/dev/mmcblk0p7", "/dev/nvme0n1p2", "/dev/sdb1"], result.Filesystems.Select(f => f.Device));
        Assert.Equal(["ext4", "ext4", "ext4"], result.Filesystems.Select(f => f.Type));
        Assert.Equal([false, true, true], result.Filesystems.Select(f => f.Mounted));
        Assert.Equal([null, "/", "/srv/dev-disk-by-uuid-e3e37ad4"], result.Filesystems.Select(f => f.MountPoint));
        Assert.Equal([null, 974159228928L, 4960385134592L], result.Filesystems.Select(f => f.SizeBytes));
        Assert.Equal([OmvRpcCall.Filesystems], omv.Calls.Select(c => c.Call));
    }

    [Fact]
    [Trait("Criteria", "UC-23.AC2")]
    public async Task A_value_OMV_does_not_report_is_null_and_the_rest_is_reported()
    {
        var result = await Reader(Omv(Real)).ReadAsync();

        Assert.True(result.Ok);
        Assert.Equal(["USER-DATA", null, null], result.Filesystems.Select(f => f.Label));
        Assert.Null(result.Filesystems[0].Used);
        Assert.Equal("db287638-4c71-42d2-a5c7-fe942847592e", result.Filesystems[0].Uuid);
    }

    [Fact]
    [Trait("Criteria", "UC-23.AC1")]
    public async Task The_used_space_is_OMVs_own_text_and_the_free_space_and_percentage_are_exact_numbers()
    {
        var result = await Reader(Omv(Real)).ReadAsync();

        Assert.Equal([null, "22.10 GiB", "4.35 TiB"], result.Filesystems.Select(f => f.Used));
        Assert.Equal([null, 900863815680L, 173097644032L], result.Filesystems.Select(f => f.AvailableBytes));
        Assert.Equal([null, 3, 97], result.Filesystems.Select(f => f.UsedPercent));
    }

    [Fact]
    [Trait("Criteria", "UC-23.AC2")]
    public async Task A_filesystem_with_no_used_space_or_percentage_has_them_null_and_the_rest_is_reported()
    {
        var result = await Reader(Omv("""[{"canonicaldevicefile":"/dev/sda1","mounted":true,"mountpoint":"/x","size":"100"}]""")).ReadAsync();

        Assert.True(result.Ok);
        var filesystem = result.Filesystems.Single();
        Assert.Null(filesystem.Used);
        Assert.Null(filesystem.AvailableBytes);
        Assert.Null(filesystem.UsedPercent);
        Assert.Equal(100L, filesystem.SizeBytes);
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("\"text\"")]
    [InlineData("""[{"canonicaldevicefile":"/dev/sda1","mounted":true},{"mounted":true}]""")]
    [InlineData("""[{"canonicaldevicefile":" ","mounted":true}]""")]
    [Trait("Criteria", "UC-23.AC3")]
    public async Task An_answer_Vault_cannot_read_is_a_bad_response_with_no_partial_result(string answer)
    {
        var result = await Reader(Omv(answer)).ReadAsync();

        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Empty(result.Filesystems);
    }

    [Fact]
    [Trait("Criteria", "UC-23.AC4")]
    public async Task No_filesystems_is_a_normal_result_with_a_message()
    {
        var result = await Reader(Omv("[]")).ReadAsync();

        Assert.True(result.Ok);
        Assert.Empty(result.Filesystems);
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
