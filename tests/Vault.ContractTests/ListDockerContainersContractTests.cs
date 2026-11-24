using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>list_docker_containers</c>.</summary>
public sealed class ListDockerContainersContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Criteria", "UC-24.AC1, UC-24.AC3, UC-24.AC4")]
    public async Task It_lists_the_containers_in_snake_case_with_health_and_the_Compose_project()
    {
        var result = await _server.CallAsync("list_docker_containers");

        Assert.NotEqual(true, result.IsError);
        var containers = result.StructuredContent!.Value.GetProperty("containers").EnumerateArray().ToList();
        Assert.Equal(["jellyfin", "standalone"], containers.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("healthy", containers[0].GetProperty("health").GetString());
        Assert.Equal("2026-10-26T12:08:00+01:00", containers[0].GetProperty("created_at").GetString());
        Assert.Equal("jellyfin", containers[0].GetProperty("compose_project").GetString());
        Assert.Equal("exited", containers[1].GetProperty("state").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, containers[1].GetProperty("compose_project").ValueKind);
        Assert.Equal(
            ["Compose.getContainerListBg", "Exec.getOutput", "Compose.getServicesListBg", "Exec.getOutput"],
            _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC6")]
    public async Task The_result_has_no_ports_mounts_or_command()
    {
        var result = await _server.CallAsync("list_docker_containers");

        var container = result.StructuredContent!.Value.GetProperty("containers")[0];
        string[] names = [.. container.EnumerateObject().Select(p => p.Name)];
        Assert.Equal(["name", "image", "state", "status", "health", "created_at", "network", "compose_project", "compose_service"], names);
    }
}
