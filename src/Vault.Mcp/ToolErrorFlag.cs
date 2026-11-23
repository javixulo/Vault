using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Vault.Mcp;

/// <summary>
/// Sets the MCP <c>isError</c> flag on every result whose structured content carries an <c>error</c> or <c>errors</c> (technical spec,
/// section 6). The tools keep one shape for every outcome, and there is no <c>ok</c> flag: this flag is the one signal
/// that the call did not do what was asked, so the model treats the text as a problem to fix. Nothing in the content changes.
/// </summary>
public static class ToolErrorFlag
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Apply(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            if (IsRejection(result.StructuredContent))
            {
                result.IsError = true;
            }

            return result;
        };

    /// <summary>True when the content has an <c>error</c> or a non-empty <c>errors</c> list: that is what a rejection looks like.</summary>
    public static bool IsRejection(JsonElement? content) =>
        content is { ValueKind: JsonValueKind.Object } obj
        && (HasValue(obj, "error", JsonValueKind.Object) || (HasValue(obj, "errors", JsonValueKind.Array) && obj.GetProperty("errors").GetArrayLength() > 0));

    private static bool HasValue(JsonElement obj, string name, JsonValueKind kind) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == kind;
}
