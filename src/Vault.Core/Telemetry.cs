using System.Diagnostics;

namespace Vault.Core;

/// <summary>
/// The one place that names what Vault records in its spans (technical spec, section 3). Names are lowercase and
/// dot-separated, from the OpenTelemetry conventions when one exists (<c>error.type</c>, <c>rpc.service</c>, <c>rpc.method</c>)
/// and <c>&lt;entity&gt;.id</c> for domain ids.
/// </summary>
public static class Telemetry
{
    public const string SourceName = "Vault";

    public const string RpcService = "rpc.service";
    public const string RpcMethod = "rpc.method";
    public const string FoldersCount = "folders.count";
    public const string PrivilegesCount = "privileges.count";
    public const string AclEntriesCount = "acl.entries.count";
    public const string UsersCount = "users.count";
    public const string GroupsCount = "groups.count";
    public const string ServicesCount = "services.count";
    public const string DisksCount = "disks.count";
    public const string FilesystemsCount = "filesystems.count";
    public const string ErrorType = "error.type";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>Records the error codes of an outcome that was not a success. A success has no <c>error.type</c>.</summary>
    public static void SetErrorTypes(this Activity? activity, IEnumerable<string> codes)
    {
        var list = codes.Distinct().ToArray();
        if (list.Length > 0)
        {
            activity?.SetTag(ErrorType, list);
        }
    }

    /// <summary>Marks a step as failed because of something unexpected.</summary>
    public static void Fail(this Activity? activity, Exception error)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag(ErrorType, error.GetType().FullName);
    }
}
