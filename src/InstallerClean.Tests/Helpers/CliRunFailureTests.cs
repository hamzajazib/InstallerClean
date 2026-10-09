using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What Main reports for an exception thrown out of the run (<c>Program.ReportRunFailure</c>):
/// the unexpected-error line, one HardError entry, exit 1, and the note saying the
/// Application log refused an entry where it did, printed once in the run.
/// </summary>
public class CliRunFailureTests
{
    [Fact]
    public void A_run_failure_whose_entry_the_log_refused_prints_the_log_note_after_the_error_line()
    {
        var run = Report(logRefuses: true, workFirst: false);

        Assert.Equal(CliExitCode.Error, run.ExitCode);
        Assert.Empty(run.Entries);
        var lines = run.Stdout.Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines[0], ErrorLines);
        Assert.Equal(Strings.Cli_EventLogUnavailable, lines[1]);
        Assert.Equal(string.Empty, lines[2]);
    }

    [Fact]
    public void A_run_failure_whose_entry_the_log_took_prints_no_log_note()
    {
        var run = Report(logRefuses: false, workFirst: false);

        Assert.Equal(CliExitCode.Error, run.ExitCode);
        var entry = Assert.Single(run.Entries);
        Assert.Equal(CliEventClass.HardError, entry.Class);
        var lines = run.Stdout.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines[0], ErrorLines);
        Assert.Equal(string.Empty, lines[1]);
    }

    [Fact]
    public void A_run_failure_after_the_work_has_printed_the_log_note_does_not_print_it_again()
    {
        var run = Report(logRefuses: true, workFirst: true);

        var lines = run.Stdout.Split(Environment.NewLine);
        Assert.Equal(1, lines.Count(l => l == Strings.Cli_EventLogUnavailable));
        Assert.Contains(lines[^2], ErrorLines);
    }

    /// <summary>
    /// The two forms of the unexpected-error line for the exception <see cref="Report"/>
    /// reports: crash.log written, and crash.log refused.
    /// </summary>
    private static string[] ErrorLines =>
    [
        string.Format(Strings.Cli_GenericError, nameof(InvalidOperationException), CrashLog.LogPath),
        string.Format(Strings.Cli_GenericError_NoLog, nameof(InvalidOperationException)),
    ];

    private sealed record RunResult(
        int ExitCode, string Stdout, IReadOnlyList<(CliEventClass Class, string Text)> Entries);

    /// <summary>
    /// Reports an exception for a /s run, with an Application log that refuses every entry
    /// where <paramref name="logRefuses"/> is set and records them otherwise. Where
    /// <paramref name="workFirst"/> is set, the run's work goes first, a scan that offers
    /// nothing, so the work has printed the note before the exception is reported.
    /// </summary>
    private static RunResult Report(bool logRefuses, bool workFirst)
    {
        // Console.SetOut, the sink, the flag and the note's latch are process-global; the
        // assembly disables test parallelisation.
        var original = Console.Out;
        var sink = EventLogWriter.Sink;
        var unavailable = EventLogWriter.EventLogUnavailable;
        var notePrinted = Program.EventLogNotePrinted;
        using var console = new StringWriter();
        try
        {
            Console.SetOut(console);
            EventLogWriter.EventLogUnavailable = false;
            Program.EventLogNotePrinted = false;
            EventLogWriter.Sink = (entry, text) =>
            {
                if (logRefuses) throw new InvalidOperationException("The log refused the entry.");
                EventLogRecorder.Sink(entry, text);
            };

            if (workFirst)
            {
                var scan = Substitute.For<IFileSystemScanService>();
                scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
                    .Returns(new ScanResult(Array.Empty<OrphanedFile>(), Array.Empty<RegisteredPackage>(), 0));
                Program.RunWorkAsync("/s", new CliInvocation(CliCommand.ScanOnly, null, null),
                    CancellationToken.None, CliRunFixtures.Services(scan)).GetAwaiter().GetResult();
            }

            EventLogRecorder.Clear();
            var exitCode = Program.ReportRunFailure(["/s"], new InvalidOperationException("thrown out of the run"));
            return new RunResult(exitCode, console.ToString(), EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
            EventLogWriter.Sink = sink;
            EventLogWriter.EventLogUnavailable = unavailable;
            Program.EventLogNotePrinted = notePrinted;
        }
    }
}
