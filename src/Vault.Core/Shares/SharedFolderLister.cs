using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Shares;

/// <summary>UC-03 to UC-05: lists the shared folders OMV defines (<c>ShareMgmt.getList</c>), one bounded page at a time.</summary>
public sealed class SharedFolderLister(IOmvRpcClient omv, ILogger<SharedFolderLister> logger)
{
    public const int DefaultLimit = 50;
    private const string What = "Listing the shared folders";

    public async Task<SharedFoldersResult> ListAsync(int? limit, int? offset, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_shared_folders");
        SharedFoldersResult result;
        try
        {
            result = await ListCoreAsync(limit, offset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = SharedFoldersResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = SharedFoldersResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.FoldersCount, result.Folders.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    private async Task<SharedFoldersResult> ListCoreAsync(int? limit, int? offset, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > Messages.MaxLimit)
        {
            return SharedFoldersResult.Failure(new VaultError(ErrorCodes.InvalidArguments, Messages.InvalidLimit(limit)));
        }

        if (offset is < 0)
        {
            return SharedFoldersResult.Failure(new VaultError(ErrorCodes.InvalidArguments, Messages.InvalidOffset(offset)));
        }

        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;
        var response = await omv.CallAsync(OmvRpcCall.SharedFolders, new { start = skip, limit = take }, cancellationToken);

        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "ShareMgmt.getList did not return an object with a 'data' array.");
        }

        var folders = data.EnumerateArray()
            .Select(Map)
            .OfType<SharedFolder>()
            .ToList();
        var total = response.ReadLong("total") is { } reported ? (int)reported : skip + folders.Count;

        if (folders.Count == 0)
        {
            LogEmpty(total, skip);
            return SharedFoldersResult.Empty(total, Messages.NoFolders(total, skip));
        }

        return SharedFoldersResult.Found(folders, total, hasMore: skip + folders.Count < total);
    }

    /// <summary>Maps one entry of <c>data</c>. An entry with no name is dropped: it cannot be told apart.</summary>
    public static SharedFolder? Map(JsonElement entry)
    {
        var name = entry.ReadString("name");
        return string.IsNullOrWhiteSpace(name)
            ? null
            : new SharedFolder(name, entry.ReadString("uuid"), entry.ReadString("reldirpath"), entry.ReadString("comment", "description"));
    }

#pragma warning disable CA1848, CA1873, CA1727
    private void LogEmpty(int total, int offset) =>
        logger.LogWarning("No shared folders returned at offset {offset} of {folders.count}", offset, total);

    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the shared folders failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the shared folders failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
