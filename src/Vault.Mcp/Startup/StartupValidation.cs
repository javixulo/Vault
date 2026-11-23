using Vault.Core.Rpc;

namespace Vault.Mcp.Startup;

/// <summary>
/// Checks on start that everything the server needs is in place (technical spec, section 4). Every problem is
/// reported with what is wrong and how to fix it. Whether OMV answers is not checked here: it may start after Vault, so
/// <c>Program</c> only warns about it.
/// </summary>
public static class StartupValidation
{
    public static IReadOnlyList<string> Validate(VaultOptions options)
    {
        var problems = new List<string>();

        if (OmvEndpoint.RpcUri(options.OmvBaseUrl) is null)
        {
            problems.Add(string.IsNullOrWhiteSpace(options.OmvBaseUrl)
                ? "The OMV address is not set. Set Vault__OmvBaseUrl to the address of the OMV web interface, for example https://nas.local."
                : $"The OMV address '{options.OmvBaseUrl}' is not an absolute http or https URL. Fix Vault__OmvBaseUrl, for example https://nas.local.");
        }

        if (options.TimeoutSeconds is < 1 or > 120)
        {
            problems.Add($"Vault__TimeoutSeconds is {options.TimeoutSeconds}: it must be between 1 and 120.");
        }

        CheckCredentials(options.CredentialsFile, problems);
        return problems;
    }

    private static void CheckCredentials(string path, List<string> problems)
    {
        var full = Path.GetFullPath(path);
        string content;
        try
        {
            content = File.ReadAllText(full);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            problems.Add(
                $"The credentials file '{full}' cannot be read by the running user: {error.Message} " +
                "Mount the OMV account as a Docker secret (see docker-compose.yml), make it readable by the container's user, or set Vault__CredentialsFile.");
            return;
        }

        var (_, problem) = OmvCredentials.Parse(content);
        if (problem is not null)
        {
            problems.Add($"The credentials file '{full}' is not valid: {problem}. It must be a JSON object like {{\"username\": \"...\", \"password\": \"...\"}}.");
        }
    }
}
