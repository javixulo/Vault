using System.ComponentModel;
using ModelContextProtocol.Server;
using Vault.Core.Models;
using Vault.Core.ScheduledTasks;

namespace Vault.Mcp.Tools;

[McpServerToolType]
public sealed class ListScheduledTasksTool(ScheduledTasksReader reader)
{
    [McpServerTool(Name = "list_scheduled_tasks", Title = "List scheduled tasks", UseStructuredContent = true, ReadOnly = true, OpenWorld = false)]
    [Description(
        "Lists the scheduled tasks (cron jobs) OpenMediaVault (OMV) has configured: name (the comment OMV stores, or the command when there is none), " +
        "schedule (a cron expression such as '0 3 * * *', or '@daily', '@reboot') and whether it is enabled. " +
        "Use it to check that a backup or maintenance job exists and is enabled. " +
        "OMV does not record when a job last ran or whether it succeeded, so this tool cannot say that. " +
        "schedule is null when OMV does not give enough to build it. An empty list is normal when no job is configured. " +
        "Read-only: it never runs, creates or changes a job. " +
        "Takes no arguments.")]
    public Task<ScheduledTasksResult> ListScheduledTasks(CancellationToken cancellationToken = default) =>
        reader.ReadAsync(cancellationToken);
}
