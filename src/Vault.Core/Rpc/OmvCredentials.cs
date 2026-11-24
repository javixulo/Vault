namespace Vault.Core.Rpc;

/// <summary>The OMV account Vault logs in with. The password never appears in <see cref="ToString"/>, logs or messages.</summary>
public sealed record OmvCredentials(string Username, string Password)
{
    public override string ToString() => $"OmvCredentials {{ Username = {Username}, Password = *** }}";

    /// <summary>
    /// Reads the content of the password secret: the password as plain text. One trailing line break is ignored (editors and
    /// <c>echo</c> add it). Returns the password, or a description of what is wrong (never containing the secret).
    /// </summary>
    public static (string? Password, string? Problem) ParsePassword(string content)
    {
        var password = content.EndsWith("\r\n", StringComparison.Ordinal) ? content[..^2]
            : content.EndsWith('\n') ? content[..^1]
            : content;
        return password.Length == 0 ? (null, "it is empty") : (password, null);
    }
}

/// <summary>Where the credentials come from. They are read when a login happens, so a rotated secret is picked up without a restart.</summary>
public interface IOmvCredentialsProvider
{
    OmvCredentials Load();
}

/// <summary>The user name is configuration; the password is read from a file (a Docker secret, mounted under <c>/run/secrets</c>).</summary>
public sealed class FileCredentialsProvider(string username, string path) : IOmvCredentialsProvider
{
    public OmvCredentials Load()
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"The password file '{path}' cannot be read: {error.Message}", inner: error);
        }

        var (password, problem) = OmvCredentials.ParsePassword(content);
        return password is null
            ? throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"The password file '{path}' is not valid: {problem}.")
            : new OmvCredentials(username, password);
    }
}
