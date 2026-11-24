using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Disks;
using Vault.Core.Models;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListDisksTool(DisksReader reader)
{
    [McpServerTool(Name = "list_disks", Title = "List disks", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists the disks OpenMediaVault (OMV) sees: device (/dev/sda), model, size in bytes, SMART status and temperature in Celsius. " +
        "smart_status is 'passed', 'failed' or 'unknown' (the disk has no SMART support or OMV does not report it, as with some USB drives). " +
        "Use it to check the health of the disks before blaming a share or a service. " +
        "model, size_bytes and temperature_celsius are null when OMV does not report them. " +
        "Read-only: it never runs a SMART test or changes anything on the NAS. " +
        "Takes no arguments.")]
    public Task<DisksResult> ListDisks(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
