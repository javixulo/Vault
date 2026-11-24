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
/// <c>Session.login</c>, <c>System.getInformation</c>, <c>ShareMgmt.getList</c>, <c>ShareMgmt.getPrivileges</c>, <c>ShareMgmt.getFileACL</c>, <c>UserMgmt.enumerateAllUsers</c>, <c>UserMgmt.enumerateAllGroups</c>, <c>Services.getStatus</c>, <c>Cron.getList</c>, <c>DiskMgmt.enumerateDevices</c>, <c>Smart.getList</c> and <c>FileSystemMgmt.enumerateFilesystems</c>, and it
/// records every call it receives. Its login, session and error answers (and the shape of <c>System.getInformation</c>) were
/// checked against a real OMV 8.5.1; the shared folder and privilege entries against a real OMV 8.5.12.
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

    /// <summary>
    /// The privileges the fake knows, by shared folder name (the fake looks the uuid up). <c>Perms</c> is OMV's number:
    /// 0 deny, 5 read-only, 7 read and write. The shape of <c>ShareMgmt.getPrivileges</c> was checked against a real
    /// OMV 8.5.12: every user and group is listed, with <c>perms</c> null when no privilege is set.
    /// </summary>
    public Dictionary<string, List<(string Type, string Name, int Perms)>> Privileges { get; } = new()
    {
        ["media"] = [("group", "family", 7), ("user", "kid", 5), ("user", "guest", 0)],
    };

    /// <summary>
    /// The ACL of a shared folder's root, by folder name: POSIX owner, group, base permissions (0 to 7) and the named entries.
    /// The shape of <c>ShareMgmt.getFileACL</c> was checked against a real OMV 8.5.12: <c>{file, owner, group, acl: {user, group,
    /// other, users: [...], groups: [...]}}</c>, listing every user and group of the NAS with <c>perms</c> null when it has no entry.
    /// A folder not listed here has owner root, group users, 7/0/0 and no named entry.
    /// </summary>
    public Dictionary<string, (string Owner, string Group, int User, int GroupPerms, int Other, List<(string Type, string Name, int Perms)> Entries)> Acls { get; } = new()
    {
        ["media"] = ("root", "users", 7, 5, 0, [("group", "family", 7), ("user", "kid", 5)]),
    };

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
            ("ShareMgmt", "getPrivileges") => FolderPrivileges(parameters),
            ("ShareMgmt", "getFileACL") => FolderAcl(parameters),
            // Shapes checked against a real OMV 8.5.12 (2026-11-24): users {name, uid, gid, comment, dir, shell, groups, system}, groups {name, gid, members, system}.
            ("UserMgmt", "enumerateAllUsers") => Ok(new[]
            {
                new { name = "ana", uid = 1000, gid = 100, comment = "Ana Lopez", dir = "/home/ana", shell = "/bin/sh", groups = new[] { "family" }, system = false },
                new { name = "kid", uid = 1001, gid = 100, comment = "", dir = "/home/kid", shell = "/bin/sh", groups = new[] { "family" }, system = false },
            }),
            ("UserMgmt", "enumerateAllGroups") => Ok(new[]
            {
                new { name = "family", gid = 1000, members = new[] { "ana", "kid" }, system = false },
                new { name = "admins", gid = 1001, members = Array.Empty<string>(), system = false },
            }),
            // Shape checked against a real OMV 8.5.12 (2026-11-24): a plain list of {name, title, enabled, running}.
            ("Services", "getStatus") => Ok(new[]
            {
                new { name = "ssh", title = "SSH", enabled = true, running = true },
                new { name = "smb", title = "SMB/CIFS", enabled = true, running = false },
            }),
            // Shapes checked against a real OMV 8.5.12 (2026-11-24): enumerateDevices is a plain list (size and temperature as
            // text, empty when unknown); Smart.getList is {total, data} and only has the disks with SMART support.
            ("DiskMgmt", "enumerateDevices") => Ok(new[]
            {
                new { devicename = "sda", canonicaldevicefile = "/dev/sda", model = "ST4000VN006-3CW104", size = "4000787030016", temperature = "36" },
                new { devicename = "sdb", canonicaldevicefile = "/dev/sdb", model = "BUP BK", size = "5000981077504", temperature = "" },
            }),
            // Checked against a real OMV 8.5.12 (2026-11-24): a plain list; size and available are bytes as text, used is human
            // text, and an unmounted filesystem has "-1" in them and an empty mountpoint.
            ("FileSystemMgmt", "enumerateFilesystems") => Ok(new[]
            {
                new { devicename = "sda1", canonicaldevicefile = "/dev/sda1", uuid = "b1c995cf-ac11-418f-8d73-54fed3bc2ec2", label = "", type = "ext4", mounted = true, mountpoint = "/srv/dev-disk-by-uuid-b1c995cf", used = "532.59 GiB", available = "3364930629632", size = "3936819658752" },
                new { devicename = "mmcblk0p7", canonicaldevicefile = "/dev/mmcblk0p7", uuid = "db287638-4c71-42d2-a5c7-fe942847592e", label = "USER-DATA", type = "ext4", mounted = false, mountpoint = "", used = "-1", available = "-1", size = "-1" },
            }),
            // Cron.getList needs a type list and answers {total, data}; checked against a real OMV 8.5.12, which has no jobs
            // (the entry fields are OMV's documented ones, not yet seen in a real answer).
            ("Cron", "getList") => Ok(new
            {
                total = 2,
                data = new object[]
                {
                    new { uuid = "u1", enable = true, execution = "exactly", minute = "0", everynminute = false, hour = "3", everynhour = false, dayofmonth = "*", everyndayofmonth = false, month = "*", dayofweek = "*", username = "root", command = "/usr/local/bin/backup.sh", sendemail = false, comment = "Nightly backup", type = "userdefined" },
                    new { uuid = "u2", enable = false, execution = "daily", username = "root", command = "/usr/bin/cleanup", comment = "", type = "userdefined" },
                },
            }),
            ("Smart", "getList") => Ok(new
            {
                total = 1,
                data = new[] { new { devicename = "sda", canonicaldevicefile = "/dev/sda", model = "ST4000VN006-3CW104", size = "4000787030016", temperature = "36", overallstatus = "GOOD" } },
            }),
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

    private IResult FolderPrivileges(JsonElement parameters)
    {
        var uuid = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("uuid", out var u) ? u.GetString() : null;
        var folder = SharedFolders.FirstOrDefault(f => f.Uuid == uuid);
        if (folder.Name is null)
        {
            return Error(1, "Shared folder not found.");
        }

        // As a real OMV 8.5.12 does: one entry for every user and group, perms null when none is set.
        var set = Privileges.TryGetValue(folder.Name, out var list) ? list : [];
        var subjects = new[] { ("user", "ana"), ("user", "kid"), ("user", "guest"), ("group", "family"), ("group", "admins") };
        return Ok(subjects.Select(s => new
        {
            type = s.Item1,
            name = s.Item2,
            perms = set.Any(p => p.Type == s.Item1 && p.Name == s.Item2) ? (int?)set.First(p => p.Type == s.Item1 && p.Name == s.Item2).Perms : null,
        }).ToList());
    }

    private IResult FolderAcl(JsonElement parameters)
    {
        var uuid = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("uuid", out var u) ? u.GetString() : null;
        var file = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("file", out var f) ? f.GetString() : null;
        var folder = SharedFolders.FirstOrDefault(s => s.Uuid == uuid);
        if (folder.Name is null || file != "/")
        {
            return Error(1, "Shared folder not found.");
        }

        var acl = Acls.TryGetValue(folder.Name, out var found) ? found : ("root", "users", 7, 0, 0, []);
        int? Perms(string type, string name) =>
            acl.Entries.Where(e => e.Type == type && e.Name == name).Select(e => (int?)e.Perms).FirstOrDefault();

        // As a real OMV 8.5.12 does: every user and group of the NAS (system ones too), perms null when there is no entry.
        return Ok(new
        {
            file = "srv/dev-disk-by-uuid-fake/" + folder.RelPath,
            owner = acl.Owner,
            group = acl.Group,
            acl = new
            {
                user = acl.User,
                group = acl.GroupPerms,
                other = acl.Other,
                users = new[] { ("root", 0, true), ("ana", 1000, false), ("kid", 1001, false), ("guest", 1002, false) }
                    .Select(s => new { name = s.Item1, perms = Perms("user", s.Item1), uid = s.Item2, system = s.Item3 }).ToList(),
                groups = new[] { ("root", 0, true), ("family", 1000, false), ("admins", 1001, false) }
                    .Select(s => new { name = s.Item1, perms = Perms("group", s.Item1), gid = s.Item2, system = s.Item3 }).ToList(),
            },
        });
    }

    private static IResult Ok(object response) =>
        Results.Json(new { response, error = (object?)null });

    private static IResult Error(int code, string message, int status = 200) =>
        Results.Json(new { response = (object?)null, error = new { code, message, trace = "(fake)" } }, statusCode: status);
}
