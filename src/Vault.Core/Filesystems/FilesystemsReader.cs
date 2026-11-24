using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Filesystems;

/// <summary>
/// UC-23: lists the filesystems OMV manages, mounted or not. One call, <c>FileSystemMgmt.enumerateFilesystems</c>, checked against
/// a real OMV 8.5.12: a plain list of <c>{canonicaldevicefile, uuid, label, type, mounted, mountpoint, size, used, available, ...}</c>.
/// <c>size</c> and <c>available</c> are bytes as text, <c>percentage</c> a number, and <c>used</c> is human text (<c>"22.10 GiB"</c>), passed on as it is
/// (OMV only gives it rounded); an unmounted filesystem has <c>-1</c> there and an empty <c>mountpoint</c>, and an empty
/// <c>label</c> means none. The mapping stays tolerant: only an answer that is not a list, or an entry without a device, is a bad response.
/// </summary>
public sealed class FilesystemsReader(IOmvRpcClient omv, ILogger<FilesystemsReader> logger)
{
    private const string What = "Listing the filesystems";

    public async Task<FilesystemsResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_filesystems");
        FilesystemsResult result;
        try
        {
            var response = await omv.CallAsync(OmvRpcCall.Filesystems, null, cancellationToken);
            result = Map(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = FilesystemsResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = FilesystemsResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.FilesystemsCount, result.Filesystems.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    public static FilesystemsResult Map(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("data", out var data))
        {
            response = data;
        }

        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The answer for the filesystems is not a list.");
        }

        var filesystems = response.EnumerateArray()
            .Select(entry =>
            {
                var device = NullIfBlank(entry.ReadString("canonicaldevicefile")) ?? NullIfBlank(entry.ReadString("devicefile"))
                    ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A filesystem entry has no device.");
                var mountPoint = NullIfBlank(entry.ReadString("mountpoint"));
                var mounted = entry.ReadBool("mounted") ?? mountPoint is not null;
                return new Filesystem(
                    device,
                    NullIfBlank(entry.ReadString("uuid")),
                    NullIfBlank(entry.ReadString("type")),
                    NullIfBlank(entry.ReadString("label")),
                    mounted,
                    mounted ? mountPoint : null,
                    mounted ? NonNegative(entry.ReadLong("size")) : null,
                    mounted ? UsedText(entry) : null,
                    mounted ? NonNegative(entry.ReadLong("available")) : null,
                    mounted && NonNegative(entry.ReadLong("percentage")) is { } percent ? (int)percent : null);
            })
            .ToList();

        return filesystems.Count == 0 ? FilesystemsResult.Empty(Messages.NoFilesystems) : FilesystemsResult.Found(filesystems);
    }

    /// <summary>OMV reports <c>-1</c> for what it cannot know (not mounted).</summary>
    private static long? NonNegative(long? value) => value is >= 0 ? value : null;

    /// <summary>The used space exactly as OMV shows it (<c>"22.10 GiB"</c>). <c>-1</c> or empty means OMV does not know.</summary>
    private static string? UsedText(JsonElement entry)
    {
        var text = NullIfBlank(entry.ReadString("used"))?.Trim();
        return text is null or "-1" ? null : text;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the filesystems failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the filesystems failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
