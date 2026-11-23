using System.Text.Json;
using Vault.Core.Rpc;

namespace Vault.UnitTests;

/// <summary>An in-memory double of the OMV port: it answers with what the test says and records the calls.</summary>
internal sealed class FakeRpcClient : IOmvRpcClient
{
    public List<(OmvRpcCall Call, JsonElement Parameters)> Calls { get; } = [];

    public Func<OmvRpcCall, JsonElement> Handler { get; set; } = call => throw new InvalidOperationException($"No answer set for {call}.");

    public static FakeRpcClient Answering(string json) => new() { Handler = _ => Json(json) };

    public static FakeRpcClient Failing(Exception error) => new() { Handler = _ => throw error };

    public async Task<JsonElement> CallAsync(OmvRpcCall rpcCall, object? parameters, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        Calls.Add((rpcCall, JsonSerializer.SerializeToElement(parameters)));
        return Handler(rpcCall);
    }

    public Task VerifyAccessAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
