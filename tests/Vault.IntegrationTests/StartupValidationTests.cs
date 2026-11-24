using Vault.Mcp;
using Vault.Mcp.Startup;
using Xunit;

namespace Vault.IntegrationTests;

/// <summary>What the server checks before it starts (technical spec, section 4), with real files.</summary>
public sealed class StartupValidationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vault-startup-" + Guid.NewGuid().ToString("N"));

    public StartupValidationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Password(string content)
    {
        var path = Path.Combine(_folder, "omv_password");
        File.WriteAllText(path, content);
        return path;
    }

    private VaultOptions Options(string? url = "https://nas.local", string? file = null, string user = "reader") => new()
    {
        OmvBaseUrl = url ?? "",
        OmvUsername = user,
        OmvPasswordFile = file ?? Password("p"),
    };

    [Fact]
    public void A_complete_configuration_has_no_problems()
    {
        Assert.Empty(StartupValidation.Validate(Options()));
    }

    [Theory]
    [InlineData("", "not set")]
    [InlineData("nas.local", "not an absolute http or https URL")]
    [Trait("Criteria", "UC-09.AC1, UC-09.AC2")]
    public void A_missing_or_invalid_address_stops_the_start_and_says_how_to_fix_it(string url, string expected)
    {
        var problem = Assert.Single(StartupValidation.Validate(Options(url)));

        Assert.Contains(expected, problem, StringComparison.Ordinal);
        Assert.Contains("Vault__OmvBaseUrl", problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC3")]
    public void A_missing_password_file_stops_the_start_and_says_how_to_fix_it()
    {
        var problem = Assert.Single(StartupValidation.Validate(Options(file: Path.Combine(_folder, "nope"))));

        Assert.Contains("cannot be read", problem, StringComparison.Ordinal);
        Assert.Contains("Docker secret", problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC4")]
    public void An_empty_password_file_stops_the_start_and_says_how_to_fix_it()
    {
        var problem = Assert.Single(StartupValidation.Validate(Options(file: Password(""))));

        Assert.Contains("is empty", problem, StringComparison.Ordinal);
        Assert.Contains("password file", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Criteria", "UC-09.AC7")]
    public void A_missing_user_name_stops_the_start_and_says_how_to_fix_it(string user)
    {
        var problem = Assert.Single(StartupValidation.Validate(Options(user: user)));

        Assert.Contains("Vault__OmvUsername", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    [Trait("Criteria", "UC-09.AC6")]
    public void A_time_limit_out_of_range_stops_the_start_and_says_the_range(int seconds)
    {
        var options = Options();
        options.TimeoutSeconds = seconds;

        var problem = Assert.Single(StartupValidation.Validate(options));

        Assert.Contains("between 1 and 120", problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteria", "UC-09.AC5")]
    public void Every_problem_is_reported_at_once()
    {
        var options = Options(url: "", file: Path.Combine(_folder, "nope"));

        Assert.Equal(2, StartupValidation.Validate(options).Count);
    }
}
