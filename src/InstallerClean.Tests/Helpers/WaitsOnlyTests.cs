using InstallerClean.Helpers;
using InstallerClean.Models;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The reporter the Move or Delete heading puts in front of its own: the waits a scan
/// reports reach the reporter behind it, and nothing else the scan reports does.
/// </summary>
public class WaitsOnlyTests
{
    /// <summary>Keeps what it is told, in order, on the thread that tells it.</summary>
    private sealed class Told : IProgress<ScanProgressUpdate>
    {
        public List<ScanProgressUpdate> Updates { get; } = [];

        public void Report(ScanProgressUpdate value) => Updates.Add(value);
    }

    [Fact]
    public void A_wait_and_its_end_are_passed_on_in_order()
    {
        var told = new Told();
        var waits = new WaitsOnly(told);
        var wait = ScanProgressUpdate.Waiting("D:");
        var end = ScanProgressUpdate.Waiting(null);

        waits.Report(wait);
        waits.Report(end);

        Assert.Equal(new[] { wait, end }, told.Updates);
    }

    [Fact]
    public void A_milestone_and_a_ticker_update_are_not_passed_on()
    {
        var told = new Told();
        var waits = new WaitsOnly(told);

        waits.Report(new ScanProgressUpdate("Checking remaining files..."));
        waits.Report(new ScanProgressUpdate("12 of 40", IsMilestone: false, Position: 12, Total: 40));

        Assert.Empty(told.Updates);
    }
}
