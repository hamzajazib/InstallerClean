using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What Main reports for an exception thrown out of the run before its work begins
/// (<c>Program.ReportRunFailure</c>): the unexpected-error line, one HardError entry, exit 1,
/// and the note saying the Application log refused the entry where it did.
/// </summary>
public class CliRunFailureTests
{
    [Fact]
    public void A_run_failure_whose_entry_the_log_refused_prints_the_log_note_after_the_error_line()
    {
        var run = Report(logRefuses: true);

        Assert.Equal(CliExitCode.Error, run.ExitCode);
        Assert.Empty(run.Entries);
        var lines = run.Stdout.Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.Equal(Strings.Cli_EventLogUnavailable, lines[1]);
        Assert.Equal(string.Empty, lines[2]);
    }

    [Fact]
    public void A_run_failure_whose_entry_the_log_took_prints_no_log_note()
    {
        var run = Report(logRefuses: false);

        Assert.Equal(CliExitCode.Error, run.ExitCode);
        var entry = Assert.Single(run.Entries);
        Assert.Equal(CliEventClass.HardError, entry.Class);
        var lines = run.Stdout.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain(Strings.Cli_EventLogUnavailable, run.Stdout, StringComparison.Ordinal);
    }

    private sealed record RunResult(
        int ExitCode, string Stdout, IReadOnlyList<(CliEventClass Class, string Text)> Entries);

    /// <summary>
    /// Reports an exception for a /d run, with an Application log that refuses every entry
    /// where <paramref name="logRefuses"/> is set and records them otherwise.
    /// </summary>
    private static RunResult Report(bool logRefuses)
    {
        // Console.SetOut, the sink and the flag are process-global; the assembly disables
        // test parallelisation.
        var original = Console.Out;
        var sink = EventLogWriter.Sink;
        var unavailable = EventLogWriter.EventLogUnavailable;
        using var console = new StringWriter();
        try
        {
            Console.SetOut(console);
            EventLogRecorder.Clear();
            EventLogWriter.EventLogUnavailable = false;
            EventLogWriter.Sink = (entry, text) =>
            {
                if (logRefuses) throw new InvalidOperationException("The log refused the entry.");
                EventLogRecorder.Sink(entry, text);
            };

            var exitCode = Program.ReportRunFailure(["/d"], new InvalidOperationException("thrown before the work"));
            return new RunResult(exitCode, console.ToString(), EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
            EventLogWriter.Sink = sink;
            EventLogWriter.EventLogUnavailable = unavailable;
        }
    }
}
