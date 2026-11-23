using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.SystemInfo;

/// <summary>UC-01 and UC-02: reads the state of the machine (<c>System.getInformation</c>) and maps it to <see cref="SystemStatus"/>.</summary>
public sealed class SystemStatusReader(IOmvRpcClient omv, ILogger<SystemStatusReader> logger)
{
    private const string What = "Reading the system status";

    public async Task<SystemStatusResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("system_status");
        SystemStatusResult result;
        try
        {
            var response = await omv.CallAsync(OmvRpcCall.SystemInformation, null, cancellationToken);
            result = SystemStatusResult.Success(Map(response));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = SystemStatusResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = SystemStatusResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    /// <summary>Maps the fields of <c>System.getInformation</c> that Vault exposes. Each one is optional.</summary>
    public static SystemStatus Map(JsonElement information) => new(
        Hostname: information.ReadString("hostname"),
        OmvVersion: information.ReadString("version"),
        Kernel: information.ReadString("kernel"),
        CpuModel: information.ReadString("cpuModelName"),
        CpuUsagePercent: information.ReadDouble("cpuUtilization"),
        MemoryTotalBytes: information.ReadLong("memTotal"),
        MemoryUsedBytes: information.ReadLong("memUsed"),
        UptimeSeconds: information.ReadLong("uptime"));

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Reading the system status failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Reading the system status failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
