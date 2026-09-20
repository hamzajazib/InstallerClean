using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// What a held run RETURNS and what class it records, driven from the enum itself.
///
/// CliPendingRebootStringsTests walks the same enum twice, once per string surface, so
/// a reason added without its sentence or without its label is a red test. The emitter
/// reads the exit code and the entry class off the reason as well, in one expression,
/// and a member it does not name falls to the arm that promises a caller nothing about
/// coming back. So a reason added later still takes both without anybody choosing them,
/// and the two string surfaces give no cover here: a member can carry a sentence and a
/// label and still have had no thought given to what a scheduler should do about it.
///
/// THE TABLE IS THE DECISION AND THE WALK IS THE GATE. One row per reason; a member
/// with no row fails below, and writing its row is the choice being made.
///
/// WHAT THE TWO COLUMNS ARE WORTH, BECAUSE THEY ARE NOT EQUAL AND A READER SHOULD NOT
/// ASSUME THEY ARE. The exit code is read back from a real run driven through the work
/// method. THE EVENT CLASS IS NEVER READ BACK FROM ANYTHING THE APP WRITES. It is
/// handed to a static write that takes it directly, and nothing here observes what
/// reaches the channel. What holds that column instead is a check that it agrees with
/// the exit code declared beside it, which is two declared values agreeing with each
/// other rather than either of them being compared to behaviour. Observing the class
/// would need a seam in shipping code, and that is ruled out.
/// </summary>
public class CliPendingRebootOutcomeTests
{
    private const string OfferA = @"C:\Windows\Installer\offer-a.msi";
    private const string OfferB = @"C:\Windows\Installer\offer-b.msi";

    private static readonly Dictionary<PendingRebootReason, (int Exit, CliEventClass Class)> Declared = new()
    {
        [PendingRebootReason.MsiExecuteMutexHeld]     = (CliExitCode.Transient, CliEventClass.TransientSkip),
        [PendingRebootReason.MsiExecuteMutexAccessRefused] = (CliExitCode.Error, CliEventClass.HardError),
        [PendingRebootReason.InstallerInProgress]     = (CliExitCode.Transient, CliEventClass.TransientSkip),
        [PendingRebootReason.PendingRenameInCache]    = (CliExitCode.Transient, CliEventClass.TransientSkip),
        [PendingRebootReason.PendingRenameUnresolved] = (CliExitCode.Transient, CliEventClass.TransientSkip),
        [PendingRebootReason.RegistryCheckUnreadable] = (CliExitCode.Error, CliEventClass.HardError),
    };

    [Fact]
    public void Every_reason_has_a_row_in_the_table()
    {
        var undeclared = Enum.GetValues<PendingRebootReason>()
            .Where(reason => !Declared.ContainsKey(reason))
            .ToList();

        // Named rather than counted, so the failure says which member needs the
        // decision rather than only that one does.
        Assert.True(undeclared.Count == 0,
            "No exit code or event class has been chosen for: " + string.Join(", ", undeclared));
    }

    /// <summary>
    /// The band an exit code's entry belongs in, taken from the pairs the host writes:
    /// Ok and Partial go out with a 1000-band class, Transient and Cancelled with the
    /// 2000, Error with the 4000.
    ///
    /// EVERY MEMBER OF CliExitCode IS HERE AND NOT ONLY THE ONE THE TABLE USES, so a
    /// row that declares a different code is held rather than passed over by a check
    /// shaped around the single value in front of it today.
    /// </summary>
    private static readonly Dictionary<int, int> BandForExit = new()
    {
        [CliExitCode.Ok] = 1000,
        [CliExitCode.Partial] = 1000,
        [CliExitCode.Transient] = 2000,
        [CliExitCode.Cancelled] = 2000,
        [CliExitCode.Error] = 4000,
    };

    [Fact]
    public void Every_row_declares_an_event_class_whose_band_matches_its_exit_code()
    {
        // WHAT THIS IS AND IS NOT, so the column above is not read as more than it is.
        // It does not make the emitter observable: what reaches the channel is written
        // by a static this cannot see. It compares two DECLARED values with each other
        // and reads no behaviour at all. What it buys is that the class column can no
        // longer hold a value the exit code beside it contradicts, which is what a
        // column nothing looks at would otherwise allow.
        var wrong = new List<string>();

        foreach (var (reason, declared) in Declared)
        {
            if (!BandForExit.TryGetValue(declared.Exit, out var band))
            {
                wrong.Add($"{reason}: exit {declared.Exit} is in no band this test knows");
                continue;
            }

            var id = CliContract.EventIdFor(declared.Class);
            if (id / 1000 * 1000 != band)
                wrong.Add($"{reason}: exit {declared.Exit} belongs in the {band} band "
                    + $"and {declared.Class} is Event ID {id}");
        }

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public async Task A_held_run_exits_with_the_code_declared_for_its_reason()
    {
        var wrong = new List<string>();

        foreach (var reason in Enum.GetValues<PendingRebootReason>())
        {
            if (!Declared.TryGetValue(reason, out var expected)) continue;

            var exit = await RunHeldBy(reason);
            if (exit != expected.Exit) wrong.Add($"{reason}: expected {expected.Exit}, got {exit}");
        }

        // Every reason is walked before anything is asserted, so one wrong code does
        // not hide the rest of the table behind it.
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public async Task A_reason_the_gate_has_never_met_still_holds_the_run()
    {
        // Cast past the enum's members deliberately, the way the strings tests do:
        // this is the state a new reason arrives in. Whatever else is undecided, the
        // run must not go on to touch the folder, so the code is asserted to be a
        // holding one rather than to be any particular value.
        const PendingRebootReason unwritten = (PendingRebootReason)99;

        var exit = await RunHeldBy(unwritten);

        Assert.NotEqual(CliExitCode.Ok, exit);
        Assert.NotEqual(CliExitCode.Partial, exit);
    }

    /// <summary>
    /// A lock the action service meets at its acquire is reported under the gate's own
    /// reason for the same condition, so a run exits with the same code and prints the
    /// same sentence whether the gate met the lock or the acquire did. One row per
    /// mapping the host makes, for both flags.
    ///
    /// THE REFUSAL'S ROWS ARE THE ONES WITH TEETH. The acquire meets a refusal only when
    /// it began after the gate ran, the two asking for the same rights, so a scheduler
    /// that is told one thing on the night the refusal appears and another on every
    /// night after it has been told two things about one condition.
    /// </summary>
    [Theory]
    [InlineData("/d", PendingRebootReason.MsiExecuteMutexHeld)]
    [InlineData("/m", PendingRebootReason.MsiExecuteMutexHeld)]
    [InlineData("/d", PendingRebootReason.MsiExecuteMutexAccessRefused)]
    [InlineData("/m", PendingRebootReason.MsiExecuteMutexAccessRefused)]
    public async Task A_lock_met_at_the_acquire_reports_what_the_gate_reports_for_it(
        string arg, PendingRebootReason reason)
    {
        var sentence = Program.PendingRebootBlockedMessage(arg, reason, detail: null);

        var (gateExit, gateStdout) = await Run(arg, PendingRebootResult.Block(reason), metAtAcquire: null);
        var (acquireExit, acquireStdout) = await Run(arg, PendingRebootResult.Clean, metAtAcquire: reason);

        Assert.Equal(Declared[reason].Exit, gateExit);
        Assert.Equal(gateExit, acquireExit);
        Assert.Contains(sentence, gateStdout);
        Assert.Contains(sentence, acquireStdout);
        // A refusal never tells the operator something is using Windows Installer,
        // at either point; nothing has been seen holding the lock.
        if (reason == PendingRebootReason.MsiExecuteMutexAccessRefused)
        {
            Assert.DoesNotContain(Strings.Cli_PendingRebootBlocked_MsiExecuteMutex, gateStdout);
            Assert.DoesNotContain(Strings.Cli_PendingRebootBlocked_MsiExecuteMutex, acquireStdout);
        }
    }

    private static async Task<int> RunHeldBy(PendingRebootReason reason) =>
        (await Run("/d", PendingRebootResult.Block(reason), metAtAcquire: null)).ExitCode;

    /// <summary>
    /// A move destination fully qualified on either host and outside both forbidden
    /// sets, so the destination gates pass it. Nothing is created there: the move
    /// service is a substitute.
    /// </summary>
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "installerclean-cli-pending-reboot-outcome-test");

    /// <summary>
    /// Runs <paramref name="arg"/> to its end against a gate answering
    /// <paramref name="gate"/>, and an action service that, when the run reaches it,
    /// refuses at its acquire for the lock condition <paramref name="metAtAcquire"/>
    /// names: held, or refused permission to open. Returns the exit code and stdout.
    /// </summary>
    private static async Task<(int ExitCode, string Stdout)> Run(
        string arg, PendingRebootResult gate, PendingRebootReason? metAtAcquire)
    {
        // The gate sits after the scan, so the offer has to be non-empty or the run
        // returns on "nothing to do" before ever reaching it.
        var scan = Substitute.For<IFileSystemScanService>();
        scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResult(
                [new OrphanedFile(OfferA, 1024, false, false, false, "unclaimed"),
                 new OrphanedFile(OfferB, 1024, false, false, false, "unclaimed")],
                Array.Empty<RegisteredPackage>(), 0));

        var reboot = Substitute.For<IPendingRebootService>();
        reboot.Check().Returns(gate);

        // Both files survive the re-verify, so a run the gate lets through reaches the
        // action service with a batch to act on.
        var reverifier = Substitute.For<IRemovableReverifier>();
        reverifier.ReverifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ReverifyResult(new[] { OfferA, OfferB }, Array.Empty<string>()));

        var busy = metAtAcquire == PendingRebootReason.MsiExecuteMutexHeld;
        var refused = metAtAcquire == PendingRebootReason.MsiExecuteMutexAccessRefused;

        var delete = Substitute.For<IDeleteFilesService>();
        delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(0, Array.Empty<FileOperationError>(),
                InstallerBusy: busy, InstallerLockAccessRefused: refused));

        var move = Substitute.For<IMoveFilesService>();
        move.MoveFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(),
                Arg.Any<UnderLeaseClaims>(), Arg.Any<IProgress<OperationProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new MoveResult(0, Array.Empty<FileOperationError>(),
                InstallerBusy: busy, InstallerLockAccessRefused: refused));

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
