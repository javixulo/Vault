using System.Text.Json.Serialization;

namespace Vault.Core.Models;

/// <summary>One reason why a tool could not do what the client asked (functional spec, section 1).</summary>
public sealed record VaultError(string Code, string Message);

/// <summary>What OpenMediaVault says about the machine. Every field is optional: OMV versions differ in what they report.</summary>
public sealed record SystemStatus(
    string? Hostname,
    string? OmvVersion,
    string? Kernel,
    string? CpuModel,
    double? CpuUsagePercent,
    long? MemoryTotalBytes,
    long? MemoryUsedBytes,
    long? UptimeSeconds);

/// <summary>Outcome of <c>system_status</c>. A failure has <c>Error</c> and is sent as a tool error (<c>isError</c>).</summary>
public sealed record SystemStatusResult(SystemStatus? Status, VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static SystemStatusResult Success(SystemStatus status) => new(status);

    public static SystemStatusResult Failure(VaultError error) => new(null, error);
}

/// <summary>A shared folder as OMV defines it.</summary>
public sealed record SharedFolder(string Name, string? Uuid, string? RelativePath, string? Description);

/// <summary>Outcome of <c>list_shared_folders</c>. <c>Message</c> is set when there is nothing to return: why, and what to do next.</summary>
public sealed record SharedFoldersResult(
    IReadOnlyList<SharedFolder> Folders,
    int Total,
    bool HasMore,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static SharedFoldersResult Found(IReadOnlyList<SharedFolder> folders, int total, bool hasMore) => new(folders, total, hasMore);

    public static SharedFoldersResult Empty(int total, string message) => new([], total, false, message);

    public static SharedFoldersResult Failure(VaultError error) => new([], 0, false, null, error);
}

/// <summary>What one user or group may do with a shared folder, as OMV's "Shared Folder Privileges" assigns it.</summary>
/// <param name="SubjectType"><c>user</c> or <c>group</c>.</param>
/// <param name="SubjectName">Name of the user or group.</param>
/// <param name="Access"><c>read_write</c>, <c>read_only</c> or <c>deny</c>.</param>
public sealed record Privilege(string SubjectType, string SubjectName, string Access);

/// <summary>Outcome of <c>get_folder_permissions</c>. <c>Message</c> is set when there is no explicit privilege.</summary>
public sealed record FolderPermissionsResult(
    string Folder,
    IReadOnlyList<Privilege> Privileges,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static FolderPermissionsResult Found(string folder, IReadOnlyList<Privilege> privileges) => new(folder, privileges);

    public static FolderPermissionsResult Empty(string folder, string message) => new(folder, [], message);

    public static FolderPermissionsResult Failure(string folder, VaultError error) => new(folder, [], null, error);
}

/// <summary>One named user or group with an entry in the extended ACL of a shared folder's root.</summary>
/// <param name="SubjectType"><c>user</c> or <c>group</c>.</param>
/// <param name="SubjectName">Name of the user or group.</param>
/// <param name="Permissions">Three characters, <c>r</c>, <c>w</c> and <c>x</c> or <c>-</c> (for example <c>rwx</c>, <c>r-x</c>).</param>
public sealed record AclEntry(string SubjectType, string SubjectName, string Permissions);

/// <summary>
/// Outcome of <c>get_folder_acl</c>: the POSIX owner, group and base permissions of the folder's root plus its named ACL entries.
/// <c>Message</c> is set when there is no named entry.
/// </summary>
public sealed record FolderAclResult(
    string Folder,
    string? Owner,
    string? Group,
    string? OwnerPermissions,
    string? GroupPermissions,
    string? OtherPermissions,
    IReadOnlyList<AclEntry> Entries,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static FolderAclResult Found(string folder, string? owner, string? group, string ownerPerms, string groupPerms, string otherPerms, IReadOnlyList<AclEntry> entries, string? message = null) =>
        new(folder, owner, group, ownerPerms, groupPerms, otherPerms, entries, message);

    public static FolderAclResult Failure(string folder, VaultError error) => new(folder, null, null, null, null, null, [], null, error);
}

/// <summary>An OMV user. <c>Uid</c> and <c>FullName</c> are null when OMV reports none.</summary>
public sealed record OmvUser(string Username, long? Uid, string? FullName, IReadOnlyList<string> Groups);

/// <summary>An OMV group and the usernames of its members. <c>Gid</c> is null when OMV reports none.</summary>
public sealed record OmvGroup(string Name, long? Gid, IReadOnlyList<string> Members);

/// <summary>The users and the groups of one side of <c>list_users_and_groups</c>.</summary>
public sealed record UsersAndGroups(IReadOnlyList<OmvUser> Users, IReadOnlyList<OmvGroup> Groups)
{
    public static UsersAndGroups None { get; } = new([], []);

    [JsonIgnore]
    public bool IsEmpty => Users.Count == 0 && Groups.Count == 0;
}

/// <summary>
/// Outcome of <c>list_users_and_groups</c>: the accounts and groups of the operating system (<c>System</c>) apart from the ones
/// created in OMV (<c>Custom</c>). <c>Message</c> is set when both are empty.
/// </summary>
public sealed record UsersAndGroupsResult(
    UsersAndGroups System,
    UsersAndGroups Custom,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static UsersAndGroupsResult Found(UsersAndGroups system, UsersAndGroups custom, string? message = null) => new(system, custom, message);

    public static UsersAndGroupsResult Failure(VaultError error) => new(UsersAndGroups.None, UsersAndGroups.None, null, error);
}

/// <summary>A service OMV manages. <c>Enabled</c> and <c>Running</c> are null when OMV does not say.</summary>
public sealed record OmvService(string Name, bool? Enabled, bool? Running);

/// <summary>Outcome of <c>list_services</c>. <c>Message</c> is set when there is nothing to return.</summary>
public sealed record ServicesResult(
    IReadOnlyList<OmvService> Services,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static ServicesResult Found(IReadOnlyList<OmvService> services) => new(services);

    public static ServicesResult Empty(string message) => new([], message);

    public static ServicesResult Failure(VaultError error) => new([], null, error);
}

public static class SmartStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Unknown = "unknown";
}

/// <summary>A disk OMV sees. <c>SmartStatus</c> is passed, failed or unknown (no SMART support); the rest is null when OMV does not say.</summary>
public sealed record Disk(string Device, string? Model, long? SizeBytes, string SmartStatus, double? TemperatureCelsius);

/// <summary>Outcome of <c>list_disks</c>. <c>Message</c> is set when there is nothing to return.</summary>
public sealed record DisksResult(
    IReadOnlyList<Disk> Disks,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static DisksResult Found(IReadOnlyList<Disk> disks) => new(disks);

    public static DisksResult Empty(string message) => new([], message);

    public static DisksResult Failure(VaultError error) => new([], null, error);
}

/// <summary>
/// A filesystem OMV manages. The label is null when it has none; the mount point and everything after it are null when it is not
/// mounted. <c>Used</c> is the text OMV shows (<c>"4.35 TiB"</c>), not bytes: OMV only gives it rounded.
/// </summary>
public sealed record Filesystem(string Device, string? Uuid, string? Type, string? Label, bool Mounted, string? MountPoint, long? SizeBytes, string? Used, long? AvailableBytes, int? UsedPercent);

/// <summary>Outcome of <c>list_filesystems</c>. <c>Message</c> is set when there is nothing to return.</summary>
public sealed record FilesystemsResult(
    IReadOnlyList<Filesystem> Filesystems,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static FilesystemsResult Found(IReadOnlyList<Filesystem> filesystems) => new(filesystems);

    public static FilesystemsResult Empty(string message) => new([], message);

    public static FilesystemsResult Failure(VaultError error) => new([], null, error);
}

/// <summary>
/// A cron job OMV has configured. <c>Name</c> is its comment, or its command when it has none; <c>Schedule</c> is the cron
/// expression (<c>"*/5 * * * *"</c>, or <c>"@daily"</c>) and is null when Vault cannot build it. OMV does not record when a job last ran.
/// </summary>
public sealed record ScheduledTask(string Name, string? Schedule, bool? Enabled);

/// <summary>Outcome of <c>list_scheduled_tasks</c>. <c>Message</c> is set when there is nothing to return.</summary>
public sealed record ScheduledTasksResult(
    IReadOnlyList<ScheduledTask> Tasks,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static ScheduledTasksResult Found(IReadOnlyList<ScheduledTask> tasks) => new(tasks);

    public static ScheduledTasksResult Empty(string message) => new([], message);

    public static ScheduledTasksResult Failure(VaultError error) => new([], null, error);
}

/// <summary>
/// A package with an update available. <c>CurrentVersion</c> is the installed version, null for a package that is new (OMV gives none);
/// <c>CandidateVersion</c> is the one available.
/// </summary>
public sealed record PendingUpdate(string Package, string? CurrentVersion, string? CandidateVersion);

/// <summary>
/// Outcome of <c>list_pending_updates</c>. <c>Count</c> is always reported; <c>Updates</c> only when the client asked for the detail.
/// <c>Message</c> is set when nothing is pending.
/// </summary>
public sealed record PendingUpdatesResult(
    int Count,
    IReadOnlyList<PendingUpdate> Updates,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static PendingUpdatesResult Found(int count, IReadOnlyList<PendingUpdate> updates) => new(count, updates);

    public static PendingUpdatesResult UpToDate(string message) => new(0, [], message);

    public static PendingUpdatesResult Failure(VaultError error) => new(0, [], null, error);
}

/// <summary>
/// A Docker container as OMV's Compose plugin reports it (functional spec, section 1). <c>Health</c> is <c>healthy</c>, <c>unhealthy</c>,
/// <c>starting</c> or null; <c>CreatedAt</c> is null when OMV gives none or it cannot be read; the Compose fields are null when the
/// container is not from Compose or the match is ambiguous.
/// </summary>
public sealed record DockerContainer(
    string Name,
    string? Image,
    string? State,
    string? Status,
    string? Health,
    DateTimeOffset? CreatedAt,
    string? Network,
    string? ComposeProject,
    string? ComposeService);

/// <summary>Outcome of <c>list_docker_containers</c>. <c>Message</c> is set when there are no containers.</summary>
public sealed record DockerContainersResult(
    IReadOnlyList<DockerContainer> Containers,
    string? Message = null,
    VaultError? Error = null)
{
    /// <summary>Derived, not part of the contract: the call worked when it has no error.</summary>
    [JsonIgnore]
    public bool Ok => Error is null;

    public static DockerContainersResult Found(IReadOnlyList<DockerContainer> containers) => new(containers);

    public static DockerContainersResult Empty(string message) => new([], message);

    public static DockerContainersResult Failure(VaultError error) => new([], null, error);
}

public static class ErrorCodes
{
    public const string InvalidArguments = "invalid_arguments";
    public const string NotFound = "not_found";
    public const string OmvUnreachable = "omv_unreachable";
    public const string OmvAuthFailed = "omv_auth_failed";
    public const string OmvRpcError = "omv_rpc_error";
    public const string OmvBadResponse = "omv_bad_response";
    public const string Failed = "failed";
}
