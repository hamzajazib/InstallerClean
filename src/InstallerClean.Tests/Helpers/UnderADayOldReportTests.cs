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

    [Fact]
    public void The_zone_is_asked_for_once_and_only_where_there_is_a_sentence()
    {
        var asked = 0;
        TimeZoneInfo Zone()
        {
            asked++;
            return SevenAhead;
        }

        Assert.Equal(string.Empty, UnderADayOldReport.Line(null, Zone));
        Assert.Equal(string.Empty, UnderADayOldReport.Line(Holding(0, 0, null), Zone));
        Assert.Equal(string.Empty, UnderADayOldReport.Line(Holding(2, Bytes, null), Zone));
        Assert.Equal(0, asked);

        var scan = Holding(3, Bytes, Utc(16, 9, 40));
        var line = UnderADayOldReport.Line(scan, Zone);
        Assert.Equal(1, asked);
        Assert.NotEqual(string.Empty, line);
        Assert.Equal(UnderADayOldReport.Line(scan, SevenAhead), line);
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

    [Fact]
    public void United_States_formats_with_the_app_in_English_give_a_twelve_hour_time_and_the_month_first()
    {
        using var scope = new LocalisationScope(British, CultureInfo.GetCultureInfo("en-US"));

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead);

        var at = new DateTime(2030, 6, 16, 16, 40, 0);
        Assert.Contains(
            $"after {at.ToString("t", CultureInfo.GetCultureInfo("en-US"))} on June 16 ", line);
        Assert.Contains("PM", line);
        Assert.DoesNotContain("16:40", line);
    }

    [Fact]
    public void German_formats_with_the_app_in_English_keep_the_English_month()
    {
        using var scope = new LocalisationScope(British, CultureInfo.GetCultureInfo("de-DE"));

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead);

        Assert.Contains("after 16:40 on 16 June ", line);
        Assert.DoesNotContain("Juni", line);
    }

    [Fact]
    public void German_formats_with_the_app_in_German_are_used()
    {
        var germany = CultureInfo.GetCultureInfo("de-DE");
        using var scope = new LocalisationScope(CultureInfo.GetCultureInfo("de"), germany);

        var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead);

        var at = new DateTime(2030, 6, 16, 16, 40, 0);
        Assert.Contains(at.ToString("t", germany), line);
        Assert.Contains(at.ToString("M", germany), line);
    }

    [Fact]
    public void A_regional_format_of_the_displayed_language_is_used_where_it_differs_from_that_language()
    {
        // Austrian German calls January "Jänner" where German has "Januar", and Australian
        // English writes "pm" where British English writes a 24-hour time, so both lines can
        // only have come from the regional format.
        var january = new DateTime(2031, 1, 16, 9, 40, 0, DateTimeKind.Utc);

        using (new LocalisationScope(CultureInfo.GetCultureInfo("de"), CultureInfo.GetCultureInfo("de-AT")))
            Assert.Contains("16. Jänner", UnderADayOldReport.Line(Holding(3, Bytes, january), SevenAhead));

        using (new LocalisationScope(British, CultureInfo.GetCultureInfo("en-AU")))
            Assert.Contains("pm on 16 January", UnderADayOldReport.Line(Holding(3, Bytes, january), SevenAhead));
    }

    [Fact]
    public void A_regional_format_the_app_would_not_display_in_the_sentence_s_language_is_not_used()
    {
        // Taiwan's formats share Chinese's two-letter code and the app displays them in
        // English, so the Simplified Chinese sentence keeps its own 24-hour time rather than
        // Taiwan's "下午". Saudi Arabia's formats are displayed in English too, and the English
        // sentence keeps "16:40" and "16 June" rather than Arabic.
        using (new LocalisationScope(CultureInfo.GetCultureInfo("zh-Hans"), CultureInfo.GetCultureInfo("zh-TW")))
        {
            var line = UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead);
            Assert.Contains("16:40", line);
            Assert.DoesNotContain("下午", line);
        }

        using (new LocalisationScope(British, CultureInfo.GetCultureInfo("ar-SA")))
            Assert.Contains("after 16:40 on 16 June ",
                UnderADayOldReport.Line(Holding(3, Bytes, Utc(16, 9, 40)), SevenAhead));
    }

    private sealed class LocalisationScope : IDisposable
    {
        public LocalisationScope(CultureInfo uiCulture, CultureInfo formatCulture) =>
            Localisation.Set(uiCulture, formatCulture);

        public void Dispose() => Localisation.Reset();
    }
}
