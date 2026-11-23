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
