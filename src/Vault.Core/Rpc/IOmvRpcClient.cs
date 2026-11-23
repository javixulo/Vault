using System.Text.Json;

namespace Vault.Core.Rpc;

/// <summary>
/// The port to OpenMediaVault. It only accepts an <see cref="OmvRpcCall"/>, which can only be one of the fixed, read-only
/// calls of the functional spec: there is no method that takes a service or method name as text.
/// </summary>
public interface IOmvRpcClient
{
    /// <summary>
    /// Calls one RPC method and returns its <c>response</c>. Logs in first when there is no session, and once more if the
    /// session expired. Throws <see cref="OmvRpcException"/> for anything that goes wrong with OMV.
    /// </summary>
    Task<JsonElement> CallAsync(OmvRpcCall rpcCall, object? parameters, CancellationToken cancellationToken = default);

    /// <summary>Logs in (or checks the session) without calling anything else. Used at startup to say early whether OMV is reachable.</summary>
    Task VerifyAccessAsync(CancellationToken cancellationToken = default);
}
