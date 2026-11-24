using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Updates;

/// <summary>
/// UC-18 and UC-19: reports the system updates OMV has pending. One call, <c>Apt.enumerateUpgraded</c>, checked against a real OMV
/// 8.5.12: a plain list of APT package records (<c>name</c>, <c>package</c>, <c>version</c> the candidate, <c>oldversion</c> the installed
/// one, empty for a package that is new, and many fields Vault does not pass on). The first guess, <c>Apt.getUpgradeList</c>, does not
/// exist. The mapping stays tolerant (a list, or an object with a <c>data</c> list): only an answer that is not a list, or an entry
/// without a name, is a bad response, because a package left out would make the count wrong.
/// </summary>
public sealed class PendingUpdatesReader(IOmvRpcClient omv, ILogger<PendingUpdatesReader> logger)
{
    private const string What = "Listing the pending updates";

    public async Task<PendingUpdatesResult> ReadAsync(bool? detailed, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_pending_updates");
        PendingUpdatesResult result;
        try
        {
            var response = await omv.CallAsync(OmvRpcCall.PendingUpdates, null, cancellationToken);
            result = Map(response, detailed ?? false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = PendingUpdatesResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = PendingUpdatesResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.PendingUpdatesCount, result.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    public static PendingUpdatesResult Map(JsonElement response, bool detailed)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("data", out var data))
        {
            response = data;
        }

        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The answer for the pending updates is not a list.");
        }

        var updates = response.EnumerateArray()
            .Select(entry => new PendingUpdate(
                NullIfBlank(entry.ReadString("package")) ?? NullIfBlank(entry.ReadString("name"))
                    ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A pending update entry has no package name."),
                NullIfBlank(entry.ReadString("oldversion")),
                NullIfBlank(entry.ReadString("version"))))
            .ToList();

        return updates.Count == 0
            ? PendingUpdatesResult.UpToDate(Messages.NoPendingUpdates)
            : PendingUpdatesResult.Found(updates.Count, detailed ? updates : []);
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the pending updates failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the pending updates failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
