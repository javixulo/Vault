using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_services</c>.</summary>
public sealed class ListServicesContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-14.AC1, UC-14.AC2")]
    public async Task Returns_the_services_in_snake_case_with_null_for_a_state_OMV_does_not_report()
    {
        var result = await _server.CallAsync("list_services");

        Assert.NotEqual(true, result.IsError);
        var services = result.StructuredContent!.Value.GetProperty("services").EnumerateArray().ToList();
        Assert.Equal(["ssh", "smb", "nfs"], services.Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(["True", "True", "False"], services.Select(s => s.GetProperty("enabled").GetBoolean().ToString()));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, services[1].GetProperty("running").ValueKind);
        Assert.Equal(["Services.getStatus"], _server.Omv.Calls);
    }
}
