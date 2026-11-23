# Vault

**Ask your NAS how it is doing, without giving anyone a shell.**

Vault is a small, **read-only** [MCP](https://modelcontextprotocol.io) server that wraps the JSON-RPC API of [OpenMediaVault](https://www.openmediavault.org) (OMV). It plugs into your AI assistant and lets it look at the real state of the NAS (system, shared folders and, as the catalog grows, permissions, services, disks, users and backups) instead of trusting documentation that may be out of date.

> **Status: skeleton.** Two tools are built to prove the design end to end: `system_status` and `list_shared_folders`. The rest of the catalog is still to be specified (see the open questions in [`docs/functional_spec.md`](docs/functional_spec.md)). Both tools have been run against OMV 8.5.12 (a real NAS) and 8.5.1 (a Docker image); other versions are untested.

## Tools

Every tool is **read-only**: none changes anything on the NAS.

| Tool | What it does | Typical use |
|---|---|---|
| `system_status` | Hostname, OMV version, kernel, CPU, memory and uptime | "How is the NAS doing?" |
| `list_shared_folders` | The shared folders OMV defines, one page at a time (`limit`, `offset`, `total`, `has_more`) | "Which shared folders does the NAS have?" |

## Read-only by construction

OMV has no API keys with limited permissions: Vault logs in with an OMV account, and that account can do anything its role allows. So the **read-only boundary is enforced by Vault's own code**, not by OMV:

- Vault can only make a **fixed list of RPC calls**, each documented as read-only in the functional spec (section "RPC calls"). There is no tool that runs "any RPC method", and no tool argument names a service or a method.
- A test fails if the code exposes a call that the spec does not document.
- The credentials are a **Docker secret** (a file), never an environment variable in plain text.

Use a dedicated OMV account for Vault, not the one you use yourself. See [Set up the OMV account](#set-up-the-omv-account): that account needs administrator rights, so read what that means before you create it.

---

# Technical guide

Vault is written in **C# / .NET 10**.

## Requirements

- .NET SDK 10 (see `global.json`), or Docker.
- An OMV (its web interface address) and an account for Vault.

## Set up the OMV account

OMV has **no API keys** (checked in the code of OMV 8.5): its API is the one the web interface uses, and it is reached by logging in with a user name and a password. So Vault needs an OMV user.

1. In the OMV web interface, go to **Users → Users** and create a user just for Vault (for example `vault-mcp`). Give it a long, unique password: it will live in a file on the machine that runs Vault, so do not reuse one.
2. Add that user to the group **`openmediavault-admin`** (the web interface administrators). It is a system group: if it is not in the list when you edit the user, look for the option that shows system groups.
3. Put the user name and the password in `secrets/omv_credentials.json` (see [Run](#run)).

**Why the administrator group.** Tested on OMV 8.5.12: a user in no group can log in and `system_status` works, but OMV answers HTTP 403 to `list_shared_folders`, because OMV demands the administrator role for most of its calls. Without the group, Vault says so in the tool's error ("the account lacks the role it needs"). Which other tools will work without it is not known yet.

**What that means for security.** The account is a full administrator of OMV, so anyone who gets its password can change the NAS. Vault itself cannot: it only knows a fixed list of read-only calls (see [Read-only by construction](#read-only-by-construction)). Protect the password:

- Keep it only in `secrets/omv_credentials.json`, never in `.env`, in the compose file or in the repository (both files are git-ignored).
- Make that file readable only by you and by the container's user.
- Do not reuse the password, and use the account for nothing else. Changing it is enough to cut Vault off.

## Run

Put the account in a JSON file (copy `secrets/omv_credentials.example.json`; the `secrets/` folder is git-ignored):

```json
{ "username": "an-omv-account-for-vault", "password": "..." }
```

```powershell
$env:Vault__OmvBaseUrl = "https://nas.local"
$env:Vault__CredentialsFile = "secrets/omv_credentials.json"
dotnet run --project src/Vault.Mcp
```

The server speaks MCP over Streamable HTTP at `http://localhost:5000/mcp` (`/health` is the health check). To use it from Claude Code:

```bash
claude mcp add --transport http vault http://localhost:5000/mcp
```

### Configuration

Environment variables, using the ASP.NET Core convention:

| Variable | Default | Meaning |
|---|---|---|
| `Vault__OmvBaseUrl` | none, **required** | Base URL of the OMV web interface, for example `https://nas.local`. Vault adds `/rpc.php` |
| `Vault__CredentialsFile` | `/run/secrets/omv_credentials` | JSON file with `username` and `password` of the OMV account |
| `Vault__TimeoutSeconds` | `10` | Time limit of each request to OMV, 1 to 120 |
| `Vault__AllowInvalidCertificate` | `false` | Accept any TLS certificate from OMV (it ships a self-signed one). With it on, anyone on your network can impersonate OMV and receive the password |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | When set, logs and traces are exported through OTLP. For Seq: `http://seq:5341/ingest/otlp` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (`http/protobuf` in `docker-compose.yml`) | Seq only accepts `http/protobuf`: set it when exporting to Seq |
| `OTEL_RESOURCE_ATTRIBUTES` | unset | For example `deployment.environment.name=production`; the server adds `service.name` and `service.version` itself |

On start the server validates its configuration (OMV address, time limit, credentials file) and logs every problem, with how to fix it, before exiting. If OMV simply does not answer yet, it starts anyway and warns.

## Docker

```powershell
Copy-Item .env.template .env                                                   # settings that are not secret: edit it
Copy-Item secrets/omv_credentials.example.json secrets/omv_credentials.json   # the OMV account (the secret): edit it
docker compose up -d --build
```

`.env` holds the address of OMV, the port and the user of the container; the account (username and password) goes only in `secrets/omv_credentials.json`, never in `.env`. Both files are git-ignored.

A minimal `docker-compose.yml` (the repository's own file has the same shape and more comments):

```yaml
services:
  vault:
    build: .                     # or `image:` if you pushed it to a registry
    image: vault-mcp:latest
    container_name: vault-mcp
    restart: unless-stopped
    user: "1000:1000"            # must be able to read the secret file
    ports:
      - "8080:8080"              # MCP endpoint (Streamable HTTP) and /health
    cap_drop: [ALL]
    security_opt: ["no-new-privileges:true"]
    environment:
      TZ: Europe/Madrid
      Vault__OmvBaseUrl: https://nas.local
      OTEL_SERVICE_NAME: vault-mcp
      OTEL_EXPORTER_OTLP_PROTOCOL: http/protobuf   # Seq does not speak gRPC
      OTEL_EXPORTER_OTLP_ENDPOINT: ""   # e.g. http://seq:5341/ingest/otlp ; empty exports nothing
    # SECURITY: the OMV credentials are a Docker SECRET, mounted at /run/secrets/omv_credentials.
    # Never put them in `environment:` in plain text, in this file or in the image.
    secrets:
      - omv_credentials

secrets:
  omv_credentials:
    file: ./secrets/omv_credentials.json   # {"username": "...", "password": "..."}; chmod 0400, git-ignored
```

The container runs as a non-root user, has a health check on `/health` and `restart: unless-stopped`. It keeps nothing: there is no volume. It needs no network access except to OMV.

## Tests

```bash
dotnet test
```

Every acceptance criterion of the functional spec is covered by at least one automated test. The tests are split by level, each in its own project:

| Project | Level | What it checks |
|---|---|---|
| `Vault.UnitTests` | Unit | Pure logic with a double of the OMV port: mapping, validation, errors, the fixed list of calls |
| `Vault.IntegrationTests` | Integration | The RPC client and the services over real HTTP against a stand-in of OMV (session, renewal, each failure); startup validation; the spec lists the same RPC calls as the code |
| `Vault.ContractTests` | Contract | The real server in process, spoken to with an MCP client over HTTP: tool names, annotations (all read-only), schemas and messages |
| `Vault.SystemTests` | System | The Docker image running against a stand-in of OMV. Skipped unless `VAULT_SYSTEM_IMAGE` is set |

Run one level with `dotnet test --project tests/Vault.UnitTests` (or the name of another project). The system tests need the image: `docker build -t vault-mcp . && VAULT_SYSTEM_IMAGE=vault-mcp dotnet test --project tests/Vault.SystemTests`.

### Continuous integration

GitHub Actions ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds the solution once on every push to `main` and every pull request, then runs the three test projects in parallel. Another job builds the Docker image, runs `scripts/smoke-test.sh` against it and, on pushes to `main`, the system tests. `ci-ok` is the one check to require. A manual workflow, [`release-dry-run.yml`](.github/workflows/release-dry-run.yml), rehearses publishing the image against a throwaway registry and publishes nothing. More detail in [`docs/technical_spec.md`](docs/technical_spec.md), section 8.

## Architecture

```
 MCP client (Claude, ...)
        │  Streamable HTTP  (/mcp)
        ▼
┌──────────────────────────────────────────────┐
│ Vault.Mcp   ASP.NET Core host              │
│   tool classes (thin: bind args, call a    │
│   service, return its result)              │
└──────────────────┬─────────────────┘
                   ▼
┌──────────────────────────────────────────────┐
│ Vault.Core   all the logic                 │
│   services: SystemStatusReader,            │
│   SharedFolderLister                       │
│   port: IOmvRpcClient  ── OmvRpcCall       │
│   (a fixed list of read-only calls)        │
└──────────────────┬─────────────┘
                   ▼  POST /rpc.php (Session.login, then the call)
             OpenMediaVault
```

The main design decisions:

- **Read-only by construction.** `OmvRpcCall` has a private constructor and a fixed list of instances: the code cannot call an OMV method that is not in it, and the functional spec must document each one first.
- **Thin tools, logic in `Vault.Core`.** A tool class only binds arguments and calls a service.
- **Live, no storage.** No database, no cache, no data folder: every answer is read from OMV when the tool is called.
- **Session handling.** One login, reused; renewed once if OMV expires it; one login shared by concurrent calls.
- **Outcomes are data, not protocol errors.** A failure is a normal tool result with an `error` and the MCP flag `isError`; the message says what happened and what to do, and never carries a credential, an address or what OMV said.
- **Observability.** Logs go to the console on stderr and, when configured, to OpenTelemetry (OTLP) together with traces: one span per tool call and one per OMV call. A `traceparent` header from the client is continued. Credentials are never recorded.

## Project structure

| Path | What |
|---|---|
| `src/Vault.Mcp/` | The MCP server: host and HTTP transport (`Program.cs`), the tool classes (`Tools/`), configuration (`VaultOptions.cs`) and startup validation (`Startup/`) |
| `src/Vault.Core/` | The logic: the OMV RPC client and the fixed list of calls (`Rpc/`), one service per tool (`SystemInfo/`, `Shares/`), results and messages (`Models/`) |
| `tests/Vault.UnitTests/` | Unit tests: pure logic |
| `tests/Vault.IntegrationTests/` | Integration tests: the client and services against a stand-in of OMV, startup validation |
| `tests/Vault.ContractTests/` | Contract tests through a real MCP client |
| `tests/Vault.SystemTests/` | System tests against the Docker image |
| `tests/shared/` | The stand-in of the OMV RPC endpoint, shared by the integration and system tests |
| `secrets/` | Where the OMV credentials file goes (git-ignored; only the example is versioned) |
| `docs/` | Functional spec (what the tools do) and technical spec (how and why) |
| `Dockerfile`, `docker-compose.yml` | Container image and deployment |
| `.env.template` | Template of `.env`, the non-secret settings for compose |
| `scripts/smoke-test.sh` | Smoke test of the Docker image: it starts, `/health` answers and the tools are listed |

## License

[MIT](LICENSE) © Francisco Javier Montoro Martínez ([LinkedIn](https://www.linkedin.com/in/javier-montoro/)).
