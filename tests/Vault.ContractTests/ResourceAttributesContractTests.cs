using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Vault.ContractTests;

/// <summary>
/// Every span and log carries who sent it (technical spec, section 3): the name from <c>OTEL_SERVICE_NAME</c>, the version of the
/// build and the environment from <c>OTEL_RESOURCE_ATTRIBUTES</c>. That is how a shared backend tells projects, builds and environments apart.
/// </summary>
public sealed class ResourceAttributesContractTests : IDisposable
{
    private const string Variable = "OTEL_RESOURCE_ATTRIBUTES";
    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);

    [Fact]
    public async Task The_resource_has_the_service_name_the_version_and_the_environment()
    {
        Environment.SetEnvironmentVariable(Variable, "deployment.environment.name=staging");
        await using var server = VaultTestServer.Create(configure: builder => builder.UseSetting("OTEL_SERVICE_NAME", "vault-under-test"));

        var attributes = server.Factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("vault-under-test", attributes["service.name"]);
        Assert.Equal("staging", attributes["deployment.environment.name"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)attributes["service.version"]));
    }
}
