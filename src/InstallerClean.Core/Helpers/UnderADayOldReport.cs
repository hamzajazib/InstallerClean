using InstallerClean.Models;
using InstallerClean.Resources;

namespace InstallerClean.Helpers;

/// <summary>
/// Renders the files a scan held back for being less than a day old as one counted
/// sentence, with the time from which a scan can first offer the newest of them.
///
/// It lives in Core rather than in either host for the reason
/// <see cref="HeldBackReport"/> does: the window and the command line print the same
/// sentence about one scan and do not share the code that prints it.
///
/// THE COUNT AND THE CAUSE ARE ONE ARM'S, SO THE CAUSE IS TRUE OF EVERY FILE IT COUNTS.
/// <see cref="WithholdingSplit.UnderADayOldCount"/> counts only the files the age check
/// kept as under a day old, after every other check on that half of the scan had let
/// them through. A file whose age was not established is in another arm and is not
/// spoken of here.
/// </summary>
internal static class UnderADayOldReport
{
    /// <summary>
    /// The sentence for <paramref name="scan"/>, or empty where there is no scan or its
    /// <see cref="ScanResult.HasUnderADayOldLine"/> is false. The time and date are
    /// <see cref="ScanResult.WithheldUnderADayOldAllADayOldAtUtc"/> rounded up to the
    /// next whole minute, in <paramref name="zone"/>; the hosts pass
    /// <see cref="TimeZoneInfo.Local"/>.
    /// </summary>
    internal static string Line(ScanResult? scan, TimeZoneInfo zone)
    {
        if (scan is null || !scan.HasUnderADayOldLine) return string.Empty;

        var allADayOldAtUtc = scan.WithheldUnderADayOldAllADayOldAtUtc!.Value;
        var count = scan.WithheldBy.UnderADayOldCount;
        var (time, date) = DisplayHelpers.FormatTimeAndDate(RoundUpToMinute(allADayOldAtUtc), zone);

        // The one-form names the size and not the numeral, so it spends {2}, {3} and {4}
        // and leaves {0} and {1} unused. Every argument is passed on either form so the
        // two cannot disagree about which index is which.
        return string.Format(
            DisplayHelpers.Pluralise(count,
                Strings.Completion_UnderADayOld_Singular,
                Strings.Completion_UnderADayOld_Plural,
                "Completion.UnderADayOld"),
            DisplayHelpers.FormatCount(count),
            DisplayHelpers.PluraliseFile(count),
            DisplayHelpers.FormatSize(scan.WithheldUnderADayOldBytes),
            time,
            date);
    }

    /// <summary>
    /// <paramref name="utc"/> moved forward to the next whole minute, or left where it is
    /// on one. The sentence shows minutes, so a scan at the minute it names is at or past
    /// the instant rather than up to a minute short of it.
    /// </summary>
    internal static DateTime RoundUpToMinute(DateTime utc)
    {
        var intoMinute = utc.Ticks % TimeSpan.TicksPerMinute;
        return intoMinute == 0 ? utc : utc.AddTicks(TimeSpan.TicksPerMinute - intoMinute);
    }
}
