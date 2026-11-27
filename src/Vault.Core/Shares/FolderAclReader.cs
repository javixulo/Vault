using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Shares;

/// <summary>
/// UC-20 to UC-22, UC-27 and UC-28: reads the extended POSIX ACL of a shared folder's root or of a subfolder, and, on request,
/// its direct subfolders. The name the client gives is looked up in <c>ShareMgmt.getList</c>, then <c>ShareMgmt.getFileACL</c> and
/// <c>FolderBrowser.get</c> are asked for that uuid and the path. A subfolder is first looked for in the list of its parent:
/// OMV 8.5.12 answers an empty list for a path that does not exist, and the check keeps Vault from reading the ACL of a file.
/// </summary>
public sealed class FolderAclReader(IOmvRpcClient omv, ILogger<FolderAclReader> logger)
{
    private const string What = "Reading the folder ACL";

    public async Task<FolderAclResult> ReadAsync(string? folder, string? relativePath = null, bool includeSubfolders = false, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("get_folder_acl");
        var asked = folder ?? string.Empty;
        FolderAclResult result;
        try
        {
            result = await ReadCoreAsync(asked, relativePath, includeSubfolders, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = FolderAclResult.Failure(asked, relativePath, Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = FolderAclResult.Failure(asked, relativePath, new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.AclEntriesCount, result.Entries.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    private async Task<FolderAclResult> ReadCoreAsync(string folder, string? relativePath, bool includeSubfolders, CancellationToken cancellationToken)
    {
        FolderAclResult Fail(string code, string message) => FolderAclResult.Failure(folder, relativePath, new VaultError(code, message));

        if (string.IsNullOrWhiteSpace(folder))
        {
            return Fail(ErrorCodes.InvalidArguments, Messages.BlankFolder);
        }

        if (!RelativePath.TryParse(relativePath, out var levels))
        {
            return Fail(ErrorCodes.InvalidArguments, Messages.BadRelativePath);
        }

        var uuid = await SharedFolderLookup.FindUuidAsync(omv, folder, cancellationToken);
        if (uuid is null)
        {
            LogNotFound();
            return Fail(ErrorCodes.NotFound, Messages.FolderNotFound(folder));
        }

        var subfolder = levels.Length == 0 ? null : string.Join('/', levels);
        if (subfolder is not null)
        {
            var siblings = await ListSubfoldersAsync(uuid, RelativePath.ForOmv(levels.AsSpan(0, levels.Length - 1)), cancellationToken);
            if (!siblings.Contains(levels[^1], StringComparer.Ordinal))
            {
                LogSubfolderNotFound();
                return Fail(ErrorCodes.NotFound, Messages.SubfolderNotFound(folder, subfolder));
            }
        }

        var path = RelativePath.ForOmv(levels);
        var response = await omv.CallAsync(OmvRpcCall.SharedFolderAcl, new { uuid, file = path }, cancellationToken);
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("acl", out var acl) || acl.ValueKind != JsonValueKind.Object)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "ShareMgmt.getFileACL did not return an object with an 'acl' object.");
        }

        var ownerPerms = BasePermissions(acl, "user");
        var groupPerms = BasePermissions(acl, "group");
        var otherPerms = BasePermissions(acl, "other");

        // OMV lists every user and group of the NAS, with "perms": null for those that have no entry in the ACL; only the
        // others are reported. An entry Vault cannot read is a bad response, not a skipped line: dropping it would hide
        // an access from an audit.
        var entries = ReadEntries(acl, "users", "user").Concat(ReadEntries(acl, "groups", "group")).ToList();
        var subfolders = includeSubfolders ? await ListSubfoldersAsync(uuid, path, cancellationToken) : null;
        return FolderAclResult.Found(
            folder,
            subfolder,
            response.ReadString("owner"),
            response.ReadString("group"),
            ownerPerms,
            groupPerms,
            otherPerms,
            entries,
            subfolders,
            entries.Count == 0 ? Messages.NoAclEntries(folder, subfolder) : null);
    }

    /// <summary>The names of the direct subfolders. A name Vault cannot read is a bad response, not a skipped line.</summary>
    private async Task<List<string>> ListSubfoldersAsync(string uuid, string path, CancellationToken cancellationToken)
    {
        var response = await omv.CallAsync(OmvRpcCall.FolderBrowser, new { uuid, type = "sharedfolder", path }, cancellationToken);
        if (response.ValueKind != JsonValueKind.Array ||
            response.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString())))
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "FolderBrowser.get did not return a list of folder names.");
        }

        return [.. response.EnumerateArray().Select(item => item.GetString()!)];
    }

    private static string BasePermissions(JsonElement acl, string name) =>
        Format(acl.ReadLong(name)) ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The ACL has no valid '{name}' permissions.");

    private static IEnumerable<AclEntry> ReadEntries(JsonElement acl, string property, string subjectType)
    {
        if (!acl.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The ACL has no '{property}' array.");
        }

        foreach (var entry in list.EnumerateArray())
        {
            var name = entry.ReadString("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "An ACL entry has no name.");
            }

            if (!entry.TryGetProperty("perms", out var perms) || perms.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var text = Format(entry.ReadLong("perms")) ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "An ACL entry has an unknown 'perms' value.");
            yield return new AclEntry(subjectType, name, text);
        }
    }

    /// <summary>OMV sends the POSIX bits as a number from 0 to 7 (4 read, 2 write, 1 execute): <c>rwx</c>, <c>r-x</c>, <c>---</c>.</summary>
    public static string? Format(long? bits) =>
        bits is >= 0 and <= 7
            ? string.Create(3, (int)bits.Value, static (span, value) =>
            {
                span[0] = (value & 4) != 0 ? 'r' : '-';
                span[1] = (value & 2) != 0 ? 'w' : '-';
                span[2] = (value & 1) != 0 ? 'x' : '-';
            })
            : null;

#pragma warning disable CA1848, CA1873, CA1727
    private void LogNotFound() =>
        logger.LogWarning("No shared folder with the requested name was found");

    private void LogSubfolderNotFound() =>
        logger.LogWarning("No subfolder with the requested relative path was found");

    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Reading the folder ACL failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Reading the folder ACL failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
