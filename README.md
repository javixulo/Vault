# Vault

**Ask your NAS how it is doing, without giving anyone a shell.**

Vault is a small, **read-only** [MCP](https://modelcontextprotocol.io) server that wraps the JSON-RPC API of [OpenMediaVault](https://www.openmediavault.org) (OMV). It plugs into your AI assistant and lets it look at the real state of the NAS (system, shared folders and, as the catalog grows, permissions, services, disks, users and backups) instead of trusting documentation that may be out of date.

**It runs as a container on the very NAS it watches.** Vault is a single small Docker image, so you do not need another machine: add it as a stack in OMV's own Compose plugin, paste the compose file and the environment file from [Run with Docker](#run-with-docker), and start it. It then talks to the OMV on the same box over your local network. Nothing is installed on OMV itself and nothing changes on it: remove the stack and it is gone. (Give it the NAS's address on your network, for example `http://192.168.1.7:8080`, not `localhost`: inside a container, `localhost` is the container itself.)

> **Status: beta.** Eleven tools are built and have been run against a real NAS with OMV 8.5.12: `system_status`, `list_shared_folders`, `get_folder_permissions`, `get_folder_acl`, `list_users_and_groups`, `list_services`, `list_disks`, `list_filesystems`, `list_scheduled_tasks`, `list_pending_updates` and `list_docker_containers`. Other OMV versions are untested. The rest of the catalog is specified but not built (see the open questions in [`docs/functional_spec.md`](docs/functional_spec.md)). Vault stays strictly read-only and keeps only this one Docker tool: the other Docker tools (images, volumes, networks, ports, and later managing containers) are planned as a separate MCP over the Docker socket.

## Tools

Every tool is **read-only**: none changes anything on the NAS.

| Tool | What it does | Typical use |
|---|---|---|
| `system_status` | Hostname, OMV version, kernel, CPU, memory and uptime | "How is the NAS doing?" |
| `list_shared_folders` | The shared folders OMV defines, one page at a time (`limit`, `offset`, `total`, `has_more`) | "Which shared folders does the NAS have?" |
| `get_folder_permissions` | The privileges OMV has set for one shared folder: each user or group and its access (`read_write`, `read_only`, `deny`) | "Who can write to `media`?" |
| `get_folder_acl` | The extended POSIX ACL of a shared folder's root: owner, group, base permissions and the named users and groups with their `rwx` | "Does `Documentos` have extra ACL entries?" |
| `list_users_and_groups` | The OMV users (with their groups) and groups (with their members), in two blocks: `system` (the operating system's own) and `custom` (created in OMV). An optional `name` narrows both blocks: a piece of the name (`fam`), or a `*` wildcard on the whole name (`grupo-*`) | "Who is in the `family` group?" |
| `list_services` | Whether each service OMV manages (smb, nfs, ssh...) is enabled and running. Built on `Services.getStatus` | "Is SMB running?" |
| `list_disks` | The disks OMV sees: device, model, size, SMART status (`passed`, `failed`, `unknown`) and temperature. Built on `DiskMgmt.enumerateDevices` and `Smart.getList` | "Are the disks healthy?" |
| `list_filesystems` | The filesystems OMV manages, mounted or not: device, uuid, type, label, mount point, size, used space (as text), free space and used percentage. Built on `FileSystemMgmt.enumerateFilesystems` | "How full is each data disk?" |
| `list_scheduled_tasks` | The cron jobs OMV has configured: name (comment or command), schedule (a cron expression or `@daily`...) and whether it is enabled. OMV keeps no last-run data, so it does not say whether a job ran. Built on `Cron.getList` | "Is the nightly backup job still enabled?" |
| `list_pending_updates` | How many system updates are pending; with `detailed: true`, each package with its installed and candidate version. Reads what OMV already knows, it never runs `apt update` or installs anything. Built on `Apt.enumerateUpgraded` | "Does the NAS need updates?" |
| `list_docker_containers` | The Docker containers: name, image, state, status, health (`healthy`, `unhealthy`, `starting` or null), creation date, network and the Compose project and service. No ports, mounts, commands or environment variables. Built on `Compose.getContainerListBg`, `Compose.getServicesListBg` and `Exec.getOutput` (OMV runs each as a short background job and Vault reads its output) | "Are my containers up and healthy?" |

## Read-only by construction

OMV has no API keys with limited permissions: Vault logs in with an OMV account, and that account can do anything its role allows. So the **read-only boundary is enforced by Vault's own code**, not by OMV:

- Vault can only make a **fixed list of RPC calls**, each documented as read-only in the functional spec (section "RPC calls"). There is no tool that runs "any RPC method", and no tool argument names a service or a method.
- A test fails if the code exposes a call that the spec does not document.
- The password is a **Docker secret** (a file inside the container), never an environment variable.

Use a dedicated OMV account for Vault, not the one you use yourself. See [Set up the OMV account](#set-up-the-omv-account): that account needs administrator rights, so read what that means before you create it.

---

# Technical guide

Vault is written in **C# / .NET 11**.

## Requirements

- Docker with Compose 2.23 or later (to run the published image), or the .NET SDK 11 (see `global.json`) to build and run it from source.
- OpenMediaVault 8.x (tested on 8.5.12), its web interface address, and an account for Vault.

## Set up the OMV account

OMV has **no API keys** (checked in the code of OMV 8.5): its API is the one the web interface uses, and it is reached by logging in with a user name and a password. So Vault needs an OMV user.

1. In the OMV web interface, go to **Users → Users** and create a user just for Vault (for example `vault-mcp`). Give it a long, unique password: it will be kept in your compose settings, so do not reuse one.
2. Add that user to the group **`openmediavault-admin`** (the web interface administrators). It is a system group: if it is not in the list when you edit the user, look for the option that shows system groups.
3. Put the user name and the password in the compose settings (see [Run with Docker](#run-with-docker)).

**Why the administrator group.** Tested on OMV 8.5.12: a user in no group can log in and `system_status` works, but OMV answers HTTP 403 to `list_shared_folders`, because OMV demands the administrator role for most of its calls. Without the group, Vault says so in the tool's error ("the account lacks the role it needs"). Which other tools will work without it is not known yet.

**What that means for security.** The account is a full administrator of OMV, so anyone who gets its password can change the NAS. Vault itself cannot: it only knows a fixed list of read-only calls (see [Read-only by construction](#read-only-by-construction)). Protect the password:

- Inside the container it is a Docker secret (a file), never an environment variable, so `docker inspect` does not show it. In your settings it is a plain variable: keep your `.env` (or your stack's variables) readable only by you and out of any repository.
- Do not reuse the password, and use the account for nothing else. Changing it is enough to cut Vault off.

## Run with Docker

Vault is configured **only through its compose file**: there is no other file to create on the host. Save this as `docker-compose.yml` (the repository's own file has the same shape, with more comments) and give a value to the `${VARIABLES}`:

```yaml
services:
  vault:
    image: ghcr.io/javixulo/vault:${VAULT_VERSION}   # required, for example 0.1.0
    container_name: vault-mcp
    restart: unless-stopped
    user: "1000:1000"
    ports:
      - "${VAULT_PORT:-7842}:8080"       # MCP endpoint (Streamable HTTP) and /health
    cap_drop: [ALL]
    security_opt: ["no-new-privileges:true"]
    environment:
      TZ: ${TZ:-Europe/Madrid}
      Vault__OmvBaseUrl: ${VAULT_OMV_URL}           # required, for example https://nas.local
      Vault__OmvUsername: ${VAULT_OMV_USERNAME}     # required, the OMV account for Vault
      # Only if OMV has a self-signed HTTPS certificate (false is the safe default; read the risk in "Configuration"):
      Vault__AllowInvalidCertificate: ${VAULT_ALLOW_INVALID_CERTIFICATE:-false}
      # Logs and traces (OpenTelemetry). Empty exports nothing; logs still go to `docker logs`.
      OTEL_SERVICE_NAME: vault-mcp
      OTEL_EXPORTER_OTLP_ENDPOINT: ${OTEL_EXPORTER_OTLP_ENDPOINT:-}                  # for Seq: http://seq:5341/ingest/otlp
      OTEL_EXPORTER_OTLP_PROTOCOL: ${OTEL_EXPORTER_OTLP_PROTOCOL:-http/protobuf}     # Seq only accepts http/protobuf
      OTEL_RESOURCE_ATTRIBUTES: deployment.environment.name=production
    secrets:
      - omv_password

secrets:
  omv_password:
    environment: VAULT_OMV_PASSWORD     # required: compose turns it into the file /run/secrets/omv_password
```

Give the variables a value in a `.env` next to the file (copy `.env.template`), in the environment of the shell that runs compose, or in the variables of your stack editor (Portainer, Dockge...):

| Variable | Example | Meaning |
|---|---|---|
| `VAULT_VERSION` | `0.1.0` | **Required.** Version of the image. Use an exact version, so updates happen on purpose (`latest` is the newest stable one; a pre-release needs its exact version) |
| `VAULT_PORT` | `7842` | Optional (default 7842). Port Vault listens on, on this machine |
| `TZ` | `Europe/Madrid` | Optional. Timezone |
| `VAULT_UID`, `VAULT_GID`, `VAULT_ALLOW_INVALID_CERTIFICATE` | `1000`, `1000`, `false` | Optional, only in the repository's compose file (see `.env.template`) |
| `VAULT_OMV_URL` | `https://nas.local` | **Required.** Address of the OMV web interface, as the container sees it: use the NAS's address on your network (`http://192.168.1.7:8080`), never `localhost` |
| `VAULT_OMV_USERNAME` | `vault-mcp` | **Required.** The OMV account of [the previous section](#set-up-the-omv-account) |
| `VAULT_OMV_PASSWORD` | `a-long-unique-password` | **Required.** Its password. In a `.env` file, put it in single quotes if it has `$` or `#` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://seq:5341/ingest/otlp` | Optional. Where to send logs and traces; empty sends nothing |

An example of the matching `.env` (the environment file, in OMV's compose plugin), with a value for every variable. The first four are the ones you must set; change the others only if you need to:

```ini
# Required
VAULT_VERSION=0.1.0
VAULT_OMV_URL=https://nas.local
VAULT_OMV_USERNAME=vault-mcp
VAULT_OMV_PASSWORD='a-long-unique-password'

# Optional (these are the defaults; the logging endpoint is empty by default)
VAULT_PORT=7842
TZ=Europe/Madrid
VAULT_ALLOW_INVALID_CERTIFICATE=false
OTEL_EXPORTER_OTLP_ENDPOINT=http://seq:5341/ingest/otlp
```

`VAULT_UID` and `VAULT_GID` (default `1000`) exist only in the repository's compose file; the sample above hardcodes `user: "1000:1000"`.

```powershell
Copy-Item .env.template .env      # then edit it
docker compose up -d
docker compose logs vault         # "Startup checks passed" and "OpenMediaVault answered and accepted the credentials"
```

Then, to use it from Claude Code:

```bash
claude mcp add --transport http vault http://localhost:7842/mcp
```

The container runs as a non-root user, has a health check on `/health` and `restart: unless-stopped`. It keeps nothing: there is no volume. It needs no network access except to OMV. TLS and authentication are the job of your reverse proxy, not Vault's.

Why a secret and not an environment variable: an environment variable is visible with `docker inspect` and in the process list of the host. The password you give compose is turned into a file that only the container sees. It is not encrypted: anyone who can read your compose settings can read it, which is why the account must be only for Vault.

**The image.** `ghcr.io/javixulo/vault` (GitHub's container registry), for `linux/amd64` and `linux/arm64`. Tags: `0.1.0` (never moves), `0.1` (the newest patch), `latest` (the newest stable version; never a pre-release) and `sha-<commit>`. If the package is private, log in once with `docker login ghcr.io` using a GitHub token with `read:packages`.

**Updating and going back.** Change `VAULT_VERSION` and run `docker compose pull && docker compose up -d`. The previous image stays on the machine until you remove it, so going back is setting the old version again. [`CHANGELOG.md`](CHANGELOG.md) says what changed in each version, configuration included. The version that runs is in the image label (`docker inspect --format '{{ index .Config.Labels "org.opencontainers.image.version" }}' vault-mcp`), in the logs and in the MCP handshake.

### Configuration

What the container reads (the compose file above sets them from the variables):

| Variable | Default | Meaning |
|---|---|---|
| `Vault__OmvBaseUrl` | none, **required** | Base URL of the OMV web interface, for example `https://nas.local`. Vault adds `/rpc.php` |
| `Vault__OmvUsername` | none, **required** | User name of the OMV account |
| `Vault__OmvPasswordFile` | `/run/secrets/omv_password` | File with the account's password as plain text (one trailing line break is ignored). It is read again at each login, so a rotated secret needs no restart |
| `Vault__TimeoutSeconds` | `20` | Time limit of each request to OMV, 1 to 120 |
| `Vault__AllowInvalidCertificate` | `false` | Accept any TLS certificate from OMV (it ships a self-signed one). With it on, anyone on your network can impersonate OMV and receive the password |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | When set, logs and traces are exported through OTLP. For Seq: `http://seq:5341/ingest/otlp` |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (`http/protobuf` in the repository's compose file) | Seq only accepts `http/protobuf`: set it when exporting to Seq |
| `OTEL_RESOURCE_ATTRIBUTES` | unset | For example `deployment.environment.name=production`; the server adds `service.name` and `service.version` itself |

On start the server validates its configuration (OMV address, user, time limit, password file) and logs every problem, with how to fix it, before exiting. If OMV simply does not answer yet, it starts anyway and warns.

## Run from source

```powershell
$env:Vault__OmvBaseUrl = "https://nas.local"
$env:Vault__OmvUsername = "vault-mcp"
Set-Content secrets/omv_password "the-password" -NoNewline      # the secrets/ folder is git-ignored
$env:Vault__OmvPasswordFile = "secrets/omv_password"
dotnet run --project src/Vault.Mcp
```

It listens on `http://localhost:5000/mcp` (`/health` is the health check). To build the image yourself, uncomment `build: .` in the repository's compose file and run `docker compose up -d --build`.

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

### Releasing

Publishing is continuous delivery of the **image**, triggered by a version tag. [`release.yml`](.github/workflows/release.yml) runs when a tag such as `v0.1.0` (or `v0.1.0-beta.1`, a pre-release) is pushed: it checks that the commit is in `main`, runs the tests, builds the image, smoke-tests and system-tests that build, scans it (a warning, never a block), pushes it to `ghcr.io/javixulo/vault` for `amd64` and `arm64` with an SBOM and build provenance, and sets the GitHub release's title and notes from the changelog entry. Steps to release: update `CHANGELOG.md` (the entry's heading is `## [0.1.0] - 2026-12-01`) and commit it to `main`; then, on GitHub, **Releases → Draft a new release**, choose *Create new tag on publish* with the version (`v0.1.0`), tick *pre-release* if the version has a suffix, and publish (the notes you type there are replaced by the changelog's). From a terminal, `git tag v0.1.0 && git push origin v0.1.0` does the same. Check the run in the Actions tab. Deploying to the NAS is your compose file: set `VAULT_VERSION` and pull (see [Run with Docker](#run-with-docker)). Details and reasons: [`docs/technical_spec.md`](docs/technical_spec.md), section 9.

## Architecture

```
 MCP client (Claude, ...)
        │  Streamable HTTP  (/mcp)
        ▼
┌────────────────────────────────────────────┐
│ Vault.Mcp   ASP.NET Core host              │
│   tool classes (thin: bind args, call a    │
│   service, return its result)              │
└──────────────────┬─────────────────────────┘
                   ▼
┌────────────────────────────────────────────┐
│ Vault.Core   all the logic                 │
│   one reader service per tool              │
│   (SystemStatusReader, DisksReader, ...)   │
│   port: IOmvRpcClient  ── OmvRpcCall       │
│   (a fixed list of read-only calls)        │
└──────────────────┬─────────────────────────┘
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
| `src/Vault.Core/` | The logic: the OMV RPC client and the fixed list of calls (`Rpc/`), one reader per tool, grouped by area (`SystemInfo/`, `Shares/`, `Users/`, `Disks/`, `Docker/`...), results and messages (`Models/`) |
| `tests/Vault.UnitTests/` | Unit tests: pure logic |
| `tests/Vault.IntegrationTests/` | Integration tests: the client and services against a stand-in of OMV, startup validation |
| `tests/Vault.ContractTests/` | Contract tests through a real MCP client |
| `tests/Vault.SystemTests/` | System tests against the Docker image |
| `tests/shared/` | The stand-in of the OMV RPC endpoint, shared by the integration and system tests |
| `secrets/` | Optional, git-ignored: where a password file goes when running from source |
| `CHANGELOG.md` | What changed in each version |
| `.github/workflows/` | CI (`ci.yml`), the release that publishes the image (`release.yml`) and its rehearsal (`release-dry-run.yml`) |
| `docs/` | Functional spec (what the tools do) and technical spec (how and why) |
| `CLAUDE.md` | Rules for working on this repository with Claude Code. Its skills and agent come from the [`grimoire`](https://github.com/javixulo/grimoire) plugin, which is not part of this repository: install it to use them (`claude plugin marketplace add javixulo/grimoire`, then `claude plugin install grimoire@grimoire`). You do not need it to build, run or use Vault |
| `Dockerfile`, `docker-compose.yml` | Container image and deployment |
| `.env.template` | Template of `.env`, the variables the compose file needs (the OMV account included: keep your copy private) |
| `scripts/smoke-test.sh` | Smoke test of the Docker image: it starts, `/health` answers and the tools are listed |

## License

[MIT](LICENSE) © Francisco Javier Montoro Martínez ([LinkedIn](https://www.linkedin.com/in/javier-montoro/)).
