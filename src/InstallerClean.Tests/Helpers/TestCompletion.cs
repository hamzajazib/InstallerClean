using InstallerClean.Services;
using InstallerClean.ViewModels;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// A completion card for a test that reads what the card shows and nothing of the
/// report: every service it takes is a bare substitute, bar the window service where
/// the test hands one in to watch.
/// </summary>
internal static class TestCompletion
{
    internal static CompletionViewModel Create(IWindowService? windowService = null) =>
        new(Substitute.For<IResultLogService>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<IEarlierRunCheck>(),
            Substitute.For<IFirstRunMark>(),
            Substitute.For<IWindowsRegion>(),
            windowService ?? Substitute.For<IWindowService>());
}
