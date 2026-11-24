using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.Updates;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListPendingUpdatesTool(PendingUpdatesReader reader)
{
    [McpServerTool(Name = "list_pending_updates", Title = "List pending updates", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Reports the system updates (packages) OpenMediaVault (OMV) has pending. By default it only says how many (count); " +
        "pass detailed=true to also list every package with its installed (current_version) and available (candidate_version) version. " +
        "Use it to check whether the NAS needs updating. " +
        "current_version is null for a package that is new (OMV gives none). " +
        "Read-only: it only reads what OMV already knows, it never runs apt update and never installs anything, " +
        "so the list is what OMV last found, not necessarily what the repositories offer right now. " +
        "If nothing is pending, count is 0 and message says the system is up to date: not an error.")]
    public Task<PendingUpdatesResult> ListPendingUpdates(
        [Description("Optional, default false. When false only count is reported; when true updates lists every package with an update available.")] bool? detailed = null,
        CancellationToken cancellationToken = default) =>
        reader.ReadAsync(detailed, cancellationToken);
}
