using InstallerClean.Services;

namespace InstallerClean.Tests.Services;

/// <summary>
/// The rule that decides whether a cached file has been shown to be a day old, which
/// a file the folder walk found must be before the scan offers it.
///
/// EVERY FIXTURE STATES ITS TIMES AGAINST ONE CLOCK, so each test varies one time and
/// the reader can see which one moved the answer.
/// </summary>
public class CachedFileAgeTests
{
    private static readonly DateTimeOffset Clock =
        new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTime TwoDaysBefore = Clock.UtcDateTime.AddDays(-2);

    private static FileTimes AllAt(DateTime at) => new(at, at, at);

    // ---- What lets a file through ----

    [Fact]
    public void A_file_whose_three_times_are_two_days_old_is_shown_a_day_old()
    {
        Assert.True(CachedFileAge.ShownADayOld(FileTimesRead.Read, AllAt(TwoDaysBefore), Clock));
    }

    [Fact]
    public void A_file_whose_latest_time_is_exactly_a_day_old_is_shown_a_day_old()
    {
        // "Under a day" keeps the file, so exactly a day lets it through. The tick
        // either side is the next test.
        Assert.True(CachedFileAge.ShownADayOld(
            FileTimesRead.Read, AllAt(Clock.UtcDateTime - CachedFileAge.MinimumAge), Clock));
    }

    // ---- What keeps a file back ----

    [Fact]
    public void A_file_one_tick_short_of_a_day_old_is_not()
    {
        Assert.False(CachedFileAge.ShownADayOld(
            FileTimesRead.Read,
            AllAt(Clock.UtcDateTime - CachedFileAge.MinimumAge + TimeSpan.FromTicks(1)),
            Clock));
    }

    [Fact]
    public void A_recent_change_time_keeps_a_file_whose_creation_and_last_write_are_old()
    {
        // A copy given its source's creation and last-write times, whose change
        // time is its own.
        var times = new FileTimes(
            CreationUtc: TwoDaysBefore.AddYears(-5),
            LastWriteUtc: TwoDaysBefore.AddYears(-5),
            ChangeUtc: Clock.UtcDateTime.AddMinutes(-5));

        Assert.False(CachedFileAge.ShownADayOld(FileTimesRead.Read, times, Clock));
    }

    [Fact]
    public void A_recent_creation_time_keeps_a_file_whose_last_write_and_change_are_old()
    {
        // A copy given its source's last-write and change times, whose creation time
        // is its own.
        var times = new FileTimes(
            CreationUtc: Clock.UtcDateTime.AddMinutes(-5),
            LastWriteUtc: TwoDaysBefore.AddYears(-5),
            ChangeUtc: TwoDaysBefore.AddYears(-5));

        Assert.False(CachedFileAge.ShownADayOld(FileTimesRead.Read, times, Clock));
    }

    [Fact]
    public void A_recent_last_write_keeps_a_file_whose_creation_and_change_are_old()
    {
        var times = new FileTimes(
            CreationUtc: TwoDaysBefore,
            LastWriteUtc: Clock.UtcDateTime.AddMinutes(-5),
            ChangeUtc: TwoDaysBefore);

        Assert.False(CachedFileAge.ShownADayOld(FileTimesRead.Read, times, Clock));
    }

    [Fact]
    public void A_time_later_than_the_scan_clock_keeps_the_file()
    {
        Assert.False(CachedFileAge.ShownADayOld(
            FileTimesRead.Read, AllAt(Clock.UtcDateTime.AddDays(3)), Clock));
    }

    [Fact]
    public void Every_answer_but_a_read_keeps_the_file_whatever_times_it_carries()
    {
        // Walked over the whole enum with times that would pass, so the rule has to
        // be refusing on the outcome and not on the times. A member added later
        // keeps the file by default, and this is where that is held.
        var permitting = Enum.GetValues<FileTimesRead>()
            .Where(o => CachedFileAge.ShownADayOld(o, AllAt(TwoDaysBefore), Clock))
            .ToArray();

        Assert.Equal(new[] { FileTimesRead.Read }, permitting);
    }

    // ---- Which of the two keeping verdicts ----

    [Fact]
    public void A_latest_time_a_day_after_the_clock_is_under_a_day_old()
    {
        // A copy written just before the clock was set back reads as ahead of it, and
        // is new. A day ahead is still that.
        Assert.Equal(CachedFileAgeVerdict.UnderADayOld, CachedFileAge.Judge(
            FileTimesRead.Read, AllAt(Clock.UtcDateTime + CachedFileAge.MinimumAge), Clock));
    }

    [Fact]
    public void A_latest_time_more_than_a_day_after_the_clock_is_not_an_age()
    {
        Assert.Equal(CachedFileAgeVerdict.Unestablished, CachedFileAge.Judge(
            FileTimesRead.Read,
            AllAt(Clock.UtcDateTime + CachedFileAge.MinimumAge + TimeSpan.FromTicks(1)),
            Clock));
    }

    [Fact]
    public void A_file_read_as_under_a_day_before_the_clock_is_under_a_day_old()
    {
        Assert.Equal(CachedFileAgeVerdict.UnderADayOld, CachedFileAge.Judge(
            FileTimesRead.Read, AllAt(Clock.UtcDateTime.AddHours(-1)), Clock));
    }

    [Fact]
    public void Every_answer_but_a_read_is_an_age_not_established()
    {
        // Times that would read as under a day old, so the verdict has to come from
        // the outcome.
        var notEstablished = Enum.GetValues<FileTimesRead>()
            .Where(o => CachedFileAge.Judge(o, AllAt(Clock.UtcDateTime.AddHours(-1)), Clock)
                == CachedFileAgeVerdict.Unestablished)
            .ToArray();

        Assert.Equal(Enum.GetValues<FileTimesRead>().Where(o => o != FileTimesRead.Read), notEstablished);
    }

    // ---- Which time the age is taken from ----

    [Fact]
    public void The_latest_of_the_three_is_the_one_the_age_is_taken_from()
    {
        var early = TwoDaysBefore.AddDays(-10);
        var mid = TwoDaysBefore.AddDays(-5);
        var late = TwoDaysBefore;

        Assert.Equal(late, CachedFileAge.Latest(new FileTimes(late, early, mid)));
        Assert.Equal(late, CachedFileAge.Latest(new FileTimes(early, late, mid)));
        Assert.Equal(late, CachedFileAge.Latest(new FileTimes(mid, early, late)));
    }

    [Fact]
    public void The_minimum_age_is_one_day()
    {
        Assert.Equal(TimeSpan.FromDays(1), CachedFileAge.MinimumAge);
    }

    // ---- The volume root the reader builds on ----

    [Theory]
    [InlineData(@"\\?\Volume{0a1b2c3d-0000-1111-2222-333344445555}\Windows\Installer\1a2b3.msi",
        @"\\?\Volume{0a1b2c3d-0000-1111-2222-333344445555}\")]
    [InlineData(@"\\?\volume{0a1b2c3d-0000-1111-2222-333344445555}\a.msp",
        @"\\?\volume{0a1b2c3d-0000-1111-2222-333344445555}\")]
    public void A_volume_guid_path_yields_its_root(string finalPath, string root)
    {
        Assert.Equal(root, FileTimesReader.VolumeRoot(finalPath));
    }

    [Theory]
    [InlineData(@"C:\Windows\Installer\1a2b3.msi")]
    [InlineData(@"\\?\C:\Windows\Installer\1a2b3.msi")]
    [InlineData(@"\\?\UNC\server\share\Installer\1a2b3.msi")]
    [InlineData(@"\\?\UNC\server\share{0a1b2c3d}\Installer\1a2b3.msi")]
    [InlineData(@"C:\Folder{0a1b2c3d}\1a2b3.msi")]
    [InlineData(@"\\?\Volume{0a1b2c3d-0000-1111-2222-333344445555")]
    [InlineData("")]
    public void Anything_but_a_volume_guid_path_yields_no_root(string finalPath)
    {
        // Null is what keeps the file: the reader answers VolumeUnestablished for it.
        Assert.Null(FileTimesReader.VolumeRoot(finalPath));
    }
}
