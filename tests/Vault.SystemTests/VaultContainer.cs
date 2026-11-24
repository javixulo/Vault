using System.Diagnostics;
using ModelContextProtocol.Client;
using Xunit;

namespace Vault.SystemTests;

/// <summary>
/// The Docker image running as a container, as the compose file runs it: as a non-root user, with the OMV password as a
/// secret file under <c>/run/secrets</c>. It is driven with the <c>docker</c> command line. The tests are skipped unless
/// <c>VAULT_SYSTEM_IMAGE</c> names the image to test. The OMV it talks to is a stand-in running inside the test process,
/// reached through <c>host.docker.internal</c>.
/// </summary>
public sealed class VaultContainer : IAsyncDisposable
{
    public const string ImageVariable = "VAULT_SYSTEM_IMAGE";
    public const string HostForContainer = "host.docker.internal";
    private const string User = "1000:1000";

    private readonly string _name = "vault-system-" + Guid.NewGuid().ToString("N")[..12];
    private readonly string _image;
    private readonly string _secretFile = Path.Combine(Path.GetTempPath(), "vault-secret-" + Guid.NewGuid().ToString("N"));
    private McpClient? _client;

    private VaultContainer(string image) => _image = image;

    public McpClient Client => _client ?? throw new InvalidOperationException("The container is not started.");

    /// <summary>Skips the calling test unless an image to test was given.</summary>
    public static string RequireImage()
    {
        var image = Environment.GetEnvironmentVariable(ImageVariable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(image), $"Set {ImageVariable} to the image to test (for example vault-mcp:latest).");
        return image!;
    }

    /// <summary>Starts the image with the given OMV address and account and waits until it answers <c>/health</c>.</summary>
    public static async Task<VaultContainer> StartAsync(string omvBaseUrl, string username, string password)
    {
        var container = new VaultContainer(RequireImage());
        try
        {
            await container.CreateAsync(omvBaseUrl, username, password);
            await Docker("start", container._name);
            await container.ConnectAsync();
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    /// <summary>Runs the image in the foreground with no secret mounted and returns how it ended: a good image stops by itself and says why.</summary>
    public static async Task<(int ExitCode, string Output)> RunWithoutSecretAsync(string omvBaseUrl)
    {
        var container = new VaultContainer(RequireImage());
        try
        {
            var result = await DockerRaw("run", "--rm", "--name", container._name, "--user", User,
                "--add-host", $"{HostForContainer}:host-gateway", "-e", $"Vault__OmvBaseUrl={omvBaseUrl}", "-e", "Vault__OmvUsername=reader", container._image);
            return (result.ExitCode, result.Output);
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    /// <summary>Everything the container wrote to its logs.</summary>
    public async Task<string> LogsAsync() => (await DockerRaw("logs", _name)).Output;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
            _client = null;
        }

        await DockerRaw("rm", "-f", _name);
        File.Delete(_secretFile);
    }

    private async Task CreateAsync(string omvBaseUrl, string username, string password)
    {
        File.WriteAllText(_secretFile, password);
        if (!OperatingSystem.IsWindows())
        {
            // The container's user is not the owner of this file: it must be readable by others.
            File.SetUnixFileMode(_secretFile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        await Docker("create", "--name", _name, "--user", User, "-p", "127.0.0.1::8080",
            "--add-host", $"{HostForContainer}:host-gateway", "-e", $"Vault__OmvBaseUrl={omvBaseUrl}", "-e", $"Vault__OmvUsername={username}", _image);

        // A secret is a file at /run/secrets/omv_password. It is copied into the container that is not started yet,
        // which also works where bind-mounting a temporary file is not possible (Docker Desktop on macOS).
        await Docker("cp", _secretFile, _name + ":/run/secrets/omv_password");
    }

    private async Task ConnectAsync()
    {
        var port = await WaitForHealthAsync();
        var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress, "/mcp") }, http);
        _client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<string> WaitForHealthAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (!string.Equals(await Docker("inspect", "-f", "{{.State.Running}}", _name), "true", StringComparison.Ordinal))
            {
                var logs = await DockerRaw("logs", _name);
                throw new InvalidOperationException($"The container stopped by itself:\n{logs.Output}");
            }

            var mapping = (await DockerRaw("port", _name, "8080/tcp")).Output.Split('\n')[0].Trim();
            if (mapping.Length > 0)
            {
                var port = mapping[(mapping.LastIndexOf(':') + 1)..];
                try
                {
                    var response = await http.GetAsync($"http://127.0.0.1:{port}/health", TestContext.Current.CancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return port;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        var last = await DockerRaw("logs", "--tail", "30", _name);
        throw new TimeoutException($"/health did not answer within 90 seconds. Last logs:\n{last.Output}");
    }

    private static async Task<string> Docker(params string[] arguments)
    {
        var (exitCode, output) = await DockerRaw(arguments);
        return exitCode == 0
            ? output.Trim()
            : throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed ({exitCode}):\n{output}");
    }

    private static async Task<(int ExitCode, string Output)> DockerRaw(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("docker could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        return (process.ExitCode, await output + await error);
    }
}
