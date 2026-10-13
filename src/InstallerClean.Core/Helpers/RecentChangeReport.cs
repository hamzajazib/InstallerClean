using InstallerClean.Models;
using InstallerClean.Resources;

namespace InstallerClean.Helpers;

/// <summary>
/// Renders the files a scan held back because their times are not before its clock as
/// one counted sentence.
///
/// It lives in Core rather than in either host for the reason
/// <see cref="HeldBackReport"/> does: the window and the command line print the same
/// sentence about one scan and do not share the code that prints it.
///
/// THE COUNT AND THE CAUSE ARE ONE ARM'S, SO THE CAUSE IS TRUE OF EVERY FILE IT COUNTS.
/// <see cref="WithholdingSplit.NotBeforeTheClockCount"/> counts only the files the age
/// check kept because the latest of their times, as Windows records them, is at or after
/// the moment the scan began, every other check on that half of the scan having let them
/// through. The sentence says that and offers an install as a possibility, never as the
/// cause: a clock set back puts a file there too. A file whose times were not
/// established is in another arm, and a file held because one of Windows Installer's
/// in-progress signals was set is spoken of through that signal's own line
/// (<see cref="ScanSignalReport"/>), so neither is counted here.
/// </summary>
internal static class RecentChangeReport
{
    /// <summary>
    /// The sentence for <paramref name="scan"/>, or empty where there is no scan or its
    /// <see cref="ScanResult.HasRecentChangeLine"/> is false.
    /// </summary>
    internal static string Line(ScanResult? scan)
    {
        if (scan is null || !scan.HasRecentChangeLine) return string.Empty;

        var count = scan.WithheldBy.NotBeforeTheClockCount;

        // The one-form names the size and not the numeral, so it spends {2} and leaves
        // {0} and {1} unused. Every argument is passed on either form so the two cannot
        // disagree about which index is which.
        return string.Format(
            DisplayHelpers.Pluralise(count,
                Strings.Completion_RecentChange_Singular,
                Strings.Completion_RecentChange_Plural,
                "Completion.RecentChange"),
            DisplayHelpers.FormatCount(count),
            DisplayHelpers.PluraliseFile(count),
            DisplayHelpers.FormatSize(scan.WithheldNotBeforeTheClockBytes));
    }
}
