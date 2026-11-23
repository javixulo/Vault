# syntax=docker/dockerfile:1
# The build stage always runs on the machine that builds (the output is the same for every architecture: portable
# assemblies), so a multi-platform build does not compile under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Vault.Core/Vault.Core.csproj src/Vault.Core/
COPY src/Vault.Mcp/Vault.Mcp.csproj src/Vault.Mcp/
RUN dotnet restore src/Vault.Mcp
COPY src/ src/
RUN dotnet publish src/Vault.Mcp -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# curl is only for the HEALTHCHECK: the runtime image has no HTTP client.
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .

# Vault keeps nothing: there is no data folder and no volume. The OMV account is a Docker secret, a file that docker
# compose mounts at /run/secrets/omv_credentials (see docker-compose.yml). The folder exists so the file can also be
# copied in by the tests. The OMV address is not in the image: set Vault__OmvBaseUrl.
RUN mkdir -p /run/secrets
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Vault__CredentialsFile=/run/secrets/omv_credentials

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl -fs http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Vault.Mcp.dll"]
