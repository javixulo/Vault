using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Shares;

/// <summary>
/// UC-10 to UC-12: reads the privileges OMV has configured for one shared folder. OMV identifies a folder by uuid, so the
/// name the client gives is first looked up in <c>ShareMgmt.getList</c>, then <c>ShareMgmt.getPrivileges</c> is asked for it.
/// </summary>
public sealed class FolderPermissionsReader(IOmvRpcClient omv, ILogger<FolderPermissionsReader> logger)
{
    private const string What = "Reading the folder permissions";

    // OMV's permission levels for a shared folder: 0 no access, 5 read-only, 7 read and write.
    private const int PermsDeny = 0;
    private const int PermsReadOnly = 5;
    private const int PermsReadWrite = 7;

    public async Task<FolderPermissionsResult> ReadAsync(string? folder, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("get_folder_permissions");
        var asked = folder ?? string.Empty;
        FolderPermissionsResult result;
        try
        {
            result = await ReadCoreAsync(asked, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = FolderPermissionsResult.Failure(asked, Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = FolderPermissionsResult.Failure(asked, new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.PrivilegesCount, result.Privileges.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    private async Task<FolderPermissionsResult> ReadCoreAsync(string folder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return FolderPermissionsResult.Failure(folder, new VaultError(ErrorCodes.InvalidArguments, Messages.BlankFolder));
        }

        var uuid = await FindUuidAsync(folder, cancellationToken);
        if (uuid is null)
        {
            LogNotFound();
            return FolderPermissionsResult.Failure(folder, new VaultError(ErrorCodes.NotFound, Messages.FolderNotFound(folder)));
        }

        var response = await omv.CallAsync(OmvRpcCall.SharedFolderPrivileges, new { uuid }, cancellationToken);
        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "ShareMgmt.getPrivileges did not return an array.");
        }

        // An entry Vault cannot read is a bad response, not a skipped line: dropping it would hide a privilege from an audit.
        // OMV lists every user and group; the ones with "perms": null have no explicit privilege and are not reported.
        var privileges = response.EnumerateArray().Select(Map).OfType<Privilege>().ToList();
        return privileges.Count == 0
            ? FolderPermissionsResult.Empty(folder, Messages.NoPrivileges(folder))
            : FolderPermissionsResult.Found(folder, privileges);
    }

    /// <summary>The uuid of the shared folder with exactly this name (OMV names are case-sensitive), or null when there is none.</summary>
    private async Task<string?> FindUuidAsync(string folder, CancellationToken cancellationToken)
    {
        var response = await omv.CallAsync(OmvRpcCall.SharedFolders, new { start = 0, limit = (int?)null }, cancellationToken);
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "ShareMgmt.getList did not return an object with a 'data' array.");
        }

        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ReadString("name") != folder)
            {
                continue;
            }

            return entry.ReadString("uuid") is { Length: > 0 } uuid
                ? uuid
                : throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The shared folder entry has no uuid.");
        }

        return null;
    }

    /// <summary>
    /// Maps one entry of <c>ShareMgmt.getPrivileges</c> (checked against OMV 8.5.12: one entry per user and group, with
    /// <c>perms</c> null when none is set). Null when the entry has no explicit privilege.
    /// </summary>
    public static Privilege? Map(JsonElement entry)
    {
        var type = entry.ReadString("type");
        var name = entry.ReadString("name");
        if (type is not ("user" or "group") || string.IsNullOrWhiteSpace(name))
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A privilege entry has no valid type or name.");
        }

        var hasPerms = entry.TryGetProperty("perms", out var perms) && perms.ValueKind != JsonValueKind.Null;
        if (!hasPerms)
        {
            return null;
        }

        var access = entry.ReadLong("perms") switch
        {
            PermsDeny => "deny",
            PermsReadOnly => "read_only",
            PermsReadWrite => "read_write",
            _ => throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A privilege entry has an unknown 'perms' value."),
        };
        return new Privilege(type, name, access);
    }

#pragma warning disable CA1848, CA1873, CA1727
    private void LogNotFound() =>
        logger.LogWarning("No shared folder with the requested name was found");

    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Reading the folder permissions failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Reading the folder permissions failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
