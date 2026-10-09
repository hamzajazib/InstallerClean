using InstallerClean.Models;
using InstallerClean.Services;
using InstallerClean.ViewModels;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A completion card for a test that reads what the card shows and nothing of the
/// report: every service it takes is a bare substitute, bar the window service where
/// the test hands one in to watch. <paramref name="lastScan"/> is the scan the window
/// shows, which the card after a Move or Delete speaks for; none where it is not given.
/// </summary>
internal static class TestCompletion
{
    internal static CompletionViewModel Create(
        IWindowService? windowService = null, Func<ScanResult?>? lastScan = null) =>
        new(Substitute.For<IResultLogService>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<IEarlierRunCheck>(),
            Substitute.For<IFirstRunMark>(),
            Substitute.For<IWindowsRegion>(),
            windowService ?? Substitute.For<IWindowService>(),
            lastScan ?? (() => null));
}
