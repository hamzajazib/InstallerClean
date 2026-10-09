using System.Runtime.CompilerServices;
using InstallerClean.Cli;
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

    /// <summary>
    /// Clears the log's refused flag and the command line's note latch
    /// (<see cref="Program.EventLogNotePrinted"/>), and puts both back, with the writer's
    /// sink, when disposed. All three are process-global, and the flag and the latch go
    /// together: a flag cleared with the latch still set would leave a later run's note
    /// unprinted.
    /// </summary>
    internal static IDisposable FreshLogState() => new LogState();

    private sealed class LogState : IDisposable
    {
        private readonly Action<CliEventClass, string>? _sink = EventLogWriter.Sink;
        private readonly bool _unavailable = EventLogWriter.EventLogUnavailable;
        private readonly bool _notePrinted = Program.EventLogNotePrinted;

        internal LogState()
        {
            EventLogWriter.EventLogUnavailable = false;
            Program.EventLogNotePrinted = false;
        }

        public void Dispose()
        {
            EventLogWriter.Sink = _sink;
            EventLogWriter.EventLogUnavailable = _unavailable;
            Program.EventLogNotePrinted = _notePrinted;
        }
    }
}
