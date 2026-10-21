using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Resources;
using InstallerClean.Services;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What a command-line run does with what its update ended in
/// (<c>Program.ConcludeUpdate</c>): the exit code where the run ends there, or none where it
/// goes on, what it prints and the entries it writes to the Application log. Each result
/// that ends the run with nothing run writes exactly one entry, since one entry per run
/// is what an RMM counts runs by.
/// </summary>
/// <remarks>
/// Stdout is read back through <c>Console.SetOut</c>, which is process-global, and the log
/// through <see cref="EventLogRecorder"/>; the assembly disables test parallelisation,
/// which is what makes both safe here.
/// </remarks>
public class CliUpdateConclusionTests
{
    private const string From = "3.4.0";
    private const string To = "3.5.0";
    private const int ControlCExit = unchecked((int)0xC000013A);

    private static string CancelledNoWork(string arg) =>
        MachineContract.English(() => string.Format(Strings.Cli_EventLogCancelledNoWork, arg));

    [Fact]
    public void A_newer_version_ended_by_the_Ctrl_C_this_run_took_writes_the_cancelled_runs_entry()
    {
        var run = Conclude(new CommandLineUpdateResult.HandedOver(ControlCExit, From, To, "/s"), cancelled: true);

        Assert.Equal(CliExitCode.Cancelled, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_Cancelled }, run.Lines);
        var entry = Assert.Single(run.Entries);
        Assert.Equal((CliEventClass.TransientSkip, CancelledNoWork("/s")), entry);
    }

    [Fact]
    public void The_same_exit_code_with_no_Ctrl_C_taken_is_passed_on_as_it_is()
    {
        var run = Conclude(new CommandLineUpdateResult.HandedOver(ControlCExit, From, To, "/s"), cancelled: false);

        Assert.Equal(ControlCExit, run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Empty(run.Entries);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void The_newer_versions_own_exit_code_is_the_runs_and_its_summary_is_its_own(int exitCode, bool cancelled)
    {
        var run = Conclude(new CommandLineUpdateResult.HandedOver(exitCode, From, To, "/s"), cancelled);

        Assert.Equal(exitCode, run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Empty(run.Entries);
    }

    [Fact]
    public void A_newer_version_that_ran_after_the_log_refused_the_3005_has_the_note_printed_after_it()
    {
        var run = Conclude(new CommandLineUpdateResult.HandedOver(0, From, To, "/s"), cancelled: false,
            logRefuses: true, handOverWritten: true);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_EventLogUnavailable }, run.Lines);
        Assert.Empty(run.Entries);
    }

    [Fact]
    public void A_newer_version_that_ran_after_the_log_took_the_3005_has_nothing_printed_after_it()
    {
        var run = Conclude(new CommandLineUpdateResult.HandedOver(0, From, To, "/s"), cancelled: false,
            handOverWritten: true);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Equal(CliEventClass.UpdatedNotice, Assert.Single(run.Entries).Class);
    }

    [Fact]
    public void Ctrl_C_before_the_newer_version_was_in_place_writes_the_cancelled_runs_entry()
    {
        var run = Conclude(new CommandLineUpdateResult.Cancelled(), cancelled: true, arg: "/d");

        Assert.Equal(CliExitCode.Cancelled, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_Cancelled }, run.Lines);
        Assert.Equal((CliEventClass.TransientSkip, CancelledNoWork("/d")), Assert.Single(run.Entries));
    }

    [Fact]
    public void Ctrl_C_after_the_newer_version_went_in_writes_the_3005_as_the_runs_one_entry()
    {
        var run = Conclude(new CommandLineUpdateResult.UpdatedThenCancelled(From, To), cancelled: true);

        Assert.Equal(CliExitCode.Cancelled, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_Cancelled }, run.Lines);
        Assert.Equal(
            (CliEventClass.UpdatedThenCancelledNotice,
                "/s mode: InstallerClean updated itself from 3.4.0 to 3.5.0, then the run was cancelled, so nothing ran."),
            Assert.Single(run.Entries));
    }

    [Theory]
    [InlineData(UpdateFailureStage.Download,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateFailureStage.Install,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the install failed), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateFailureStage.InUse,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use), then the run was cancelled, so nothing ran.")]
    public void Ctrl_C_after_an_update_that_failed_writes_the_3006_as_the_runs_one_entry(
        UpdateFailureStage stage, string expected)
    {
        var run = Conclude(new CommandLineUpdateResult.FailedThenCancelled(stage, From, To), cancelled: true);

        Assert.Equal(CliExitCode.Cancelled, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_Cancelled }, run.Lines);
        Assert.Equal((CliEventClass.UpdateFailedNotice, expected), Assert.Single(run.Entries));
    }

    [Fact]
    public void An_update_that_failed_writes_the_3006_and_the_run_goes_on()
    {
        var run = Conclude(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Install, From, To), cancelled: false);

        Assert.Null(run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Equal(
            (CliEventClass.UpdateFailedNotice,
                "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the install failed) and ran /s with 3.4.0."),
            Assert.Single(run.Entries));
    }

    [Fact]
    public void Ctrl_C_after_the_run_said_it_goes_on_writes_the_3006_as_the_runs_one_entry()
    {
        var run = Conclude(new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Download, From, To), cancelled: true);

        Assert.Equal(CliExitCode.Cancelled, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_Cancelled }, run.Lines);
        Assert.Equal(
            (CliEventClass.UpdateFailedNotice,
                "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed), then the run was cancelled, so nothing ran."),
            Assert.Single(run.Entries));
    }

    [Fact]
    public void Nothing_tried_with_Ctrl_C_writes_nothing_and_leaves_the_stop_to_the_run()
    {
        var run = Conclude(CommandLineUpdateResult.NothingTried, cancelled: true);

        Assert.Null(run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Empty(run.Entries);
    }

    [Fact]
    public void Nothing_tried_writes_nothing_and_the_run_goes_on()
    {
        var run = Conclude(CommandLineUpdateResult.NothingTried, cancelled: false);

        Assert.Null(run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Empty(run.Entries);
    }

    [Fact]
    public void A_newer_version_in_place_that_would_not_start_writes_the_3006_and_ends_the_run_with_an_error()
    {
        var run = Conclude(new CommandLineUpdateResult.NotStarted(From, To), cancelled: false);

        Assert.Equal(CliExitCode.Error, run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Equal(
            (CliEventClass.UpdateFailedNotice,
                "/s mode: InstallerClean updated itself from 3.4.0 to 3.5.0 but could not start it, so nothing ran."),
            Assert.Single(run.Entries));
    }

    [Fact]
    public void A_failure_with_this_exe_not_put_back_writes_the_hard_error_and_ends_the_run_with_an_error()
    {
        var run = Conclude(new CommandLineUpdateResult.Stopped(nameof(UpdateFailedException), null), cancelled: false);

        Assert.Equal(CliExitCode.Error, run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Equal(
            (CliEventClass.HardError, MachineContract.English(
                () => string.Format(Strings.Cli_EventLogHardError_NoLog, "/s", nameof(UpdateFailedException)))),
            Assert.Single(run.Entries));
    }

    [Theory]
    [InlineData(nameof(CommandLineUpdateResult.HandedOver))]
    [InlineData(nameof(CommandLineUpdateResult.Cancelled))]
    [InlineData(nameof(CommandLineUpdateResult.UpdatedThenCancelled))]
    [InlineData(nameof(CommandLineUpdateResult.FailedThenCancelled))]
    [InlineData(nameof(CommandLineUpdateResult.CarriedOn))]
    [InlineData(nameof(CommandLineUpdateResult.NotStarted))]
    [InlineData(nameof(CommandLineUpdateResult.Stopped))]
    public void Each_ending_with_one_entry_prints_the_note_where_the_log_refuses_it(string ending)
    {
        CommandLineUpdateResult result = ending switch
        {
            nameof(CommandLineUpdateResult.HandedOver) => new CommandLineUpdateResult.HandedOver(ControlCExit, From, To, "/s"),
            nameof(CommandLineUpdateResult.Cancelled) => new CommandLineUpdateResult.Cancelled(),
            nameof(CommandLineUpdateResult.UpdatedThenCancelled) => new CommandLineUpdateResult.UpdatedThenCancelled(From, To),
            nameof(CommandLineUpdateResult.FailedThenCancelled) =>
                new CommandLineUpdateResult.FailedThenCancelled(UpdateFailureStage.Install, From, To),
            nameof(CommandLineUpdateResult.CarriedOn) =>
                new CommandLineUpdateResult.CarriedOn(UpdateFailureStage.Download, From, To),
            nameof(CommandLineUpdateResult.NotStarted) => new CommandLineUpdateResult.NotStarted(From, To),
            _ => new CommandLineUpdateResult.Stopped(nameof(UpdateFailedException), null),
        };

        var run = Conclude(result, cancelled: true, logRefuses: true);

        Assert.Empty(run.Entries);
        Assert.Equal(Strings.Cli_EventLogUnavailable, run.Lines[^1]);
        Assert.Equal(1, run.Lines.Count(line => line == Strings.Cli_EventLogUnavailable));
    }

    private sealed record Concluded(
        int? ExitCode, string[] Lines, IReadOnlyList<(CliEventClass Class, string Text)> Entries);

    /// <param name="handOverWritten">
    /// Whether the 3005 the host writes as it hands over is written first, as it is on a
    /// real run that ends in <see cref="CommandLineUpdateResult.HandedOver"/>.
    /// </param>
    private static Concluded Conclude(
        CommandLineUpdateResult result, bool cancelled, string arg = "/s", bool logRefuses = false,
        bool handOverWritten = false)
    {
        var original = Console.Out;
        using var console = new StringWriter();
        try
        {
            Console.SetOut(console);
            return CliRunFixtures.WithTheLogRefusing(_ => logRefuses, () =>
            {
                EventLogRecorder.Clear();
                if (handOverWritten)
                    MachineContract.WriteEventLog(CliEventClass.UpdatedNotice, () => "the hand-over's entry");
                var exitCode = Program.ConcludeUpdate(result, arg, [arg], cancelled);
                var lines = console.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                return Task.FromResult(new Concluded(exitCode, lines, EventLogRecorder.Entries));
            }).GetAwaiter().GetResult();
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
