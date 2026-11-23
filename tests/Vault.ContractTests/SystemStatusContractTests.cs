using Vault.Core.Rpc;
using Xunit;

namespace Vault.ContractTests;

/// <summary>Contract tests of <c>system_status</c>: the real server in process, spoken to with the SDK's MCP client.</summary>
public sealed class SystemStatusContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Exposes_system_status_with_a_snake_case_schema_and_no_arguments()
    {
        var tool = Assert.Single(await _server.ToolsAsync(), t => t.Name == "system_status");

        Assert.NotNull(tool.ReturnJsonSchema);
        var schema = tool.ReturnJsonSchema!.Value.GetRawText();
        Assert.Contains("omv_version", schema, StringComparison.Ordinal);
        Assert.Contains("memory_used_bytes", schema, StringComparison.Ordinal);
        Assert.Contains("uptime_seconds", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("omvVersion", schema, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC1")]
    public async Task Returns_what_OMV_reports_in_snake_case()
    {
        var result = await _server.CallAsync("system_status");

        Assert.NotEqual(true, result.IsError);
        var status = result.StructuredContent!.Value.GetProperty("status");
        Assert.Equal("nas", status.GetProperty("hostname").GetString());
        Assert.Equal("7.4.0", status.GetProperty("omv_version").GetString());
        Assert.Equal(2000, status.GetProperty("memory_used_bytes").GetInt64());
        Assert.Equal(3600, status.GetProperty("uptime_seconds").GetInt64());
        Assert.Equal(["System.getInformation"], _server.Omv.Calls);
    }

    [Fact]
    [Trait("Criteria", "UC-01.AC2")]
    public async Task A_field_OMV_does_not_report_is_null_not_an_error()
    {
        await using var server = VaultTestServer.Create(new StubOmv { Answer = _ => StubOmv.Parse("""{"hostname":"nas"}""") });

        var result = await server.CallAsync("system_status");

        Assert.NotEqual(true, result.IsError);
        var status = result.StructuredContent!.Value.GetProperty("status");
        Assert.Equal("nas", status.GetProperty("hostname").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, status.GetProperty("kernel").ValueKind);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC3, UC-02.AC4")]
    public async Task An_error_of_OMV_and_an_unexpected_answer_are_told_apart()
    {
        await using var rpc = VaultTestServer.Create(new StubOmv { Answer = _ => throw new OmvRpcException(OmvRpcErrorKind.RpcError, "x", 4711) });
        await using var bad = VaultTestServer.Create(new StubOmv { Answer = _ => throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "x") });

        var rpcResult = await rpc.CallAsync("system_status");
        var badResult = await bad.CallAsync("system_status");

        Assert.Equal("omv_rpc_error", rpcResult.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("4711", rpcResult.StructuredContent!.Value.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("omv_bad_response", badResult.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }
}
