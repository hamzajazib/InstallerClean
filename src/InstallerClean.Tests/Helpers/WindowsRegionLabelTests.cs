using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

public class WindowsRegionLabelTests
{
    // The two shapes GetUserDefaultGeoName documents, letters upper-cased.
    [Theory]
    [InlineData("GB", "GB")]
    [InlineData("de", "DE")]
    [InlineData("Us", "US")]
    [InlineData("419", "419")]
    [InlineData("001", "001")]
    public void A_code_or_a_number_is_carried(string region, string expected)
        => Assert.Equal(expected, WindowsRegionLabel.For(region));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_read_is_unreadable(string? region)
        => Assert.Equal(WindowsRegionLabel.Unreadable, WindowsRegionLabel.For(region));

    // Anything else stays inside the receiver's pattern by becoming one fixed word.
    [Theory]
    [InlineData("G")]
    [InlineData("GBR")]
    [InlineData("41")]
    [InlineData("4190")]
    [InlineData("G1")]
    [InlineData("GB ")]
    [InlineData("ÉS")]
    [InlineData("１２３")]
    public void Any_other_value_is_unrecognised(string region)
        => Assert.Equal(WindowsRegionLabel.Unrecognised, WindowsRegionLabel.For(region));
}
