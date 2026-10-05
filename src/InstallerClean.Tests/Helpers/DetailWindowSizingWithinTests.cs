using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The size a Details window opens at: its preferred size, raised to its minimum, and
/// never larger than the widest or tallest it may be on its work area. Where the work
/// area is smaller than the minimum, the work area wins.
/// </summary>
public class DetailWindowSizingWithinTests
{
    [Fact]
    public void A_preferred_size_inside_the_limit_is_kept()
    {
        Assert.Equal(950.0, DetailWindowSizing.Within(950, minimum: 700, limit: 1896));
    }

    [Fact]
    public void A_preferred_size_over_the_limit_gives_the_limit()
    {
        // 225% text on a work area 1366 wide, less the 24 kept from its edges.
        Assert.Equal(1342.0, DetailWindowSizing.Within(950 * 2.25, minimum: 700, limit: 1342));
    }

    [Fact]
    public void A_preferred_size_under_the_minimum_gives_the_minimum()
    {
        Assert.Equal(380.0, DetailWindowSizing.Within(300, minimum: 380, limit: 1000));
    }

    [Fact]
    public void A_limit_under_the_minimum_gives_the_limit()
    {
        // A work area 696 wide, less 24, under the Registered window's MinWidth of 700.
        Assert.Equal(672.0, DetailWindowSizing.Within(950, minimum: 700, limit: 672));
    }

    [Theory]
    [InlineData(1000, 680)]
    [InlineData(950, 700)]
    [InlineData(620, 380)]
    [InlineData(770, 500)]
    public void The_size_never_comes_out_above_the_limit(double preferred, double minimum)
    {
        foreach (var factor in new[] { 1.0, 1.5, 2.25 })
        {
            for (var limit = 100.0; limit <= 3000; limit += 25)
            {
                var size = DetailWindowSizing.Within(preferred * factor, minimum, limit);
                Assert.True(size <= limit, $"{preferred} at {factor:P0} and a limit of {limit}: {size}.");
            }
        }
    }
}
