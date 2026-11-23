using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.Shares;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class GetFolderAclTool(FolderAclReader reader)
{
    [McpServerTool(Name = "get_folder_acl", Title = "Get folder ACL", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Reads the extended POSIX ACL (setfacl/getfacl) of the root of one shared folder: its owner and group, the base permissions of owner, group and other, " +
        "and the named users and groups with an ACL entry, each with its permissions as rwx (for example r-x). " +
        "This is the ACL layer of OpenMediaVault (OMV), separate from the Shared Folder Privileges that get_folder_permissions returns. " +
        "Only the folder's own root, never a file or subfolder inside it. " +
        "Read-only: it never changes anything on the NAS. " +
        "Pass the folder name exactly as list_shared_folders returns it (case-sensitive). " +
        "If no named user or group has an entry the list is empty and message says so: not an error. " +
        "If no shared folder has that name the result is a not_found error that says to check the name with list_shared_folders.")]
    public Task<FolderAclResult> GetFolderAcl(
        // Declared optional on purpose, as in get_folder_permissions: a missing value must come back as invalid_arguments (UC-22.AC1).
        [Description("Required. Name of the shared folder, as list_shared_folders returns it. Example: media.")] string? folder = null,
        CancellationToken cancellationToken = default) =>
        reader.ReadAsync(folder, cancellationToken);
}
