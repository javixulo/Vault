namespace Vault.Core.Rpc;

/// <summary>
/// One OMV RPC method that Vault is allowed to call. The constructor is private: the only instances are the static fields
/// below, so the list of calls Vault can make is fixed in this file and nowhere else. There is no way to build a call from
/// a string that comes from a client. Adding one is a change to the contract: it must be documented as read-only in the
/// functional spec (section "RPC calls") first, and a test checks that the spec and this list agree.
/// </summary>
public sealed class OmvRpcCall
{
    private OmvRpcCall(string service, string method)
    {
        Service = service;
        Method = method;
    }

    public string Service { get; }

    public string Method { get; }

    public string Name => $"{Service}.{Method}";

    /// <summary>Authentication. Vault's own plumbing, not behind any tool, and not part of <see cref="Exposed"/>.</summary>
    internal static readonly OmvRpcCall Login = new("Session", "login");

    /// <summary>Hostname, OMV version, kernel, CPU, memory, uptime. Used by <c>system_status</c>.</summary>
    public static readonly OmvRpcCall SystemInformation = new("System", "getInformation");

    /// <summary>The list of shared folders. Used by <c>list_shared_folders</c>.</summary>
    public static readonly OmvRpcCall SharedFolders = new("ShareMgmt", "getList");

    /// <summary>Every call a tool can make, in a fixed order. <see cref="Login"/> is not here.</summary>
    public static IReadOnlyList<OmvRpcCall> Exposed { get; } = [SystemInformation, SharedFolders];

    public override string ToString() => Name;
}
