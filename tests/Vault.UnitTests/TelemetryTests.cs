using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Vault.Core;
using Vault.Core.Rpc;
using Vault.Core.SystemInfo;
using Xunit;

namespace Vault.UnitTests;

public sealed class TelemetryTests
{
    private static readonly ActivitySource TestSource = new("Vault.UnitTests");

    [Fact]
    public async Task A_failed_call_has_the_error_code_on_its_span_but_is_not_a_failed_step()
    {
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is Telemetry.SourceName or "Vault.UnitTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var reader = new SystemStatusReader(FakeRpcClient.Failing(new OmvRpcException(OmvRpcErrorKind.Unreachable, "down")), NullLogger<SystemStatusReader>.Instance);

        // The listener is global and other tests run in parallel: only the spans of this test's own trace count.
        using var root = TestSource.StartActivity("test", ActivityKind.Internal, parentContext: default)!;
        await reader.ReadAsync();

        var span = Assert.Single(spans, s => s.OperationName == "system_status" && s.TraceId == root.TraceId);
        Assert.Equal(["omv_unreachable"], (string[])span.GetTagItem(Telemetry.ErrorType)!);
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
    }
}
