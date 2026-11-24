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

    /// <summary>The output of each background job by file name; the start calls answer the name, and Exec.getOutput answers the whole output.</summary>
    public Dictionary<string, string> JobOutputs { get; } = new()
    {
        ["/tmp/bgstatusCONT"] = """{"total":2,"data":[{"name":"jellyfin","image":"lscr.io/linuxserver/jellyfin:latest","ports":"","mounts":"/srv/media","created":"2026-10-26 12:08:00 +0100 CET","state":"running","status":"Up 4 weeks (healthy)","command":"s6","network":"host"},{"name":"standalone","image":"busybox","state":"exited","status":"Exited (1) 3 hours ago"}]}""",
        ["/tmp/bgstatusSERV"] = """{"total":1,"data":[{"name":"jellyfin","project":"jellyfin","service":"jellyfin","filepath":"/Docker_Compose/Jellyfin/Jellyfin.yml"}]}""",
    };

    public Func<OmvRpcCall, JsonElement> Answer { get; set; } = call =>
        call == OmvRpcCall.SystemInformation
            ? Parse("""{"hostname":"nas","version":"7.4.0","kernel":"Linux 6.1","cpuModelName":"N100","cpuUtilization":3.5,"memTotal":8000,"memUsed":2000,"uptime":3600}""")
            : call == OmvRpcCall.Disks
                ? Parse("""[{"canonicaldevicefile":"/dev/sda","model":"ST4000","size":"4000787030016","temperature":"36"},{"canonicaldevicefile":"/dev/sdb","model":"","size":"5000981077504","temperature":""}]""")
            : call == OmvRpcCall.Filesystems
                ? Parse("""[{"canonicaldevicefile":"/dev/sda1","uuid":"u1","type":"ext4","label":"","mounted":true,"mountpoint":"/srv/data","used":"1.00 KiB","size":"1000","available":"900","percentage":10},{"canonicaldevicefile":"/dev/sdb1","uuid":"u2","type":"ext4","label":"DATA","mounted":false,"mountpoint":"","used":"-1","size":"-1"}]""")
            : call == OmvRpcCall.ScheduledTasks
                ? Parse("""{"total":2,"data":[{"comment":"Nightly backup","command":"/usr/local/bin/backup.sh","enable":true,"execution":"exactly","minute":"0","everynminute":false,"hour":"3","everynhour":false,"dayofmonth":"*","everyndayofmonth":false,"month":"*","dayofweek":"*"},{"comment":"","command":"/usr/bin/cleanup","enable":false,"execution":"daily"}]}""")
            : call == OmvRpcCall.DockerContainers
                ? Parse("\"/tmp/bgstatusCONT\"")
            : call == OmvRpcCall.ComposeServices
                ? Parse("\"/tmp/bgstatusSERV\"")
            : call == OmvRpcCall.PendingUpdates
                ? Parse("""[{"name":"curl","package":"curl","version":"7.1","oldversion":"7.0"},{"name":"linux-image","package":"linux-image","version":"6.1","oldversion":""}]""")
            : call == OmvRpcCall.DisksSmart
                ? Parse("""{"total":1,"data":[{"canonicaldevicefile":"/dev/sda","overallstatus":"GOOD"}]}""")
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

        if (rpcCall.Name == "Exec.getOutput")
        {
            var output = JobOutputs[JsonSerializer.SerializeToElement(parameters).GetProperty("filename").GetString()!];
            return Task.FromResult(Parse(JsonSerializer.Serialize(new { filename = "x", pos = output.Length, output, pendingOutput = false, running = false })));
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
        var password = Path.Combine(_root, "omv_password");
        File.WriteAllText(password, "p");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Vault:OmvBaseUrl", "https://omv.test");
            builder.UseSetting("Vault:OmvUsername", "reader");
            builder.UseSetting("Vault:OmvPasswordFile", password);
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
