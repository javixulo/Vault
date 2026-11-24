using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_disks</c>.</summary>
public sealed class ListDisksContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-15.AC1, UC-15.AC2")]
    public async Task Returns_the_disks_in_snake_case_with_unknown_for_a_disk_without_SMART()
    {
        var result = await _server.CallAsync("list_disks");

        Assert.NotEqual(true, result.IsError);
        var disks = result.StructuredContent!.Value.GetProperty("disks").EnumerateArray().ToList();
        Assert.Equal(["/dev/sda", "/dev/sdb"], disks.Select(d => d.GetProperty("device").GetString()));
        Assert.Equal(["passed", "unknown"], disks.Select(d => d.GetProperty("smart_status").GetString()));
        Assert.Equal(4000787030016L, disks[0].GetProperty("size_bytes").GetInt64());
        Assert.Equal(36, disks[0].GetProperty("temperature_celsius").GetDouble());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, disks[1].GetProperty("temperature_celsius").ValueKind);
        Assert.Equal(["DiskMgmt.enumerateDevices", "Smart.getList"], _server.Omv.Calls);
    }
}
