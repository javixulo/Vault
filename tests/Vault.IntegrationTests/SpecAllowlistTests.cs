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

    [GeneratedRegex(@"^\|\s*`(?<name>[A-Za-z]+\.[A-Za-z]+)`\s*\|", RegexOptions.Multiline)]
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

        var documented = RpcRow().Matches(section).Select(m => m.Groups["name"].Value).Where(n => n != "Session.login").Order().ToList();
        var exposed = OmvRpcCall.Exposed.Select(c => c.Name).Order().ToList();

        Assert.Equal(exposed, documented);
    }
}
