using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.Users;

/// <summary>
/// UC-13: lists the OMV users and groups (<c>UserMgmt.enumerateAllUsers</c> and <c>UserMgmt.enumerateAllGroups</c>). Checked against a
/// real OMV 8.5.12: a user is <c>{name, uid, gid, comment, dir, shell, groups, system}</c> and a group <c>{name, gid, members, system}</c>.
/// The mapping stays tolerant: a field Vault does not find is empty, and only an entry without a name is a bad response. The groups
/// of a user are the ones the user entry lists plus the ones that list the user as a member.
/// </summary>
public sealed class UsersAndGroupsReader(IOmvRpcClient omv, ILogger<UsersAndGroupsReader> logger)
{
    private const string What = "Listing the users and groups";

    public async Task<UsersAndGroupsResult> ReadAsync(string? name, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_users_and_groups");
        UsersAndGroupsResult result;
        try
        {
            var users = await omv.CallAsync(OmvRpcCall.Users, new { detail = "basic" }, cancellationToken);
            var groups = await omv.CallAsync(OmvRpcCall.Groups, null, cancellationToken);
            result = Map(users, groups, name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = UsersAndGroupsResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = UsersAndGroupsResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.UsersCount, result.System.Users.Count + result.Custom.Users.Count);
        activity?.SetTag(Telemetry.GroupsCount, result.System.Groups.Count + result.Custom.Groups.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    /// <summary>
    /// Maps both answers, splits the entries into the operating system's (<c>system</c> true) and the rest (false, or not reported),
    /// and keeps only the ones that match <paramref name="name"/> when it is given (see <see cref="NameFilter"/>). The groups of a user
    /// are worked out before filtering, so a filtered user still lists all its groups.
    /// </summary>
    public static UsersAndGroupsResult Map(JsonElement usersResponse, JsonElement groupsResponse, string? name = null)
    {
        var filter = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        bool Matches(string candidate) => filter is null || NameFilter.Matches(candidate, filter);

        var groups = Entries(groupsResponse, "groups")
            .Select(entry => (Name: NameOf(entry, "group"), Gid: entry.ReadLong("gid"), System: entry.ReadBool("system") == true, Members: Names(entry, "members")))
            .ToList();

        var users = Entries(usersResponse, "users")
            .Select(entry =>
            {
                var username = NameOf(entry, "user");
                var own = entry.ValueKind == JsonValueKind.Object ? Names(entry, "groups") : [];
                var memberOf = groups.Where(g => g.Members.Contains(username, StringComparer.Ordinal)).Select(g => g.Name);
                return (
                    System: entry.ReadBool("system") == true,
                    User: new OmvUser(
                        username,
                        entry.ReadLong("uid"),
                        entry.ReadString("comment") is { } text && !string.IsNullOrWhiteSpace(text) ? text : null,
                        [.. own.Concat(memberOf).Distinct(StringComparer.Ordinal)]));
            })
            .ToList();

        UsersAndGroups Side(bool system) => new(
            [.. users.Where(u => u.System == system && Matches(u.User.Username)).Select(u => u.User)],
            [.. groups.Where(g => g.System == system && Matches(g.Name)).Select(g => new OmvGroup(g.Name, g.Gid, g.Members))]);

        var (systemSide, customSide) = (Side(true), Side(false));
        return UsersAndGroupsResult.Found(
            systemSide,
            customSide,
            systemSide.IsEmpty && customSide.IsEmpty ? Messages.NoUsersOrGroups(filter) : null);
    }

    /// <summary>The items of an answer: a bare array, or an object with a <c>data</c> array. Anything else is a bad response.</summary>
    private static JsonElement.ArrayEnumerator Entries(JsonElement response, string what)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("data", out var data))
        {
            response = data;
        }

        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"The answer for the {what} is not a list.");
        }

        return response.EnumerateArray();
    }

    /// <summary>An entry is an object with a <c>name</c>, or just the name as text. One without a name is a bad response, not a skipped line.</summary>
    private static string NameOf(JsonElement entry, string what)
    {
        var name = entry.ValueKind == JsonValueKind.String ? entry.GetString() : entry.ReadString("name");
        return string.IsNullOrWhiteSpace(name)
            ? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, $"A {what} entry has no name.")
            : name;
    }

    private static List<string> Names(JsonElement entry, string property) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String).Select(i => i.GetString()!).Where(s => s.Length > 0)]
            : [];

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the users and groups failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the users and groups failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
