# Changelog

All notable changes to Vault are listed here, newest first. Vault follows [semantic versioning](https://semver.org); while
the version is 0.x, a breaking change raises the minor number. The public surface (what a breaking change is judged
against) is in [`docs/technical_spec.md`](docs/technical_spec.md), section 10.

## [Unreleased]

## [0.1.0] - 2026-11-24

First version of Vault: a read-only MCP server for the OpenMediaVault (OMV) API, so an AI assistant can ask about the
real state of a NAS without shell access. Tested against OMV 8.5.12.

### Tools

All of them are read-only: none changes anything on the NAS.

- `system_status`: hostname, OMV version, kernel, CPU, memory and uptime.
- `list_shared_folders`: the shared folders OMV defines, one page at a time.
- `get_folder_permissions`: the privileges OMV has set for one shared folder.
- `get_folder_acl`: the extended POSIX ACL of a shared folder's root.
- `list_users_and_groups`: the OMV users and groups, with an optional name filter.
- `list_services`: whether each service OMV manages is enabled and running.
- `list_disks`: the disks OMV sees, with SMART status and temperature.
- `list_filesystems`: the filesystems OMV manages, with their size and used space.
- `list_scheduled_tasks`: the cron jobs OMV has configured.
- `list_pending_updates`: how many system updates are pending, and which.
- `list_docker_containers`: the Docker containers, with their state and health.

### Running it

- A Docker image on `ghcr.io/javixulo/vault` for `linux/amd64` and `linux/arm64`, with an SBOM and build provenance.
  It can run on the same NAS it watches, for example as a stack in OMV's Compose plugin. The MCP endpoint is
  `http://<host>:7842/mcp` (Streamable HTTP) and `/health` is the health check.
- The container is configured only through its compose file and an environment file: the OMV address
  (`Vault__OmvBaseUrl`), the account's user name (`Vault__OmvUsername`) and its password, which is a Docker secret
  (`/run/secrets/omv_password`) and never an environment variable of the container. Optional: the time limit for OMV
  (`Vault__TimeoutSeconds`, 20 seconds by default), accepting a self-signed certificate and OpenTelemetry export of
  logs and traces (for example to Seq).
- It refuses to start with a bad configuration and says what to fix. It runs as a non-root user with no extra
  capabilities.
- The OMV account must be in the group `openmediavault-admin`: OMV refuses most of its calls to any other account.
