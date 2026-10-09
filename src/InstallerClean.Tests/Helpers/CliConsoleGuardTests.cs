using System.Text;
using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The guard every run's output goes through (<see cref="ConsoleGuard"/>): a write that
/// fails loses what it was writing and never ends the run, so a run's one outcome entry and
/// its exit code are the ones its work earns. Driven on its own, through the real work method
/// with a failing console behind the guard as Main puts it there, and through Main.
///
/// WHAT EVERY RUN FIXTURE SETS UP is a scan offering two files, a clean pending-reboot gate
/// and a re-verify that keeps both, so the run reaches whichever service the test scripted,
/// and a console that fails at a line printed after that service has answered.
/// </summary>
public class CliConsoleGuardTests
{
    /// <summary>
    /// Temp is fully qualified on either host and outside both forbidden sets, so the
    /// destination gates pass it. Nothing is created and nothing is written: the move
    /// service is a substitute.
    /// </summary>
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "installerclean-cli-guard-test");

    private const string File1 = @"C:\Windows\Installer\a.msi";
    private const string File2 = @"C:\Windows\Installer\b.msp";

    private static readonly PatchClaim SurvivingA =
        new(File1, "{AAAA1111-0000-0000-0000-000000000001}", "{PPPP1111-0000-0000-0000-000000000001}", null, 2);
    private static readonly PatchClaim SurvivingB =
        new(File2, "{AAAA1111-0000-0000-0000-000000000002}", "{PPPP1111-0000-0000-0000-000000000001}", null, 2);

    private static readonly string NewLine = Environment.NewLine;

    // ---- The guard ----

    [Fact]
    public void Every_write_reaches_the_console_while_it_takes_them()
    {
        var console = new StringWriter();
        var guard = new ConsoleGuard(console);

        guard.Write("a");
        guard.Write('b');
        guard.WriteLine("c");
        guard.WriteLine();

        Assert.Equal("abc" + NewLine + NewLine, console.ToString());
        Assert.False(guard.Failed);
    }

    [Fact]
    public void A_write_that_fails_loses_its_own_text_and_the_next_write_starts_a_line_of_its_own()
    {
        var console = new ConsoleFailingAt("second");
        var guard = new ConsoleGuard(console);

        guard.WriteLine("first");
        var thrown = Record.Exception(() =>
        {
            guard.WriteLine("second");
            guard.WriteLine("third");
            guard.Write('x');
            guard.Flush();
        });

        Assert.Null(thrown);
        Assert.True(guard.Failed);
        Assert.Equal(1, console.Refused);
        Assert.Equal("first" + NewLine + NewLine + "third" + NewLine + "x", console.ToString());
    }

    [Fact]
    public void A_line_a_failed_write_cuts_short_is_not_joined_by_the_next_line_out()
    {
        // The writer the console gives a run: a StreamWriter with the console's 256-character
        // buffer, flushing after every write, behind TextWriter.Synchronized. The first line
        // goes to the disk in two pieces. The disk takes the first piece and refuses the second,
        // then refuses the line break the guard puts ahead of the next line, then has room.
        var disk = new DiskRefusingWrites(2, 3);
        var console = TextWriter.Synchronized(
            new StreamWriter(disk, new UTF8Encoding(false), 256, leaveOpen: true) { AutoFlush = true });
        var guard = new ConsoleGuard(console);

        var thrown = Record.Exception(() =>
        {
            guard.WriteLine(new string('a', 300));
            guard.WriteLine("lost");
            guard.WriteLine("last");
        });

        Assert.Null(thrown);
        Assert.True(guard.Failed);
        Assert.Equal(5, disk.Writes);
        Assert.Equal(
            new string('a', 256) + NewLine + "last" + NewLine,
            Encoding.UTF8.GetString(disk.ToArray()));
    }

    [Fact]
    public void A_failed_write_goes_to_the_crash_log_once()
    {
        var folder = Path.Combine(Path.GetTempPath(), "InstallerClean.Tests", Guid.NewGuid().ToString("N"));
        var suite = CrashLog.FolderForTests;
        CrashLog.FolderForTests = folder;
        try
        {
            var console = new ConsoleFailingAt("");
            var guard = new ConsoleGuard(console);

            guard.WriteLine("first");
            guard.WriteLine("second");

            Assert.True(console.Refused == 2, "The second write never reached the console.");
            var log = File.ReadAllText(Path.Combine(folder, "crash.log"));
            Assert.Equal(1, log.Split(ConsoleFailingAt.Message).Length - 1);
        }
        finally
        {
            CrashLog.FolderForTests = suite;
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    // ---- Runs whose output fails ----

    [Fact]
    public async Task A_delete_whose_output_fails_after_the_batch_is_logged_and_exits_as_that_delete()
    {
        var delete = Substitute.For<IDeleteFilesService>();
        delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(2, Array.Empty<FileOperationError>()));

        var run = await Run("/d", Services(delete: delete), FailsAt(Strings.Cli_DeletedFiles));

        Assert.True(run.Refused > 0, "The console refused no write, so it never failed after the batch.");
        Assert.Equal(CliExitCode.Ok, run.ExitCode);
        var summary = Assert.Single(run.Entries, IsOutcome);
        Assert.Equal(CliEventClass.Ok, summary.Class);
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogDeleteSummary,
            "/d", 2, 2, DisplayHelpers.PluraliseFile(2), DisplayHelpers.FormatSizeForMachine(300),
            0, DisplayHelpers.PluraliseError(0))), summary.Text);
    }

    [Fact]
    public async Task A_move_whose_output_fails_after_the_batch_is_logged_and_exits_as_that_move()
    {
        var move = Substitute.For<IMoveFilesService>();
        move.MoveFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(),
                Arg.Any<UnderLeaseClaims>(), Arg.Any<IProgress<OperationProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new MoveResult(2, Array.Empty<FileOperationError>()));

        var run = await Run("/m", Services(move: move), FailsAt(Strings.Cli_MovedFiles));

        Assert.True(run.Refused > 0, "The console refused no write, so it never failed after the batch.");
        Assert.Equal(CliExitCode.Ok, run.ExitCode);
        var summary = Assert.Single(run.Entries, IsOutcome);
        Assert.Equal(CliEventClass.Ok, summary.Class);
        Assert.Equal(MachineContract.English(() => string.Format(Strings.Cli_EventLogMoveSummary,
            "/m", 2, 2, DisplayHelpers.PluraliseFile(2), Destination,
            DisplayHelpers.FormatSizeForMachine(300), 0, DisplayHelpers.PluraliseError(0))), summary.Text);
    }

    [Theory]
    [InlineData("the line counting what was deleted")]
    [InlineData("the line saying the run was cancelled")]
    public async Task A_cancelled_delete_whose_output_fails_is_logged_and_exits_as_that_cancellation(string failsAt)
    {
        using var cts = new CancellationTokenSource();
        var delete = Substitute.For<IDeleteFilesService>();
        delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new DeleteResult(1, Array.Empty<FileOperationError>(), Cancelled: true);
            });

        var run = await Run("/d", Services(delete: delete),
            failsAt == "the line saying the run was cancelled"
                ? FailsAt(Strings.Cli_Cancelled)
                : FailsAt(Strings.Cli_DeletedFiles),
            cts.Token);

        Assert.True(run.Refused > 0, $"The console refused no write, so it never failed at {failsAt}.");
        Assert.Equal(CliExitCode.Partial, run.ExitCode);
        var summary = Assert.Single(run.Entries, IsOutcome);
        Assert.Equal(CliEventClass.Partial, summary.Class);
    }

    [Fact]
    public async Task A_stopped_move_whose_output_fails_is_logged_and_exits_as_that_stopped_move()
    {
        var move = Substitute.For<IMoveFilesService>();
        move.MoveFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(),
                Arg.Any<UnderLeaseClaims>(), Arg.Any<IProgress<OperationProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns<MoveResult>(_ => throw new MoveAbortedException(
                "stopped", new MoveResult(1, Array.Empty<FileOperationError>()),
                Destination, MoveAbortReason.ResolvesElsewhere));
        var stopped = CliContract.ClassifyAbortedMove(1);

        var run = await Run("/m", Services(move: move), FailsAt(Strings.Cli_MovedFiles));

        Assert.True(run.Refused > 0, "The console refused no write, so it never failed after the stop.");
        Assert.Equal(stopped.ExitCode, run.ExitCode);
        var summary = Assert.Single(run.Entries, IsOutcome);
        Assert.Equal(stopped.EventClass, summary.Class);
    }

    [Fact]
    public async Task A_run_refused_for_an_install_in_progress_keeps_its_come_back_later_code_when_its_output_fails()
    {
        var reboot = Substitute.For<IPendingRebootService>();
        reboot.Check().Returns(PendingRebootResult.Block(PendingRebootReason.MsiExecuteMutexHeld));

        // Every write fails, the scanning line first, so nothing this run prints gets out.
        var run = await Run("/d", Services(reboot: reboot), new ConsoleFailingAt(""));

        Assert.True(run.Refused > 0, "The console refused no write.");
        Assert.Equal(CliExitCode.Transient, run.ExitCode);
        var summary = Assert.Single(run.Entries, IsOutcome);
        Assert.Equal(CliEventClass.TransientSkip, summary.Class);
    }

    // ---- Main ----

    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    public void A_help_or_version_request_whose_output_fails_exits_1_and_writes_no_entry(string arg)
    {
        // Through Main, which puts the output behind the guard. A failed write that got past
        // the guard would reach Main's catch-all, which writes a HardError entry.
        var console = new ConsoleFailingAt("");
        var original = Console.Out;
        try
        {
            Console.SetOut(console);
            EventLogRecorder.Clear();

            var exitCode = Program.Main([arg]);

            Assert.True(console.Refused > 0, "The console refused no write.");
            Assert.Equal(CliExitCode.Error, exitCode);
            Assert.Empty(EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("--help")]
    public void A_help_or_version_request_whose_output_is_written_exits_0(string arg)
    {
        var console = new StringWriter();
        var original = Console.Out;
        try
        {
            Console.SetOut(console);
            EventLogRecorder.Clear();

            var exitCode = Program.Main([arg]);

            Assert.NotEqual(string.Empty, console.ToString());
            Assert.Equal(CliExitCode.Ok, exitCode);
            Assert.Empty(EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    // ---- fixtures ----

    /// <summary>A console that fails at the first write carrying the words of <paramref name="value"/> up to its first placeholder.</summary>
    private static ConsoleFailingAt FailsAt(string value) =>
        new(value.Contains('{') ? value[..value.IndexOf('{')] : value);

    private static bool IsOutcome((CliEventClass Class, string Text) entry) =>
        entry.Class is CliEventClass.Ok or CliEventClass.Partial
            or CliEventClass.TransientSkip or CliEventClass.HardError;

    private sealed record RunResult(
        int ExitCode, int Refused, IReadOnlyList<(CliEventClass Class, string Text)> Entries);

    /// <summary>
    /// Runs the real work method with <paramref name="console"/> behind the guard, as Main
    /// puts the console there.
    /// </summary>
    private static async Task<RunResult> Run(
        string arg, IServiceProvider services, ConsoleFailingAt console, CancellationToken token = default)
    {
        var invocation = arg == "/m"
            ? new CliInvocation(CliCommand.Move, null, Destination)
            : new CliInvocation(CliCommand.Delete, null, null);

        // Console.SetOut and the recorder are process-global; the assembly disables test
        // parallelisation, which is what makes both safe to read back here.
        var original = Console.Out;
        try
        {
            var guard = new ConsoleGuard(console);
            Console.SetOut(guard);
            EventLogRecorder.Clear();
            var exitCode = await Program.RunWorkAsync(arg, invocation, token, services, () => false, guard);
            return new RunResult(exitCode, console.Refused, EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>
    /// The services the work path resolves: a scan offering two files of 100 and 200 bytes,
    /// the pending-reboot gate <paramref name="reboot"/> or a clean one, and a re-verify
    /// keeping both.
    /// </summary>
    private static IServiceProvider Services(
        IDeleteFilesService? delete = null, IMoveFilesService? move = null,
        IPendingRebootService? reboot = null)
    {
        var scan = Substitute.For<IFileSystemScanService>();
        scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(new ScanResult(
                new[]
                {
                    new OrphanedFile(File1, 100, IsPatch: false, IsRemovablePatch: false,
                        IsObsoleted: false, Reason: "unclaimed"),
                    new OrphanedFile(File2, 200, IsPatch: true, IsRemovablePatch: true,
                        IsObsoleted: false, Reason: "superseded"),
                },
                Array.Empty<RegisteredPackage>(),
                RegisteredTotalBytes: 0));

        if (reboot is null)
        {
            reboot = Substitute.For<IPendingRebootService>();
            reboot.Check().Returns(PendingRebootResult.Clean);
        }

        var reverifier = Substitute.For<IRemovableReverifier>();
        reverifier.ReverifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ReverifyResult(
                new[] { File1, File2 },
                Array.Empty<string>(),
                SurvivingPatchClaims: new[] { SurvivingA, SurvivingB },
                SiblingPatchClaims: Array.Empty<PatchClaim>()));

        return new ServiceCollection()
            .AddSingleton(scan)
            .AddSingleton(reboot)
            .AddSingleton(reverifier)
            .AddSingleton(delete ?? Substitute.For<IDeleteFilesService>())
            .AddSingleton(move ?? Substitute.For<IMoveFilesService>())
            .AddSingleton(Substitute.For<ISettingsService>())
            .AddSingleton(Substitute.For<IFirstRunMark>())
            .BuildServiceProvider();
    }
}

/// <summary>
/// A disk under a redirected standard output that refuses the stream writes whose numbers,
/// counted from one, are in <c>refused</c>, as a full disk does, and takes the rest.
/// <see cref="Writes"/> counts every write it was asked for.
/// </summary>
internal sealed class DiskRefusingWrites(params int[] refused) : MemoryStream
{
    public int Writes { get; private set; }

    public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (refused.Contains(++Writes))
            throw new IOException(ConsoleFailingAt.Message);
        base.Write(buffer, offset, count);
    }
}

/// <summary>
/// A console that takes every write until one carrying <c>text</c>, and throws on that one,
/// as a write to a redirected standard output on a full disk does. An empty
/// <c>text</c> refuses every write. <see cref="Refused"/> counts the writes it threw on.
/// </summary>
internal sealed class ConsoleFailingAt(string text) : StringWriter
{
    /// <summary>The message of the exception every refused write throws.</summary>
    internal const string Message = "There is not enough space on the disk.";

    public int Refused { get; private set; }

    public override void Write(string? value)
    {
        if (value is not null && value.Contains(text, StringComparison.Ordinal))
        {
            Refused++;
            throw new IOException(Message);
        }
        base.Write(value);
    }

    public override void WriteLine(string? value)
    {
        Write(value);
        base.WriteLine();
    }
}
