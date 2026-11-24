using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vault.Core.Models;
using Vault.Core.Rpc;

namespace Vault.Core.ScheduledTasks;

/// <summary>
/// UC-16 and UC-17: lists the cron jobs OMV has configured. One call, <c>Cron.getList</c> (<c>{start: 0, limit: -1, type: [...]}</c>;
/// <c>type</c> is required and must be a list), which answers <c>{total, data}</c>. Checked against a real OMV 8.5.12 that has no
/// jobs (<c>{total: 0, data: []}</c>); the fields of an entry (<c>comment</c>, <c>command</c>, <c>enable</c>, <c>execution</c>,
/// <c>minute</c>, <c>hour</c>, <c>dayofmonth</c>, <c>month</c>, <c>dayofweek</c> and the <c>everyn*</c> flags) are OMV's documented
/// ones and are not confirmed against a real entry yet. OMV does not record when a job last ran, so Vault does not report it.
/// The mapping stays tolerant: only an answer that is not a list, or an entry with neither comment nor command, is a bad response.
/// </summary>
public sealed class ScheduledTasksReader(IOmvRpcClient omv, ILogger<ScheduledTasksReader> logger)
{
    private const string What = "Listing the scheduled tasks";

    private static readonly string[] Types = ["userdefined", "reboot", "shutdown"];

    public async Task<ScheduledTasksResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.Source.StartActivity("list_scheduled_tasks");
        ScheduledTasksResult result;
        try
        {
            var response = await omv.CallAsync(OmvRpcCall.ScheduledTasks, new { start = 0, limit = -1, type = Types }, cancellationToken);
            result = Map(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OmvRpcException error)
        {
            LogRpcFailed(error);
            result = ScheduledTasksResult.Failure(Messages.ForRpc(error, What));
        }
        catch (Exception error)
        {
            LogFailed(error);
            result = ScheduledTasksResult.Failure(new VaultError(ErrorCodes.Failed, Messages.Failed(What)));
            activity.Fail(error);
        }

        activity?.SetTag(Telemetry.ScheduledTasksCount, result.Tasks.Count);
        if (result.Error is { } failure)
        {
            activity.SetErrorTypes([failure.Code]);
        }

        return result;
    }

    public static ScheduledTasksResult Map(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("data", out var data))
        {
            response = data;
        }

        if (response.ValueKind != JsonValueKind.Array)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The answer for the scheduled tasks is not a list.");
        }

        var tasks = response.EnumerateArray()
            .Select(entry =>
            {
                var name = NullIfBlank(entry.ReadString("comment")) ?? NullIfBlank(entry.ReadString("command"))
                    ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A scheduled task entry has neither a comment nor a command.");
                return new ScheduledTask(name, Schedule(entry), entry.ReadBool("enable"));
            })
            .ToList();

        return tasks.Count == 0 ? ScheduledTasksResult.Empty(Messages.NoScheduledTasks) : ScheduledTasksResult.Found(tasks);
    }

    /// <summary>
    /// The cron expression. <c>execution</c> is <c>exactly</c> (five fields; with the <c>everyn*</c> flag a field is <c>*/n</c>) or one of
    /// <c>hourly</c>, <c>daily</c>, <c>weekly</c>, <c>monthly</c>, <c>yearly</c> and <c>reboot</c>, which are cron's own <c>@</c> shortcuts.
    /// Null when OMV does not say enough to build it.
    /// </summary>
    private static string? Schedule(JsonElement entry)
    {
        var execution = NullIfBlank(entry.ReadString("execution"))?.Trim().ToLowerInvariant();
        switch (execution)
        {
            case "hourly" or "daily" or "weekly" or "monthly" or "yearly" or "reboot":
                return "@" + execution;
            case "exactly" or null:
                var minute = Field(entry, "minute", "everynminute");
                var hour = Field(entry, "hour", "everynhour");
                var dayOfMonth = Field(entry, "dayofmonth", "everyndayofmonth");
                var month = NullIfBlank(entry.ReadString("month"));
                var dayOfWeek = NullIfBlank(entry.ReadString("dayofweek"));
                return minute is null || hour is null || dayOfMonth is null || month is null || dayOfWeek is null
                    ? null
                    : $"{minute} {hour} {dayOfMonth} {month} {dayOfWeek}";
            default:
                return null;
        }
    }

    private static string? Field(JsonElement entry, string name, string everyNFlag)
    {
        var value = NullIfBlank(entry.ReadString(name));
        return value is not null && entry.ReadBool(everyNFlag) == true ? $"*/{value}" : value;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

#pragma warning disable CA1848, CA1873, CA1727
    private void LogRpcFailed(OmvRpcException error) =>
        logger.LogError(error, "Listing the scheduled tasks failed ({error.type})", error.Kind);

    private void LogFailed(Exception error) =>
        logger.LogError(error, "Listing the scheduled tasks failed unexpectedly");
#pragma warning restore CA1848, CA1873, CA1727
}
