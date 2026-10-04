using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The regions where the report box on the PC's first card starts unticked, and the
/// rule that reads Windows' answer against them.
/// </summary>
public class ReportBoxRegionsTests
{
    private static readonly string[] EuropeanUnion =
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE",
        "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
    };

    private static readonly string[] RestOfTheEea = { "IS", "LI", "NO" };

    private static readonly string[] PartsWithCodesOfTheirOwn = { "GF", "GP", "MQ", "RE", "YT", "MF", "AX" };

    [Fact]
    public void The_list_is_the_27_member_states_the_rest_of_the_EEA_and_the_parts_with_codes_of_their_own()
    {
        Assert.Equal(27, EuropeanUnion.Length);
        var expected = EuropeanUnion.Concat(RestOfTheEea).Concat(PartsWithCodesOfTheirOwn)
            .OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(expected, ReportBoxRegions.StartUnticked.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_region_in_the_list_starts_the_box_unticked()
    {
        foreach (var region in ReportBoxRegions.StartUnticked)
            Assert.False(ReportBoxRegions.StartsTicked(region), region);
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("US")]
    [InlineData("CH")]
    [InlineData("TR")]
    [InlineData("JP")]
    public void A_region_outside_the_list_starts_the_box_ticked(string region) =>
        Assert.True(ReportBoxRegions.StartsTicked(region));

    [Theory]
    [InlineData("de")]
    [InlineData("419")]
    [InlineData("001")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("G")]
    [InlineData("GBR")]
    [InlineData("G1")]
    public void Anything_but_a_two_letter_code_outside_the_list_starts_the_box_unticked(string? region) =>
        Assert.False(ReportBoxRegions.StartsTicked(region));
}
