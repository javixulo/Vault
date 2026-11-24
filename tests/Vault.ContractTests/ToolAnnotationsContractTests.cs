using System.Text.Json;
using ModelContextProtocol.Protocol;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.ContractTests;

/// <summary>
/// Contract tests of what a client sees of the whole tool list: names, annotations, titles, the arguments each tool accepts
/// and the shape of results and rejections (technical spec, section 6). Vault is read-only by construction: every tool says so.
/// </summary>
public sealed class ToolAnnotationsContractTests : IAsyncLifetime
{
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    // Catches: a tool added or renamed without saying so (the tool list is the contract).
    [Fact]
    public async Task Lists_the_tools_in_a_fixed_order()
    {
        var names = (await _server.ToolsAsync()).Select(t => t.Name).ToList();

        Assert.Equal(["system_status", "list_shared_folders", "get_folder_permissions", "get_folder_acl", "list_users_and_groups"], names);
    }

    // Catches: a tool that is not marked read-only (the reason Vault exists), or has no title, or reaches outside systems.
    [Fact]
    [Trait("Criteria", "UC-07.AC3")]
    public async Task Every_tool_is_read_only_closed_world_and_has_a_title()
    {
        foreach (var tool in await _server.ToolsAsync())
        {
            var annotations = tool.ProtocolTool.Annotations;
            Assert.NotNull(annotations);
            Assert.True(annotations!.ReadOnlyHint, tool.Name);
            Assert.False(annotations.OpenWorldHint, tool.Name);
            Assert.False(string.IsNullOrWhiteSpace(annotations.Title), tool.Name);
        }
    }

    // Catches: a tool that lets the client choose an RPC service or method: the boundary of read-only is the fixed list.
    [Fact]
    [Trait("Criteria", "UC-07.AC2")]
    public async Task No_tool_accepts_anything_that_names_an_RPC_call()
    {
        var allowed = new Dictionary<string, string[]>
        {
            ["system_status"] = [],
            ["list_shared_folders"] = ["limit", "offset"],
            ["get_folder_permissions"] = ["folder"],
            ["get_folder_acl"] = ["folder"],
            ["list_users_and_groups"] = [],
        };

        foreach (var tool in await _server.ToolsAsync())
        {
            var schema = tool.JsonSchema;
            var properties = schema.TryGetProperty("properties", out var found)
                ? found.EnumerateObject().Select(p => p.Name).Order().ToArray()
                : [];
            Assert.Equal(allowed[tool.Name], properties);
        }
    }

    // Catches: clients that read only text get nothing: the same JSON must travel as text too.
    [Fact]
    public async Task The_structured_result_is_also_sent_as_text()
    {
        var result = await _server.CallAsync("system_status");

        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        using var parsed = JsonDocument.Parse(text);
        Assert.Equal(result.StructuredContent!.Value.GetRawText(), parsed.RootElement.GetRawText());
    }

    // Catches: an `ok` flag coming back (isError already says whether the call worked), or a rejection that is not isError.
    [Fact]
    public async Task No_result_carries_an_ok_flag_and_a_rejection_is_told_by_isError_and_its_error()
    {
        foreach (var tool in await _server.ToolsAsync())
        {
            Assert.DoesNotContain("\"ok\"", tool.ReturnJsonSchema?.GetRawText() ?? "", StringComparison.Ordinal);
        }

        var success = await _server.CallAsync("system_status");
        var rejection = await _server.CallAsync("list_shared_folders", new() { ["limit"] = 0 });

        Assert.NotEqual(true, success.IsError);
        Assert.False(success.StructuredContent!.Value.TryGetProperty("ok", out _));
        Assert.True(rejection.IsError);
        Assert.False(rejection.StructuredContent!.Value.TryGetProperty("ok", out _));
        Assert.Equal("invalid_arguments", rejection.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
    }

    // Catches: a failure of OMV that reaches the client as a protocol error, or as a success.
    [Theory]
    [InlineData(OmvRpcErrorKind.Unreachable, "omv_unreachable")]
    [InlineData(OmvRpcErrorKind.AuthFailed, "omv_auth_failed")]
    [Trait("Criteria", "UC-02.AC1, UC-02.AC2")]
    public async Task A_failure_of_OMV_is_a_tool_error_in_every_tool(OmvRpcErrorKind kind, string code)
    {
        await using var server = VaultTestServer.Create(new StubOmv { Answer = _ => throw new OmvRpcException(kind, "detail only for the logs", 7) });

        foreach (var tool in new[] { "system_status", "list_shared_folders", "get_folder_permissions", "get_folder_acl", "list_users_and_groups" })
        {
            var result = await server.CallAsync(tool, tool.StartsWith("get_folder", StringComparison.Ordinal) ? new() { ["folder"] = "media" } : null);

            Assert.True(result.IsError, tool);
            Assert.Equal(code, result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain("detail only for the logs", result.StructuredContent!.Value.GetRawText(), StringComparison.Ordinal);
        }
    }

    // Catches: a raw exception reaching the client.
    [Fact]
    [Trait("Criteria", "UC-02.AC5, UC-02.AC6")]
    public async Task An_unexpected_failure_is_a_failed_error_without_its_cause()
    {
        await using var server = VaultTestServer.Create(new StubOmv { Answer = _ => throw new InvalidOperationException("secret-internal-detail") });

        var result = await server.CallAsync("system_status");

        Assert.True(result.IsError);
        Assert.Equal("failed", result.StructuredContent!.Value.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("secret-internal-detail", result.StructuredContent!.Value.GetRawText(), StringComparison.Ordinal);
    }
}
