using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.Users;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListUsersAndGroupsTool(UsersAndGroupsReader reader)
{
    [McpServerTool(Name = "list_users_and_groups", Title = "List users and groups", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists every OpenMediaVault (OMV) user and group, and which groups each user belongs to. " +
        "Use it to read the names that get_folder_permissions and get_folder_acl return against who actually exists and who is in which group. " +
        "Read-only: it never changes anything on the NAS. " +
        "system is true for operating-system accounts and groups (root, sshd...), false for the ones created in OMV. full_name and system are null when OMV does not report them. No arguments.")]
    public Task<UsersAndGroupsResult> ListUsersAndGroups(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
