using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_scheduled_tasks</c>.</summary>
public sealed class ListScheduledTasksContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-16.AC1")]
    public async Task Returns_the_scheduled_tasks_in_snake_case_without_a_last_run()
    {
        var result = await _server.CallAsync("list_scheduled_tasks");

        Assert.NotEqual(true, result.IsError);
        var tasks = result.StructuredContent!.Value.GetProperty("tasks").EnumerateArray().ToList();
        Assert.Equal(["Nightly backup", "/usr/bin/cleanup"], tasks.Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(["0 3 * * *", "@daily"], tasks.Select(t => t.GetProperty("schedule").GetString()));
        Assert.Equal([true, false], tasks.Select(t => t.GetProperty("enabled").GetBoolean()));
        Assert.False(tasks[0].TryGetProperty("last_run_at", out _));
        Assert.Equal(["Cron.getList"], _server.Omv.Calls);
    }
}
