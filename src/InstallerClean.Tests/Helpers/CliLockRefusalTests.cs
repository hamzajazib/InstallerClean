using System.Globalization;
using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The two lines a <c>/d</c> or <c>/m</c> emits when the action service refused
/// the batch for want of <c>Global\_MSIExecute</c>: the sentence the operator
/// reads and the Application-channel entry an RMM matches on. Most of this file is
/// the refusal with nothing shown holding the lock; the last test is the line a lock
/// the app was refused permission to open writes.
/// </summary>
/// <remarks>
/// The two wording tests read the lines back through the methods that build them,
/// which is what lets the wording be held without an assertion on the Application
/// channel itself. The last two tests drive a whole run instead, so what they hold
/// is that a refused batch reaches those lines at all and what it exits with.
///
/// WHAT IS STILL NOT HELD ANYWHERE: the class an entry is written under. It is
/// handed to a static write that nothing here observes, so the Event ID a
/// monitoring tool filters on rests on the pair of values in the emitter rather
/// than on anything read back. CliPendingRebootOutcomeTests says the same of its
/// own column.
///
/// A refused lock goes out through the pending-reboot emitter under the gate's own
/// reason for it, wherever it is met, and CliPendingRebootOutcomeTests holds its
/// exit code at both points.
/// </remarks>
public class CliLockRefusalTests
{
    [Fact]
    public void The_stdout_sentence_follows_the_flag_that_ran()
    {
        // The selection is the whole of what this can get wrong, the two sentences
        // being interchangeable as far as the compiler is concerned. Each closes
        // by naming what did not happen to the files, so the pair swapped over
        // tells an operator their files were moved when they were deleted.
        Assert.Equal(Strings.Cli_InstallerLockUnavailable, Program.InstallerLockUnavailableLine("/d"));
        Assert.Equal(Strings.Cli_MoveInstallerLockUnavailable, Program.InstallerLockUnavailableLine("/m"));
        // And that they are two sentences rather than one key wired to both,
        // which the assertions above would not notice.
        Assert.NotEqual(
            Program.InstallerLockUnavailableLine("/d"),
            Program.InstallerLockUnavailableLine("/m"));
    }

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public void The_event_log_line_opens_by_naming_the_flag(string arg)
    {
        // Every entry this tool writes opens with the flag, so that a fleet's
        // history can be filtered by what was actually run. One of them did not,
        // and a search for delete runs on those machines returned nothing.
        var line = MachineContract.English(
            () => Program.InstallerLockUnavailableEventLogLine(arg));

        Assert.StartsWith(arg, line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_event_log_line_is_one_line_and_its_remainder_serves_both_flags()
    {
        // One entry covers /d and /m, and the flag it opens with is the only
        // thing that may vary with which ran. Everything after it is read by
        // both, which is what this pins.
        var delete = MachineContract.English(() => Program.InstallerLockUnavailableEventLogLine("/d"));
        var move = MachineContract.English(() => Program.InstallerLockUnavailableEventLogLine("/m"));

        Assert.Equal(delete["/d".Length..], move["/m".Length..]);

        // What that shared remainder may not do, and the fault it had: it ended
        // "so nothing was deleted", which was false of every /m run that could
        // produce it. So a wording naming one of the two actions has to name the
        // other. A wording naming neither is fine and passes here on purpose,
        // "no files were touched" being true of both; what cannot stand is a
        // sentence true of half the runs it is written for.
        Assert.Equal(
            delete.Contains("deleted", StringComparison.OrdinalIgnoreCase),
            delete.Contains("moved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_event_log_line_reads_English_on_a_machine_that_is_not_and_leaves_the_thread_as_it_found_it()
    {
        // Two properties, and the second is the one with teeth. The line is
        // machine-read, so it is built inside MachineContract.English and an RMM
        // gets the same words whatever language Windows is in. That much would
        // also hold today without the forcing, every satellite stripping the
        // machine-contract keys, so the English assertion is holding the guarantee
        // rather than proving the mechanism is what delivers it.
        //
        // The restore is different: English swaps two thread cultures and puts
        // them back in a finally, and a leak would leave en-GB on the thread for
        // everything that ran afterwards, sizes and nouns included. Nothing else
        // in the suite holds that.
        //
        // Safe to write the thread cultures because the assembly disables test
        // parallelisation (AssemblyInfo.cs, for a reason of its own).
        var ui = CultureInfo.CurrentUICulture;
        var format = CultureInfo.CurrentCulture;
        var italian = CultureInfo.GetCultureInfo("it-IT");
        try
        {
            CultureInfo.CurrentUICulture = italian;
            CultureInfo.CurrentCulture = italian;

            var line = MachineContract.English(() => Program.InstallerLockUnavailableEventLogLine("/d"));

            Assert.Contains("mode aborted", line, StringComparison.Ordinal);
            Assert.Contains("could not be acquired", line, StringComparison.Ordinal);
            // By name rather than by instance: what matters is the culture the
            // thread is left in, not which object carries it.
            Assert.Equal("it-IT", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("it-IT", CultureInfo.CurrentCulture.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = ui;
            CultureInfo.CurrentCulture = format;
        }
    }

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public void A_refused_lock_writes_one_line_naming_the_flag_and_both_actions(string arg)
    {
        // The whole line, because Application-log tooling matches on its words: a
        // refused lock writes exactly this, at the pending-reboot check and at the
        // acquire alike, and the flag it opens with is the only part that varies.
        var line = MachineContract.English(() => Program.PendingRebootEventLogLine(
            arg, PendingRebootReason.MsiExecuteMutexAccessRefused, detail: null));

        Assert.Equal(
            $"{arg} mode aborted: access to the Windows Installer mutex was refused, so "
            + "whether an installation was in progress could not be established and no "
            + "files were moved or deleted.",
            line);
    }

    /// <summary>
    /// The whole run, for the refusal with nothing shown to be holding the lock.
    /// The wording tests above read the two lines back from the methods that build
    /// them; this says that a batch refused that way prints the one the operator
    /// reads and exits the code a scheduler acts on, which no assertion on a
    /// builder can say.
    /// </summary>
    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public async Task A_batch_refused_with_nothing_holding_the_lock_prints_its_line_and_exits_transient(string arg)
    {
        var (exitCode, stdout) = await RunRefusedByUnavailableLock(arg);

        Assert.Equal(CliExitCode.Transient, exitCode);
        Assert.Contains(Program.InstallerLockUnavailableLine(arg), stdout);
        // Not the refusal the app was not allowed to look at, which is a different
        // condition with a different sentence and a different exit code.
        Assert.DoesNotContain(Program.InstallerLockAccessRefusedLine(arg), stdout);
    }

    private const string OfferA = @"C:\Windows\Installer\offer-a.msi";
    private const string OfferB = @"C:\Windows\Installer\offer-b.msi";

    /// <summary>
    /// Temp is fully qualified on either host and outside both forbidden sets, so
    /// the /m destination gates pass it. Nothing is created there: the move service
    /// is a substitute.
    /// </summary>
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "installerclean-cli-lock-refusal-test");

    /// <summary>
    /// Runs <paramref name="arg"/> against a clean gate and an action service that
    /// refuses at its acquire with nothing shown to be holding the lock. Returns
    /// the exit code and stdout.
    /// </summary>
    private static async Task<(int ExitCode, string Stdout)> RunRefusedByUnavailableLock(string arg)
    {
        // The gate sits after the scan, so the offer has to be non-empty or the run
        // returns on "nothing to do" before ever reaching the service.
        var scan = Substitute.For<IFileSystemScanService>();
        scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResult(
                [new OrphanedFile(OfferA, 1024, false, false, false, "unclaimed"),
                 new OrphanedFile(OfferB, 1024, false, false, false, "unclaimed")],
                Array.Empty<RegisteredPackage>(), 0));

        var reboot = Substitute.For<IPendingRebootService>();
        reboot.Check().Returns(PendingRebootResult.Clean);

        var reverifier = Substitute.For<IRemovableReverifier>();
        reverifier.ReverifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ReverifyResult(new[] { OfferA, OfferB }, Array.Empty<string>()));

        var delete = Substitute.For<IDeleteFilesService>();
        delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(0, Array.Empty<FileOperationError>(), InstallerLockUnavailable: true));

        var move = Substitute.For<IMoveFilesService>();
        move.MoveFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(),
                Arg.Any<UnderLeaseClaims>(), Arg.Any<IProgress<OperationProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new MoveResult(0, Array.Empty<FileOperationError>(), InstallerLockUnavailable: true));

        var services = new ServiceCollection()
            .AddSingleton(scan)
            .AddSingleton(reboot)
            .AddSingleton(reverifier)
            .AddSingleton(delete)
            .AddSingleton(move)
            .AddSingleton(Substitute.For<ISettingsService>())
            .BuildServiceProvider();

        var invocation = arg == "/m"
            ? new CliInvocation(CliCommand.Move, null, Destination)
            : new CliInvocation(CliCommand.Delete, null, null);

        // Console.SetOut is process-global; the assembly disables test
        // parallelisation, which is what makes reading stdout back safe here.
        var original = Console.Out;
        using var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            var exitCode = await Program.RunWorkAsync(arg, invocation, CancellationToken.None, services);
            return (exitCode, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
