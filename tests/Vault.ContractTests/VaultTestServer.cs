using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.ContractTests;

/// <summary>A double of the OMV port for the contract tests: canned answers, or a failure, and a record of what was asked.</summary>
internal sealed class StubOmv : IOmvRpcClient
{
    public List<string> Calls { get; } = [];

    public Func<OmvRpcCall, JsonElement> Answer { get; set; } = call =>
        call == OmvRpcCall.SystemInformation
            ? Parse("""{"hostname":"nas","version":"7.4.0","kernel":"Linux 6.1","cpuModelName":"N100","cpuUtilization":3.5,"memTotal":8000,"memUsed":2000,"uptime":3600}""")
            : call == OmvRpcCall.ServicesStatus
                ? Parse("""[{"name":"ssh","title":"SSH","enabled":true,"running":true},{"name":"smb","title":"SMB/CIFS","enabled":true},{"name":"nfs","title":"NFS","enabled":false,"running":false}]""")
            : call == OmvRpcCall.Users
                ? Parse("""[{"name":"ana","comment":"Ana Lopez","groups":["family"],"system":false},{"name":"kid","comment":"","groups":["family"],"system":false},{"name":"root","comment":"root","groups":["root"],"system":true}]""")
            : call == OmvRpcCall.Groups
                ? Parse("""[{"name":"family","members":["ana","kid"],"system":false},{"name":"admins","members":[],"system":false},{"name":"root","members":["root"],"system":true}]""")
            : call == OmvRpcCall.SharedFolderAcl
                ? Parse("""{"file":"srv/x/media/","owner":"root","group":"users","acl":{"user":7,"group":5,"other":0,"users":[{"name":"root","perms":null,"uid":0,"system":true},{"name":"kid","perms":5,"uid":1001,"system":false}],"groups":[{"name":"family","perms":7,"gid":1000,"system":false},{"name":"admins","perms":null,"gid":1001,"system":false}]}}""")
            : call == OmvRpcCall.SharedFolderPrivileges
                ? Parse("""[{"type":"group","name":"family","perms":7},{"type":"user","name":"kid","perms":5},{"type":"user","name":"guest","perms":0}]""")
                : Parse("""{"total":3,"data":[{"uuid":"u1","name":"media","reldirpath":"media/","comment":"Movies"},{"uuid":"u2","name":"photos","reldirpath":"photos/"},{"uuid":"u3","name":"backups","reldirpath":"backups/"}]}""");

    public Task<JsonElement> CallAsync(OmvRpcCall rpcCall, object? parameters, CancellationToken cancellationToken = default)
    {
        lock (Calls)
        {
            Calls.Add(rpcCall.Name);
        }

        return Task.FromResult(Answer(rpcCall));
    }

    public Task VerifyAccessAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>The real server in process, with a stub instead of OMV, and an MCP client over HTTP, as a real client would talk to it.</summary>
internal sealed class VaultTestServer : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vault-mcp-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    private VaultTestServer(StubOmv omv, Action<IWebHostBuilder>? configure)
    {
        Omv = omv;
        Directory.CreateDirectory(_root);
        var credentials = Path.Combine(_root, "omv_credentials");
        File.WriteAllText(credentials, """{"username":"reader","password":"p"}""");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Vault:OmvBaseUrl", "https://omv.test");
            builder.UseSetting("Vault:CredentialsFile", credentials);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IOmvRpcClient>();
                services.AddSingleton<IOmvRpcClient>(omv);
            });
            configure?.Invoke(builder);
        });
        Http = Factory.CreateClient();
    }

    public StubOmv Omv { get; }

    public WebApplicationFactory<Program> Factory { get; }

    public HttpClient Http { get; }

    public static VaultTestServer Create(StubOmv? omv = null, Action<IWebHostBuilder>? configure = null) => new(omv ?? new StubOmv(), configure);

    /// <summary>An MCP client with one connection to the server (stateless HTTP, so a new client is as good as the first).</summary>
    public async Task<McpClient> ClientAsync()
    {
        if (_client is null)
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(Http.BaseAddress!, "/mcp") }, Http);
            _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        }

        return _client;
    }

    public async Task<ModelContextProtocol.Protocol.CallToolResult> CallAsync(string tool, Dictionary<string, object?>? arguments = null) =>
        await (await ClientAsync()).CallToolAsync(tool, arguments ?? [], cancellationToken: TestContext.Current.CancellationToken);

    public async Task<IList<McpClientTool>> ToolsAsync() =>
        await (await ClientAsync()).ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        Http.Dispose();
        await Factory.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
