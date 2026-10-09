using InstallerClean.Helpers;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What the command-line tests share when they drive the real work method
/// (<see cref="InstallerClean.Cli.Program.RunWorkAsync"/>): the services it resolves, and
/// which of the entries a run writes is its summary.
/// </summary>
internal static class CliRunFixtures
{
    /// <summary>
    /// The services the work method resolves. Each one a test leaves out is a bare
    /// substitute, scripted to nothing, and the settings service always is one.
    /// </summary>
    internal static IServiceProvider Services(
        IFileSystemScanService scan,
        IPendingRebootService? reboot = null,
        IRemovableReverifier? reverifier = null,
        IDeleteFilesService? delete = null,
        IMoveFilesService? move = null,
        IFirstRunMark? firstRunMark = null) =>
        new ServiceCollection()
            .AddSingleton(scan)
            .AddSingleton(reboot ?? Substitute.For<IPendingRebootService>())
            .AddSingleton(reverifier ?? Substitute.For<IRemovableReverifier>())
            .AddSingleton(delete ?? Substitute.For<IDeleteFilesService>())
            .AddSingleton(move ?? Substitute.For<IMoveFilesService>())
            .AddSingleton(Substitute.For<ISettingsService>())
            .AddSingleton(firstRunMark ?? Substitute.For<IFirstRunMark>())
            .BuildServiceProvider();

    /// <summary>
    /// Whether an entry of <paramref name="entryClass"/> is a run's summary: Ok, Partial,
    /// TransientSkip or HardError, the four outcome classes, each run writing exactly one.
    /// Every other class is a notice.
    /// </summary>
    internal static bool IsSummary(CliEventClass entryClass) =>
        entryClass is CliEventClass.Ok or CliEventClass.Partial
            or CliEventClass.TransientSkip or CliEventClass.HardError;

    /// <inheritdoc cref="IsSummary(CliEventClass)"/>
    internal static bool IsSummary((CliEventClass Class, string Text) entry) => IsSummary(entry.Class);
}
