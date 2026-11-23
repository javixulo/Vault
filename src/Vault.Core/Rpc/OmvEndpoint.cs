namespace Vault.Core.Rpc;

/// <summary>Turns the configured base URL of OMV into the URL of its RPC endpoint.</summary>
public static class OmvEndpoint
{
    public const string RpcPath = "rpc.php";

    /// <summary>The RPC URL (<c>&lt;base&gt;/rpc.php</c>), or <c>null</c> when the base URL is empty or is not an absolute http(s) URL.</summary>
    public static Uri? RpcUri(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return new Uri(root, RpcPath);
    }
}
