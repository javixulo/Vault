using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Docker;
using Vault.Core.Models;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListDockerContainersTool(DockerContainersReader reader)
{
    [McpServerTool(Name = "list_docker_containers", Title = "List Docker containers", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists the Docker containers on the NAS, as OpenMediaVault's (OMV) Compose plugin sees them: name, image, state, status (Docker's own text), " +
        "health (healthy, unhealthy, starting, or null when the container has no health check), created_at, network, and the Compose project and Compose " +
        "service it belongs to (null when it was not created by Compose). Use it to check whether the containers of the NAS are running and healthy. " +
        "It does not report ports, mounts, commands or environment variables. " +
        "Read-only: it never starts, stops or changes a container. If OMV reports none, containers is empty and message says so: not an error. " +
        "If you expected some, list_services shows whether the Docker service is running.")]
    public Task<DockerContainersResult> ListDockerContainers(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
