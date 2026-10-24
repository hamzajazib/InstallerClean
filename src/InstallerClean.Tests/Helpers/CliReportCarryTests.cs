using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Which runs of the command line carry the anonymous report. A run at a console that
/// finished, on a PC with no earlier run, hands the report to the offer: a scan that
/// listed files or found none, and a Delete or Move that found nothing to act on or ran
/// to its end, with or without files acted on. A cancelled run, a refused one and a
/// stopped Move carry nothing. A run nobody can answer builds no report and does not
/// start the PC's first-run check. Driven through the real work method with substitute
/// services.
/// </summary>
public class CliReportCarryTests
{
    private static readonly string Destination =
        Path.Combine(Path.GetTempPath(), "installerclean-cli-report-carry-test");

    private const string File1 = @"C:\Windows\Installer\a.msi";
    private const string File2 = @"C:\Windows\Installer\b.msp";

    private static readonly FileOperationError[] NoErrors = Array.Empty<FileOperationError>();

    [Fact]
    public async Task A_scan_that_listed_files_carries_the_report_and_makes_this_the_first_run()
    {
        var rig = new Rig();

        var exitCode = await rig.Run("/s");

        Assert.Equal(CliExitCode.Ok, exitCode);
        var entry = Assert.IsType<ResultLogEntry>(rig.Report.Entry);
        Assert.Equal(OperationKinds.Scan, entry.Operation.Kind);
        Assert.Equal("commandLine", entry.App.Host);
        Assert.Equal("GB", entry.App.WindowsRegion);
        rig.Mark.Received(1).Set();
    }

    [Fact]
    public async Task A_scan_that_offered_nothing_carries_the_report()
    {
        var rig = new Rig { Offered = false };

        var exitCode = await rig.Run("/s");

        Assert.Equal(CliExitCode.Ok, exitCode);
        Assert.Equal(OperationKinds.Scan, rig.Report.Entry!.Operation.Kind);
        rig.Mark.Received(1).Set();
    }

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public async Task A_delete_or_move_that_found_nothing_to_act_on_carries_a_scan_only_report(string arg)
    {
        var rig = new Rig { Offered = false };

        var exitCode = await rig.Run(arg, arg == "/m" ? Destination : null);

        Assert.Equal(CliExitCode.Ok, exitCode);
        Assert.Equal(OperationKinds.Scan, rig.Report.Entry!.Operation.Kind);
    }

    [Fact]
    public async Task A_scan_on_a_PC_with_an_earlier_run_carries_nothing_and_leaves_the_mark()
    {
        var rig = new Rig { EarlierRun = true };

        await rig.Run("/s");

        Assert.Null(rig.Report.Entry);
        rig.Mark.DidNotReceive().Set();
    }

    [Theory]
    [InlineData("/s")]
    [InlineData("/d")]
    [InlineData("/m")]
    public async Task A_run_nobody_can_answer_builds_no_report_and_starts_no_check(string arg)
    {
        var rig = new Rig { CanAsk = false };

        await rig.Run(arg);

        Assert.Null(rig.Report.Entry);
        _ = rig.Earlier.DidNotReceive().ShowsAnEarlierRunAsync();
        _ = rig.Earlier.DidNotReceive().ShowsAnEarlierRunWithinAsync(Arg.Any<TimeSpan>());
        rig.Region.DidNotReceive().Read();
    }

    [Fact]
    public async Task A_run_given_no_report_to_fill_runs_as_it_always_did()
    {
        var rig = new Rig();

        var exitCode = await rig.Run("/s", withReport: false);

        Assert.Equal(CliExitCode.Ok, exitCode);
        _ = rig.Earlier.DidNotReceive().ShowsAnEarlierRunAsync();
    }

    [Fact]
    public async Task A_delete_that_deleted_files_carries_a_delete_report_decided_before_the_batch()
    {
        var rig = new Rig();
        rig.Delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(2, NoErrors));

        var exitCode = await rig.Run("/d");

        Assert.Equal(CliExitCode.Ok, exitCode);
        var entry = Assert.IsType<ResultLogEntry>(rig.Report.Entry);
        Assert.Equal(OperationKinds.Delete, entry.Operation.Kind);
        Assert.Equal(2, entry.Operation.FilesProcessed);
        Assert.Equal("commandLine", entry.App.Host);
        // The batch sets the mark itself, so the answer it would read is taken first.
        Received.InOrder(() =>
        {
            _ = rig.Earlier.ShowsAnEarlierRunWithinAsync(Arg.Any<TimeSpan>());
            _ = rig.Delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task A_delete_on_a_PC_with_an_earlier_run_carries_nothing()
    {
        var rig = new Rig { EarlierRun = true };
        rig.Delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(2, NoErrors));

        await rig.Run("/d");

        Assert.Null(rig.Report.Entry);
    }

    [Fact]
    public async Task A_delete_in_which_some_files_failed_still_carries_the_report()
    {
        var rig = new Rig();
        rig.Delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(1, new FileOperationError[] { new UnknownError(File2) }));

        var exitCode = await rig.Run("/d");

        Assert.Equal(CliExitCode.Partial, exitCode);
        var entry = Assert.IsType<ResultLogEntry>(rig.Report.Entry);
        Assert.Equal(1, entry.Operation.FilesFailed);
    }

    [Fact]
    public async Task A_delete_where_the_check_before_it_took_every_file_back_carries_the_report()
    {
        var rig = new Rig { ReverifyKeeps = false };

        var exitCode = await rig.Run("/d");

        Assert.Equal(CliExitCode.Ok, exitCode);
        Assert.Equal(OperationKinds.Delete, rig.Report.Entry!.Operation.Kind);
        Assert.Equal(0, rig.Report.Entry.Operation.FilesProcessed);
    }

    [Fact]
    public async Task A_move_that_moved_files_carries_a_move_report_naming_where_it_went()
    {
        var rig = new Rig();
        rig.Move.MoveFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(),
                Arg.Any<UnderLeaseClaims>(), Arg.Any<IProgress<OperationProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new MoveResult(2, NoErrors));

        var exitCode = await rig.Run("/m", Destination);

        Assert.Equal(CliExitCode.Ok, exitCode);
        var entry = Assert.IsType<ResultLogEntry>(rig.Report.Entry);
        Assert.Equal(OperationKinds.Move, entry.Operation.Kind);
        Assert.False(string.IsNullOrEmpty(entry.Operation.MoveDestinationKind));
    }

    [Fact]
    public async Task A_cancelled_delete_carries_nothing()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig();
        rig.Delete.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new DeleteResult(1, NoErrors, Cancelled: true);
            });

        var exitCode = await rig.Run("/d", token: cts.Token);

        Assert.Equal(CliExitCode.Partial, exitCode);
        Assert.Null(rig.Report.Entry);
    }

    [Fact]
    public async Task A_stopped_move_carries_nothing()
    {
        var rig = new Rig();
        rig.Move.MoveFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<string>(),
                Arg.Any<UnderLeaseClaims>(), Arg.Any<IProgress<OperationProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns<MoveResult>(_ => throw new MoveAbortedException(
                "stopped", new MoveResult(1, NoErrors), Destination, MoveAbortReason.ResolvesElsewhere));

        await rig.Run("/m", Destination);

        Assert.Null(rig.Report.Entry);
    }

    [Theory]
    [InlineData("/d")]
    [InlineData("/m")]
    public async Task A_run_the_pending_reboot_gate_refuses_carries_nothing(string arg)
    {
        var rig = new Rig();
        rig.Reboot.Check().Returns(PendingRebootResult.Block(PendingRebootReason.MsiExecuteMutexHeld));

        var exitCode = await rig.Run(arg, arg == "/m" ? Destination : null);

        Assert.NotEqual(CliExitCode.Ok, exitCode);
        Assert.Null(rig.Report.Entry);
        rig.Mark.DidNotReceive().Set();
    }

    [Fact]
    public async Task A_report_that_cannot_be_built_leaves_the_run_and_the_mark_as_they_were()
    {
        var rig = new Rig();
        rig.Region.Read().Returns(_ => throw new InvalidOperationException("region"));

        var exitCode = await rig.Run("/s");

        Assert.Equal(CliExitCode.Ok, exitCode);
        Assert.Null(rig.Report.Entry);
        rig.Mark.DidNotReceive().Set();
    }

    // ---- the offer, as Main calls it ----

    [Fact]
    public void A_report_carried_by_the_run_is_offered_and_not_a_waiting_one_sent()
    {
        var entry = ResultLogEntry.ForScanOnly(
            new ScanResult(Array.Empty<OrphanedFile>(), Array.Empty<RegisteredPackage>(), RegisteredTotalBytes: 0),
            1, "GB", ReportHost.CommandLine, AppCopyKind.CommandLineDownload);
        var report = new RunReport(canAsk: true) { Entry = entry };
        var log = Substitute.For<IResultLogService>();
        log.WriteAsync(entry, Arg.Any<CancellationToken>()).Returns(true);
        var console = new SilentConsole("n");

        Program.OfferReport(report, console, log, Substitute.For<ISettingsService>(), CancellationToken.None);

        log.Received(1).WriteAsync(entry, Arg.Any<CancellationToken>());
        log.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_run_with_no_report_looks_for_one_an_earlier_run_left()
    {
        var report = new RunReport(canAsk: true);
        var settings = Substitute.For<ISettingsService>();
        settings.TryLoad(out Arg.Any<AppSettings>()).Returns(call =>
        {
            call[0] = new AppSettings { ReportSendPending = true };
            return true;
        });
        var log = Substitute.For<IResultLogService>();
        log.LastLogExists().Returns(true);
        log.ReadLastLogAsync(Arg.Any<CancellationToken>()).Returns("{}");
        log.SendAsync("{}", Arg.Any<CancellationToken>()).Returns(ResultLogSendOutcome.Sent);

        Program.OfferReport(report, new SilentConsole(), log, settings, CancellationToken.None);

        log.Received(1).SendAsync("{}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_run_nobody_can_answer_offers_and_sends_nothing()
    {
        var report = new RunReport(canAsk: false);
        var log = Substitute.For<IResultLogService>();
        var settings = Substitute.For<ISettingsService>();

        Program.OfferReport(report, new SilentConsole(), log, settings, CancellationToken.None);

        Assert.Empty(log.ReceivedCalls());
        Assert.Empty(settings.ReceivedCalls());
    }

    [Fact]
    public void A_run_that_Ctrl_C_has_ended_offers_and_sends_nothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var report = new RunReport(canAsk: true) { Entry = null };
        var log = Substitute.For<IResultLogService>();
        var settings = Substitute.For<ISettingsService>();

        Program.OfferReport(report, new SilentConsole(), log, settings, cts.Token);

        Assert.Empty(log.ReceivedCalls());
        Assert.Empty(settings.ReceivedCalls());
    }

    // ---- fixtures ----

    /// <summary>A console that prints nowhere and answers each read with the next answer it was given, then with the end of input.</summary>
    private sealed class SilentConsole(params string?[] answers) : IConsolePrompt
    {
        private int _next;

        public void WriteLine(string line) { }

        public void Write(string text) { }

        public string? ReadLine(CancellationToken token) => _next < answers.Length ? answers[_next++] : null;
    }

    private sealed class Rig
    {
        public Rig()
        {
            Mark = Substitute.For<IFirstRunMark>();
            Earlier = Substitute.For<IEarlierRunCheck>();
            Earlier.ShowsAnEarlierRunAsync().Returns(_ => Task.FromResult(EarlierRun));
            Earlier.ShowsAnEarlierRunWithinAsync(Arg.Any<TimeSpan>()).Returns(_ => Task.FromResult(EarlierRun));
            Region = Substitute.For<IWindowsRegion>();
            Region.Read().Returns("GB");
            Reboot = Substitute.For<IPendingRebootService>();
            Reboot.Check().Returns(PendingRebootResult.Clean);
            Delete = Substitute.For<IDeleteFilesService>();
            Move = Substitute.For<IMoveFilesService>();
        }

        public IFirstRunMark Mark { get; }
        public IEarlierRunCheck Earlier { get; }
        public IWindowsRegion Region { get; }
        public IPendingRebootService Reboot { get; }
        public IDeleteFilesService Delete { get; }
        public IMoveFilesService Move { get; }
        public RunReport Report { get; private set; } = new(canAsk: true);

        public bool EarlierRun { get; set; }
        public bool CanAsk { get; set; } = true;
        public bool Offered { get; set; } = true;
        public bool ReverifyKeeps { get; set; } = true;

        public async Task<int> Run(
            string arg, string? destination = null, CancellationToken token = default, bool withReport = true)
        {
            Report = new RunReport(CanAsk);
            var command = arg switch
            {
                "/s" => CliCommand.ScanOnly,
                "/m" => CliCommand.Move,
                _ => CliCommand.Delete,
            };
            EventLogRecorder.Clear();
            var original = Console.Out;
            using var buffer = new StringWriter();
            try
            {
                Console.SetOut(buffer);
                return await Program.RunWorkAsync(
                    arg, new CliInvocation(command, null, destination), token, Services(), () => false,
                    report: withReport ? Report : null);
            }
            finally
            {
                Console.SetOut(original);
            }
        }

        private IServiceProvider Services()
        {
            var files = Offered
                ? new[]
                {
                    new OrphanedFile(File1, 100, IsPatch: false, IsRemovablePatch: false,
                        IsObsoleted: false, Reason: "unclaimed"),
                    new OrphanedFile(File2, 200, IsPatch: true, IsRemovablePatch: true,
                        IsObsoleted: false, Reason: "superseded"),
                }
                : Array.Empty<OrphanedFile>();
            var scan = Substitute.For<IFileSystemScanService>();
            scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
                .Returns(new ScanResult(files, Array.Empty<RegisteredPackage>(), RegisteredTotalBytes: 0));

            var reverifier = Substitute.For<IRemovableReverifier>();
            reverifier.ReverifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(ReverifyKeeps
                    ? new ReverifyResult(
                        new[] { File1, File2 }, Array.Empty<string>(),
                        SurvivingPatchClaims: new[]
                        {
                            new PatchClaim(File2, "{AAAA1111-0000-0000-0000-000000000002}",
                                "{PPPP1111-0000-0000-0000-000000000001}", null, 2),
                        },
                        SiblingPatchClaims: Array.Empty<PatchClaim>())
                    : new ReverifyResult(Array.Empty<string>(), new[] { File1, File2 }));

            return new ServiceCollection()
                .AddSingleton(scan)
                .AddSingleton(Reboot)
                .AddSingleton(reverifier)
                .AddSingleton(Delete)
                .AddSingleton(Move)
                .AddSingleton(Substitute.For<ISettingsService>())
                .AddSingleton(Mark)
                .AddSingleton(Earlier)
                .AddSingleton(Region)
                .AddSingleton(Substitute.For<IRegistryReader>())
                .BuildServiceProvider();
        }
    }
}
