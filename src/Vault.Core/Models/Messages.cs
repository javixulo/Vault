using Vault.Core.Rpc;

namespace Vault.Core.Models;

/// <summary>
/// The messages of the tool results, written for the AI that reads them: what happened, whether anything changed (Vault
/// never changes anything on the NAS) and what to do next (technical spec, section 6). They never carry a path, a URL,
/// a credential or the text OMV sent back.
/// </summary>
public static class Messages
{
    public const int MaxLimit = 200;

    public static string Failed(string what) =>
        $"{what} failed because of an unexpected problem inside Vault, not because of the request. Nothing was changed on the NAS. " +
        "Do not retry the same call; tell the user, who can find the cause in the Vault logs.";

    public static string InvalidLimit(int? limit) =>
        $"The limit {limit} is not valid: it must be a whole number between 1 and {MaxLimit}. Call the tool again with a valid limit, or leave it out.";

    public static string InvalidOffset(int? offset) =>
        $"The offset {offset} is not valid: it must be 0 or more. Call the tool again with a valid offset, or leave it out.";

    public static string NoFolders(int total, int offset) =>
        total == 0
            ? "OpenMediaVault has no shared folders defined. Nothing is wrong with the call; tell the user the NAS has none."
            : $"The offset {offset} is past the end of the list, which has {total} shared folders. Call again with an offset below {total}, or leave it out.";

    public const string BlankFolder =
        "The folder argument is missing or blank: it must be the name of a shared folder, as list_shared_folders returns it. " +
        "Call the tool again with the folder name.";

    public static string FolderNotFound(string folder) =>
        $"OpenMediaVault has no shared folder named '{folder}'. Nothing was changed. " +
        "Check the exact name (it is case-sensitive) with list_shared_folders and call again with it.";

    public static string NoPrivileges(string folder) =>
        $"The shared folder '{folder}' has no explicit privilege set for any user or group, so OpenMediaVault's default applies. " +
        "Nothing is wrong with the call; tell the user no user or group has been granted or denied access explicitly.";

    public const string BadRelativePath =
        "The relative_path argument is not a valid path of a subfolder: write the folder names inside the shared folder separated by '/', " +
        "for example Manuales/Timbre, with no '.', '..' or empty level. Call the tool again with a valid relative_path, or without it for the shared folder's root.";

    public static string SubfolderNotFound(string folder, string relativePath) =>
        $"The shared folder '{folder}' has no subfolder '{relativePath}' (it may not exist, or be a file). Nothing was changed. " +
        "Check the exact names (they are case-sensitive): call get_folder_acl with the parent path and include_subfolders true, and use a name from its subfolders.";

    public static string NoAclEntries(string folder, string? relativePath) =>
        (relativePath is null ? $"The root of the shared folder '{folder}'" : $"The subfolder '{relativePath}' of the shared folder '{folder}'") +
        " has no extended ACL entry for any named user or group; only the owner, group and other permissions apply. " +
        "Nothing is wrong with the call; tell the user the folder has no extra ACL entries.";

    public static string NoUsersOrGroups(string? name) =>
        name is null
            ? "OpenMediaVault reports no users or groups. Nothing is wrong with the call; tell the user the NAS has none."
            : $"No user or group has a name that contains '{name}'. Nothing is wrong with the call. Try a shorter or different text, or leave the name out to list everything.";

    public const string NoServices =
        "OpenMediaVault reports no services. Nothing is wrong with the call; tell the user OMV listed none.";

    public const string NoDisks =
        "OpenMediaVault reports no disks. Nothing is wrong with the call; tell the user OMV listed none.";

    public const string NoFilesystems =
        "OpenMediaVault reports no filesystems. Nothing is wrong with the call; tell the user OMV listed none.";

    public const string NoPendingUpdates =
        "OpenMediaVault has no pending updates: the system is up to date as far as OMV knows. Nothing is wrong with the call; tell the user nothing needs updating. " +
        "This is what OMV last found; Vault does not run apt update.";

    public const string NoDockerContainers =
        "OpenMediaVault reports no Docker containers. Nothing is wrong with the call; tell the user OMV listed none. " +
        "If some were expected, list_services shows whether the Docker service is running.";

    public const string NoScheduledTasks =
        "OpenMediaVault has no scheduled tasks (cron jobs). Nothing is wrong with the call; tell the user OMV has none configured.";

    /// <summary>The error a tool returns when OMV could not be asked (or did not answer as expected).</summary>
    public static VaultError ForRpc(OmvRpcException error, string what) => error.Kind switch
    {
        OmvRpcErrorKind.Unreachable => new VaultError(
            ErrorCodes.OmvUnreachable,
            $"{what} failed: Vault could not reach OpenMediaVault (no answer within the time limit, or the connection was refused). Nothing was changed. " +
            "Tell the user the NAS web interface may be down or unreachable from Vault; retry once at most."),
        OmvRpcErrorKind.AuthFailed => new VaultError(
            ErrorCodes.OmvAuthFailed,
            $"{what} failed: OpenMediaVault refused the credentials Vault has (or the session could not be renewed). Nothing was changed. " +
            "Do not retry; tell the user to check the OMV account and the credentials secret of Vault."),
        OmvRpcErrorKind.Forbidden => new VaultError(
            ErrorCodes.OmvAuthFailed,
            $"{what} failed: OpenMediaVault accepted the account Vault logs in with, but does not allow it this call (the account lacks the role it needs). Nothing was changed. " +
            "Do not retry; tell the user the OMV account of Vault needs more rights for this tool."),
        OmvRpcErrorKind.BadResponse => new VaultError(
            ErrorCodes.OmvBadResponse,
            $"{what} failed: OpenMediaVault answered in a format Vault does not understand (the OMV version may differ from the one Vault supports). Nothing was changed. " +
            "Do not retry; tell the user."),
        _ => new VaultError(
            ErrorCodes.OmvRpcError,
            $"{what} failed: OpenMediaVault answered with an error{(error.RpcCode is { } code ? $" (code {code})" : "")}. Nothing was changed. " +
            "Do not retry the same call; tell the user, who can find the detail in the Vault logs."),
    };
}
