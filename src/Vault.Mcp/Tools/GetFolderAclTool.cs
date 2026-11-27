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
        "Reads the extended POSIX ACL (setfacl/getfacl) of one shared folder, or of a subfolder inside it: its owner and group, the base permissions of owner, group and other, " +
        "and the named users and groups with an ACL entry, each with its permissions as rwx (for example r-x). " +
        "With include_subfolders true it also returns subfolders, the names of the direct subfolders of the folder read: to browse deeper, call again with relative_path set to the current one plus / plus a name. Leave it false (the default) unless you need those names: it is slower and the list can be very long, for example in a photo library; subfolders is then null, which does not mean there are none. " +
        "This is the ACL layer of OpenMediaVault (OMV), separate from the Shared Folder Privileges that get_folder_permissions returns. " +
        "Folders only, never files. Subfolders can have an ACL different from their parent's, so reading the root says nothing certain about them. " +
        "Read-only: it never changes anything on the NAS. " +
        "Pass the folder name exactly as list_shared_folders returns it (case-sensitive). " +
        "If no named user or group has an entry the list is empty and message says so: not an error. " +
        "If no shared folder has that name, or it has no such subfolder, the result is a not_found error that says how to check the name.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707", Justification = "The parameter name is the argument name of the tool's contract, in snake_case.")]
    public Task<FolderAclResult> GetFolderAcl(
        // Declared optional on purpose, as in get_folder_permissions: a missing value must come back as invalid_arguments (UC-22.AC1).
        [Description("Required. Name of the shared folder, as list_shared_folders returns it. Example: media.")] string? folder = null,
        [Description("Optional. Relative path of a subfolder inside the shared folder, as OpenMediaVault calls it: folder names separated by /, case-sensitive. Example: Manuales/Timbre. It is not the relative_path that list_shared_folders returns (that one is where the shared folder lives on the disk). Leave it out for the shared folder's root.")] string? relative_path = null,
        [Description("Optional, default false. True to also return the names of the direct subfolders of the folder read. Ask only when you need to browse: it can be slow and long.")] bool include_subfolders = false,
        CancellationToken cancellationToken = default) =>
        reader.ReadAsync(folder, relative_path, include_subfolders, cancellationToken);
}
