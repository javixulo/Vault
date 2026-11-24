using System.Text.Json;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.UnitTests;

public sealed class BackgroundJobReaderTests
{
    private const string FileName = "/tmp/bgstatusq0v7mvmac7h0fz9AiSL";

    private static BackgroundJobReader Reader(FakeRpcClient omv, int limitMilliseconds = 5000) =>
        new(omv, TimeSpan.FromMilliseconds(limitMilliseconds), TimeSpan.FromMilliseconds(1));

    private static JsonElement Piece(string output, long pos, bool running, bool pending = false) =>
        FakeRpcClient.Json(JsonSerializer.Serialize(new { filename = FileName, pos, output, pendingOutput = pending, running }));

    /// <summary>The first call starts the job and answers the file name; the next ones answer the pieces in order.</summary>
    private static FakeRpcClient Job(string startAnswer, params JsonElement[] pieces)
    {
        var next = 0;
        return new FakeRpcClient
        {
            Handler = call => call.Name == "Exec.getOutput" ? pieces[Math.Min(next++, pieces.Length - 1)] : FakeRpcClient.Json(startAnswer),
        };
    }

    [Fact]
    [Trait("Criteria", "UC-26.AC1")]
    public async Task The_output_is_read_in_pieces_from_where_the_previous_one_ended_and_joined()
    {
        var omv = Job($"\"{FileName}\"", Piece("""{"total":1,""", 11, running: true), Piece("""
            "data":[]}
            """.Trim(), 18, running: false));

        var output = await Reader(omv).RunAsync(OmvRpcCall.DockerContainers, null);

        Assert.Equal(1, output.GetProperty("total").GetInt32());
        var reads = omv.Calls.Where(c => c.Call.Name == "Exec.getOutput").Select(c => c.Parameters).ToList();
        Assert.Equal([0, 11], reads.Select(p => p.GetProperty("pos").GetInt64()));
        Assert.All(reads, p => Assert.Equal(FileName, p.GetProperty("filename").GetString()));
    }

    [Fact]
    [Trait("Criteria", "UC-26.AC1")]
    public async Task Output_still_pending_when_the_job_has_stopped_is_read_before_finishing()
    {
        var omv = Job($"\"{FileName}\"", Piece("""{"total":0,""", 10, running: false, pending: true), Piece("\"data\":[]}", 20, running: false));

        var output = await Reader(omv).RunAsync(OmvRpcCall.DockerContainers, null);

        Assert.Equal(0, output.GetProperty("total").GetInt32());
        Assert.Equal(2, omv.Calls.Count(c => c.Call.Name == "Exec.getOutput"));
    }

    [Theory]
    [InlineData("\"/etc/passwd\"")]
    [InlineData("\"/tmp/bgstatus\"")]
    [InlineData("\"/tmp/bgstatus../../etc/shadow\"")]
    [InlineData("\"/tmp/bgstatusabc/def\"")]
    [InlineData("\"/tmp/otherstatusabc\"")]
    [InlineData("\"/tmp/bgstatusñ\"")]
    [InlineData("""{"file":"/tmp/bgstatusabc"}""")]
    [InlineData("null")]
    [Trait("Criteria", "UC-26.AC2")]
    public async Task Only_a_file_name_that_looks_like_a_background_job_is_read_and_nothing_else_is_asked(string startAnswer)
    {
        var omv = Job(startAnswer, Piece("{}", 2, running: false));

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => Reader(omv).RunAsync(OmvRpcCall.DockerContainers, null));

        Assert.Equal(OmvRpcErrorKind.BadResponse, error.Kind);
        Assert.DoesNotContain(omv.Calls, c => c.Call.Name == "Exec.getOutput");
    }

    [Fact]
    [Trait("Criteria", "UC-26.AC3")]
    public async Task A_job_that_does_not_finish_in_the_time_limit_is_unreachable()
    {
        var omv = Job($"\"{FileName}\"", Piece("", 0, running: true));

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => Reader(omv, limitMilliseconds: 150).RunAsync(OmvRpcCall.DockerContainers, null));

        Assert.Equal(OmvRpcErrorKind.Unreachable, error.Kind);
    }

    [Fact]
    [Trait("Criteria", "UC-02.AC5")]
    public async Task A_cancellation_by_the_caller_is_not_turned_into_an_error()
    {
        var omv = Job($"\"{FileName}\"", Piece("", 0, running: true));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader(omv).RunAsync(OmvRpcCall.DockerContainers, null, cancelled.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Error: docker is not running")]
    [Trait("Criteria", "UC-26.AC4")]
    public async Task A_job_that_ends_with_no_usable_output_is_a_bad_response(string output)
    {
        var omv = Job($"\"{FileName}\"", Piece(output, output.Length, running: false));

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => Reader(omv).RunAsync(OmvRpcCall.DockerContainers, null));

        Assert.Equal(OmvRpcErrorKind.BadResponse, error.Kind);
    }

    [Theory]
    [InlineData("""{"filename":"x","output":"{}","running":false}""")]
    [InlineData("""{"filename":"x","output":"{}","pos":2}""")]
    [Trait("Criteria", "UC-26.AC4")]
    public async Task A_piece_without_a_position_or_without_saying_whether_it_runs_is_a_bad_response(string piece)
    {
        var omv = Job($"\"{FileName}\"", FakeRpcClient.Json(piece));

        var error = await Assert.ThrowsAsync<OmvRpcException>(() => Reader(omv).RunAsync(OmvRpcCall.DockerContainers, null));

        Assert.Equal(OmvRpcErrorKind.BadResponse, error.Kind);
    }
}
