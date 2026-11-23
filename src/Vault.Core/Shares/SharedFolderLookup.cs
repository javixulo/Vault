using System.Text.Json;
using Vault.Core.Rpc;

namespace Vault.Core.Shares;

/// <summary>OMV identifies a shared folder by uuid; the tools that take a folder name look the uuid up here.</summary>
internal static class SharedFolderLookup
{
    /// <summary>The uuid of the shared folder with exactly this name (OMV names are case-sensitive), or null when there is none.</summary>
    public static async Task<string?> FindUuidAsync(IOmvRpcClient omv, string folder, CancellationToken cancellationToken)
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
}
