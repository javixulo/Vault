# Vault MCP (C#) — Functional Specification (DRAFT)

> Draft in review: use cases and acceptance criteria, one section per tool. Vault is a **read-only** MCP server that wraps the JSON-RPC API of OpenMediaVault (OMV), so an agent can ask about the state of the NAS (permissions, services, disks, users, backups) without shell access.
>
> **Eight tools are built and confirmed** against a real OMV 8.5.12 (`get_folder_permissions` and `get_folder_acl` were checked by reading the real answers of `ShareMgmt.getPrivileges` and `ShareMgmt.getFileACL` on 2026-11-23, and `list_users_and_groups`, `list_services`, `list_disks` and `list_filesystems` those of `UserMgmt.*`, `Services.getStatus`, `DiskMgmt.enumerateDevices` / `Smart.getList` and `FileSystemMgmt.enumerateFilesystems` on 2026-11-24). Two more are specified below but **not built**: `list_scheduled_tasks` (2.9) and `list_pending_updates` (2.10) — their RPC calls (section 4) are a plain guess from OMV's source and documentation; see the open question *Remaining tool catalog*. The Docker family (2.11–2.16: `list_docker_containers`, `list_docker_images`, `list_docker_services`, `list_docker_volumes`, `list_docker_networks` and `list_docker_ports`) are bare working titles, because neither their data source nor whether they should be one tool or several is decided yet — see the open question *Docker tools: data source and shape*.

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

This is the folder as OMV defines it. Its access is read with two other tools: `get_folder_permissions` (OMV's privileges) and `get_folder_acl` (the POSIX ACL of its root).

### Privilege

An entry of `get_folder_permissions`: what one user or group may do with a shared folder, as OMV's own "Shared Folder Privileges" assigns it. Not the POSIX owner or mode, and not an extended ACL (`setfacl`): OMV models access to a shared folder as this list, which is what the permissions document this capability audits actually describes.

| Field | Notes |
|---|---|
| `subject_type` | `user` or `group` |
| `subject_name` | Name of the user or group |
| `access` | `read_write`, `read_only` or `deny` |

### ACL entry

An entry of `get_folder_acl`: a named user or group that has its own entry in the extended POSIX ACL of a shared folder's root. The owner, the group and *other* are not entries: they are the base permissions of the result.

| Field | Notes |
|---|---|
| `subject_type` | `user` or `group` |
| `subject_name` | Name of the user or group |
| `permissions` | Three characters, `r`, `w` and `x` or `-`: `rwx`, `r-x`, `---`. OMV sends them as a number from 0 to 7 (4 read, 2 write, 1 execute) |

### User

An entry of `list_users_and_groups`.

| Field | Notes |
|---|---|
| `username` | Always present |
| `full_name` | The user's comment in OMV. `null` when blank |
| `groups` | Names of the groups this user belongs to, all of them, also when the result is filtered by name |

Whether the account belongs to the operating system or was created in OMV is not a field: it is the block of the result the user is in (`system` or `custom`, see 2.5). OMV has no "disabled" flag for a user: the first draft of this model had one, and the real answer showed there is none.

### Group

An entry of `list_users_and_groups`.

| Field | Notes |
|---|---|
| `name` | Always present |
| `members` | Usernames of its members |

### Service

An entry of `list_services`.

| Field | Notes |
|---|---|
| `name` | The service name as OMV gives it (`Docker`, `nfs`, `samba`, `ssh`... as the test NAS reports them, with `samba` and not `smb`): whichever services OMV reports, Vault keeps no list of its own |
| `enabled` | Whether the service is configured to run. `null` when OMV does not say (see UC-14.AC2) |
| `running` | Whether it is running right now. `null` when OMV does not say (see UC-14.AC2) |

### Disk

An entry of `list_disks`.

| Field | Notes |
|---|---|
| `device` | For example `/dev/sda` |
| `model` | As OMV reports it |
| `size_bytes` | Capacity |
| `smart_status` | `passed`, `failed` or `unknown` (no SMART support, for example a USB drive) |
| `temperature_celsius` | `null` when OMV reports none |

### File system

An entry of `list_filesystems`: a filesystem OMV manages, mounted or not — a disk from `list_disks` can hold several of these, or none. Confirmed against a real OMV 8.5.12 (2026-11-24).

| Field | Notes |
|---|---|
| `device` | The partition or device it lives on, for example `/dev/sda1` |
| `uuid` | Filesystem UUID OMV identifies it by |
| `type` | For example `ext4`, `btrfs`, `xfs` |
| `label` | Volume label. `null` when none |
| `mounted` | Whether it is currently mounted |
| `mount_point` | Where it is mounted. `null` when not mounted |
| `size_bytes` | Capacity in bytes. `null` when not mounted |
| `used` | Used space as the text OMV shows (`22.10 GiB`, `4.35 TiB`), not bytes: OMV only reports it rounded, so Vault does not turn it into a number. `null` when not mounted |
| `available_bytes` | Free space in bytes, exact. `null` when not mounted |
| `used_percent` | Percentage of the capacity in use, a whole number. `null` when not mounted |

### Scheduled task

An entry of `list_scheduled_tasks`.

| Field | Notes |
|---|---|
| `name` | The comment OMV stores for the cron job, or its command when there is no comment |
| `schedule` | The cron expression |
| `enabled` | Whether the job is active |
| `last_run_at` | `null` when it has never run |
| `last_run_succeeded` | `null` when it has never run |

### Pending update

An entry of `list_pending_updates`.

| Field | Notes |
|---|---|
| `package` | Package name |
| `current_version` | Installed version |
| `candidate_version` | Version available |

### Error

| Field | Notes |
|---|---|
| `code` | A stable code a client can act on: `invalid_arguments`, `not_found`, `omv_unreachable`, `omv_auth_failed`, `omv_rpc_error`, `omv_bad_response` or `failed` |
| `message` | Written for the AI that reads it: what happened, that nothing was changed, and what to do next |

A result with an `error` is a tool error (`isError`). A result without one worked. There is no `ok` field.

## 2. Tools

Every tool of Vault is read-only: none changes anything on the NAS, and each one maps to a fixed call of OMV documented in section 4. One subsection per tool; what every tool does when it cannot get a usable answer from OMV is common to all of them and lives in section 3, not repeated here.

### 2.1 Tool `system_status`

Reads the state of the machine from OMV.

**Input:** none.

**Output** (`SystemStatusResult`)

| Field | Type | Notes |
|---|---|---|
| `status` | System status | Empty when the call failed |
| `error` | Error | Empty when the call worked. See section 3 |

#### UC-01 · Read the system status

The client asks how the NAS is doing and gets the machine's current state.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-01.AC1 | Report what OMV says | OMV is reachable and reports every field | The client calls the tool | Success, and `status` has each field with the value OMV reported. `error` is empty |
| UC-01.AC2 | A field OMV does not report | OMV does not report some field, or reports a number as text | The client calls the tool | Success, the field that is missing or cannot be read is empty, and a number sent as text is read as a number. The call is not an error |

### 2.2 Tool `list_shared_folders`

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
| `error` | Error | Empty when the call worked. See section 3 and UC-05 |

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

### 2.3 Tool `get_folder_permissions`

Reads the privileges OMV has configured for one shared folder.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `folder` | string | Yes | The shared folder's `name`, as `list_shared_folders` returns it |

**Output** (`FolderPermissionsResult`)

| Field | Type | Notes |
|---|---|---|
| `folder` | string | The folder asked for |
| `privileges` | list of Privilege | One entry per user or group that has an explicit privilege |
| `message` | text | Set when `privileges` is empty: that no explicit privilege is set and OMV's default applies |
| `error` | Error | Empty when the call worked. See section 3 and UC-12 |

How it works: OMV identifies a folder by uuid, so Vault first looks the name up in the list of shared folders (`ShareMgmt.getList`, all of them) and then asks for the privileges of that uuid (`ShareMgmt.getPrivileges`). The name must match exactly (it is case-sensitive). OMV answers with one entry for every user and group, whose `perms` is `null` when no privilege is set for it: those are not reported (they are not explicit privileges). Otherwise `perms` is a number (0 deny, 5 read-only, 7 read and write) and Vault reports the three words of the *Privilege* model. An entry Vault cannot read (unknown type, no name, or a number other than those three) makes the whole call fail with `omv_bad_response` rather than being left out, because a privilege missing from an audit would look like no privilege.

#### UC-10 · Read a folder's privileges

The client asks who can do what with a shared folder.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-10.AC1 | Report what OMV says | The folder exists and OMV has explicit privileges on it | The client calls the tool with that folder | Success, and `privileges` has one entry per user or group, each with its `access`. `message` is empty |
| UC-10.AC2 | No explicit privilege | The folder exists and OMV has no explicit privilege on it | The client calls the tool with that folder | Success, an empty `privileges`, and a `message` that says no explicit privilege is set and OMV's default applies |

#### UC-11 · Folder not found

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-11.AC1 | No such folder | `folder` does not match any shared folder OMV has | The client calls the tool | Failed, with `not_found`. The message names the folder asked for and says to check it with `list_shared_folders` |

#### UC-12 · Reject invalid arguments

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-12.AC1 | No folder given | | The client calls the tool with `folder` blank or missing | Rejected, with `invalid_arguments` that names `folder`. OMV is not called |

Note: `folder` is required by the contract but is declared optional in the tool's schema, so a missing value reaches Vault and is rejected with this error instead of a protocol error from the MCP SDK.

### 2.4 Tool `get_folder_acl`

Reads the extended POSIX ACL (`setfacl`/`getfacl`) of a shared folder's own root — the layer OMV keeps separate from the simple privileges of `get_folder_permissions` (OMV's own admin page calls them "Privileges" and "ACL (Access Control List)", two different pages for the same shared folder).

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `folder` | string | Yes | The shared folder's `name`, as `list_shared_folders` returns it |

**Output** (`FolderAclResult`)

| Field | Type | Notes |
|---|---|---|
| `folder` | string | The folder asked for |
| `owner` | text | POSIX owner of the folder's root |
| `group` | text | POSIX group of the folder's root |
| `owner_permissions`, `group_permissions`, `other_permissions` | text | The base permissions of the owner, the group and everyone else, as `rwx` / `r-x` / `---` |
| `entries` | list of ACL entry | The named users and groups that have an entry in the ACL, users first and then groups, each in OMV's order |
| `message` | text | Set when `entries` is empty: that no named user or group has an entry and only the base permissions apply |
| `error` | Error | Empty when the call worked. See section 3 and UC-22 |

How it works: resolves `folder` to its uuid the same way `get_folder_permissions` does, then calls `ShareMgmt.getFileACL` with that uuid and `file: "/"` — the ACL of the shared folder's own root, never of a file or subfolder inside it (out of scope for now: Vault has no tool that takes a path inside a folder). OMV answers (checked against a real OMV 8.5.12) with `{file, owner, group, acl: {user, group, other, users, groups}}`: `user`, `group` and `other` are the base permissions as a number from 0 to 7, and `users` and `groups` list **every** user and group of the NAS (system ones too) with `perms` set to the same kind of number when it has an entry in the ACL and `null` when it has none. Vault reports only the ones with a `perms`; the ones with `null` are not entries. An entry Vault cannot read (no name, or a number outside 0 to 7), or base permissions it cannot read, make the whole call fail with `omv_bad_response` rather than being left out, because an access missing from an audit would look like no access. A number sent as text (`"5"`) is read as a number, as everywhere else. A missing `owner` or `group` is reported as empty (`null`), not as a failure: they are information, not an access. The path OMV reports in `file` is not passed on.

#### UC-20 · Read a folder's ACL

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-20.AC1 | Report what OMV says | The folder exists and OMV reports named ACL entries for its root | The client calls the tool with that folder | Success, with the `owner`, `group` and the three base permissions, and `entries` has one item per named user or group that OMV gives a permission, each with its `permissions`. `message` and `error` are empty |
| UC-20.AC2 | No named entry | The folder exists and no named user or group has an entry in its ACL | The client calls the tool with that folder | Success, with the owner, group and base permissions, an empty `entries`, and a `message` that says no named entry exists and only the base permissions apply. The call is not an error |
| UC-20.AC3 | An entry Vault cannot read | OMV sends an ACL entry without a name or with a number outside 0 to 7, or base permissions that are missing or outside 0 to 7 | The client calls the tool | Failed, with `omv_bad_response`, and no partial `entries`. The entry is not left out (unlike UC-03.AC4, because a missing access would look like no access) |

#### UC-21 · Folder not found

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-21.AC1 | No such folder | `folder` does not match any shared folder OMV has. The match is exact and case-sensitive, so `Media` does not match `media` | The client calls the tool | Failed, with `not_found`. The message names the folder asked for and says to check it with `list_shared_folders` |

#### UC-22 · Reject invalid arguments

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-22.AC1 | No folder given | | The client calls the tool with `folder` blank or missing | Rejected, with `invalid_arguments` that names `folder`. OMV is not called |

### 2.5 Tool `list_users_and_groups`

Lists the OMV users and groups, and which group each user belongs to — the other half of the piece that `get_folder_permissions` needs to be read against the permissions document. A NAS has many entries of the operating system (70 groups and 41 users in the test NAS, of which 6 groups and 8 users were created in OMV), so the result keeps the two apart and can be narrowed by name.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `name` | string | No | Search by name, ignoring case. Without a `*`, an entry is kept when its name contains the text (`fam` finds `family`). With `*`, which stands for any run of characters, the whole name must match: `grupo-*` finds the names that start with `grupo-`, `*fotos` the ones that end with `fotos`, `*foto*` the ones that contain `foto`. It is a wildcard, not a regular expression: every other character means itself. Blank or missing means no filter |

**Output** (`UsersAndGroupsResult`)

| Field | Type | Notes |
|---|---|---|
| `system` | Users and groups | The users and groups of the operating system (`root`, `sshd`, `sudo`...): the ones OMV marks as `system` |
| `custom` | Users and groups | The ones created in OMV, and any entry OMV does not say is a system one |
| `message` | text | Set when both blocks are empty: why, and what to do next |
| `error` | Error | Empty when the call worked. See section 3 |

*Users and groups* is a block with two lists: `users` (list of User) and `groups` (list of Group), each in OMV's order.

#### UC-13 · List users and groups

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-13.AC1 | Report what OMV says | OMV reports its users and groups | The client calls the tool with no argument | Success, and every user and group OMV reported is in `system` or in `custom`, each with the fields OMV reported. `message` and `error` are empty |
| UC-13.AC2 | A field OMV does not report | A user or group entry is missing a field OMV may not send (for example `full_name`, or `system`) | The client calls the tool | Success, that field is empty on that entry, and an entry that does not say it is a system one is in `custom`. The call is not an error |
| UC-13.AC3 | An answer Vault cannot read | OMV answers with something that is not a list, or sends a user or group entry without a name | The client calls the tool | Failed, with `omv_bad_response`, and no partial result. The entry is not left out, because a missing user or group would look like one that does not exist |
| UC-13.AC4 | System apart from custom | OMV has users and groups of the operating system and created in OMV | The client calls the tool | The ones OMV marks as `system` are only in `system`, and the others only in `custom` |
| UC-13.AC5 | Search by name | OMV has users and groups | The client gives a `name` without `*` | Only the users and groups whose name contains that text, ignoring case, are returned, in both blocks. A user still lists all its groups. A blank `name` is the same as none |
| UC-13.AC6 | Nothing matches | No user or group matches the `name` given (or OMV has none) | The client calls the tool | Success, both blocks empty, and a `message` that says nothing matched, and to try another text or leave the name out. The call is not an error |
| UC-13.AC7 | Search with a wildcard | OMV has users and groups | The client gives a `name` with one or more `*` | Only the users and groups whose whole name matches are returned, ignoring case, with each `*` standing for any run of characters, also none: `grupo-*` is the names that start with `grupo-`, `*fotos` the ones that end with `fotos`. Any other character, a `.` or a `?` too, means itself: the name is not a regular expression |

How it works: Vault calls `UserMgmt.enumerateAllUsers` (`{detail: "basic"}`) and `UserMgmt.enumerateAllGroups`, always for the whole NAS (the search is done by Vault, not by OMV), and reports their entries in OMV's order. Checked against a real OMV 8.5.12 on 2026-11-24: both answers are a plain list; a user is `{name, uid, gid, comment, dir, shell, groups, system}` (`groups` is a list of group names) and a group is `{name, gid, members, system}`, where `system` is a boolean. Vault reports `username`, `full_name` (from `comment`) and `groups`, and `name` and `members`; the uid, gid, home directory and shell are not passed on, and `system` is turned into the block the entry goes in. The mapping stays tolerant (a list, or an object with a `data` list; a missing field is `null`), and the `groups` of a user are the ones its own entry lists plus every group that lists the user as a member, worked out before filtering. Only a missing name or an answer that is not a list is a failure.

### 2.6 Tool `list_services`

Reports whether SMB, NFS, SSH and the other OMV-managed services are enabled and running. The set of services is whatever OMV reports: Vault has no list of its own.

**Input:** none.

**Output** (`ServicesResult`)

| Field | Type | Notes |
|---|---|---|
| `services` | list of Service | Every service OMV reports, in OMV's order |
| `message` | text | Set when `services` is empty: why, and what to do next |
| `error` | Error | Empty when the call worked. See section 3 |

#### UC-14 · List service status

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-14.AC1 | Report what OMV says | OMV reports the state of its services | The client calls the tool | Success, and `services` has one entry per service OMV reported, in OMV's order, each with `name`, `enabled` and `running`. `message` and `error` are empty |
| UC-14.AC2 | A state OMV does not report | A service entry has no `enabled` or no `running` | The client calls the tool | Success, and that field is `null` on that entry; every other field and entry is reported normally. The call is not an error |
| UC-14.AC3 | An answer Vault cannot read | OMV answers with something that is not a list, or sends a service entry without a name | The client calls the tool | Failed, with `omv_bad_response`, and no partial result: an entry left out would look like a service that does not exist |
| UC-14.AC4 | No services | OMV reports an empty list | The client calls the tool | Success, `services` empty and a `message` that says OMV reported none. The call is not an error |

How it works: Vault calls `Services.getStatus` once, which answers with a list of `{name, title, enabled, running}`. Checked against a real OMV 8.5.12 on 2026-11-24: the answer is the plain list described, with `enabled` and `running` as booleans. The mapping stays tolerant (a list, or an object with a `data` list; a boolean may arrive as 0/1; a missing field is `null`), and `title` is not passed on.

### 2.7 Tool `list_disks`

Reports the disks OMV sees, with their SMART status, size and temperature.

**Input:** none.

**Output** (`DisksResult`)

| Field | Type | Notes |
|---|---|---|
| `disks` | list of Disk | Every disk OMV reports, in OMV's order |
| `message` | text | Set when `disks` is empty: why, and what to do next |
| `error` | Error | Empty when the call worked. See section 3 |

#### UC-15 · List disks and their SMART status

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-15.AC1 | Report what OMV says | OMV reports its disks, with SMART data | The client calls the tool | Success, and `disks` has one entry per disk OMV reported, in OMV's order, each with `device`, `model`, `size_bytes`, `smart_status` and `temperature_celsius`. `message` and `error` are empty |
| UC-15.AC2 | A disk without SMART | A disk has no SMART support or no SMART status (for example a USB drive or an SD card) | The client calls the tool | Success, that disk's `smart_status` is `unknown`. The call is not an error |
| UC-15.AC3 | A value OMV does not report | A disk has no model, or no temperature (OMV sends an empty text) | The client calls the tool | Success, that field is `null` on that disk, and every other field and disk is reported normally |
| UC-15.AC4 | An answer Vault cannot read | OMV answers with something that is not a list, or sends a disk entry without a device | The client calls the tool | Failed, with `omv_bad_response`, and no partial result: a disk left out would look like one that does not exist |
| UC-15.AC5 | No disks | OMV reports an empty list | The client calls the tool | Success, `disks` empty and a `message` that says OMV reported none. The call is not an error |

How it works: Vault calls `DiskMgmt.enumerateDevices` (every disk, a plain list) and `Smart.getList` (`{start: 0, limit: -1}`; `{total, data}` with only the disks that have SMART support), and joins them by `canonicaldevicefile` (`/dev/sda`). Checked against a real OMV 8.5.12 on 2026-11-24 (five disks: an SD card, an NVMe, two SATA and two USB; `Smart.getList` listed four, without the SD card). `device` is `canonicaldevicefile`; `size` and `temperature` arrive as text (`temperature` is `""` when unknown). `smart_status` comes from `overallstatus`: `GOOD` is `passed`, any `BAD_*` (the real USB drive answered `BAD_STATUS`) is `failed`, and a missing or any other value is `unknown`. The temperature is the one of `enumerateDevices`, or else the one of `Smart.getList`. The mapping stays tolerant (a list, or an object with a `data` list; a missing field is `null`); the serial number, WWN, vendor, power mode and device links are not passed on. A failure of either call fails the tool: reporting `unknown` because the SMART call failed would hide a real problem.

RAID status is not part of this tool yet; see the open question *Remaining tool catalog*.

### 2.8 Tool `list_filesystems`

Lists the filesystems OMV manages, mounted or not — distinct from `list_disks` (2.7), which reports the physical disks: one disk can hold several filesystems, or none.

**Input:** none.

**Output** (`FilesystemsResult`)

| Field | Type | Notes |
|---|---|---|
| `filesystems` | list of File system | Every filesystem OMV reports, in OMV's order |
| `message` | text | Set when `filesystems` is empty: why, and what to do next |
| `error` | Error | Empty when the call worked. See section 3 |

#### UC-23 · List filesystems

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-23.AC1 | Report what OMV says | OMV reports its filesystems | The client calls the tool | Success, and `filesystems` has one entry per filesystem OMV reported, in OMV's order, each with its fields. `message` is empty |
| UC-23.AC2 | A value OMV does not report | A filesystem has no label, or is not mounted (no `mount_point`, `size_bytes`, `used`, `available_bytes` or `used_percent`) | The client calls the tool | Success, that field is `null` on that entry, and every other field and entry is reported normally. The call is not an error |
| UC-23.AC3 | An answer Vault cannot read | OMV answers with something that is not a list, or sends a filesystem entry without a device | The client calls the tool | Failed, with `omv_bad_response`, and no partial result: an entry left out would look like one that does not exist |
| UC-23.AC4 | No filesystems | OMV reports an empty list | The client calls the tool | Success, `filesystems` empty and a `message` that says OMV reported none. The call is not an error |

How it works: Vault calls `FileSystemMgmt.enumerateFilesystems` once, which answers with a plain list of every filesystem OMV manages, mounted or not. Checked against a real OMV 8.5.12 on 2026-11-24 (twelve filesystems: three data disks, the system one and the EFI partition mounted; the SD card's partitions and a swap not mounted). `device` is `canonicaldevicefile` (`/dev/sdb1`); `uuid`, `type`, `label` (an empty text is `null`) and `mounted` are passed on. `size` and `available` arrive as bytes in text and `percentage` as a number, but `used` as human text (`22.10 GiB`, `4.35 TiB`): Vault passes that text on as it is instead of turning it into a number that would look exact (the free space is not `size - used`, because ext4 reserves blocks, so `available_bytes` is the exact figure). A filesystem that is not mounted has an empty `mountpoint` and `-1` in `size`, `used`, `available` and `percentage`: Vault reports them as `null`. The mapping stays tolerant (a list, or an object with a `data` list; a missing field is `null`, and so is a used space Vault cannot read); the description, `blocks`, device links and the `prop*` flags are not passed on.

### 2.9 Tool `list_scheduled_tasks`

Lists the OMV cron jobs (scheduled tasks and backups) and the result of their last run.

**Input:** none.

**Output** (`ScheduledTasksResult`)

| Field | Type | Notes |
|---|---|---|
| `tasks` | list of Scheduled task | Every cron job OMV has configured |
| `message` | text | Set when `tasks` is empty: that OMV has no scheduled tasks |
| `error` | Error | Empty when the call worked. See section 3 |

#### UC-16 · List scheduled tasks

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-16.AC1 | Report what OMV says | OMV has scheduled tasks, some already run | The client calls the tool | Success, and `tasks` has one entry per job, with its last run when it has one. `message` is empty |
| UC-16.AC2 | A task that has never run | A job exists but has never run | The client calls the tool | Success, that entry's `last_run_at` and `last_run_succeeded` are empty. The call is not an error |

#### UC-17 · No scheduled tasks

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-17.AC1 | None configured | OMV has no cron jobs | The client calls the tool | Success, an empty list, and a `message` that says OMV has none and that nothing is wrong with the call |

### 2.10 Tool `list_pending_updates`

Lists the system updates OMV has pending. By default it only says how many; the full list is returned only when asked for.

**Input**

| Field | Type | Required | Notes |
|---|---|---|---|
| `detailed` | boolean | No | Default `false`. When `false`, only `count` is reported. When `true`, `updates` also lists every package with an update available |

**Output** (`PendingUpdatesResult`)

| Field | Type | Notes |
|---|---|---|
| `count` | integer | How many packages have an update available. Always reported, in both modes |
| `updates` | list of Pending update | Only reported when `detailed` is `true`; empty when it is `false` |
| `message` | text | Set when `count` is 0: that the system is up to date |
| `error` | Error | Empty when the call worked. See section 3 |

#### UC-18 · List pending updates

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-18.AC1 | Count by default | OMV has updates pending | The client calls the tool with no argument | Success, `count` is the number of packages pending, and `updates` is empty. `message` is empty |
| UC-18.AC2 | Full list on request | OMV has updates pending | The client calls the tool with `detailed: true` | Success, `count` as above, and `updates` has one entry per package, with its current and candidate version |

#### UC-19 · Up to date

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-19.AC1 | Nothing pending | OMV has no update pending | The client calls the tool, with or without `detailed` | Success, `count` is 0, `updates` is empty, and a `message` that says the system is up to date |

### 2.11 Tool `list_docker_containers` (working title)

Not yet specified, and its data source is not decided either (OMV's own Docker plugin, if any; a socket is very unlikely to be acceptable for a read-only MCP). See the open question *Docker tools: data source and shape*.

### 2.12 Tool `list_docker_images` (working title)

Not yet specified. Would report the Docker images on the NAS and, ideally, whether a newer version of each is available — only real traffic against OMV's Docker plugin (or whatever source is chosen) will say if that is possible without reaching outside the NAS. See the open question *Docker tools: data source and shape*.

### 2.13 Tool `list_docker_services` (working title)

Not yet specified. Whether OMV's Docker setup has a concept of "service" distinct from a container (for example Docker Compose services, or Swarm, which a single-host NAS is unlikely to run) is itself unconfirmed. See the open question *Docker tools: data source and shape*.

### 2.14 Tool `list_docker_volumes` (working title)

Not yet specified. See the open question *Docker tools: data source and shape*.

### 2.15 Tool `list_docker_networks` (working title)

Not yet specified. See the open question *Docker tools: data source and shape*.

### 2.16 Tool `list_docker_ports` (working title)

Not yet specified. Whether the ports in use are a concept of their own, or just a field already covered by `list_docker_containers` (a container's own port mappings), is itself unconfirmed. See the open question *Docker tools: data source and shape*.

## 3. Common behaviour

This applies to every tool above; none of it is a tool of its own, so none of it is called directly by a client.

### 3.1 Failing to get an answer from OMV

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
| UC-02.AC2 | Credentials refused | OMV refuses the account Vault has, or accepts it but forbids it the call (HTTP 403) | The client calls any tool | Failed, with `omv_auth_failed`. The message says not to retry. When the login was refused it says to check the account and the secret; when the call was forbidden it says the account lacks the rights this tool needs |
| UC-02.AC3 | RPC error | OMV answers with an RPC error | The client calls any tool | Failed, with `omv_rpc_error`. The message gives the numeric code of the error, and says not to retry the same call |
| UC-02.AC4 | Unexpected answer | OMV answers with something that is not what the call expects: not JSON, or without the data the tool needs | The client calls any tool | Failed, with `omv_bad_response`. The message says the OMV version may differ from the one Vault supports |
| UC-02.AC5 | Unexpected failure | An unexpected failure occurs inside Vault | The client calls any tool | Failed, with `failed`. The message says it was not caused by the request and that retrying will not help. The cause is only in the logs |
| UC-02.AC6 | Nothing leaks | Any of the failures above, with a password, an address or an OMV error text involved | The client calls any tool | The result, and the logs, contain no password. The result contains no address, path or text sent by OMV |

### 3.2 The session with OMV and the read-only boundary

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

Vault can only make the calls of section 4, each documented as read-only. The client can never choose which OMV method is called.

**Acceptance criteria**

| ID | Name | Given | When | Then |
|---|---|---|---|---|
| UC-07.AC1 | Only the documented calls | Any tool is used | The client calls it | The only calls that reach OMV are the ones in section 4, plus the login |
| UC-07.AC2 | No tool names an OMV call | | The client reads the arguments every tool accepts | None of them is a service, a method or any other way to choose the OMV call |
| UC-07.AC3 | Tools say they are read-only | | The client lists the tools | Every tool is annotated as read-only and as not reaching outside systems, and has a title |
| UC-07.AC4 | The spec and the code agree | A call exposed by the code | The calls of section 4 are compared with those the code exposes | No call is exposed that section 4 does not document, and every call of the confirmed table is exposed. A call of the *not confirmed* table may be exposed once its tool is built |

### 3.3 Starting the server

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

## 4. RPC calls

The calls Vault can make to OMV (`POST <base>/rpc.php`). **A call is exposed only if it is listed here as read-only**: adding one means adding its row first, and its tool in section 2. A test checks that the code exposes nothing this section does not list, and every call of the confirmed table, ignoring the login. Nothing else is ever called: there is no way to pass an OMV service or method name through a tool.

**Confirmed against a real OMV (8.5.12-1):**

| RPC call | Used by | What it does | Why it is read-only |
|---|---|---|---|
| `Session.login` | none (Vault's own login) | Authenticates the account and starts the session | It changes no NAS configuration: it only creates the session of Vault |
| `System.getInformation` | `system_status` | Returns hostname, OMV version, kernel, CPU, memory and uptime | A `get` that reads the state of the machine |
| `ShareMgmt.getList` | `list_shared_folders`, `get_folder_permissions` and `get_folder_acl` (to find a folder's uuid) | Returns the shared folders, paged with a start and a limit | A `get` that reads the configured list |
| `ShareMgmt.getPrivileges` | `get_folder_permissions` | Returns, for a shared folder given by uuid, one entry per OMV user and group: `type`, `id`, `name` and `perms` (0 deny, 5 read-only, 7 read-write, or `null` when no privilege is set) | A `get` that reads the configured privileges |
| `ShareMgmt.getFileACL` (`params: {uuid, file: "/"}`) | `get_folder_acl` | Returns, for a shared folder given by uuid, the POSIX `owner` and `group` of its root and an `acl` with the base permissions (`user`, `group`, `other`, each 0 to 7) and one entry per OMV user and group in `users` and `groups`, with `perms` (0 to 7, or `null` when it has no entry) | A `get` that reads the configured ACL; does not run `setfacl` |
| `UserMgmt.enumerateAllUsers` (`params: {detail: "basic"}`) | `list_users_and_groups` | Returns every OMV user (system ones too) as a list of `{name, uid, gid, comment, dir, shell, groups, system}` | A `get` that reads the configured list |
| `UserMgmt.enumerateAllGroups` | `list_users_and_groups` | Returns every OMV group (system ones too) as a list of `{name, gid, members, system}` | A `get` that reads the configured list |
| `Services.getStatus` | `list_services` | Returns every service OMV manages as a list of `{name, title, enabled, running}`; on the test NAS: `Docker`, `cterm`, `nfs`, `rsyncd`, `samba` and `ssh` | A `get` that reads the state of the services; starts and stops none |
| `DiskMgmt.enumerateDevices` | `list_disks` | Returns every disk OMV sees as a plain list of `{devicename, canonicaldevicefile, devicefile, model, size, serialnumber, temperature, ...}` (`size` and `temperature` as text, `temperature` empty when unknown) | A `get` that reads the devices |
| `Smart.getList` (`params: {start: 0, limit: -1}`) | `list_disks` | Returns `{total, data}` with the disks that have SMART support, each with its `overallstatus` (`GOOD`, `BAD_STATUS`...) | A `get` that reads the status OMV already has; does not run a SMART test |
| `FileSystemMgmt.enumerateFilesystems` | `list_filesystems` | Returns every filesystem OMV manages, mounted or not, as a plain list of `{canonicaldevicefile, uuid, label, type, mounted, mountpoint, size, used, available, ...}` (`size` and `available` as text in bytes, `used` as human text, `-1` when not mounted) | A `get` that reads the configured list; mounts and unmounts nothing |

**Not confirmed at all yet** — the team's best reading of OMV's own source and documentation for the rest of sections 2.9 and 2.10, never tested against a server and never seen in real traffic either. A call here can change name, parameters or disappear once checked; see the open question *Remaining tool catalog*. None of the calls of this table is exposed in the code yet; when a call is confirmed (request or, better, a real response), its row moves up.

| RPC call | Used by | What it does | Why it is read-only |
|---|---|---|---|
| `Cron.getList` | `list_scheduled_tasks` | Returns the configured cron jobs and their last run | A `get` that reads the configured list |
| `Apt.getUpgradeList` | `list_pending_updates` | Returns the packages with an update available | A `get` that reads what APT already knows; does not run `apt update` |

## 5. Open questions

| # | Title | Description | Status | Priority | Comments |
|---|---|---|---|---|---|
| 1 | OMV version | Whether the services Vault calls (`Session`, `System`, `ShareMgmt`) keep the same methods, parameters and answer fields as OMV is updated. | Half-way | High | The real NAS runs OMV 8.5.12-1 (a UGREEN DXP2800, updated regularly), so Vault targets OMV 8.x. Checked against OMV 8.5.1 in a community Docker image (no systemd, no disks, no shared folders): the login answer (`status: "authenticated"`, not `authenticated: true`), the session cookie, HTTP 401 for an invalid session, HTTP 400 for a bad login and the fields of `System.getInformation` are confirmed, and Vault works against it. Also checked against the real 8.5.12-1 with an account in the group `openmediavault-admin`: both tools work and `ShareMgmt.getList` returns `reldirpath` and `comment` as assumed. Left: whether an account outside that group can call these methods (the real test was not done without the group), and how to notice that an OMV update changed an answer. |
| 2 | Remaining tool catalog | Tool 2.5 (`list_users_and_groups`) is built and confirmed against the real NAS (2026-11-24: OMV's users have `comment`, `groups` and `system` and no "disabled" flag, so the model's `disabled` is gone and `system` splits the result in two blocks). Tool 2.6 (`list_services`) is built and confirmed against the real NAS (2026-11-24: one `Services.getStatus` call answers a plain list of `{name, title, enabled, running}`); tool 2.7 (`list_disks`) is built and confirmed against the real NAS (2026-11-24: `DiskMgmt.enumerateDevices` answers a plain list and `Smart.getList` a `{total, data}` with only the disks that have SMART, each with an `overallstatus`); tool 2.8 (`list_filesystems`) is built and confirmed against the real NAS (2026-11-24: `FileSystemMgmt.enumerateFilesystems` answers a plain list; `used` is human text, not bytes, and an unmounted filesystem has `-1` in its sizes); tools 2.9 and 2.10 (`list_scheduled_tasks`, `list_pending_updates`) are specified with their input, output and acceptance criteria but not built; `get_folder_permissions` (2.3) and `get_folder_acl` (2.4) are built and confirmed against the real NAS (both real answers differed from a first guess: each lists every user and group of the NAS, with `perms` null when none is set; `getFileACL` also gives the POSIX owner, group and base permissions). What is still open: for `list_scheduled_tasks` and `list_pending_updates` even the calls themselves are unconfirmed. And a non-admin OMV account may not be able to call some of these either (see the question *OMV version*, which that part belongs to). | Half-way | High | Next: confirm each remaining call against the real NAS the way `list_users_and_groups` was (expose the call, read the real answer, fix the models and `FakeOmvServer`). One at a time, before building its tool; a call that turns out wrong only needs this table and the one tool's code fixed, not the rest of the spec. |
| 3 | Credentials secret | How the OMV account reaches the container. Settled: a Docker secret mounted as a file (`secrets/omv_credentials.json`, see `.example.json`), never an environment variable or the repository; and the file is the JSON object `{"username", "password"}` proposed in the technical spec. Still open: who creates and rotates the secret, and the file permissions for the non-root user the container runs as. | Half-way | Medium | Left to settle: who creates and rotates the secret, and the file permissions for the container's user (technical spec, section 4). |
| 4 | Docker tools: data source and shape | Whether the status of containers, images, services, volumes, networks and ports on the NAS comes from OMV (its Docker plugin, if it has an RPC for it), from the Docker socket or from Portainer. It is probably outside the RPC of OMV, and a socket gives much more access than Vault should have. Also open: whether the six working titles (2.11–2.16) should stay six narrow tools or be merged into fewer broader ones (for example one `list_docker` tool with a block per concept), and whether `list_docker_services` and `list_docker_ports` name a real, separate concept in OMV's Docker or just duplicate what `list_docker_containers` already reports (a container's own ports; Compose/Swarm services a single-host NAS is unlikely to run) — only real traffic against the NAS will tell. | Open | Medium | Decide once there is a real response to read for at least one of these; until then every tool from 2.12 on is a bare title, and 2.11 (`list_docker_containers`) stays one too until the source is picked. |
