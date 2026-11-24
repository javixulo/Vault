using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_pending_updates</c>.</summary>
public sealed class ListPendingUpdatesContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-18.AC1")]
    public async Task Without_arguments_it_reports_the_count_and_no_list()
    {
        var result = await _server.CallAsync("list_pending_updates");

        Assert.NotEqual(true, result.IsError);
        var content = result.StructuredContent!.Value;
        Assert.Equal(2, content.GetProperty("count").GetInt32());
        Assert.Equal(0, content.GetProperty("updates").GetArrayLength());
        Assert.Equal(["Apt.enumerateUpgraded"], _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-18.AC2")]
    public async Task Detailed_lists_the_packages_in_snake_case()
    {
        var result = await _server.CallAsync("list_pending_updates", new Dictionary<string, object?> { ["detailed"] = true });

        Assert.NotEqual(true, result.IsError);
        var updates = result.StructuredContent!.Value.GetProperty("updates").EnumerateArray().ToList();
        Assert.Equal(["curl", "linux-image"], updates.Select(u => u.GetProperty("package").GetString()));
        Assert.Equal(["7.0", null], updates.Select(u => u.GetProperty("current_version").GetString()));
        Assert.Equal(["7.1", "6.1"], updates.Select(u => u.GetProperty("candidate_version").GetString()));
    }
}
