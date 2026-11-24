using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Services;

/// <summary>
/// UC-14: lists the services OMV manages and whether each is enabled and running (<c>Services.getStatus</c>, one call, an answer
/// of <c>{name, title, enabled, running}</c> entries). Checked against a real OMV 8.5.12; the mapping stays tolerant: a field
/// Vault does not find is empty, and only an entry without a name or an answer that is not a list is a bad response.
/// </summary>
public sealed class ServicesReader(IOmvRpcClient omv, ILogger<ServicesReader> logger)
{
    private const string What = "Listing the services";

    public async Task<ServicesResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_services");
        ServicesResult result;
        try
        {
            result = Map(await omv.CallAsync(OmvRpcCall.ServicesStatus, null, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = ServicesResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = ServicesResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.ServicesCount, result.Services.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    public static ServicesResult Map(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("data", out var data))
        {
            response = data;
        }

        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The answer for the services is not a list.");
        }

        var services = response.EnumerateArray()
            .Select(entry => new OmvService(
                entry.ReadString("name") is { } name && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A service entry has no name."),
                entry.ReadBool("enabled"),
                entry.ReadBool("running")))
            .ToList();

        return services.Count == 0 ? ServicesResult.Empty(Messages.NoServices) : ServicesResult.Found(services);
    }

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the services failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the services failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
