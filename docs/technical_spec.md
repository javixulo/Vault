# Vault MCP — Technical Specification

> Status: **draft v0.1** (skeleton)
> Sibling document: [functional_spec.md](./functional_spec.md)
>
> Each section says whether it is **Decided** (agreed with the user, or inherited from the shared rules of the other MCPs of the system) or **Proposed** (a draft to review, section by section). Everything about *which OMV calls exist and what they return* is waiting for the question *OMV version* of the functional spec: the login, the session, the error answers, `System.getInformation`, `ShareMgmt.getList`, `ShareMgmt.getPrivileges`, `ShareMgmt.getFileACL`, `UserMgmt.enumerateAllUsers` and `UserMgmt.enumerateAllGroups` were checked against OMV 8.5.12 (a real NAS) and 8.5.1 (Docker); other versions are untested.

## 1. Technology rule

**Decided.** Everything in this project is written in C# on .NET 10. No other language or runtime, in the server, the tests or the tooling.

- Target framework `net10.0`, C# 14 (the SDK default), pinned in `global.json`. Same SDK pin as Tinker.
- Nullable reference types enabled and warnings treated as errors (`Directory.Build.props`), with the analysis level `latest-recommended`.
- The MCP server uses the official C# SDK (`ModelContextProtocol`) over the **Streamable HTTP transport** (ASP.NET Core, `ModelContextProtocol.AspNetCore`). It runs as a long-lived service, not as a process spawned per session.
- The contract with the clients is the tool names, schemas and messages, not the implementation. See section 6.

## 2. Solution layout

**Proposed.**

```
Vault.slnx
src/Vault.Mcp/                  ASP.NET Core host: HTTP transport, tool classes, configuration, startup validation, telemetry, health
src/Vault.Core/                 Logic: the OMV RPC client (Rpc/), models and messages, one service per tool
tests/Vault.UnitTests/          Unit tests: pure logic, no disk, network or database
tests/Vault.IntegrationTests/   Integration tests: the RPC client and the services over real HTTP against a stand-in of OMV; startup validation with real files
tests/Vault.ContractTests/      Contract tests through a real MCP client, with a double of the OMV port
tests/Vault.SystemTests/        System tests: complete flows against the Docker image (skipped without VAULT_SYSTEM_IMAGE)
tests/shared/FakeOmvServer.cs   The stand-in of the OMV RPC endpoint, linked (not copied) by the integration and system tests
scripts/smoke-test.sh           Smoke test of the Docker image (section 8)
secrets/                        Git-ignored folder for the OMV credentials file; only the example is versioned
docs/                           Functional and technical specs
```

There is no `Vault.ModelTests` (Vault has no embedding model), no data folder and no database. `tools/Traceability` of Tinker (a checker of the `Criteria` markers of the tests against the functional spec) is not copied; the markers are written the same way, so it can be added unchanged if wanted.

## 3. Observability

**Decided.** Same as the other MCPs of the system (the `app-telemetry` skill).

- **OpenTelemetry (OTLP) for logs and traces**, exported only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; the backend is Seq. **Console logging to stderr stays on**, so `docker logs` works when the backend is down. Exporter configuration uses the standard variables (`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`). Logs and traces only; no metrics.
- **Resource attributes on every record:** `service.name` (`OTEL_SERVICE_NAME`, default `vault-mcp`), `service.version` and `deployment.environment.name` (from `OTEL_RESOURCE_ATTRIBUTES`; `docker-compose.yml` sets it from `VAULT_ENVIRONMENT`).

**Proposed: what is recorded.**

- **Spans.** The incoming HTTP request is the root of the unit of work. Under it, one span per tool call (`system_status`, `list_shared_folders`, `get_folder_permissions`, `get_folder_acl`, `list_users_and_groups`, `list_services`, `list_disks`, `list_filesystems`, `list_scheduled_tasks`, `list_pending_updates`) and one child span per call to OMV, `omv_rpc`. A step that fails unexpectedly has the error status and `error.type`; a failure of OMV is recorded as an error code on the span (`error.type`) but is not an unexpected failure of the step.
- **Attributes this project adds** (the names are the contract with whoever reads the logs; renaming one is a change to say out loud). The names live in `Telemetry.cs`:

  | Name | Where | Meaning | Personal data |
  |---|---|---|---|
  | `rpc.service`, `rpc.method` | span `omv_rpc` | The OMV service and method called (OpenTelemetry convention names) | No |
  | `folders.count` | span of `list_shared_folders` | How many folders the page has | No |
  | `privileges.count` | span of `get_folder_permissions` | How many privileges the folder has | No |
  | `acl.entries.count` | span of `get_folder_acl` | How many named ACL entries the folder's root has | No |
  | `users.count`, `groups.count` | span of `list_users_and_groups` | How many users and groups the result has (after the name filter) | No |
  | `error.type` | spans and logs | The error codes of section 6 (`omv_unreachable`, `failed`, ...); absent on success | No |

- **Never recorded:** the password, the content of the credentials file, the body of a login request, or the text OMV sends in an error. The user name of the OMV account is logged when the session starts.

## 4. Deployment

**Decided** (shared rules of the system) **and proposed** (the parts marked).

- Each MCP is packaged as a Docker image and runs as its own container. Transport is HTTP; TLS and authentication are the user's existing reverse proxy, **not Vault's job** (Sigil's MCP contract: no authentication of its own, the proxy provides it).
- **Vault keeps nothing.** There is no data folder, no volume and no database. Everything is read from OMV when a tool is called. If something ever needs to be cached, that is an explicit decision recorded here, not something the skeleton carries over.
- **User and permissions.** The container runs as a **non-root user** (`user:` in compose), with `cap_drop: ALL` and `no-new-privileges`. It needs no disk access and no network access except HTTP(S) to OMV.
- **Credentials: a Docker secret.** *(Decided: never plain text in the environment; proposed: the details below.)* The OMV account is a file mounted by compose's `secrets:` at `/run/secrets/omv_credentials` (`Vault__CredentialsFile`). **Proposed format:** a JSON object `{"username": "...", "password": "..."}` (see `secrets/omv_credentials.example.json`; `OmvCredentials.Parse`). The file is read again at each login, so a rotated secret is picked up without a restart. The `secrets/` folder is git-ignored and in `.dockerignore`. OMV has no API keys with limited permissions, so this is a full OMV account: the read-only boundary is the fixed list of calls (section 5), not OMV. **Decided:** the account is kept in the group `openmediavault-admin`. A non-admin account could log in and call `System.getInformation`, but OMV answered HTTP 403 to `ShareMgmt.getList`, so a non-admin account cannot support `list_shared_folders` (nor, most likely, the shared-folder tools, ACL included). The mitigation for holding a full admin account: its password is unique to Vault, never reused for the human OMV user or anything else, and reachable only through the Docker secret of this section, never through the environment or the repository.
- **Configuration through environment variables**, using the ASP.NET Core convention:

  | Variable | Default | Meaning |
  |---|---|---|
  | `Vault__OmvBaseUrl` | none, **required** | Base URL of the OMV web interface, for example `https://nas.local`. Vault adds `/rpc.php` |
  | `Vault__CredentialsFile` | `/run/secrets/omv_credentials` | The credentials file |
  | `Vault__TimeoutSeconds` | `10` | Time limit of each HTTP request to OMV, 1 to 120 |
  | `Vault__AllowInvalidCertificate` | `false` | Accept any TLS certificate from OMV. OMV ships a self-signed one, so this exists; with it on, anyone on the network can impersonate OMV and receive the account's password. Prefer to trust OMV's certificate in the host |

  plus `ASPNETCORE_HTTP_PORTS`, `TZ` and the OpenTelemetry variables of section 3.
- **Startup validation.** On start the server checks that everything it needs is in place and, if not, **logs every problem at once with what is wrong and how to fix it, and exits with a failure code**: the OMV address is set and is an absolute http(s) URL, the time limit is between 1 and 120 seconds, and the credentials file can be read by the running user and holds a username and a password. The content of the credentials file is never logged, valid or not. Whether OMV answers is **not** a startup failure: OMV may start after Vault, so the server logs a warning, keeps running, answers `/health` and lists its tools, and tries again on every tool call (failing with `omv_unreachable` until OMV answers). This differs from the data-folder checks of Tinker because Vault does not own the thing it depends on. (Tinker has an open question on whether to exit or run degraded: here it is decided per check, as above.)
- **Timezone** through the `TZ` variable, as in all the user's images.
- **Built:** `Dockerfile` (multi-stage, .NET 10 `aspnet` runtime, `curl` only for the healthcheck, health check on `/health`, `ENTRYPOINT ["dotnet", "Vault.Mcp.dll"]`) and `docker-compose.yml`. **Verified:** the image was built and run with Docker 29, the system tests passed against it, and it served a real OMV 8.5.12. **Not verified:** `scripts/smoke-test.sh` (it needs bash) and the CI run.

## 5. Architecture

**Proposed.**

- **Tools are thin.** A tool class only binds the arguments, calls a service and returns its result. No rule lives in a tool class.
- **Services** (in `Vault.Core`): `SystemStatusReader`, `SharedFolderLister`, `FolderPermissionsReader`, `FolderAclReader`, `UsersAndGroupsReader`, `ServicesReader`, `DisksReader`, `FilesystemsReader`, `ScheduledTasksReader` and `PendingUpdatesReader`, one per tool (the two that take a folder name share `SharedFolderLookup`, which turns the name into OMV's uuid). The **port** they depend on is `IOmvRpcClient`.
- **The OMV RPC client** (`Vault.Core/Rpc`). `OmvRpcClient` talks to `POST <base>/rpc.php` with `{"service", "method", "params", "options"}` and reads `{"response", "error"}`. It logs in with `Session.login`, keeps the session cookie in the handler of its single, process-long `HttpClient` (the handler must not be recycled by an `HttpClientFactory`), renews the session once when a call finds it expired (an HTTP 401 or 403, which OMV 8.5 was seen to send with `code: 0`; an RPC error code of 5001 or 5002 also counts, as an unconfirmed guess for other versions: open question *OMV version*), retries the failed call once with the new session before giving up, and shares one login between concurrent calls (a generation counter under a lock), so a session expiring under concurrent calls still logs in once, not once per call.
- **The read-only boundary is code, not OMV.** `IOmvRpcClient.CallAsync` takes an `OmvRpcCall`, a class with a **private constructor** whose only instances are static fields in `OmvRpcCall.cs`. There is no method that takes a service or method name as text, and no tool argument names a call; the login (`OmvRpcCall.Login`) is `internal` and not in `OmvRpcCall.Exposed`. Adding a call means adding a static field **and** a row in the "RPC calls" section of the functional spec; a test fails if the two lists differ. Tests also check that the port has no `string` parameters and that no tool accepts anything that could name a call.
- **Background jobs (proposed, for the Docker tools).** The `Compose.get*Bg` calls start a job on the NAS and answer with the name of its output file; `Exec.getOutput {filename, pos}` reads it in pieces (functional spec, UC-26). The client gets one method that starts the job and reads it to the end, so a service never sees the file name: it passes only an `OmvRpcCall` for the job, never the name of a call or of a file. `Exec.getOutput` is a static field of `OmvRpcCall` and is in `Exposed` like any other call (the call is the fixed thing; its `params` always vary, as with `ShareMgmt.getPrivileges`), but only that method uses it. The name must match `^/tmp/bgstatus[A-Za-z0-9]+$` before it is read, `pos` continues from the previous answer, the read stops when `running` and `pendingOutput` are false, and the time limit for OMV covers each job as a whole (`omv_unreachable` when exceeded; UC-26). Tests check that a name outside the pattern is never read, that no tool argument can reach it, and that no code outside that method references the call. Leftover jobs and files are accepted (UC-26, rule 4).
- **Failures are typed.** `OmvRpcException` carries a kind (`Unreachable`, `AuthFailed`, `SessionExpired`, `RpcError`, `BadResponse`) and the RPC code; `Messages.ForRpc` turns it into the error of section 6, without the exception's text (which may hold what OMV said).
- **Tolerant mapping.** OMV is PHP: numbers can arrive as text and fields differ between versions. `JsonReading` returns `null` for a field that is missing or odd instead of throwing.
- **Stateless HTTP.** The tools use no sampling, elicitation or subscriptions, so the transport runs in the stateless mode of the SDK (`HttpServerSessionMode.Stateless`): no session id, nothing kept per client. The MCP endpoint is `/mcp`; `/health` is separate. The only state in the process is the OMV session, which can always be rebuilt.
- **Access control and rate limits.** None in the server: the proxy in front provides them (section 4). *Not done:* validating `Host` and `Origin`.

## 6. Tool contract

**Proposed.** This is the contract with the clients. Changes to it must be said explicitly (`CLAUDE.md`). The input and output models are in section 2 of the functional spec; this section covers how they travel.

**Tools:** `system_status`, `list_shared_folders`, `get_folder_permissions`, `get_folder_acl`, `list_users_and_groups`, `list_services`, `list_disks`, `list_filesystems`, `list_scheduled_tasks` and `list_pending_updates` (the tenth was built from the real answer of `Apt.enumerateUpgraded`, one plain list of APT package records; the first guess, `Apt.getUpgradeList`, does not exist on OMV 8.5.12; the ninth was built from the real answer of `Cron.getList`, `{total, data}` with a required `type` list; the NAS has no cron jobs, so the entry fields are OMV's documented ones, and OMV keeps no last-run data, so the tool reports none; the eighth was built from the real answer of `FileSystemMgmt.enumerateFilesystems`, one plain list in which `used` is human text such as `22.10 GiB`, passed on as text, and an unmounted filesystem has `-1` in its sizes; the seventh was built from the real answers of `DiskMgmt.enumerateDevices` and `Smart.getList`, joined by device; the sixth was built from the real answer of `Services.getStatus`, one plain list of `{name, title, enabled, running}`; the fifth was built from a request seen in OMV's own traffic and then checked against the real answers of OMV 8.5.12, which showed that users have no "disabled" flag but a `system` one; the third was built before its call was confirmed; checking it against the real OMV 8.5.12 corrected the first reading of `ShareMgmt.getPrivileges`; the fourth was built from a real answer of `ShareMgmt.getFileACL`), listed in this order on every connection — the only ten built so far; this table grows as each one is. **Every tool is read-only by construction: it is declared with `ReadOnly = true`** (`readOnlyHint`), and a contract test fails if any tool is not. Every tool has a title and `openWorldHint=false`. The hints are for the client, never a security measure: the boundary is section 5.

| Tool | `readOnlyHint` | OMV call | Why |
|---|---|---|---|
| `system_status` | true | `System.getInformation` | It only reads. |
| `list_shared_folders` | true | `ShareMgmt.getList` | It only reads. Bounded: `limit` 1 to 200 (default 50), `offset`, `total` and `has_more`. |
| `get_folder_permissions` | true | `ShareMgmt.getList`, `ShareMgmt.getPrivileges` | It only reads. One required argument, `folder`, declared optional in the schema so that a missing value is an `invalid_arguments` result and not an SDK protocol error. |
| `get_folder_acl` | true | `ShareMgmt.getList`, `ShareMgmt.getFileACL` | It only reads. One required argument, `folder`, declared optional in the schema as in `get_folder_permissions`. Returns the owner, group and base permissions plus the named entries, never the path OMV reports. |
| `list_users_and_groups` | true | `UserMgmt.enumerateAllUsers`, `UserMgmt.enumerateAllGroups` | It only reads. One optional argument, `name`, searched (ignoring case) in the names of users and groups: a piece of the name, or a `*` wildcard that must match the whole name (`NameFilter`, a plain scan, not a regular expression, so no text can make it slow). Vault filters, OMV is always asked for everything. Returns two blocks, `system` and `custom`, each with its users (with their groups) and groups (with their members). Checked against a real OMV 8.5.12. |
| `list_services` | true | `Services.getStatus` | It only reads. No arguments. Every service OMV manages, with `enabled` and `running`. |
| `list_disks` | true | `DiskMgmt.enumerateDevices`, `Smart.getList` | It only reads. No arguments. Both calls joined by device; a failure of either fails the tool. |
| `list_filesystems` | true | `FileSystemMgmt.enumerateFilesystems` | It only reads. No arguments. `used` is OMV's text, not a number. |
| `list_scheduled_tasks` | true | `Cron.getList` | It only reads. No arguments. No last-run fields: OMV keeps none. |
| `list_pending_updates` | true | `Apt.enumerateUpgraded` | It only reads. One optional argument, `detailed` (default `false`): only `count`, or also the list of packages. |

The destructive and idempotent hints are not set: they are only meaningful for a tool that is not read-only.

**Serialization.** JSON property names are `snake_case` (`omv_version`, `memory_used_bytes`, `has_more`), as the clients expect; C# types are records with `PascalCase` properties and the server's JSON options use `JsonNamingPolicy.SnakeCaseLower`. A field OMV does not report is `null`. The top-level argument names of a tool are the C# parameter names (`limit`, `offset`), which the naming policy does not touch; they are single words so they are already snake_case.

**Outcomes have no `ok` flag, and a failure sets `isError`.** Every outcome is one typed result, never a protocol error. A failure has an `error` (`code`, `message`) and the server sets the MCP flag `isError: true` in one `tools/call` filter (`ToolErrorFlag`, the same class as Tinker's), so no tool class has to remember it. A success and an empty result (no folders, with a `message`) are not errors. Error codes: `invalid_arguments`, `not_found`, `omv_unreachable`, `omv_auth_failed`, `omv_rpc_error`, `omv_bad_response`, `failed`. **Messages** say what happened, that nothing was changed, and what to do next, and **never carry** a credential, an address, a path or the text OMV sent (the code of an RPC error is the one detail they give).

**Structured content and text.** Every result is structured content with the schema generated from the C# types, and the same JSON as text, for clients that only read text. A contract test checks both.

**No raw exception ever reaches the client.** Every service call runs inside one guard that catches what is unexpected, logs it, and returns a `failed` error.

## 7. Storage

**Decided.** None. Vault has no database, no data folder and no cache: it queries OMV live. This section stays to say so on purpose; a cache would be an explicit decision recorded here, with its invalidation rule.

## 8. Testing

**Proposed.** The same approach as Tinker (the `writing-tests` skill): **a test pyramid, and each rule is tested at the lowest level that can show it.** Every acceptance criterion of the functional spec is covered by at least one automated test, and each test says which with `[Trait("Criteria", "UC-03.AC1, UC-03.AC2")]`.

- **Where does a test go?** Ask in this order and stop at the first yes: (1) does it need the whole image, a restart or the real startup? **System.** (2) Is it about what the client sees (names, schemas, `snake_case`, `isError`, annotations, the trace context)? **Contract.** (3) Does it need real HTTP to a server, or real files? **Integration.** (4) Otherwise it is a rule of the code: **Unit**, with the port replaced by a double.
- **Doubles replace ports, not rules.**
- **Levels**, each its own project:
  - **Unit** (`Vault.UnitTests`): no disk, network or database. The services with an in-memory double of `IOmvRpcClient` (`Fakes.cs`): mapping of OMV's fields, validation, error mapping, that no message leaks, that a cancellation is not turned into an error. Plus the fixed list of calls, the credentials parser and the endpoint builder.
  - **Integration** (`Vault.IntegrationTests`): the real `OmvRpcClient` and the services over real HTTP against `FakeOmvServer` (a Kestrel on a free port that plays the OMV RPC endpoint, records every call and can expire its sessions): login once and reuse, renewal, one login for concurrent calls, a refused login, an unreachable server, an RPC error, a non-JSON answer; startup validation with real files; and `SpecAllowlistTests`, which reads the functional spec (copied next to the tests, so CI needs no checkout) and fails if `OmvRpcCall.Exposed` has a call the "RPC calls" tables do not list, or the table of confirmed calls has one that is not exposed (the "Not confirmed yet" table may list calls whose tool is not built).
  - **Contract** (`Vault.ContractTests`): the real server in process, talked to with the SDK's MCP client over HTTP, with a double of the OMV port. The tools and their order, annotations and titles (**all read-only**), that no tool accepts an argument that could name an OMV call, `snake_case` schemas, the structured result also sent as text, no `ok` flag, `isError` on a failure, that no failure carries its cause, the resource attributes and the trace context (`traceparent` continued; none starts its own trace).
  - **Smoke** (`scripts/smoke-test.sh [image]`): starts the image as a non-root user with a throwaway secret and an unreachable OMV, checks that the container stays up, that `/health` answers and that `tools/list` returns the tools.
  - **System** (`Vault.SystemTests`): the image as a container, with the credentials copied in as a secret file, against a `FakeOmvServer` running in the test process (reached through `host.docker.internal`). The tools answer, only the documented calls reach OMV; a down OMV does not stop the container and the tools say `omv_unreachable`; a missing secret stops it with a message that says how to fix it; a wrong password is in neither the result nor the logs. Skipped unless `VAULT_SYSTEM_IMAGE` names the image.
- **Fixtures:** none on disk. The stand-in of OMV is code (`tests/shared/FakeOmvServer.cs`) and copies what a real OMV 8.5 answered (login, session errors, `System.getInformation`, `ShareMgmt.getList`, `ShareMgmt.getPrivileges`, `ShareMgmt.getFileACL`). See "Trying Vault by hand" below.
- **Framework:** xUnit v3 on Microsoft.Testing.Platform (`global.json` selects the runner, so `dotnet test` works on the .NET 10 SDK). Analyzer rules about test names and inline arrays are relaxed in `tests/Directory.Build.props`.

### Trying Vault by hand against a real OMV

**Decided.** The automated tests use a stand-in of OMV. To see Vault work against the real thing, there are two targets. Both use the same Vault, configured only through `Vault__OmvBaseUrl` and the credentials file.

| Target | Use it for | Cost and risk |
|---|---|---|
| **OMV in Docker** (the community image `vulncheck/openmediavault`, tested with 8.5.1) | A first contact, trying a new call, anything you would not do on the NAS. | None: it is disposable. It has no shared folders and no disks of its own, so it cannot show what a populated NAS answers. |
| **The real NAS** (tested with 8.5.12-1) | Checking that the answers have the shape the code expects, and every new call before it is documented as confirmed. | Every call is a real request to your NAS, with a full administrator account (see the README, "Set up the OMV account"). Only documented read-only calls can be made. |

**Common steps.**

1. Put the non-secret settings in `.env` (copy `.env.template`): `VAULT_OMV_URL` is the address of the web interface, **with one scheme and no path** (`http://192.168.1.7:8080`; a doubled `http://http://` makes Vault look for a host called `http`). Add `VAULT_ALLOW_INVALID_CERTIFICATE=true` only for an HTTPS OMV with a self-signed certificate.
2. Put the account in `secrets/omv_credentials.json` (copy the example). Never put the password in `.env`, a command line, a log or a chat. Both files are git-ignored.
3. `docker compose up -d --build`, then `curl http://localhost:8080/health`, and add the server to the client: `claude mcp add --transport http vault http://localhost:8080/mcp`. `docker compose logs vault` says what failed and how to fix it.
4. After changing `.env` or the secret, `docker compose up -d --force-recreate`.

**OMV in Docker.** Run the image (it needs `--privileged`, because OMV expects systemd), publish its web interface on a free host port and wait until the page answers. The default web account of a fresh OMV is `admin` / `openmediavault`, which is public: use it only here and never for a real NAS. The Vault container reaches the OMV container through the host, so `VAULT_OMV_URL` is `http://host.docker.internal:<port>`. Throw it away with `docker rm -f` when done; nothing is kept. The older image `ikogan/openmediavault` crashes on start (it is based on Debian 7) and is not usable.

**The real NAS.** Create the dedicated account first (README, "Set up the OMV account"), then point `VAULT_OMV_URL` at the NAS from the machine that runs Vault. A failure tells the cause: `omv_unreachable` is the address or the network; `omv_auth_failed` with "does not allow it this call" is an account that is not in `openmediavault-admin`; `omv_auth_failed` with "refused" is a wrong user or password.

**What to check, in this order:** `/health` is up; the tool list has the ten tools, all read-only; `system_status` returns the OMV version you run; every other tool answers, or says why not. When a real answer differs from what `FakeOmvServer` sends, fix the stand-in first (and the test that shows it), then the code: the stand-in must keep copying the real OMV, not the documentation.

**Not automated.** Nothing runs these checks in CI, and nothing detects that an OMV update changed an answer (functional spec, open question *OMV version*). A script that runs the read-only tools against a real NAS and compares their shape is the way to close that gap.

### Continuous integration

**Proposed, written from Tinker's pipeline and not run yet.** `.github/workflows/ci.yml` on GitHub Actions (`ubuntu-latest`), on every push to `main`, every pull request, every 15 days and by hand, with read-only permissions. The shape is the same as Tinker's:

```
build ─┬─ unit-tests
       ├─ integration-tests
       └─ contract-tests

image  (after build: docker build, smoke test and, outside pull requests, system tests; nothing is pushed)

ci-ok  (after every job above, even if they fail: the only check the branch protection requires)
```

The test jobs run the executables packed by `build` (no checkout, restore or compile), `scripts/ci-run-tests.sh <project>` runs each level and fails when no test ran, `ci-ok` is the only required check, every action is pinned to a commit SHA, and `release-dry-run.yml` rehearses publishing the image to a throwaway registry. Differences from Tinker: no model-tests job, no model download or cache, no trimming of native libraries (Vault has none). The reasons for each decision are in Tinker's technical spec, section 8.

## 9. Open questions

No open questions of its own yet. The pending decisions that shape this document (the OMV version, the credentials secret, the remaining tool catalog and the Docker tools) are in the functional spec; see the open questions *OMV version*, *Credentials secret*, *Remaining tool catalog* and *Docker tools: data source and shape* there.
