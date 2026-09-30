using InstallerClean.Helpers;
using InstallerClean.Models;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The line the scan's overlay, the splash and the Move or Delete heading each show a wait
/// on, and put back when the wait ends.
/// </summary>
public class WaitLineTests
{
    private const string Before = "Checking remaining files...";

    private static (WaitLine Wait, Func<string> Line) OnALine()
    {
        var line = Before;
        return (new WaitLine(() => line, text => line = text), () => line);
    }

    [Fact]
    public void A_wait_takes_the_line_and_its_end_puts_the_line_back()
    {
        var (wait, line) = OnALine();

        wait.Show(ScanProgressUpdate.Waiting("D:"));
        Assert.Equal(DisplayHelpers.WaitingFor("D:"), line());

        wait.Show(ScanProgressUpdate.Waiting(null));
        Assert.Equal(Before, line());
    }

    [Fact]
    public void A_second_wait_before_the_first_ends_puts_back_what_was_there_before_either()
    {
        var (wait, line) = OnALine();

        wait.Show(ScanProgressUpdate.Waiting("D:"));
        wait.Show(ScanProgressUpdate.Waiting(@"\\fileserver\apps"));
        Assert.Equal(DisplayHelpers.WaitingFor(@"\\fileserver\apps"), line());

        wait.Show(ScanProgressUpdate.Waiting(null));
        Assert.Equal(Before, line());
    }

    [Fact]
    public void An_end_with_no_wait_shown_leaves_the_line_alone()
    {
        var (wait, line) = OnALine();

        wait.Show(ScanProgressUpdate.Waiting(null));

        Assert.Equal(Before, line());
    }

    [Fact]
    public void A_wait_forgotten_leaves_the_line_to_whatever_replaced_it()
    {
        var line = Before;
        var wait = new WaitLine(() => line, text => line = text);

        wait.Show(ScanProgressUpdate.Waiting("D:"));
        wait.Forget();
        line = "Scan cancelled.";
        wait.Show(ScanProgressUpdate.Waiting(null));

        Assert.Equal("Scan cancelled.", line);
    }
}
