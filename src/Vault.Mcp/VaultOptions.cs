namespace Vault.Mcp;

/// <summary>
/// Configuration, from the <c>Vault</c> section: <c>Vault__OmvBaseUrl</c>, <c>Vault__OmvUsername</c>, <c>Vault__OmvPasswordFile</c>,
/// <c>Vault__TimeoutSeconds</c> and <c>Vault__AllowInvalidCertificate</c> (technical spec, section 4).
/// There is no data folder: Vault keeps nothing.
/// </summary>
public sealed class VaultOptions
{
    public const string Section = "Vault";

    /// <summary>Base URL of the OMV web interface, for example <c>https://nas.local</c>. Vault adds <c>/rpc.php</c>. Required.</summary>
    public string OmvBaseUrl { get; set; } = "";

    /// <summary>User name of the OMV account Vault logs in with. Not a secret. Required.</summary>
    public string OmvUsername { get; set; } = "";

    /// <summary>
    /// File with the password of the OMV account, as plain text. In Docker it is a secret mounted under <c>/run/secrets</c>.
    /// The password is never read from an environment variable.
    /// </summary>
    public string OmvPasswordFile { get; set; } = "/run/secrets/omv_password";

    /// <summary>Time limit of each HTTP request to OMV, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Accept any TLS certificate from OMV. OMV ships with a self-signed one, so this exists, but it is off by default: with
    /// it on, anyone on the network can impersonate OMV and receive the account's password.
    /// </summary>
    public bool AllowInvalidCertificate { get; set; }
}
