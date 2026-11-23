using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.Shares;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListSharedFoldersTool(SharedFolderLister lister)
{
    [McpServerTool(Name = "list_shared_folders", Title = "List shared folders", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists the shared folders defined in OpenMediaVault (OMV), ordered as OMV orders them: name, uuid, path relative to its file system and description. " +
        "Read-only: it never changes anything on the NAS. " +
        "The list is paged: pass limit (default 50, at most 200) and offset (default 0); has_more says whether another page exists and total is the number of folders OMV has. " +
        "It does not return permissions or ACLs. " +
        "If there are no folders, or the offset is past the end, it is not an error: the list is empty and message says why and what to do next.")]
    public Task<SharedFoldersResult> ListSharedFolders(
        [Description("How many folders to return: 1 to 200. Example: 20. Default 50.")] int? limit = null,
        [Description("How many folders to skip, for the next page: 0 or more. Example: 50. Default 0.")] int? offset = null,
        CancellationToken cancellationToken = default) =>
        lister.ListAsync(limit, offset, cancellationToken);
}
