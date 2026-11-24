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
        "Lists the OpenMediaVault (OMV) users and groups, and which groups each user belongs to. " +
        "The result has two blocks, each with its own users and groups: system (accounts and groups of the operating system, like root or sshd) " +
        "and custom (the ones created in OMV, usually the ones that matter for permissions). " +
        "Use it to read the names that get_folder_permissions and get_folder_acl return against who actually exists and who is in which group. " +
        "A NAS has many system entries: pass name to narrow both blocks, for example to the group you are auditing. " +
        "Read-only: it never changes anything on the NAS. " +
        "If nothing matches, both blocks are empty and message says so: not an error.")]
    public Task<UsersAndGroupsResult> ListUsersAndGroups(
        [Description("Optional. Search by name, ignoring case. Plain text matches any part of the name (media finds grupo-media). With * for any characters, the whole name must match: grupo-* is the names that start with grupo-, *fotos the ones that end with fotos, *foto* the ones that contain foto. It is not a regular expression. Leave it out to list everything.")] string? name = null,
        CancellationToken cancellationToken = default) =>
        reader.ReadAsync(name, cancellationToken);
}
