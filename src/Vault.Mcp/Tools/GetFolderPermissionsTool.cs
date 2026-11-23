using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.Shares;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class GetFolderPermissionsTool(FolderPermissionsReader reader)
{
    [McpServerTool(Name = "get_folder_permissions", Title = "Get folder permissions", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Reads the privileges OpenMediaVault (OMV) has configured for one shared folder: for each user or group with an explicit privilege, its access (read_write, read_only or deny). " +
        "These are OMV's own Shared Folder Privileges, not the POSIX owner or mode and not extended ACLs. " +
        "Read-only: it never changes anything on the NAS. " +
        "Pass the folder name exactly as list_shared_folders returns it (case-sensitive). " +
        "If the folder has no explicit privilege the list is empty and message says OMV's default applies: not an error. " +
        "If no shared folder has that name the result is a not_found error that says to check the name with list_shared_folders.")]
    public Task<FolderPermissionsResult> GetFolderPermissions(
        // Declared optional on purpose: a missing value must reach the reader and come back as invalid_arguments (UC-12.AC1),
        // not as a protocol error from the SDK. The description says it is required.
        [Description("Required. Name of the shared folder, as list_shared_folders returns it. Example: media.")] string? folder = null,
        CancellationToken cancellationToken = default) =>
        reader.ReadAsync(folder, cancellationToken);
}
