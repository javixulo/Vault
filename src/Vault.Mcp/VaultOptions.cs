namespace Vault.Mcp;

/// <summary>
/// Configuration, from the <c>Vault</c> section: <c>Vault__OmvBaseUrl</c>, <c>Vault__CredentialsFile</c>,
/// <c>Vault__TimeoutSeconds</c> and <c>Vault__AllowInvalidCertificate</c> (technical spec, section 4).
/// There is no data folder: Vault keeps nothing.
/// </summary>
public sealed class VaultOptions
{
    public const string Section = "Vault";

    /// <summary>Base URL of the OMV web interface, for example <c>https://nas.local</c>. Vault adds <c>/rpc.php</c>. Required.</summary>
    public string OmvBaseUrl { get; set; } = "";

    /// <summary>
    /// File with the OMV account, as JSON: <c>{"username": "...", "password": "..."}</c>. In Docker it is a secret mounted
    /// under <c>/run/secrets</c>. The credentials are never read from an environment variable.
    /// </summary>
    public string CredentialsFile { get; set; } = "/run/secrets/omv_credentials";

    /// <summary>Time limit of each HTTP request to OMV, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Accept any TLS certificate from OMV. OMV ships with a self-signed one, so this exists, but it is off by default: with
    /// it on, anyone on the network can impersonate OMV and receive the account's password.
    /// </summary>
    public bool AllowInvalidCertificate { get; set; }
}
