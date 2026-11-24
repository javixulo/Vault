using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Docker;

/// <summary>
/// UC-24 and UC-25: lists the Docker containers OMV's Compose plugin sees. Two background jobs (UC-26), both checked against a real
/// OMV 8.5.12 and asked with <c>{start: 0, limit: -1, sortdir: asc, sortfield: name}</c>: <c>Compose.getContainerListBg</c> (<c>{total, data}</c> of
/// <c>{name, image, state, status, created, network, ...}</c>; its ports and mounts are HTML and its command, environment and labels can hold
/// secrets, so none is passed on) and <c>Compose.getServicesListBg</c> (<c>{name, project, service, ...}</c>), joined by name. The mapping
/// stays tolerant: only an output without a list, or a container without a name, is a bad response, because a container left out would
/// look like one that does not exist.
/// </summary>
public sealed partial class DockerContainersReader(BackgroundJobReader jobs, ILogger<DockerContainersReader> logger)
{
    private const string What = "Listing the Docker containers";

    private static readonly object JobParameters = new { start = 0, limit = -1, sortdir = "asc", sortfield = "name" };

    public async Task<DockerContainersResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_docker_containers");
        DockerContainersResult result;
        try
        {
            var containers = await jobs.RunAsync(OmvRpcCall.DockerContainers, JobParameters, cancellationToken);
            var services = await jobs.RunAsync(OmvRpcCall.ComposeServices, JobParameters, cancellationToken);
            result = Map(containers, services);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = DockerContainersResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = DockerContainersResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.DockerContainersCount, result.Containers.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    public static DockerContainersResult Map(JsonElement containers, JsonElement services)
    {
        // A Compose service matches a container by name; a name that more than one service has is ambiguous and matches none.
        var serviceByName = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var service in Entries(services, "Compose services"))
        {
            if (NullIfBlank(service.ReadString("name")) is { } name)
            {
                if (!serviceByName.TryGetValue(name, out var matches))
                {
                    serviceByName[name] = matches = [];
                }

                matches.Add(service);
            }
        }

        var list = Entries(containers, "Docker containers")
            .Select(entry =>
            {
                var name = NullIfBlank(entry.ReadString("name")) ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A Docker container entry has no name.");
                var status = NullIfBlank(entry.ReadString("status"));
                var match = serviceByName.TryGetValue(name, out var matches) && matches.Count == 1 ? matches[0] : default;
                return new DockerContainer(
                    name,
                    NullIfBlank(entry.ReadString("image")),
                    NullIfBlank(entry.ReadString("state")),
                    status,
                    HealthOf(status),
                    CreatedAtOf(entry.ReadString("created")),
                    NullIfBlank(entry.ReadString("network")),
                    NullIfBlank(match.ReadString("project")),
                    NullIfBlank(match.ReadString("service")));
            })
            .ToList();

        return list.Count == 0 ? DockerContainersResult.Empty(Messages.NoDockerContainers) : DockerContainersResult.Found(list);
    }

    /// <summary><c>healthy</c>, <c>unhealthy</c> or <c>starting</c> only when Docker's status text ends in exactly that; anything else is null.</summary>
    public static string? HealthOf(string? status)
    {
        var text = status?.TrimEnd();
        return text switch
        {
            null => null,
            _ when text.EndsWith("(unhealthy)", StringComparison.Ordinal) => "unhealthy",
            _ when text.EndsWith("(healthy)", StringComparison.Ordinal) => "healthy",
            _ when text.EndsWith("(health: starting)", StringComparison.Ordinal) => "starting",
            _ => null,
        };
    }

    /// <summary>Reads OMV's <c>2026-10-26 12:07:38 +0100 CET</c> as a date with its offset; null when it is not that.</summary>
    public static DateTimeOffset? CreatedAtOf(string? created)
    {
        if (created is null || CreatedPattern().Match(created) is not { Success: true } match)
        {
            return null;
        }

        if (!DateTime.TryParseExact($"{match.Groups["date"].Value} {match.Groups["time"].Value}", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        var offset = new TimeSpan(int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture), 0);
        if (match.Groups["sign"].Value == "-")
        {
            offset = -offset;
        }

        return offset > TimeSpan.FromHours(14) || offset < TimeSpan.FromHours(-14) ? null : new DateTimeOffset(local, offset);
    }

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2}) (?<time>\d{2}:\d{2}:\d{2}) (?<sign>[+-])(?<h>\d{2})(?<m>\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex CreatedPattern();

    private static JsonElement.ArrayEnumerator Entries(JsonElement output, string what)
    {
        if (output.ValueKind == JsonValueKind.Object && output.TryGetProperty("data", out var data))
        {
            output = data;
        }

        return output.ValueKind == JsonValueKind.Array
            ? output.EnumerateArray()
            : throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The answer for the {what} is not a list.");
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the Docker containers failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the Docker containers failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
