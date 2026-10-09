using InstallerClean.Cli;
using InstallerClean.Helpers;
using InstallerClean.Models;
using InstallerClean.Resources;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// Where the command line prints the scan's notices: the files held back and why, the
/// drives and shares given up, the superseded files held back, the files missing from the
/// folder and the files held back for being under a day old. A /s that lists files prints
/// them ahead of the line counting the files, so the list is the last thing printed; every
/// other run prints them after the line saying what the scan found, or after the scanning
/// line where no such line is printed. Driven through the real work method.
///
/// WHAT EVERY FIXTURE SETS UP is an offer of two files and, unless it is the one without,
/// a scan meeting every condition that has a stdout line, so a notice printed in the wrong
/// place has a list or a count line to be found beside.
/// </summary>
public class CliScanNoticeOrderTests
{
    private const string OfferA = @"C:\Windows\Installer\offer-a.msi";
    private const string OfferB = @"C:\Windows\Installer\offer-b.msi";
    private static readonly SourceRootGivenUp DriveE = new("E:", SourceRootGiveUpRoute.NoAnswer, 1);
    private static readonly DateTime ADayOldAt = new(2030, 6, 16, 9, 40, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_scan_that_lists_files_prints_its_notices_ahead_of_the_count_line_and_ends_on_the_list()
    {
        var scan = EveryNotice();

        var run = await Run("/s", scan);

        var lines = run.Stdout.Split(Environment.NewLine);
        var notices = NoticeOpenings(scan)
            .Select(opening => Array.FindIndex(lines, l => l.StartsWith(opening, StringComparison.Ordinal)))
            .ToArray();
        Assert.Equal(Strings.Cli_ScanningInstaller, lines[0]);
        Assert.Equal(1, notices[0]);
        for (var i = 1; i < notices.Length; i++)
            Assert.True(notices[i] > notices[i - 1], $"Notice {i} out of order at {notices[i]}.\n{run.Stdout}");

        var found = Array.FindIndex(lines, l => l.StartsWith(Opening(Strings.Cli_FoundOrphans), StringComparison.Ordinal));
        var lastRow = Array.FindIndex(lines, l => l.StartsWith(LastRow, StringComparison.Ordinal));
        Assert.True(found == notices[^1] + 1 && lastRow == found + 2, run.Stdout);
        Assert.Equal(lines.Length - 2, lastRow);
        Assert.Equal(string.Empty, lines[^1]);
    }

    [Fact]
    public async Task A_scan_that_lists_files_and_has_no_notices_ends_at_the_last_row()
    {
        var run = await Run("/s", new ScanResult(Offer(), Array.Empty<RegisteredPackage>(), 0));

        var lines = run.Stdout.Split(Environment.NewLine);
        Assert.StartsWith(LastRow, lines[^2], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[^1]);
    }

    [Fact]
    public async Task A_delete_prints_the_notices_under_the_line_counting_what_was_found()
    {
        var scan = EveryNotice();

        var run = await Run("/d", scan);

        var lines = run.Stdout.Split(Environment.NewLine);
        var found = Array.FindIndex(lines, l => l.StartsWith(Opening(Strings.Cli_FoundOrphans), StringComparison.Ordinal));
        var deleted = Array.FindIndex(lines, l => l.StartsWith(Opening(Strings.Cli_DeletedFiles), StringComparison.Ordinal));
        var notices = NoticeOpenings(scan)
            .Select(opening => Array.FindIndex(lines, l => l.StartsWith(opening, StringComparison.Ordinal)))
            .ToArray();
        Assert.True(found >= 0, run.Stdout);
        Assert.Equal(found + 1, notices[0]);
        for (var i = 1; i < notices.Length; i++)
            Assert.True(notices[i] > notices[i - 1], $"Notice {i} out of order at {notices[i]}.\n{run.Stdout}");
        Assert.True(deleted > notices[^1], run.Stdout);
    }

    [Theory]
    [InlineData("the first notice")]
    [InlineData("the first row")]
    public async Task A_console_that_fails_while_printing_leaves_every_notice_and_the_summary_in_the_Application_log(
        string failsAt)
    {
        var console = new ConsoleFailingAt(failsAt == "the first row"
            ? Offer()[0].FileName
            : Opening(Strings.Cli_NothingListedPerFile_Singular));

        var run = await Run("/s", EveryNotice(), console);

        Assert.True(console.Refused > 0, $"The console refused no write, so it never failed at {failsAt}.");
        Assert.Equal(CliExitCode.Error, run.ExitCode);
        Assert.Contains(run.Entries, entry => entry.Class == CliEventClass.ScanNothingOfferedNotice);
        Assert.Contains(run.Entries, entry => entry.Class == CliEventClass.SourcesGivenUpNotice);
        Assert.Contains(run.Entries, entry => entry.Class == CliEventClass.ScanSupersededHeldBackNotice);
        Assert.Contains(run.Entries, entry => entry.Class == CliEventClass.ScanMissingFilesNotice);
        Assert.Contains(run.Entries, entry => entry.Class == CliEventClass.Ok
            && entry.Text == MachineContract.English(() => string.Format(Strings.Cli_EventLogScanFound,
                "/s", 2, DisplayHelpers.PluraliseFile(2), DisplayHelpers.FormatSizeForMachine(2048))));
    }

    // ---- fixtures ----

    /// <summary>The opening of the list's last row, which names the second file offered.</summary>
    private static string LastRow => "  " + Offer()[1].FileName + " ";

    private static OrphanedFile[] Offer() =>
        new[] { OfferA, OfferB }
            .Select(path => new OrphanedFile(path, 1024, false, false, false, "unclaimed"))
            .ToArray();

    /// <summary>
    /// An offer of two files beside one file held back because its program could not be
    /// established, one held back for being under a day old, one superseded file held back,
    /// a drive given up keeping a file and a registration whose file is missing.
    /// </summary>
    private static ScanResult EveryNotice() =>
        new ScanResult(
            Offer(), Array.Empty<RegisteredPackage>(), 0,
            WithheldCount: 1,
            WithheldFiles:
            [
                new OrphanedFile(@"C:\Windows\Installer\held-a.msi", 1024, false, false, false, "unclaimed"),
                new OrphanedFile(@"C:\Windows\Installer\held-b.msi", 1024, false, false, false, "unclaimed"),
            ],
            WithheldBy: new WithholdingSplit(DeclaredProductUnestablishedCount: 1, UnderADayOldCount: 1),
            WithheldUnderADayOldBytes: 1024,
            WithheldUnderADayOldAllADayOldAtUtc: ADayOldAt,
            SourceRootsGivenUp: [DriveE])
        with { MissingAffectedCount = 1 };

    /// <summary>
    /// The opening of each notice <paramref name="scan"/> prints, in the order they are
    /// printed: the sentence counting the files held back beside the offer, the heading
    /// over its reasons, the drive given up, the superseded files, the missing files and
    /// the files under a day old.
    /// </summary>
    private static string[] NoticeOpenings(ScanResult scan) =>
    [
        Opening(Strings.Cli_NothingListedPerFile_Singular),
        Strings.Cli_WithheldReasons_Header,
        SourcesGivenUpReport.CommandLine([DriveE]),
        Strings.Cli_SupersededHeldBack_Singular,
        Opening(Strings.Cli_MissingFromDisk_Singular),
        UnderADayOldReport.Line(scan, TimeZoneInfo.Local),
    ];

    // A sentence's words up to its first placeholder, which no name or count changes.
    private static string Opening(string value) =>
        value.Contains('{') ? value[..value.IndexOf('{')] : value;

    /// <summary>
    /// A console that takes every write until one carrying <paramref name="text"/>, and
    /// throws on that one, as a console whose reader has gone does. <see cref="Refused"/>
    /// counts the writes it threw on.
    /// </summary>
    private sealed class ConsoleFailingAt(string text) : StringWriter
    {
        public int Refused { get; private set; }

        public override void Write(string? value)
        {
            if (value is not null && value.Contains(text, StringComparison.Ordinal))
            {
                Refused++;
                throw new IOException("The pipe is being closed.");
            }
            base.Write(value);
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            base.WriteLine();
        }
    }

    private sealed record RunResult(
        int ExitCode, string Stdout, IReadOnlyList<(CliEventClass Class, string Text)> Entries);

    private static async Task<RunResult> Run(string arg, ScanResult result, StringWriter? console = null)
    {
        var scan = Substitute.For<IFileSystemScanService>();
        scan.ScanAsync(Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(result);

        var reboot = Substitute.For<IPendingRebootService>();
        reboot.Check().Returns(PendingRebootResult.Clean);

        var reverifier = Substitute.For<IRemovableReverifier>();
        reverifier.ReverifyAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new ReverifyResult(result.RemovableFiles.Select(f => f.FullPath).ToList(), Array.Empty<string>()));

        var deleter = Substitute.For<IDeleteFilesService>();
        deleter.DeleteFilesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<UnderLeaseClaims>(),
                Arg.Any<IProgress<OperationProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult(result.RemovableFiles.Count, Array.Empty<FileOperationError>()));

        var services = new ServiceCollection()
            .AddSingleton(scan)
            .AddSingleton(reboot)
            .AddSingleton(reverifier)
            .AddSingleton(deleter)
            .AddSingleton(Substitute.For<IMoveFilesService>())
            .AddSingleton(Substitute.For<ISettingsService>())
            .AddSingleton(Substitute.For<IFirstRunMark>())
            .BuildServiceProvider();

        var invocation = arg == "/d"
            ? new CliInvocation(CliCommand.Delete, null, null)
            : new CliInvocation(CliCommand.ScanOnly, null, null);

        // Console.SetOut and the recorder are process-global; the assembly disables test
        // parallelisation, which is what makes both safe to read back here.
        var original = Console.Out;
        using var buffer = console ?? new StringWriter();
        try
        {
            Console.SetOut(buffer);
            EventLogRecorder.Clear();
            var exitCode = await Program.RunWorkAsync(arg, invocation, CancellationToken.None, services);
            return new RunResult(exitCode, buffer.ToString(), EventLogRecorder.Entries);
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
