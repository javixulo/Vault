using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_filesystems</c>.</summary>
public sealed class ListFilesystemsContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-23.AC1, UC-23.AC2")]
    public async Task Returns_the_filesystems_in_snake_case_with_null_for_what_an_unmounted_one_does_not_have()
    {
        var result = await _server.CallAsync("list_filesystems");

        Assert.NotEqual(true, result.IsError);
        var filesystems = result.StructuredContent!.Value.GetProperty("filesystems").EnumerateArray().ToList();
        Assert.Equal(["/dev/sda1", "/dev/sdb1"], filesystems.Select(f => f.GetProperty("device").GetString()));
        Assert.Equal([true, false], filesystems.Select(f => f.GetProperty("mounted").GetBoolean()));
        Assert.Equal("/srv/data", filesystems[0].GetProperty("mount_point").GetString());
        Assert.Equal(1000L, filesystems[0].GetProperty("size_bytes").GetInt64());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, filesystems[1].GetProperty("mount_point").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, filesystems[1].GetProperty("used").ValueKind);
        Assert.Equal("1.00 KiB", filesystems[0].GetProperty("used").GetString());
        Assert.Equal(900L, filesystems[0].GetProperty("available_bytes").GetInt64());
        Assert.Equal(10, filesystems[0].GetProperty("used_percent").GetInt32());
        Assert.Equal(["FileSystemMgmt.enumerateFilesystems"], _server.Omv.Calls);
    }
}
