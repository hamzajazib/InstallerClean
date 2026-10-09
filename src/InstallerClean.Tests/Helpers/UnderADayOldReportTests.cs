using System.Globalization;
using InstallerClean.Helpers;
using InstallerClean.Models;

namespace InstallerClean.Tests.Helpers;

/// <summary>
/// The sentence both hosts print about the files a scan held back for being less than
/// a day old, as rendered.
///
/// EVERY TIME IS GIVEN IN A FIXED ZONE SEVEN HOURS AHEAD OF UTC, so a test reads the same
/// on a runner in any zone and the conversion out of UTC is visible in every expected
/// string.
/// </summary>
public class UnderADayOldReportTests
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    private static readonly TimeZoneInfo SevenAhead =
        TimeZoneInfo.CreateCustomTimeZone("Test+7", TimeSpan.FromHours(7), "Test+7", "Test+7");

    private const long Bytes = 3 * 1_048_576;

    private static ScanResult Holding(int count, long bytes, DateTime? allADayOldAtUtc) =>
        new(Array.Empty<OrphanedFile>(), Array.Empty<RegisteredPackage>(), 0,
            WithheldBy: new WithholdingSplit(UnderADayOldCount: count),
            WithheldUnderADayOldBytes: bytes,
            WithheldUnderADayOldAllADayOldAtUtc: allADayOldAtUtc);

    private static DateTime Utc(int day, int hour, int minute, int second = 0) =>
        new(2030, 6, day, hour, minute, second, DateTimeKind.Utc);

    // ---- When there is no sentence ----

    [Fact]
    public void No_scan_and_no_file_held_for_its_age_produce_no_sentence()
    {
        Assert.Equal(string.Empty, UnderADayOldReport.Line(null, SevenAhead));
        Assert.Equal(string.Empty, UnderADayOldReport.Line(Holding(0, 0, null), SevenAhead));
        // A count with no instant has no time to give, so it says nothing rather than
        // print a sentence with a hole in it.
        Assert.Equal(string.Empty, UnderADayOldReport.Line(Holding(2, Bytes, null), SevenAhead));
    }

    // ---- The sentence ----

    [Fact]
    public void Several_files_give_the_count_the_size_and_the_local_time_and_date()
    {
        using var scope = new LocalisationScope(British, British);

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead);

        Assert.Equal(
            $"3 files ({DisplayHelpers.FormatSize(Bytes)}) were held back because they're less than a day "
            + "old, and an install or update may still be using them. Scan again after 16:40 on 16 June "
            + "and InstallerClean will probably be able to offer them.",
            line);
    }

    [Fact]
    public void One_file_takes_the_sentence_naming_its_size_alone()
    {
        using var scope = new LocalisationScope(British, British);

        var line = UnderADayOldReport.Line(Holding(1, Bytes, Utc(16, 9, 40)), SevenAhead);

        Assert.Equal(
            $"One file ({DisplayHelpers.FormatSize(Bytes)}) was held back because it's less than a day "
            + "old, and an install or update may still be using it. Scan again after 16:40 on 16 June "
            + "and InstallerClean will probably be able to offer it.",
            line);
    }

    // ---- The time ----

    [Theory]
    [InlineData(39, 12, "16:40")]
    [InlineData(40, 0, "16:40")]
    [InlineData(40, 1, "16:41")]
    public void The_time_is_rounded_up_to_the_next_whole_minute(int minute, int second, string shown)
    {
        using var scope = new LocalisationScope(British, British);

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, minute, second)), SevenAhead);

        Assert.Contains($"after {shown} on 16 June", line);
    }

    [Fact]
    public void A_tick_past_the_minute_rounds_up_and_the_minute_itself_stays()
    {
        var onTheMinute = Utc(16, 9, 40);

        Assert.Equal(onTheMinute, UnderADayOldReport.RoundUpToMinute(onTheMinute));
        Assert.Equal(onTheMinute.AddMinutes(1), UnderADayOldReport.RoundUpToMinute(onTheMinute.AddTicks(1)));
    }

    [Fact]
    public void The_date_is_the_one_in_the_given_zone()
    {
        // 17:30 UTC on the 16th is half past midnight on the 17th seven hours ahead.
        using var scope = new LocalisationScope(British, British);

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 17, 30)), SevenAhead);

        Assert.Contains("after 00:30 on 17 June", line);
    }

    [Fact]
    public void The_month_is_written_in_the_language_of_the_sentence_and_not_the_format_culture()
    {
        // The window left on Automatic: German display language, United States formats.
        // The month is a word of the German sentence, so it is German.
        using var scope = new LocalisationScope(
            CultureInfo.GetCultureInfo("de"), CultureInfo.GetCultureInfo("en-US"));

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead);

        Assert.Contains("16. Juni", line);
        Assert.DoesNotContain("June", line);
    }

    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo uiCulture, CultureInfo formatCulture) =>
            Localisation.Set(uiCulture, formatCulture);

        public void Dispose() => Localisation.Reset();
    }
}
