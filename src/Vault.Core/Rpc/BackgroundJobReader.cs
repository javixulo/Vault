using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vault.Core.Rpc;

/// <summary>
/// UC-26: runs an OMV call that starts a background job and returns the JSON the job wrote. OMV answers such a call with the name
/// of a temporary file (<c>/tmp/bgstatus...</c>); <c>Exec.getOutput</c> then returns that file in pieces
/// (<c>{filename, pos, output, pendingOutput, running}</c>), checked against a real OMV 8.5.12.
/// This is the only place that uses <c>Exec.getOutput</c>, which reads a file by name: the name is never free, it is the one OMV
/// answered to the call made just before, and only if it matches <see cref="FileNamePattern"/>. The time limit covers each job as a whole.
/// </summary>
public sealed partial class BackgroundJobReader(IOmvRpcClient omv, TimeSpan timeLimit, TimeSpan? pollInterval = null)
{
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);

    [GeneratedRegex("^/tmp/bgstatus[A-Za-z0-9]+$", RegexOptions.CultureInvariant)]
    public static partial Regex FileNamePattern();

    /// <summary>Starts the job, reads it to the end and returns its output as JSON. Throws <see cref="OmvRpcException"/> when OMV does not answer as UC-26 expects.</summary>
    public async Task<JsonElement> RunAsync(OmvRpcCall start, object? parameters, CancellationToken cancellationToken = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeLimit);
        try
        {
            var answer = await omv.CallAsync(start, parameters, limit.Token);
            var fileName = answer.ValueKind == JsonValueKind.String ? answer.GetString() : null;
            if (fileName is null || !FileNamePattern().IsMatch(fileName))
            {
                throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The call did not answer with the name of a background job file.");
            }

            return Parse(await ReadAsync(fileName, limit.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OmvRpcException(OmvRpcErrorKind.Unreachable, "The background job did not finish within the time limit.");
        }
    }

    private async Task<string> ReadAsync(string fileName, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        long position = 0;
        while (true)
        {
            var piece = await omv.CallAsync(OmvRpcCall.BackgroundOutput, new { filename = fileName, pos = position }, cancellationToken);
            output.Append(piece.ReadString("output"));
            position = piece.ReadLong("pos") ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A piece of a background job has no position.");
            var running = piece.ReadBool("running") ?? throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "A piece of a background job does not say whether it is running.");
            if (!running && piece.ReadBool("pendingOutput") != true)
            {
                return output.ToString();
            }

            await Task.Delay(_pollInterval, cancellationToken);
        }
    }

    private static JsonElement Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The background job ended with no output.");
        }

        try
        {
            using var document = JsonDocument.Parse(output);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new OmvRpcException(OmvRpcErrorKind.BadResponse, "The output of the background job is not JSON.");
        }
    }
}
