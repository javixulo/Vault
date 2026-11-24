using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.Services;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListServicesTool(ServicesReader reader)
{
    [McpServerTool(Name = "list_services", Title = "List services", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists the services OpenMediaVault (OMV) manages (smb, nfs, ssh...) and, for each one, whether it is enabled (configured to run) and running right now. " +
        "Use it to check that a service is up before looking for the cause of a problem somewhere else, or to tell a service that is off by choice (enabled false) from one that should be on but is not (enabled true, running false). " +
        "A field is null when OMV does not report it. " +
        "Read-only: it never starts, stops or changes anything on the NAS. " +
        "Takes no arguments.")]
    public Task<ServicesResult> ListServices(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
