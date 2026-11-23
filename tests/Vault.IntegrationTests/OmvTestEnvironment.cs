using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core.Rpc;
using Vault.TestSupport;

namespace Vault.IntegrationTests;

/// <summary>The real RPC client wired to a <see cref="FakeOmvServer"/> over real HTTP, with the same cookie handling as the server host.</summary>
internal sealed class OmvTestEnvironment : IAsyncDisposable
{
    private readonly HttpClient _http;

    private OmvTestEnvironment(FakeOmvServer server, OmvRpcClient client, HttpClient http)
    {
        Server = server;
        Client = client;
        _http = http;
    }

    public FakeOmvServer Server { get; }

    public OmvRpcClient Client { get; }

    public static async Task<OmvTestEnvironment> StartAsync(string? passwordOverride = null)
    {
        var server = await FakeOmvServer.StartAsync();
        var http = new HttpClient(new SocketsHttpHandler { UseCookies = true, CookieContainer = new CookieContainer() })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
        var credentials = new StaticCredentials(new OmvCredentials(server.Username, passwordOverride ?? server.Password));
        var client = new OmvRpcClient(http, OmvEndpoint.RpcUri(server.Url)!, credentials, NullLogger<OmvRpcClient>.Instance);
        return new OmvTestEnvironment(server, client, http);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _http.Dispose();
        await Server.DisposeAsync();
    }

    private sealed class StaticCredentials(OmvCredentials credentials) : IOmvCredentialsProvider
    {
        public OmvCredentials Load() => credentials;
    }
}
