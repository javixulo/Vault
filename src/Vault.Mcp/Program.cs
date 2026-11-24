using OpenTelemetry;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Vault.Core;
using Vault.Core.Disks;
using Vault.Core.Filesystems;
using Vault.Core.ScheduledTasks;
using Vault.Core.Rpc;
using Vault.Core.Services;
using Vault.Core.Shares;
using Vault.Core.Updates;
using Vault.Core.SystemInfo;
using Vault.Core.Users;
using Vault.Mcp;
using Vault.Mcp.Startup;
using Vault.Mcp.Tools;

var builder = WebApplication.CreateBuilder(args);

// Logs go to the console on stderr (so `docker logs` works when the OTLP backend is down) and to OpenTelemetry.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

var otlp = builder.Services.AddOpenTelemetry()
    // service.name and service.version here; deployment.environment.name comes from OTEL_RESOURCE_ATTRIBUTES (technical spec, section 3).
    .ConfigureResource(resource => resource.AddService(
        builder.Configuration["OTEL_SERVICE_NAME"] ?? "vault-mcp",
        serviceVersion: typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]))
    .WithTracing(tracing => tracing.AddSource(Telemetry.SourceName).AddAspNetCoreInstrumentation())
    .WithLogging();
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    otlp.UseOtlpExporter();
}

builder.Services.Configure<VaultOptions>(builder.Configuration.GetSection(VaultOptions.Section));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<VaultOptions>>().Value);
builder.Services.AddSingleton<IOmvCredentialsProvider>(sp => new FileCredentialsProvider(sp.GetRequiredService<VaultOptions>().CredentialsFile));
// One client for the whole life of the process: the session cookie of OMV lives in this handler, so it must not be recycled
// by an HttpClientFactory. The pooled connections are renewed from time to time so a change of IP of the NAS is noticed.
builder.Services.AddSingleton<IOmvRpcClient>(sp =>
{
    var options = sp.GetRequiredService<VaultOptions>();
    var handler = new SocketsHttpHandler
    {
        UseCookies = true,
        CookieContainer = new CookieContainer(),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };
    if (options.AllowInvalidCertificate)
    {
#pragma warning disable CA5359 // Opt-in, off by default, documented in the technical spec (section 4): OMV ships a self-signed certificate.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
    }

#pragma warning disable CA2000 // Lives as long as the process.
    var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
#pragma warning restore CA2000
    var endpoint = OmvEndpoint.RpcUri(options.OmvBaseUrl) ?? throw new InvalidOperationException("Vault__OmvBaseUrl is not valid (startup validation should have stopped the server).");
    return new OmvRpcClient(http, endpoint, sp.GetRequiredService<IOmvCredentialsProvider>(), sp.GetRequiredService<ILogger<OmvRpcClient>>());
});
builder.Services.AddSingleton<SystemStatusReader>();
builder.Services.AddSingleton<SharedFolderLister>();
builder.Services.AddSingleton<FolderPermissionsReader>();
builder.Services.AddSingleton<FolderAclReader>();
builder.Services.AddSingleton<UsersAndGroupsReader>();
builder.Services.AddSingleton<ServicesReader>();
builder.Services.AddSingleton<DisksReader>();
builder.Services.AddSingleton<FilesystemsReader>();
builder.Services.AddSingleton<ScheduledTasksReader>();
builder.Services.AddSingleton<PendingUpdatesReader>();

// The contract uses snake_case (technical spec, section 6). Outcomes are plain results, never protocol errors.
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
};

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithRequestFilters(filters => filters.AddCallToolFilter(ToolErrorFlag.Apply))
    .WithTools<SystemStatusTool>(json)
    .WithTools<ListSharedFoldersTool>(json)
    .WithTools<GetFolderPermissionsTool>(json)
    .WithTools<GetFolderAclTool>(json)
    .WithTools<ListUsersAndGroupsTool>(json)
    .WithTools<ListServicesTool>(json)
    .WithTools<ListDisksTool>(json)
    .WithTools<ListFilesystemsTool>(json)
    .WithTools<ListScheduledTasksTool>(json)
    .WithTools<ListPendingUpdatesTool>(json);

var app = builder.Build();

var problems = StartupValidation.Validate(app.Services.GetRequiredService<VaultOptions>());
if (problems.Count > 0)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    foreach (var problem in problems)
    {
#pragma warning disable CA1848, CA1873, CA1727 // Runs once at startup.
        logger.LogCritical("Startup check failed: {problem}", problem);
#pragma warning restore CA1848, CA1873
    }

    return 1;
}

#pragma warning disable CA1848
var startup = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startup.LogInformation("Startup checks passed");

// Not a startup failure: OMV may be starting too. A tool call will try again.
try
{
    var options = app.Services.GetRequiredService<VaultOptions>();
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds * 2));
    await app.Services.GetRequiredService<IOmvRpcClient>().VerifyAccessAsync(limit.Token);
    startup.LogInformation("OpenMediaVault answered and accepted the credentials");
}
catch (Exception error) when (error is OmvRpcException or OperationCanceledException)
{
    startup.LogWarning("OpenMediaVault is not usable yet ({reason}). Vault keeps running and tries again on every tool call", error.Message);
}
#pragma warning restore CA1848

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMcp("/mcp");

await app.RunAsync();
return 0;

/// <summary>Entry point, public so the contract tests can start the real server in process.</summary>
public partial class Program;
