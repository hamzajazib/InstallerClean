using System.Runtime.CompilerServices;
using InstallerClean.Helpers;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Takes every Application-channel entry the suite writes, in place of the channel.
/// Installed as <see cref="EventLogWriter.Sink"/> when the assembly loads, before any
/// test runs, so an entry a test writes through <see cref="EventLogWriter.Write"/> lands
/// here rather than in the machine's log. One test takes it out for a single write and
/// puts it back: <c>CommandLineRunRecordIntegrationTests</c>, which runs only on a CI
/// runner.
/// </summary>
/// <remarks>
/// One list for the whole assembly, which is safe because the assembly disables test
/// parallelisation (AssemblyInfo.cs). A test that reads it calls <see cref="Clear"/>
/// before the run it is looking at, so the entries are that run's alone.
/// </remarks>
internal static class EventLogRecorder
{
    private static readonly List<(CliEventClass Class, string Text)> Recorded = [];

    internal static readonly Action<CliEventClass, string> Sink =
        (outcome, entry) => Recorded.Add((outcome, entry));

    [ModuleInitializer]
    internal static void Install() => EventLogWriter.Sink = Sink;

    internal static IReadOnlyList<(CliEventClass Class, string Text)> Entries => Recorded.ToArray();

    internal static void Clear() => Recorded.Clear();
}
