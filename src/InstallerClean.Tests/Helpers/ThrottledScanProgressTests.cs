using InstallerClean.Helpers;
using InstallerClean.Models;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What the throttle between the scan and a window lets through. Every test here runs
/// inside one interval of an hour, so only the updates the throttle passes whatever the
/// interval get through after the first ticker.
/// </summary>
public class ThrottledScanProgressTests
{
    private sealed class Collect(List<ScanProgressUpdate> into) : IProgress<ScanProgressUpdate>
    {
        public void Report(ScanProgressUpdate value) => into.Add(value);
    }

    private static readonly TimeSpan AnHour = TimeSpan.FromHours(1);

    private static ScanProgressUpdate Ticker(int position) =>
        new($"{position} of 10", IsMilestone: false, Position: position, Total: 10);

    [Fact]
    public void A_ticker_inside_the_interval_is_held_back()
    {
        var passed = new List<ScanProgressUpdate>();
        var throttle = new ThrottledScanProgress(new Collect(passed), AnHour);

        throttle.Report(Ticker(1));
        throttle.Report(Ticker(2));

        Assert.Equal(new[] { Ticker(1) }, passed);
    }

    [Fact]
    public void A_wait_and_its_end_pass_inside_the_interval()
    {
        var passed = new List<ScanProgressUpdate>();
        var throttle = new ThrottledScanProgress(new Collect(passed), AnHour);

        var wait = ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { }));
        var end = ScanProgressUpdate.Waiting(null);

        throttle.Report(Ticker(1));
        throttle.Report(wait);
        throttle.Report(end);

        Assert.Equal(new[] { Ticker(1), wait, end }, passed);
    }

    [Fact]
    public void A_wait_leaves_the_interval_running_for_the_ticker()
    {
        // A milestone would start the interval again and let the next ticker through.
        var passed = new List<ScanProgressUpdate>();
        var throttle = new ThrottledScanProgress(new Collect(passed), AnHour);

        throttle.Report(Ticker(1));
        throttle.Report(ScanProgressUpdate.Waiting(new SourceFolderWait("D:", () => { })));
        throttle.Report(ScanProgressUpdate.Waiting(null));
        throttle.Report(Ticker(2));

        Assert.DoesNotContain(Ticker(2), passed);
    }
}
