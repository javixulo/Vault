using System.Collections.Concurrent;
using System.Diagnostics;
using Xunit;

namespace Vault.ContractTests;

/// <summary>
/// Trace context (technical spec, section 3): a client that sends <c>traceparent</c> gets its trace continued by the
/// server, and a client that sends none still works, in a trace of its own. Spans are read from the real server in process.
/// </summary>
public sealed class TraceContextContractTests : IAsyncLifetime
{
    private readonly ConcurrentBag<Activity> _spans = [];
    private ActivityListener _listener = null!;
    private VaultTestServer _server = null!;

    public ValueTask InitializeAsync()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Vault",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
        _server = VaultTestServer.Create();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task The_spans_of_a_call_continue_the_trace_the_client_sent()
    {
        var traceId = ActivityTraceId.CreateRandom();
        _server.Http.DefaultRequestHeaders.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        await _server.CallAsync("system_status");

        Assert.Contains(_spans, span => span.OperationName == "system_status" && span.TraceId == traceId);
    }

    [Fact]
    public async Task Without_traceparent_the_call_works_and_starts_its_own_trace()
    {
        var result = await _server.CallAsync("system_status");

        // The listener is global and other contract tests call tools in parallel, so this checks that a span exists, not that it is the only one.
        Assert.NotNull(result.StructuredContent);
        Assert.Contains(_spans, s => s.OperationName == "system_status" && s.TraceId != default);
    }
}
