using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Docker;
using Vault.Core.Models;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.UnitTests;

public sealed class DockerContainersReaderTests
{
    // Shaped like the real OMV 8.5.12 answers (2026-11-24); ports and mounts are HTML, and command and env can hold secrets.
    private const string Containers = """
        {"total":4,"data":[
         {"id":"aa7f","name":"immich_machine_learning","image":"ghcr.io/immich-app/immich-machine-learning:release","execurl":"disabled","ports":"","mounts":"immich_model-cache","created":"2026-10-26 12:07:38 +0100 CET","state":"running","status":"Up 4 weeks (healthy)","running":"4 weeks ago","command":"tini -- ./start.sh --token=SECRET-TOKEN","network":"immich_default","env":"DB_PASSWORD=SECRET-PASSWORD"},
         {"id":"bb01","name":"immich_server","image":"ghcr.io/immich-app/immich-server:release","ports":"<a href=\"http://JaviNas.internal:2283\" target=\"_blank\">0.0.0.0:2283->2283/tcp</a>","mounts":"/etc/localtime<br/>/srv/dev/Fotos","created":"2026-10-26 12:07:40 +0100 CET","state":"running","status":"Up 4 weeks (unhealthy)","network":"immich_default"},
         {"id":"cc02","name":"jellyfin","image":"lscr.io/linuxserver/jellyfin:latest","ports":"","created":"2026-10-26 12:08:00 +0100 CET","state":"running","status":"Up 4 weeks","network":"host"},
         {"id":"dd03","name":"standalone","image":"busybox","created":"not a date","state":"exited","status":"Exited (1) 3 hours ago"}
        ]}
        """;

    private const string Services = """
        {"total":3,"data":[
         {"name":"immich_machine_learning","image":"x","project":"immich","service":"immich-machine-learning","filepath":"/Docker_Compose/Immich/Immich.yml","envpath":"/Docker_Compose/Immich/.env"},
         {"name":"immich_server","project":"immich","service":"immich-server"},
         {"name":"jellyfin","project":"jellyfin","service":"jellyfin"}
        ]}
        """;

    private static DockerContainersReader Reader(FakeRpcClient omv) =>
        new(new BackgroundJobReader(omv, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1)), NullLogger<DockerContainersReader>.Instance);

    /// <summary>A double of OMV that runs both jobs: each start call answers a file name, and Exec.getOutput answers that file's whole output.</summary>
    private static FakeRpcClient Omv(string containers = Containers, string services = Services)
    {
        var omv = new FakeRpcClient();
        omv.Handler = call => call.Name switch
        {
            "Compose.getContainerListBg" => FakeRpcClient.Json("\"/tmp/bgstatusCONT\""),
            "Compose.getServicesListBg" => FakeRpcClient.Json("\"/tmp/bgstatusSERV\""),
            "Exec.getOutput" => Piece(omv.Calls[^1].Parameters.GetProperty("filename").GetString() == "/tmp/bgstatusCONT" ? containers : services),
            _ => throw new InvalidOperationException($"Unexpected call {call}."),
        };
        return omv;
    }

    private static JsonElement Piece(string output) =>
        FakeRpcClient.Json(JsonSerializer.Serialize(new { filename = "x", pos = output.Length, output, pendingOutput = false, running = false }));

    [Fact]
    [Trait("Criteria", "UC-24.AC1, UC-07.AC1")]
    public async Task Every_container_is_reported_in_OMVs_order_with_its_fields_and_only_the_documented_calls_are_made()
    {
        var omv = Omv();

        var result = await Reader(omv).ReadAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Message);
        Assert.Equal(["immich_machine_learning", "immich_server", "jellyfin", "standalone"], result.Containers.Select(c => c.Name));
        var first = result.Containers[0];
        Assert.Equal("ghcr.io/immich-app/immich-machine-learning:release", first.Image);
        Assert.Equal("running", first.State);
        Assert.Equal("Up 4 weeks (healthy)", first.Status);
        Assert.Equal("healthy", first.Health);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 12, 7, 38, TimeSpan.FromHours(1)), first.CreatedAt);
        Assert.Equal("immich_default", first.Network);
        Assert.Equal(["Compose.getContainerListBg", "Exec.getOutput", "Compose.getServicesListBg", "Exec.getOutput"], omv.Calls.Select(c => c.Call.Name));
        Assert.Equal(-1, omv.Calls[0].Parameters.GetProperty("limit").GetInt32());
        Assert.Equal("name", omv.Calls[0].Parameters.GetProperty("sortfield").GetString());
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC2")]
    public async Task A_value_OMV_does_not_report_is_null_and_the_rest_is_reported()
    {
        var result = await Reader(Omv("""{"total":1,"data":[{"name":"bare"}]}""", """{"total":0,"data":[]}""")).ReadAsync();

        Assert.True(result.Ok);
        var container = Assert.Single(result.Containers);
        Assert.Equal("bare", container.Name);
        Assert.Null(container.Image);
        Assert.Null(container.State);
        Assert.Null(container.Status);
        Assert.Null(container.Health);
        Assert.Null(container.CreatedAt);
        Assert.Null(container.Network);
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC2")]
    public async Task A_date_that_cannot_be_read_is_null_and_does_not_fail_the_call()
    {
        var result = await Reader(Omv()).ReadAsync();

        Assert.True(result.Ok);
        Assert.Null(result.Containers.Single(c => c.Name == "standalone").CreatedAt);
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC3")]
    public async Task The_Compose_project_and_service_are_those_of_the_one_service_with_the_container_s_name_and_null_otherwise()
    {
        var result = await Reader(Omv()).ReadAsync();

        var byName = result.Containers.ToDictionary(c => c.Name);
        Assert.Equal(("immich", "immich-machine-learning"), (byName["immich_machine_learning"].ComposeProject, byName["immich_machine_learning"].ComposeService));
        Assert.Equal(("jellyfin", "jellyfin"), (byName["jellyfin"].ComposeProject, byName["jellyfin"].ComposeService));
        Assert.Null(byName["standalone"].ComposeProject);
        Assert.Null(byName["standalone"].ComposeService);
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC3")]
    public async Task A_name_that_two_Compose_services_have_is_ambiguous_and_matches_none()
    {
        var services = """{"total":2,"data":[{"name":"web","project":"a","service":"web"},{"name":"web","project":"b","service":"web"}]}""";

        var result = await Reader(Omv("""{"total":1,"data":[{"name":"web"}]}""", services)).ReadAsync();

        var container = Assert.Single(result.Containers);
        Assert.Null(container.ComposeProject);
        Assert.Null(container.ComposeService);
    }

    [Theory]
    [InlineData("Up 4 weeks (healthy)", "healthy")]
    [InlineData("Up 4 weeks (unhealthy)", "unhealthy")]
    [InlineData("Up 5 seconds (health: starting)", "starting")]
    [InlineData("Up 4 weeks", null)]
    [InlineData("Up 4 weeks (Paused)", null)]
    [InlineData("Exited (1) 3 hours ago", null)]
    [InlineData("(healthy) but not at the end", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [Trait("Criteria", "UC-24.AC4")]
    public void Health_is_read_only_from_the_end_of_the_status_and_unhealthy_is_never_healthy(string? status, string? expected)
    {
        Assert.Equal(expected, DockerContainersReader.HealthOf(status));
    }

    [Theory]
    [InlineData("""{"unexpected":true}""")]
    [InlineData("\"text\"")]
    [InlineData("""{"total":1,"data":[{"image":"x"}]}""")]
    [InlineData("""{"total":1,"data":[{"name":" "}]}""")]
    [Trait("Criteria", "UC-24.AC5")]
    public async Task A_container_list_Vault_cannot_read_is_a_bad_response_with_no_partial_result(string containers)
    {
        var result = await Reader(Omv(containers)).ReadAsync();

        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Empty(result.Containers);
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC5")]
    public async Task A_service_list_Vault_cannot_read_fails_the_tool_instead_of_hiding_the_projects()
    {
        var result = await Reader(Omv(services: """{"unexpected":true}""")).ReadAsync();

        Assert.Equal(ErrorCodes.OmvBadResponse, result.Error!.Code);
        Assert.Empty(result.Containers);
    }

    [Fact]
    [Trait("Criteria", "UC-24.AC6")]
    public async Task Nothing_sensitive_is_passed_on()
    {
        var result = await Reader(Omv()).ReadAsync();

        var text = JsonSerializer.Serialize(result);
        foreach (var secret in new[] { "SECRET", "tini", "<a href", "/srv/dev", "/etc/localtime", "Immich.yml", ".env" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("""{"total":0,"data":[]}""")]
    [InlineData("[]")]
    [Trait("Criteria", "UC-25.AC1")]
    public async Task No_containers_is_a_normal_result_with_a_message(string containers)
    {
        var result = await Reader(Omv(containers, """{"total":0,"data":[]}""")).ReadAsync();

        Assert.True(result.Ok);
        Assert.Empty(result.Containers);
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

    [Fact]
    [Trait("Criteria", "UC-02.AC5")]
    public async Task An_unexpected_failure_is_a_failed_error_without_its_cause()
    {
        var omv = new FakeRpcClient { Handler = _ => throw new InvalidOperationException("secret detail") };

        var result = await Reader(omv).ReadAsync();

        Assert.Equal(ErrorCodes.Failed, result.Error!.Code);
        Assert.DoesNotContain("secret detail", result.Error.Message, StringComparison.Ordinal);
    }
}
