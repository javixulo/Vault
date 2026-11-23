using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Vault.TestSupport;

/// <summary>
/// A stand-in for the RPC endpoint of OpenMediaVault (<c>POST /rpc.php</c>), served by a real Kestrel on a free port. It
/// is shared (linked, not copied) by the integration and system tests. It only knows what Vault calls:
/// <c>Session.login</c>, <c>System.getInformation</c> and <c>ShareMgmt.getList</c>, and it records every call it receives.
/// Its login, session and error answers (and the shape of <c>System.getInformation</c>) were checked against a real OMV 8.5.1;
/// the shared folder entries were not, because that OMV had none (open question *OMV version*).
/// </summary>
public sealed class FakeOmvServer : IAsyncDisposable
{
    public const string SessionCookie = "OPENMEDIAVAULT-SESSIONID";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, byte> _sessions = new();
    private readonly ConcurrentQueue<string> _calls = new();

    private FakeOmvServer(WebApplication app, string url, string username, string password)
    {
        _app = app;
        Url = url;
        Username = username;
        Password = password;
    }

    /// <summary>Base URL, for example <c>http://127.0.0.1:41234</c>.</summary>
    public string Url { get; }

    public string Username { get; }

    public string Password { get; }

    /// <summary>The OMV account is a normal OMV user: the shared folders the fake knows.</summary>
    public List<(string Name, string Uuid, string RelPath, string Comment)> SharedFolders { get; } =
    [
        ("media", "11111111-0000-0000-0000-000000000001", "media/", "Movies and series"),
        ("photos", "11111111-0000-0000-0000-000000000002", "photos/", "Immich library"),
        ("backups", "11111111-0000-0000-0000-000000000003", "backups/", ""),
    ];

    /// <summary>Every RPC the fake received, as <c>Service.method</c>, in order.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>Calls (as <c>Service.method</c>) the fake answers with HTTP 403, as OMV does for an account without the admin role.</summary>
    public HashSet<string> ForbiddenCalls { get; } = [];

    /// <summary>When set, the fake answers every call except the login with this RPC error code.</summary>
    public int? FailWithRpcCode { get; set; }

    /// <summary>When set, the fake answers every call except the login with this text (not JSON), as a proxy error page would.</summary>
    public string? RawAnswer { get; set; }

    public static async Task<FakeOmvServer> StartAsync(string host = "127.0.0.1", string username = "vault-reader", string password = "s3cret-for-tests")
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{host}:0");
        var app = builder.Build();
        FakeOmvServer? fake = null;
        Func<HttpContext, Task<IResult>> handler = context => fake!.HandleAsync(context);
        app.MapPost("/rpc.php", handler);
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        fake = new FakeOmvServer(app, $"http://{(host == "0.0.0.0" ? "127.0.0.1" : host)}:{port}", username, password);
        return fake;
    }

    /// <summary>Makes every session invalid, as OMV does when it expires: the next call is told the session is not authenticated.</summary>
    public void ExpireSessions() => _sessions.Clear();

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<IResult> HandleAsync(HttpContext context)
    {
        using var request = await JsonDocument.ParseAsync(context.Request.Body);
        var service = request.RootElement.GetProperty("service").GetString()!;
        var method = request.RootElement.GetProperty("method").GetString()!;
        var parameters = request.RootElement.TryGetProperty("params", out var p) ? p : default;
        _calls.Enqueue($"{service}.{method}");

        if (service == "Session" && method == "login")
        {
            return Login(context, parameters);
        }

        if (!context.Request.Cookies.TryGetValue(SessionCookie, out var session) || !_sessions.ContainsKey(session))
        {
            // As OMV 8.5 does: HTTP 401, code 0.
            return Error(0, "Session not authenticated.", 401);
        }

        if (ForbiddenCalls.Contains($"{service}.{method}"))
        {
            // As OMV 8.5.12 does for an account that is not in the group openmediavault-admin.
            return Error(0, "Forbidden.", 403);
        }

        if (FailWithRpcCode is { } code)
        {
            return Error(code, "Forced failure.");
        }

        if (RawAnswer is { } raw)
        {
            return Results.Text(raw, "text/html");
        }

        return (service, method) switch
        {
            ("System", "getInformation") => Ok(new
            {
                hostname = "nas-test",
                version = "7.4.0-1 (Sandworm)",
                kernel = "Linux 6.1.0-test",
                cpuModelName = "Test CPU @ 2.0GHz",
                cpuUtilization = 12.5,
                memTotal = "8000000000",
                memUsed = "2000000000",
                uptime = 86_400.5,
            }),
            ("ShareMgmt", "getList") => SharedFolderList(parameters),
            _ => Error(1, $"Unknown method {service}.{method}."),
        };
    }

    private IResult Login(HttpContext context, JsonElement parameters)
    {
        var ok = parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("username", out var u) && u.GetString() == Username
            && parameters.TryGetProperty("password", out var pw) && pw.GetString() == Password;
        if (!ok)
        {
            // As OMV 8.5 does (checked against a real one): HTTP 400 and an error object.
            return Error(0, "Incorrect username or password.", 400);
        }

        var session = Guid.NewGuid().ToString("N");
        _sessions[session] = 0;
        context.Response.Cookies.Append(SessionCookie, session, new CookieOptions { HttpOnly = true, Path = "/" });
        return Ok(new { username = Username, status = "authenticated", permissions = new { role = "admin" }, sessionid = session });
    }

    private IResult SharedFolderList(JsonElement parameters)
    {
        var start = parameters.TryGetProperty("start", out var s) ? s.GetInt32() : 0;
        var limit = parameters.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt32() : int.MaxValue;
        var data = SharedFolders.Skip(start).Take(limit)
            .Select(f => new { uuid = f.Uuid, name = f.Name, reldirpath = f.RelPath, comment = f.Comment, mntentref = "22222222-0000-0000-0000-000000000000" })
            .ToList();
        return Ok(new { total = SharedFolders.Count, data });
    }

    private static IResult Ok(object response) =>
        Results.Json(new { response, error = (object?)null });

    private static IResult Error(int code, string message, int status = 200) =>
        Results.Json(new { response = (object?)null, error = new { code, message, trace = "(fake)" } }, statusCode: status);
}
