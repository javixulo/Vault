using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Filesystems;
using Vault.Core.Models;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListFilesystemsTool(FilesystemsReader reader)
{
    [McpServerTool(Name = "list_filesystems", Title = "List filesystems", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists the filesystems OpenMediaVault (OMV) manages, mounted or not: device (/dev/sda1), uuid, type (ext4, btrfs, xfs...), label, " +
        "whether it is mounted, mount point, size in bytes, used space (as text, e.g. '4.35 TiB'), free space in bytes and used percentage. " +
        "Different from list_disks: that one is the physical disks; one disk can hold several filesystems, or none. " +
        "Use it to check how full a data disk is or whether a filesystem is mounted. " +
        "label is null when it has none; mount_point, size_bytes, used, available_bytes and used_percent are null when the filesystem is not mounted. " +
        "used is the rounded text OMV shows, not bytes: for exact numbers use size_bytes and available_bytes. " +
        "Read-only: it never mounts, unmounts or changes anything on the NAS. " +
        "Takes no arguments.")]
    public Task<FilesystemsResult> ListFilesystems(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
