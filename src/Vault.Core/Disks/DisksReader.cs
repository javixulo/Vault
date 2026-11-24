using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Disks;

/// <summary>
/// UC-15: lists the disks OMV sees and their SMART status. Two calls, both checked against a real OMV 8.5.12:
/// <c>DiskMgmt.enumerateDevices</c> (a plain list of every disk, with <c>canonicaldevicefile</c>, <c>model</c>, <c>size</c> as text
/// and <c>temperature</c> as text, empty when unknown) and <c>Smart.getList</c> (<c>{total, data}</c> with only the disks that
/// have SMART support and an <c>overallstatus</c> such as <c>GOOD</c> or <c>BAD_STATUS</c>). A disk with no SMART entry is
/// <c>unknown</c>. The mapping stays tolerant: only an answer that is not a list, or a disk without a device, is a bad response.
/// </summary>
public sealed class DisksReader(IOmvRpcClient omv, ILogger<DisksReader> logger)
{
    private const string What = "Listing the disks";

    public async Task<DisksResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_disks");
        DisksResult result;
        try
        {
            var devices = await omv.CallAsync(OmvRpcCall.Disks, null, cancellationToken);
            var smart = await omv.CallAsync(OmvRpcCall.DisksSmart, new { start = 0, limit = -1 }, cancellationToken);
            result = Map(devices, smart);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = DisksResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = DisksResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.DisksCount, result.Disks.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    public static DisksResult Map(JsonElement devices, JsonElement smart)
    {
        var smartByDevice = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in Entries(smart, "SMART list"))
        {
            if (DeviceOf(entry) is { } device)
            {
                smartByDevice[device] = entry;
            }
        }

        var disks = Entries(devices, "disks")
            .Select(entry =>
            {
                var device = DeviceOf(entry) ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A disk entry has no device.");
                smartByDevice.TryGetValue(device, out var smartEntry);
                return new Disk(
                    device,
                    NullIfBlank(entry.ReadString("model")),
                    entry.ReadLong("size"),
                    SmartStatusOf(smartEntry.ReadString("overallstatus")),
                    entry.ReadDouble("temperature") ?? smartEntry.ReadDouble("temperature"));
            })
            .ToList();

        return disks.Count == 0 ? DisksResult.Empty(Messages.NoDisks) : DisksResult.Found(disks);
    }

    /// <summary>OMV's overall SMART status: <c>GOOD</c> passes, any <c>BAD_*</c> fails, everything else (or nothing) is unknown.</summary>
    private static string SmartStatusOf(string? overall) => overall switch
    {
        null => SmartStatuses.Unknown,
        _ when overall.Equals("GOOD", StringComparison.OrdinalIgnoreCase) => SmartStatuses.Passed,
        _ when overall.StartsWith("BAD", StringComparison.OrdinalIgnoreCase) => SmartStatuses.Failed,
        _ => SmartStatuses.Unknown,
    };

    private static string? DeviceOf(JsonElement entry) =>
        NullIfBlank(entry.ReadString("canonicaldevicefile")) ?? NullIfBlank(entry.ReadString("devicefile"));

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static JsonElement.ArrayEnumerator Entries(JsonElement response, string what)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("data", out var data))
        {
            response = data;
        }

        return response.ValueKind == JsonValueKind.Array
            ? response.EnumerateArray()
            : throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The answer for the {what} is not a list.");
    }

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the disks failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the disks failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
