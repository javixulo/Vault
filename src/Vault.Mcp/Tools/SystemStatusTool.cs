using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.SystemInfo;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class SystemStatusTool(SystemStatusReader reader)
{
    [McpServerTool(Name = "system_status", Title = "System status", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Reads the current state of the NAS from OpenMediaVault (OMV): hostname, OMV version, kernel, CPU model and usage, memory used and total in bytes, and uptime in seconds. " +
        "Read-only: it never changes anything on the NAS. It takes no arguments. " +
        "Any field can be null when this OMV version does not report it. " +
        "If OMV cannot be reached or refuses the credentials, the result has an error whose message says what to tell the user.")]
    public Task<SystemStatusResult> SystemStatus(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
