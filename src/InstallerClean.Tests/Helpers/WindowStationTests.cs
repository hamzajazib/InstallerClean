using InstallerClean.Interop.Native;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The command line's test for an interactive window station: not interactive only where the
/// station is read and is not visible, as <see cref="Environment.UserInteractive"/> decides.
/// </summary>
public class WindowStationTests
{
    private static readonly IntPtr Station = new(0x40);

    [Fact]
    public void A_visible_station_is_interactive()
    {
        Assert.True(WindowStation.IsInteractive(() => Station, _ => 1));
    }

    [Fact]
    public void A_station_that_is_not_visible_is_not_interactive()
    {
        Assert.False(WindowStation.IsInteractive(() => Station, _ => 0));
    }

    [Fact]
    public void Only_the_visible_bit_decides()
    {
        Assert.False(WindowStation.IsInteractive(() => Station, _ => 2));
        Assert.True(WindowStation.IsInteractive(() => Station, _ => 3));
    }

    [Fact]
    public void A_station_whose_flags_cannot_be_read_is_interactive()
    {
        Assert.True(WindowStation.IsInteractive(() => Station, _ => null));
    }

    [Fact]
    public void A_process_with_no_station_is_interactive()
    {
        Assert.True(WindowStation.IsInteractive(() => IntPtr.Zero, _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void The_answer_is_the_frameworks()
    {
        Assert.Equal(Environment.UserInteractive, WindowStation.IsInteractive());
    }
}
