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

    [Theory]
    [InlineData(0)]
    [InlineData(CliExitCode.Error)]
    public void A_newer_version_that_ran_after_the_log_refused_the_3005_has_the_note_printed_after_it(int exitCode)
    {
        var run = Conclude(new CommandLineUpdateResult.HandedOver(exitCode, From, To, "/s"), cancelled: false,
            logRefuses: true, handOverWritten: true);

        Assert.Equal(exitCode, run.ExitCode);
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

    [Theory]
    [InlineData(UpdateNotMade.DownloadFailed,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.NotRun,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer version could not be run), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.InUse,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.CopyCannotTell,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it can't tell whether its setup installed it), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.FromSource,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it was built from source), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.NeedsNetFramework48,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release needs .NET Framework 4.8), then the run was cancelled, so nothing ran.")]
    [InlineData(UpdateNotMade.CannotInstallAutomatically,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release can't be installed automatically), then the run was cancelled, so nothing ran.")]
    public void Ctrl_C_after_an_update_that_was_not_made_writes_the_3006_as_the_runs_one_entry(
        UpdateNotMade notMade, string expected)
    {
        var run = Conclude(new CommandLineUpdateResult.FailedThenCancelled(notMade, From, To), cancelled: true);

        Assert.Equal(CliExitCode.Cancelled, run.ExitCode);
        Assert.Equal(new[] { Strings.Cli_Cancelled }, run.Lines);
        Assert.Equal((CliEventClass.UpdateFailedNotice, expected), Assert.Single(run.Entries));
    }

    [Theory]
    [InlineData(UpdateNotMade.DownloadFailed,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the download failed) and ran /s with 3.4.0.")]
    [InlineData(UpdateNotMade.NotRun,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer version could not be run) and ran /s with 3.4.0.")]
    [InlineData(UpdateNotMade.InUse,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (InstallerClean was in use) and ran /s with 3.4.0.")]
    [InlineData(UpdateNotMade.CopyCannotTell,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it can't tell whether its setup installed it) and ran /s with 3.4.0.")]
    [InlineData(UpdateNotMade.FromSource,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (it was built from source) and ran /s with 3.4.0.")]
    [InlineData(UpdateNotMade.NeedsNetFramework48,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release needs .NET Framework 4.8) and ran /s with 3.4.0.")]
    [InlineData(UpdateNotMade.CannotInstallAutomatically,
        "/s mode: InstallerClean could not update itself from 3.4.0 to 3.5.0 (the newer release can't be installed automatically) and ran /s with 3.4.0.")]
    public void An_update_that_was_not_made_writes_the_3006_and_the_run_goes_on(UpdateNotMade notMade, string expected)
    {
        var run = Conclude(new CommandLineUpdateResult.CarriedOn(notMade, From, To), cancelled: false);

        Assert.Null(run.ExitCode);
        Assert.Empty(run.Lines);
        Assert.Equal((Program.UpdateNotMadeClass(notMade), expected), Assert.Single(run.Entries));
    }

    [Theory]
    [InlineData(UpdateNotMade.DownloadFailed, false)]
    [InlineData(UpdateNotMade.NotRun, false)]
    [InlineData(UpdateNotMade.InUse, false)]
    [InlineData(UpdateNotMade.CopyCannotTell, true)]
    [InlineData(UpdateNotMade.FromSource, true)]
    [InlineData(UpdateNotMade.NeedsNetFramework48, true)]
    [InlineData(UpdateNotMade.CannotInstallAutomatically, true)]
    public void A_reason_true_on_every_run_is_noted_as_information_and_one_that_can_pass_as_a_warning(
        UpdateNotMade notMade, bool everyRun)
    {
        Assert.Equal(everyRun ? CliEventClass.UpdateNotPossibleNotice : CliEventClass.UpdateFailedNotice,
            Program.UpdateNotMadeClass(notMade));
    }

    [Fact]
    public void Every_reason_an_update_was_not_made_has_a_3006_of_its_own_both_ways()
    {
        var reasons = Enum.GetValues<UpdateNotMade>();
        var goesOn = reasons
            .Select(r => MachineContract.English(() => Program.UpdateFailedEventLogLine("/s", From, To, r, ["/s"])))
            .ToHashSet();
        var cancelled = reasons
            .Select(r => MachineContract.English(() => Program.UpdateFailedThenCancelledEventLogLine("/s", From, To, r)))
            .ToHashSet();

        Assert.Equal(reasons.Length, goesOn.Count);
        Assert.Equal(reasons.Length, cancelled.Count);
    }

    [Fact]
    public void Ctrl_C_after_the_run_said_it_goes_on_writes_the_3006_as_the_runs_one_entry()
    {
        var run = Conclude(new CommandLineUpdateResult.CarriedOn(UpdateNotMade.DownloadFailed, From, To), cancelled: true);

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

    [Theory]
    [InlineData(nameof(CommandLineUpdateResult.HandedOver))]
    [InlineData(nameof(CommandLineUpdateResult.Cancelled))]
    [InlineData(nameof(CommandLineUpdateResult.FailedThenCancelled))]
    [InlineData(nameof(CommandLineUpdateResult.CarriedOn))]
    public void Each_ending_with_one_entry_prints_the_note_where_the_log_refuses_it(string ending)
    {
        CommandLineUpdateResult result = ending switch
        {
            nameof(CommandLineUpdateResult.HandedOver) => new CommandLineUpdateResult.HandedOver(ControlCExit, From, To, "/s"),
            nameof(CommandLineUpdateResult.Cancelled) => new CommandLineUpdateResult.Cancelled(),
            nameof(CommandLineUpdateResult.FailedThenCancelled) =>
                new CommandLineUpdateResult.FailedThenCancelled(UpdateNotMade.NotRun, From, To),
            nameof(CommandLineUpdateResult.CarriedOn) =>
                new CommandLineUpdateResult.CarriedOn(UpdateNotMade.DownloadFailed, From, To),
            _ => throw new ArgumentOutOfRangeException(nameof(ending)),
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
