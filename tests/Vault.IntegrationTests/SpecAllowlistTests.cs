using System.Text.RegularExpressions;
using Vault.Core.Rpc;
using Xunit;

namespace Vault.IntegrationTests;

/// <summary>
/// The rule of CLAUDE.md made executable: an RPC call can be exposed only if the functional spec documents it as read-only.
/// The spec is copied next to the tests (see the csproj), so this runs in CI without a checkout.
/// </summary>
public sealed partial class SpecAllowlistTests
{
    [GeneratedRegex(@"^#{2,3} .*RPC calls.*$", RegexOptions.Multiline)]
    private static partial Regex RpcCallsHeading();

    [GeneratedRegex(@"^#{1,3} .+$", RegexOptions.Multiline)]
    private static partial Regex AnyHeading();

    [GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z]+\.[A-Za-z]+)`[^|]*\|", RegexOptions.Multiline)]
    private static partial Regex RpcRow();

    [Fact]
    [Trait("Criteria", "UC-07.AC4")]
    public void The_RPC_calls_the_spec_documents_are_exactly_the_calls_the_code_exposes()
    {
        var spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "docs", "functional_spec.md"));
        var heading = RpcCallsHeading().Match(spec);
        Assert.True(heading.Success, "The functional spec has no section titled 'RPC calls'.");
        var section = spec[(heading.Index + heading.Length)..];
        var next = AnyHeading().Match(section);
        if (next.Success)
        {
            section = section[..next.Index];
        }

        // The section has a "Confirmed" table and then the tables of calls not confirmed against a real answer yet (first the
        // ones whose request was seen in OMV's own traffic, then the plain guesses); those list calls of tools not built yet.
        var split = section.IndexOf("Request seen in the real OMV's own network traffic", StringComparison.Ordinal);
        Assert.True(split > 0, "The RPC calls section no longer separates the confirmed calls from the ones not confirmed yet.");
        string[] Rows(string text) => [.. RpcRow().Matches(text).Select(m => m.Groups["name"].Value).Where(n => n != "Session.login")];
        var confirmed = Rows(section[..split]);
        var unconfirmed = Rows(section[split..]);
        var exposed = OmvRpcCall.Exposed.Select(c => c.Name).Order().ToList();

        // Nothing is exposed that the spec does not document, and every confirmed call is exposed. A call of the second
        // table may be exposed once its tool is built (and stay there until it is checked against a real OMV).
        Assert.Empty(exposed.Except(confirmed.Concat(unconfirmed)));
        Assert.Empty(confirmed.Except(exposed));
    }
}
