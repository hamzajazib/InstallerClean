using System.IO.Abstractions.TestingHelpers;
using InstallerClean.Interop;
using InstallerClean.Models;
using InstallerClean.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The age check driven through a real scan: the real
/// <see cref="FileSystemScanService"/> and <see cref="CachedFileAge"/>, with the
/// file times scripted. <see cref="CachedFileAgeTests"/> pins the rule; these pin
/// that the scan asks it, of which files, against which clock, and accounts for what
/// it keeps back.
///
/// READ THIS BEFORE COPYING A FIXTURE HERE. The times reader is an OPTIONAL
/// collaborator and the scan's test constructor defaults it to null, which runs no
/// age check at all. So an assertion that a file was offered proves nothing on its
/// own: it passes against a reader that let the file through and against a scan with
/// no reader. Every test below that asserts an offer injects a reader and shows the
/// same reader keeping a file back in the same scan.
/// </summary>
public class FileSystemScanServiceAgeCheckTests
{
    private const string Folder = @"C:\Windows\Installer";
    private const string ProductA = "{11111111-1111-1111-1111-111111111111}";
    private const string ProductB = "{22222222-2222-2222-2222-222222222222}";

    private static readonly DateTimeOffset Now = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Old = Now.UtcDateTime.AddDays(-2);
    private static readonly DateTime Recent = Now.UtcDateTime.AddHours(-1);

    // ---- The check keeps a file back, and lets one through, in the same scan ----

    [Fact]
    public async Task A_file_changed_within_the_day_is_kept_beside_one_two_days_old_that_is_offered()
    {
        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\old.msi", Old, Old, Old);
        times.Reads($@"{Folder}\new.msi", Old, Old, Recent);

        var result = await Scan(new[] { $@"{Folder}\old.msi", $@"{Folder}\new.msi" }, times);

        var offered = Assert.Single(result.RemovableFiles);
        Assert.Equal($@"{Folder}\old.msi", offered.FullPath);

        var kept = Assert.Single(result.WithheldFiles!);
        Assert.Equal($@"{Folder}\new.msi", kept.FullPath);
        Assert.Equal(1, result.WithheldBy.UnderADayOldCount);
        Assert.Equal(0, result.WithheldBy.AgeUnestablishedCount);
        Assert.Equal(kept.SizeBytes, result.WithheldUnderADayOldBytes);
        Assert.Equal(result.WithheldFiles!.Count, result.WithheldBy.Total);
    }

    [Fact]
    public async Task A_file_kept_for_its_age_owes_no_notice()
    {
        // The treatment files kept for an installed program get: counted among the
        // files left alone, left out of the held-back sentences, and no reason line.
        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\new.msi", Recent, Recent, Recent);

        var result = await Scan(new[] { $@"{Folder}\new.msi" }, times);

        Assert.Empty(result.RemovableFiles);
        Assert.Single(result.WithheldFiles!);
        Assert.Equal(0, result.UnestablishedWithheldCount);
        Assert.Equal(0, result.UnestablishedWithheldBytes);
        Assert.Equal(WithholdingAccount.KeptWithoutNotice, result.Withholding);
        Assert.False(result.HasWithholdingToReport);
        Assert.Empty(result.WithheldBy.ArmsFired);
    }

    [Fact]
    public async Task A_new_creation_time_keeps_a_file_whose_other_times_are_old()
    {
        // A copy carrying its source's last-write and change times and a creation
        // time of its own. Beside it, a file with all three old is offered.
        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\old.msi", Old, Old, Old);
        times.Reads($@"{Folder}\copied.msi", Recent, Old.AddYears(-3), Old.AddYears(-3));

        var result = await Scan(new[] { $@"{Folder}\old.msi", $@"{Folder}\copied.msi" }, times);

        Assert.Equal($@"{Folder}\old.msi", Assert.Single(result.RemovableFiles).FullPath);
        Assert.Equal($@"{Folder}\copied.msi", Assert.Single(result.WithheldFiles!).FullPath);
    }

    [Theory]
    [InlineData(FileTimesRead.NotNtfs)]
    [InlineData(FileTimesRead.NotAFixedVolume)]
    [InlineData(FileTimesRead.VolumeUnestablished)]
    [InlineData(FileTimesRead.TimesUnavailable)]
    [InlineData(FileTimesRead.OpenRefused)]
    [InlineData(FileTimesRead.NotAPlainFile)]
    [InlineData(FileTimesRead.NamesNothing)]
    [InlineData(FileTimesRead.Faulted)]
    public async Task A_file_whose_times_could_not_be_vouched_for_is_kept_and_spoken_of(FileTimesRead answer)
    {
        // Kept like a file read as under a day old, and counted apart from one: its
        // age was not established, so it is among the files the held-back sentence
        // counts, and no reason line speaks for it, so the list under that sentence
        // is not printed.
        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\old.msi", Old, Old, Old);
        times.Answers($@"{Folder}\unvouched.msi", answer);

        var result = await Scan(new[] { $@"{Folder}\old.msi", $@"{Folder}\unvouched.msi" }, times);

        Assert.Equal($@"{Folder}\old.msi", Assert.Single(result.RemovableFiles).FullPath);
        var kept = Assert.Single(result.WithheldFiles!);
        Assert.Equal($@"{Folder}\unvouched.msi", kept.FullPath);
        Assert.Equal(1, result.WithheldBy.AgeUnestablishedCount);
        Assert.Equal(0, result.WithheldBy.UnderADayOldCount);
        Assert.Equal(result.WithheldFiles!.Count, result.WithheldBy.Total);
        Assert.Equal(1, result.UnestablishedWithheldCount);
        Assert.Equal(kept.SizeBytes, result.UnestablishedWithheldBytes);
        Assert.Equal(WithholdingAccount.PerFile, result.Withholding);
        Assert.False(result.NamedConditionsCoverEveryHeldBackFile);
    }

    [Fact]
    public async Task A_patch_file_is_put_to_the_age_check_like_a_package()
    {
        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\old.msp", Old, Old, Old);
        times.Reads($@"{Folder}\new.msp", Recent, Recent, Recent);

        var result = await Scan(new[] { $@"{Folder}\old.msp", $@"{Folder}\new.msp" }, times);

        Assert.Equal($@"{Folder}\old.msp", Assert.Single(result.RemovableFiles).FullPath);
        Assert.Equal($@"{Folder}\new.msp", Assert.Single(result.WithheldFiles!).FullPath);
    }

    // ---- Which files it is asked about ----

    [Fact]
    public async Task The_age_check_reads_only_what_the_installed_program_check_let_through()
    {
        // held.msi declares an installed product that records no package, so the
        // screen keeps it; gone.msi declares a product Windows does not hold, so the
        // screen lets it through. Only gone.msi reaches the age check, and held.msi
        // stays counted where the screen put it.
        var identities = new ScriptedPackageIdentities();
        identities.Declares($@"{Folder}\held.msi", ProductA);
        identities.Declares($@"{Folder}\gone.msi", ProductB);

        var msi = new ScriptedMsiProducts();
        msi.Installed(ProductA);
        msi.NotInstalled(ProductB, MsiError.UnknownProduct);

        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\gone.msi", Recent, Recent, Recent);

        var files = new[] { $@"{Folder}\held.msi", $@"{Folder}\gone.msi" };
        var result = await new FileSystemScanService(
            QueryReturning(Array.Empty<RegisteredPackage>()), FolderHolding(files), null,
            files, null, null,
            new DeclaredProductCheck(msi, identities),
            times, new FixedClock(Now))
            .ScanAsync();

        Assert.Equal(new[] { $@"{Folder}\gone.msi" }, times.Asked);
        Assert.Empty(result.RemovableFiles);
        Assert.Equal(1, result.WithheldBy.DeclaredProductInstalledCount);
        Assert.Equal(1, result.WithheldBy.UnderADayOldCount);
        Assert.Equal(2, result.WithheldBy.Total);
        Assert.Equal(WithholdingAccount.KeptWithoutNotice, result.Withholding);
    }

    [Fact]
    public async Task A_machine_whose_walk_offer_is_withheld_wholesale_reads_no_times()
    {
        // Every candidate is already kept back, so the age check is not run. The
        // reader throws on any path, so this fails if it runs at all.
        var census = new EnumerationCensus(PathNormalisationRefusedAtEmbeddedNullCount: 1);
        var query = Substitute.For<IInstallerQueryService>();
        query.GetRegisteredPackagesAsync(
                Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(new InstallerQueryResult(Array.Empty<RegisteredPackage>(), Census: census));

        var times = new ScriptedFileTimes();

        var result = await new FileSystemScanService(
            query, FolderHolding($@"{Folder}\a.msi"), null,
            new[] { $@"{Folder}\a.msi" }, null, null, null,
            times, new FixedClock(Now))
            .ScanAsync();

        Assert.Empty(times.Asked);
        Assert.Equal(1, result.WithheldBy.WholesaleCount);
        Assert.Equal(0, result.WithheldBy.UnderADayOldCount);
        Assert.Equal(0, result.WithheldBy.AgeUnestablishedCount);
    }

    [Fact]
    public async Task A_registered_superseded_patch_is_offered_without_being_put_to_the_age_check()
    {
        // It reaches the offer from its own registration, which names the file. The
        // reader throws on anything unscripted, and the walked orphan beside it is
        // kept by the same reader in the same scan.
        var registered = new List<RegisteredPackage>
        {
            new($@"{Folder}\superseded.msp", "Product A", ProductA, PatchState: 2, IsRemovable: true),
        };

        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\new.msi", Recent, Recent, Recent);

        var result = await Scan(new[] { $@"{Folder}\new.msi" }, times, registered);

        Assert.Equal($@"{Folder}\superseded.msp", Assert.Single(result.RemovableFiles).FullPath);
        Assert.Equal($@"{Folder}\new.msi", Assert.Single(result.WithheldFiles!).FullPath);
        Assert.Equal(new[] { $@"{Folder}\new.msi" }, times.Asked);
    }

    // ---- The clock ----

    [Fact]
    public async Task The_age_is_taken_against_the_scan_clock()
    {
        // Times two days before a clock set years ahead. Against that clock the file
        // is two days old and offered; against the system clock the same times lie
        // in the future and keep it. So the offer can only come from the injected
        // clock being the one used.
        var future = new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var twoDaysBeforeIt = future.UtcDateTime.AddDays(-2);

        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\a.msi", twoDaysBeforeIt, twoDaysBeforeIt, twoDaysBeforeIt);

        var injected = await Scan(new[] { $@"{Folder}\a.msi" }, times, clock: new FixedClock(future));
        var system = await Scan(new[] { $@"{Folder}\a.msi" }, times, clock: TimeProvider.System);

        Assert.Single(injected.RemovableFiles);
        Assert.Empty(system.RemovableFiles);
        Assert.Single(system.WithheldFiles!);
        // Decades after the system clock, which is not an age, so the file is kept as
        // one whose age was not established and the held-back sentence counts it.
        Assert.Equal(1, system.WithheldBy.AgeUnestablishedCount);
        Assert.Equal(1, system.UnestablishedWithheldCount);
    }

    [Fact]
    public async Task The_scan_reads_its_clock_once()
    {
        // One instant for every candidate, so no candidate is judged against a later
        // clock than another.
        var times = new ScriptedFileTimes();
        times.Reads($@"{Folder}\a.msi", Old, Old, Old);
        times.Reads($@"{Folder}\b.msi", Old, Old, Old);
        var clock = new FixedClock(Now);

        await Scan(new[] { $@"{Folder}\a.msi", $@"{Folder}\b.msi" }, times, clock: clock);

        Assert.Equal(1, clock.Reads);
    }

    // ---- What a scan with no reader does, and what production wires ----

    [Fact]
    public async Task A_scan_built_without_a_times_reader_runs_no_age_check()
    {
        // THIS TEST'S ONLY JOB IS TO FAIL IF THE DEFAULT EVER STOPS BEING NULL. The
        // suite's other scan tests omit the reader and assert offers of files that
        // have no times at all.
        var result = await new FileSystemScanService(
            QueryReturning(Array.Empty<RegisteredPackage>()), FolderHolding($@"{Folder}\a.msi"), null,
            new[] { $@"{Folder}\a.msi" }, null, null)
            .ScanAsync();

        Assert.Single(result.RemovableFiles);
        Assert.Empty(result.WithheldFiles!);
    }

    [Fact]
    public void The_scan_the_hosts_build_runs_the_age_check()
    {
        // Constructed by hand everywhere else in this file, where the default is no
        // reader. The container is what the hosts use.
        using var services = new ServiceCollection().AddInstallerCleanCore().BuildServiceProvider();

        var scan = Assert.IsType<FileSystemScanService>(services.GetRequiredService<IFileSystemScanService>());

        Assert.True(scan.ChecksAge);
    }

    // ---- Helpers ----

    private static IInstallerQueryService QueryReturning(IReadOnlyList<RegisteredPackage> registered)
    {
        var query = Substitute.For<IInstallerQueryService>();
        query.GetRegisteredPackagesAsync(
                Arg.Any<IProgress<ScanProgressUpdate>?>(), Arg.Any<CancellationToken>())
            .Returns(new InstallerQueryResult(registered));
        return query;
    }

    private static MockFileSystem FolderHolding(params string[] paths)
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(Folder);
        foreach (var path in paths) fs.AddFile(path, new MockFileData(new byte[100]));
        return fs;
    }

    private static Task<ScanResult> Scan(
        IEnumerable<string> walked,
        IFileTimesReader times,
        IReadOnlyList<RegisteredPackage>? registered = null,
        TimeProvider? clock = null)
    {
        var files = walked.ToArray();
        var fs = FolderHolding(files.Concat(
            (registered ?? Array.Empty<RegisteredPackage>()).Select(p => p.LocalPackagePath)).ToArray());

        return new FileSystemScanService(
            QueryReturning(registered ?? Array.Empty<RegisteredPackage>()), fs, null,
            files, null, null, null,
            times, clock ?? new FixedClock(Now))
            .ScanAsync();
    }
}

/// <summary>
/// A scripted <see cref="IFileTimesReader"/>.
///
/// AN UNSCRIPTED PATH THROWS. A read with old times is the answer that lets a file
/// through, so a fake handing one back by default would let a test assert an offer
/// the fixture produced.
/// </summary>
internal sealed class ScriptedFileTimes : IFileTimesReader
{
    private readonly Dictionary<string, (FileTimesRead Outcome, FileTimes Times)> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every path this reader was asked about, in order.</summary>
    public List<string> Asked { get; } = new();

    public void Reads(string path, DateTime creation, DateTime lastWrite, DateTime change) =>
        _byPath[path] = (FileTimesRead.Read, new FileTimes(creation, lastWrite, change));

    /// <summary>
    /// The read answers <paramref name="outcome"/>. The times it carries are old, so
    /// a caller that read them without checking the outcome would offer the file.
    /// </summary>
    public void Answers(string path, FileTimesRead outcome)
    {
        var old = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _byPath[path] = (outcome, new FileTimes(old, old, old));
    }

    public FileTimesRead ReadOutcome(string path, out FileTimes times)
    {
        Asked.Add(path);
        if (!_byPath.TryGetValue(path, out var scripted))
            throw new InvalidOperationException(
                $"the fake times reader was asked about {path}, which no test scripted");
        times = scripted.Times;
        return scripted.Outcome;
    }
}

/// <summary>A clock that always reads one instant and counts how often it was read.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public int Reads { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        Reads++;
        return now;
    }
}
