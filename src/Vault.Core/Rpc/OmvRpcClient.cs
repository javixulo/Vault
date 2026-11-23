using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;

namespace Vault.Core.Rpc;

/// <summary>
/// Talks to the JSON-RPC endpoint of OpenMediaVault (<c>POST &lt;base&gt;/rpc.php</c>). It authenticates with
/// <c>Session.login</c> and keeps the session cookie in the <see cref="HttpClient"/>'s handler, so the handler must have
/// cookies on and live as long as this client. When a call finds the session expired it logs in again, once, and repeats
/// the call. It only makes the calls of <see cref="OmvRpcCall"/> (technical spec, section 5).
/// </summary>
public sealed class OmvRpcClient(HttpClient http, Uri endpoint, IOmvCredentialsProvider credentials, ILogger<OmvRpcClient> logger) : IOmvRpcClient, IDisposable
{
    /// <summary>
    /// Codes of the RPC error object that mean "the session is not (or no longer) valid". Not yet confirmed against a real
    /// OMV 7/8 (open question *OMV version*); an HTTP 401 or 403 counts too.
    /// </summary>
    private static readonly int[] SessionErrorCodes = [5001, 5002];

    private readonly SemaphoreSlim _loginGate = new(1, 1);
    private int _generation;

    public async Task<JsonElement> CallAsync(OmvRpcCall rpcCall, object? parameters, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("omv_rpc");
        activity?.SetTag(Telemetry.RpcService, rpcCall.Service);
        activity?.SetTag(Telemetry.RpcMethod, rpcCall.Method);
        try
        {
            var generation = await EnsureSessionAsync(cancellationToken);
            try
            {
                return await PostAsync(rpcCall, parameters, cancellationToken);
            }
            catch (OmvRpcException expired) when (expired.Kind == OmvRpcErrorKind.SessionExpired)
            {
                LogSessionExpired(rpcCall.Name);
                await LoginIfStaleAsync(generation, cancellationToken);
                try
                {
                    return await PostAsync(rpcCall, parameters, cancellationToken);
                }
                catch (OmvRpcException again) when (again.Kind == OmvRpcErrorKind.SessionExpired)
                {
                    throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"OMV rejected the new session right after logging in ({again.Message}).", again.RpcCode, again);
                }
            }
        }
        catch (OmvRpcException error)
        {
            activity.SetErrorTypes([Messages.ForRpc(error, rpcCall.Name).Code]);
            throw;
        }
    }

    public void Dispose() => _loginGate.Dispose();

    public async Task VerifyAccessAsync(CancellationToken cancellationToken = default) =>
        await EnsureSessionAsync(cancellationToken);

    /// <summary>The generation of the current session, logging in first if there is none yet.</summary>
    private async Task<int> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        return generation != 0 ? generation : await LoginIfStaleAsync(0, cancellationToken);
    }

    /// <summary>
    /// Logs in unless another call already did since <paramref name="seen"/> (the generation of the session the caller used),
    /// so several calls that find the session expired at the same time cause one login. Returns the current generation.
    /// </summary>
    private async Task<int> LoginIfStaleAsync(int seen, CancellationToken cancellationToken)
    {
        await _loginGate.WaitAsync(cancellationToken);
        try
        {
            if (_generation != seen)
            {
                return _generation;
            }

            await LoginAsync(cancellationToken);
            _generation = seen + 1;
            return _generation;
        }
        finally
        {
            _loginGate.Release();
        }
    }

    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        var account = credentials.Load();
        JsonElement response;
        try
        {
            response = await PostAsync(OmvRpcCall.Login, new { username = account.Username, password = account.Password }, cancellationToken);
        }
        catch (OmvRpcException error) when (error.Kind is OmvRpcErrorKind.RpcError or OmvRpcErrorKind.SessionExpired)
        {
            // OMV refuses a bad login with an RPC error or an HTTP 401: either way, the credentials are the problem.
            throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"OMV refused the login of '{account.Username}': {error.Message}", error.RpcCode, error);
        }

        var authenticated = response.ValueKind == JsonValueKind.Object
            && response.TryGetProperty("authenticated", out var flag)
            && flag.ValueKind == JsonValueKind.True;
        if (!authenticated)
        {
            throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"OMV did not authenticate '{account.Username}' (authenticated is not true).");
        }

        LogLoggedIn(account.Username);
    }

    private async Task<JsonElement> PostAsync(OmvRpcCall call, object? parameters, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(new { service = call.Service, method = call.Method, @params = parameters, options = (object?)null });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(endpoint, content, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new OmvRpcException(OmvRpcErrorKind.Unreachable, $"OMV could not be reached: {error.Message}", inner: error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OmvRpcException(OmvRpcErrorKind.Unreachable, "OMV did not answer within the time limit.", inner: error);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (call != OmvRpcCall.Login && response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                throw new OmvRpcException(OmvRpcErrorKind.SessionExpired, $"OMV answered HTTP {(int)response.StatusCode} to {call.Name}.");
            }

            return ReadResponse(call, (int)response.StatusCode, text);
        }
    }

    private static JsonElement ReadResponse(OmvRpcCall call, int status, string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException error)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"OMV answered HTTP {status} to {call.Name} with something that is not JSON.", inner: error);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The answer to {call.Name} is not a JSON object.");
            }

            if (root.TryGetProperty("error", out var rpcError) && rpcError.ValueKind == JsonValueKind.Object)
            {
                int? code = rpcError.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var number) ? number : null;
                var message = rpcError.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var kind = code is { } known && SessionErrorCodes.Contains(known) ? OmvRpcErrorKind.SessionExpired : OmvRpcErrorKind.RpcError;
                throw new OmvRpcException(kind, $"OMV returned an error to {call.Name} (code {code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}): {message}", code);
            }

            if (!root.TryGetProperty("response", out var result))
            {
                throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The answer to {call.Name} has no 'response' (HTTP {status}).");
            }

            return result.Clone();
        }
    }

    // Plain calls instead of [LoggerMessage]: the dotted names of the shared backend (`rpc.method`) are not valid C# identifiers.
#pragma warning disable CA1848, CA1873, CA1727
    private void LogLoggedIn(string username) =>
        logger.LogInformation("Logged in to OpenMediaVault as {username}", username);

    private void LogSessionExpired(string call) =>
        logger.LogInformation("The OpenMediaVault session expired during {rpc.method}: logging in again", call);
#pragma warning restore CA1848, CA1873, CA1727
}
