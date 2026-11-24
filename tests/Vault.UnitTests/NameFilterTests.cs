using Vault.Core.Users;
using Xunit;

namespace Vault.UnitTests;

public sealed class NameFilterTests
{
    [Theory]
    [InlineData("grupo-fotos", "FOTO", true)]
    [InlineData("grupo-fotos", "grupo", true)]
    [InlineData("grupo-fotos", "media", false)]
    [InlineData("axb", "a.b", false)]
    [InlineData("grupo-fotos", "grupo?fotos", false)]
    [Trait("Criteria", "UC-13.AC5")]
    public void Without_a_star_the_filter_is_a_piece_of_the_name_ignoring_case(string name, string filter, bool expected) =>
        Assert.Equal(expected, NameFilter.Matches(name, filter));

    [Theory]
    [InlineData("grupo-fotos", "grupo-*", true)]
    [InlineData("mi-grupo-fotos", "grupo-*", false)]
    [InlineData("grupo-fotos", "*fotos", true)]
    [InlineData("grupo-fotos", "*foto", false)]
    [InlineData("grupo-fotos", "*FOTO*", true)]
    [InlineData("grupo-fotos", "*", true)]
    [InlineData("grupo-fotos", "g*s", true)]
    [InlineData("grupo-fotos", "g*o*s", true)]
    [InlineData("grupo-fotos", "*o-*s", true)]
    [InlineData("grupo-fotos", "grupo-fotos*", true)]
    [InlineData("grupo-fotos", "grupo-fotos*s", false)]
    [InlineData("grupo-fotos", "fotos*grupo", false)]
    [InlineData("aa", "a*a", true)]
    [InlineData("a", "a*a", false)]
    [InlineData("grupo-fotos", "grupo.*", false)]
    [InlineData("grupo.fotos", "grupo.*", true)]
    [Trait("Criteria", "UC-13.AC7")]
    public void With_a_star_the_whole_name_must_match_and_star_is_any_run_of_characters(string name, string filter, bool expected) =>
        Assert.Equal(expected, NameFilter.Matches(name, filter));

    [Fact]
    [Trait("Criteria", "UC-13.AC7")]
    public void A_filter_made_of_many_stars_does_not_take_long()
    {
        var name = new string('a', 5000);
        var filter = string.Concat(Enumerable.Repeat("a*", 500)) + "b";

        Assert.False(NameFilter.Matches(name, filter));
    }
}
