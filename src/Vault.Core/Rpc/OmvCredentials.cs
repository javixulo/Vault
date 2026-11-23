using System.Text.Json;

namespace Vault.Core.Rpc;

/// <summary>The OMV account Vault logs in with. The password never appears in <see cref="ToString"/>, logs or messages.</summary>
public sealed record OmvCredentials(string Username, string Password)
{
    public override string ToString() => $"OmvCredentials {{ Username = {Username}, Password = *** }}";

    /// <summary>
    /// Reads the content of the secret file: a JSON object <c>{"username": "...", "password": "..."}</c> (technical spec,
    /// section 4). Returns the credentials, or a description of what is wrong (never containing the secret).
    /// </summary>
    public static (OmvCredentials? Credentials, string? Problem) Parse(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, "it is not a JSON object");
            }

            var username = Read(document.RootElement, "username");
            var password = Read(document.RootElement, "password");
            if (string.IsNullOrWhiteSpace(username))
            {
                return (null, "'username' is missing or empty");
            }

            return string.IsNullOrEmpty(password)
                ? (null, "'password' is missing or empty")
                : (new OmvCredentials(username, password), null);
        }
        catch (JsonException)
        {
            return (null, "it is not valid JSON");
        }
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>Where the credentials come from. They are read when a login happens, so a rotated secret is picked up without a restart.</summary>
public interface IOmvCredentialsProvider
{
    OmvCredentials Load();
}

/// <summary>Reads the credentials from the secret file (a Docker secret, mounted under <c>/run/secrets</c>).</summary>
public sealed class FileCredentialsProvider(string path) : IOmvCredentialsProvider
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
            throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"The credentials file '{path}' cannot be read: {error.Message}", inner: error);
        }

        var (credentials, problem) = OmvCredentials.Parse(content);
        return credentials ?? throw new OmvRpcException(OmvRpcErrorKind.AuthFailed, $"The credentials file '{path}' is not valid: {problem}.");
    }
}
