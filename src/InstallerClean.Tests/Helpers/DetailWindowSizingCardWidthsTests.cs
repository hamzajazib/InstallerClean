using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The widths a dialog's card is given: its widths at 100% text scale multiplied by
/// the text scale, and neither wider than the widest the window may be on its work
/// area. The minimum is held to that limit as well as the maximum, because WPF lets
/// MinWidth win over MaxWidth and over the room the card is given.
/// </summary>
public class DetailWindowSizingCardWidthsTests
{
    [Theory]
    [InlineData(1.0, 440, 520)]
    [InlineData(1.5, 660, 780)]
    [InlineData(2.25, 990, 1170)]
    public void A_work_area_wide_enough_gives_the_scaled_widths(double factor, double minimum, double maximum)
    {
        Assert.Equal((minimum, maximum), DetailWindowSizing.CardWidths(440, 520, factor, limit: 3816));
    }

    [Fact]
    public void A_work_area_narrower_than_both_gives_the_limit_for_both()
    {
        // 225% text on a work area 1097 wide, less the 24 kept from its edges.
        Assert.Equal((1073.0, 1073.0), DetailWindowSizing.CardWidths(480, 520, 2.25, limit: 1073));
    }

    [Fact]
    public void A_work_area_between_the_two_holds_only_the_maximum()
    {
        Assert.Equal((990.0, 1073.0), DetailWindowSizing.CardWidths(440, 520, 2.25, limit: 1073));
    }

    [Fact]
    public void A_card_over_a_window_narrower_than_its_minimum_takes_the_window_less_its_margins()
    {
        // The completion card at 225% text in a window 700 wide, keeping 24 each side.
        Assert.Equal((652.0, 652.0),
            DetailWindowSizing.CardWidths(340, 520, 2.25, DetailWindowSizing.RoomLimit(700, 48)));
    }

    [Fact]
    public void A_room_narrower_than_the_margins_gives_no_width_rather_than_less()
    {
        Assert.Equal(0.0, DetailWindowSizing.RoomLimit(40, 48));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.25)]
    public void The_minimum_never_comes_out_above_the_maximum(double factor)
    {
        for (var limit = 100.0; limit <= 2000; limit += 50)
        {
            var (minimum, maximum) = DetailWindowSizing.CardWidths(440, 520, factor, limit);
            Assert.True(minimum <= maximum, $"At {factor:P0} and a limit of {limit}: {minimum} over {maximum}.");
            Assert.True(maximum <= limit, $"At {factor:P0} and a limit of {limit}: {maximum} over the limit.");
        }
    }
}
