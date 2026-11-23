# Vault MCP (C#) — Functional Specification (DRAFT)

> Draft in review: use cases and acceptance criteria, one section per tool. Vault is a **read-only** MCP server that wraps the JSON-RPC API of OpenMediaVault (OMV), so an agent can ask about the state of the NAS (permissions, services, disks, users, backups) without shell access.
>
> **Only two tools are specified and built** (`system_status` and `list_shared_folders`), to prove the skeleton end to end. The rest of the catalog is not specified on purpose: it depends on which OMV version the NAS runs (see the open questions at the end).

## 1. Information model

Vault keeps no inventory and stores nothing: every answer is read from OMV when the tool is called. What a client sees is below. Every field that OMV may not report is optional (`null`), because OMV versions differ in what they send.

### System status

| Field | Notes |
|---|---|
| `hostname` | Name of the machine |
| `omv_version` | Version of OpenMediaVault |
| `kernel` | Operating system kernel |
| `cpu_model` | Model of the CPU |
| `cpu_usage_percent` | Current CPU use, 0 to 100 |
| `memory_total_bytes`, `memory_used_bytes` | Memory, in bytes |
| `uptime_seconds` | Time since the machine started, in seconds |

### Shared folder

| Field | Notes |
|---|---|
| `name` | Name of the shared folder in OMV. Always present |
| `uuid` | Identifier OMV gives it |
| `relative_path` | Path of the folder relative to the file system it lives on |
| `description` | The comment of the folder in OMV |

This is the folder as OMV defines it. Its permissions and ACLs are not part of it yet (see the open question *Remaining tool catalog*).

### Error

| Field | Notes |
|---|---|
| `code` | A stable code a client can act on: `invalid_arguments`, `omv_unreachable`, `omv_auth_failed`, `omv_rpc_error`, `omv_bad_response` or `failed` |
| `message` | Written for the AI that reads it: what happened, that nothing was changed, and what to do next |

A result with an `error` is a tool error (`isError`). A result without one worked. There is no `ok` field.

## 2. Tools

Every tool of Vault is read-only: none changes anything on the NAS, and each one maps to a fixed call of OMV documented in section 3.

### 2.1 Tool `system_status`

Reads the state of the machine from OMV.

**Input:** none.

**Output** (`SystemStatusResult`)

| Field | Type | Notes |
|---|---|---|
| `status` | System status | Empty when the call failed |
| `error` | Error | Empty when the call worked. See UC-02 |

#### UC-01 · Read the system status

The client asks how the NAS is doing and gets the machine's current state.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-01.AC1 | Report what OMV says | OMV is reachable and reports every field | The client calls the tool | Success, and `status` has each field with the value OMV reported. `error` is empty |
| UC-01.AC2 | A field OMV does not report | OMV does not report some field, or reports a number as text | The client calls the tool | Success, the field that is missing or cannot be read is empty, and a number sent as text is read as a number. The call is not an error |

### 2.2 Failing to get an answer from OMV

This applies to every tool.

#### UC-02 · Fail to get an answer from OMV

A tool call fails when Vault cannot get a usable answer from OMV for any of these reasons:

1. OMV does not answer within the time limit, or refuses the connection.
2. OMV refuses the credentials Vault has.
3. OMV answers the call with an RPC error.
4. OMV answers in a shape Vault does not expect.
5. Something unexpected fails inside Vault.

Failed means: the result has an `error` and is a tool error; nothing was changed on the NAS (Vault never changes anything); the failure is logged with its detail; and the message tells the client what happened and what to do next, without a credential, an address, a path or text sent by OMV.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-02.AC1 | OMV not reachable | OMV is down, unreachable or too slow | The client calls any tool | Failed, with `omv_unreachable`. The message says OMV could not be reached and that at most one retry is worth it |
| UC-02.AC2 | Credentials refused | OMV refuses the account Vault has | The client calls any tool | Failed, with `omv_auth_failed`. The message says not to retry and to tell the user to check the account and the secret |
| UC-02.AC3 | RPC error | OMV answers with an RPC error | The client calls any tool | Failed, with `omv_rpc_error`. The message gives the numeric code of the error, and says not to retry the same call |
| UC-02.AC4 | Unexpected answer | OMV answers with something that is not what the call expects: not JSON, or without the data the tool needs | The client calls any tool | Failed, with `omv_bad_response`. The message says the OMV version may differ from the one Vault supports |
| UC-02.AC5 | Unexpected failure | An unexpected failure occurs inside Vault | The client calls any tool | Failed, with `failed`. The message says it was not caused by the request and that retrying will not help. The cause is only in the logs |
| UC-02.AC6 | Nothing leaks | Any of the failures above, with a password, an address or an OMV error text involved | The client calls any tool | The result, and the logs, contain no password. The result contains no address, path or text sent by OMV |

### 2.3 Tool `list_shared_folders`

Lists the shared folders OMV defines.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `limit` | integer | No | How many folders to return, 1 to 200. Default 50 |
| `offset` | integer | No | How many folders to skip, 0 or more. Default 0 |

**Output** (`SharedFoldersResult`)

| Field | Type | Notes |
|---|---|---|
| `folders` | list of Shared folder | The page asked for, in the order OMV gives |
| `total` | integer | How many folders OMV has |
| `has_more` | boolean | Whether another page exists after this one |
| `message` | text | Set when `folders` is empty: why, and what to do next |
| `error` | Error | Empty when the call worked. See UC-02 and UC-05 |

#### UC-03 · List the shared folders

The client asks which shared folders the NAS has and gets them one page at a time.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-03.AC1 | First page by default | OMV has shared folders | The client calls the tool with no argument | Success, and `folders` has the first folders in OMV's order, up to the default limit, each with its fields. `message` is empty |
| UC-03.AC2 | Limit and offset | OMV has more folders than the limit | The client gives a limit and an offset | `folders` has at most that many folders, starting after the offset |
| UC-03.AC3 | Another page | OMV has more folders than the page returned | The client reads a page | `has_more` is true and `total` is the number of folders OMV has. On the last page `has_more` is false |
| UC-03.AC4 | An entry without a name | OMV sends a folder entry that has no name | The client lists the folders | That entry is left out. The call is not an error |

#### UC-04 · List shared folders without a result

The client gets an empty page and is told why and what to do.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-04.AC1 | No shared folders | OMV has no shared folders | The client lists the folders | Success, an empty list, and a `message` that says OMV has none and that nothing is wrong with the call |
| UC-04.AC2 | Offset at or past the end | OMV has at least one folder, and the offset is equal to or greater than their number | The client lists the folders | Success, an empty list, and a `message` that says the offset is past the end, how many folders there are and to call again with a lower offset |

#### UC-05 · Reject invalid arguments

A call is rejected if it breaks any of these rules:

1. The limit, when given, is a whole number between 1 and 200.
2. The offset, when given, is 0 or more.

Rejected means: OMV is not called, the result has an `error` and is a tool error, and the message says which argument is wrong and what it must be. When both rules are broken, only the first (the limit) is reported.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-05.AC1 | Limit too low | | The client gives a limit below the minimum | Rejected, with `invalid_arguments` that names the limit |
| UC-05.AC2 | Limit too high | | The client gives a limit above the maximum | Rejected, with `invalid_arguments` that names the limit |
| UC-05.AC3 | Negative offset | | The client gives an offset below zero | Rejected, with `invalid_arguments` that names the offset |
| UC-05.AC4 | Both broken | | The client gives a limit and an offset that are both invalid | Rejected, with `invalid_arguments` that names the limit only |

### 2.4 The session with OMV and the read-only boundary

These behaviours are not a tool. They are what every tool relies on.

#### UC-06 · Keep a session with OMV

Vault logs in to OMV with its account when it needs to, reuses that session, and renews it by itself when it expires, so the client never sees an expired session.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-06.AC1 | One login, many calls | No session yet | Several tools are called one after the other | Vault logs in once and every call uses that session |
| UC-06.AC2 | Expired session | A session that OMV has expired | A tool is called | Vault logs in again, repeats the call once, and the call succeeds |
| UC-06.AC3 | Expired session, concurrent calls | A session that OMV has expired | Several tools are called at the same time | Vault logs in once, not once per call, and every call succeeds |

#### UC-07 · Make only read-only calls

Vault can only make the calls of section 3, each documented as read-only. The client can never choose which OMV method is called.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-07.AC1 | Only the documented calls | Any tool is used | The client calls it | The only calls that reach OMV are the ones in section 3, plus the login |
| UC-07.AC2 | No tool names an OMV call | | The client reads the arguments every tool accepts | None of them is a service, a method or any other way to choose the OMV call |
| UC-07.AC3 | Tools say they are read-only | | The client lists the tools | Every tool is annotated as read-only and as not reaching outside systems, and has a title |
| UC-07.AC4 | The spec and the code agree | A call exposed by the code | The calls of section 3 are compared with those the code exposes | They are the same set: no call is exposed that section 3 does not document |

### 2.5 Starting the server

#### UC-08 · Start the server

The server starts when its configuration is complete, whether or not OMV is up yet.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-08.AC1 | Complete configuration | The OMV address and the credentials are configured | The server starts | It passes its checks, answers on `/health` and lists its tools |
| UC-08.AC2 | OMV not up yet | The configuration is complete and OMV does not answer | The server starts | It starts anyway and logs a warning. Tool calls fail with `omv_unreachable` until OMV answers |

#### UC-09 · Refuse to start with a bad configuration

The server does not start if any of these rules is broken:

1. The OMV address is set and is an absolute http or https URL.
2. The time limit for OMV is between 1 and 120 seconds.
3. The file with the OMV account can be read by the running user and holds a username and a password.

Refused means: the server stops with a failure exit code before it listens, every problem is logged at once at critical level with what is wrong and how to fix it, and the content of the credentials file is never logged.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-09.AC1 | No OMV address | The address is not set | The server starts | Refused, and the log names the setting to fix |
| UC-09.AC2 | Address that is not a URL | The address is not an absolute http or https URL | The server starts | Refused, and the log names the setting to fix |
| UC-09.AC3 | Credentials file missing | The file does not exist or cannot be read by the running user | The server starts | Refused, and the log says to mount the account as a Docker secret readable by the container's user |
| UC-09.AC4 | Credentials file invalid | The file is not valid, or lacks a username or a password | The server starts | Refused, and the log says what is wrong with it without printing its content |
| UC-09.AC5 | Several problems | More than one rule is broken | The server starts | Refused, and the log reports every problem, not only the first |
| UC-09.AC6 | Time limit out of range | The time limit is outside the range | The server starts | Refused, and the log says the range |

## 3. RPC calls

The calls Vault can make to OMV (`POST <base>/rpc.php`). **A call is exposed only if it is listed here as read-only**: adding one means adding its row first. A test checks that this table and the code list the same calls, ignoring the login. Nothing else is ever called: there is no way to pass an OMV service or method name through a tool.

| RPC call | Used by | What it does | Why it is read-only |
|---|---|---|---|
| `Session.login` | none (Vault's own login) | Authenticates the account and starts the session | It changes no NAS configuration: it only creates the session of Vault |
| `System.getInformation` | `system_status` | Returns hostname, OMV version, kernel, CPU, memory and uptime | A `get` that reads the state of the machine |
| `ShareMgmt.getList` | `list_shared_folders` | Returns the shared folders, paged with a start and a limit | A `get` that reads the configured list |

## 4. Open questions

| # | Title | Description | Status | Priority | Comments |
|---|---|---|---|---|---|
| 1 | OMV version | Which version of OMV runs on the NAS (probably 7 or 8, on a UGREEN DXP2800) and whether the services Vault calls (`Session`, `System`, `ShareMgmt`) still exist with the same methods, parameters and answer fields. The two built tools were written from the documentation, not against a real OMV: the field names, the session cookie and the RPC codes that mean "session expired" are assumptions. | Open | High | Needs a check against the real NAS (a login and the two calls). |
| 2 | Remaining tool catalog | Which tools come after the first two, and what each returns: permissions and ACLs of a folder (the key piece to audit the permissions document against reality), users and groups, services, disks and SMART, scheduled tasks and backups, pending updates. Each one needs its RPC call documented as read-only in section 3 before it is built. | Blocked | High | Blocked by the question *OMV version*: the calls and their answers depend on it. |
| 3 | Dedicated read-only OMV user | Whether to create an OMV user for Vault that is not an administrator. OMV has no API keys with fine permissions, so the only barrier is Vault's fixed list of calls. A user with fewer rights would add a second barrier, if OMV lets that user make these calls at all. | Open | High | To decide with the user; it also depends on what OMV allows to a non-admin account. |
| 4 | Credentials secret | How the OMV account reaches the container. Settled: a Docker secret mounted as a file, never an environment variable or the repository. Still open: who creates and rotates the secret, the permissions of the file for the non-root user of the container, and whether the file format proposed in the technical spec (a JSON object with `username` and `password`) is the one to keep. | Half-way | Medium | Left to settle: who creates and rotates the secret, the file permissions for the container's user, and whether to keep the proposed JSON format (technical spec, section 4). |
| 5 | Docker containers source | Whether the status of the containers on the NAS comes from OMV (its Docker plugin, if it has an RPC for it), from the Docker socket or from Portainer. It is probably outside the RPC of OMV, and a socket gives much more access than Vault should have. | Open | Medium | Decide before building the containers tool. |
