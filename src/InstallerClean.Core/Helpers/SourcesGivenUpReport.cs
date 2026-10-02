using InstallerClean.Models;
using InstallerClean.Resources;

namespace InstallerClean.Helpers;

/// <summary>
/// Renders the drives and shares a scan, or the check made before a Move or Delete, gave
/// up on while files still had to be checked against them: which ones, that those files
/// were left alone, and what to do once they respond normally.
///
/// It lives in Core beside <see cref="HeldBackReport"/> for the same reason: both hosts
/// name these drives and shares in one way whichever screen or line carries them.
///
/// THE LINE NAMES THE DRIVES AND SHARES AND COUNTS NO FILE. Every file it speaks of was
/// kept where its check stopped at a read the pass refused for one of them, so "left
/// alone" is true of each, and it sits beside whatever a screen already says about the
/// files it held back rather than inside it.
///
/// THE CALLER HANDS OVER ONLY THE ROOTS AT WHICH FILES WERE KEPT
/// (<see cref="ScanResult.SourceRootsGivenUpKeepingFiles"/> and its twin on the check's
/// result). A root given up with nothing kept at it changed nothing the pass decided, and
/// is not named.
/// </summary>
internal static class SourcesGivenUpReport
{
    /// <summary>
    /// The window's line, or empty when <paramref name="roots"/> is empty. It ends by
    /// saying to Re-scan.
    /// </summary>
    internal static string WindowLine(IReadOnlyList<SourceRootGivenUp> roots) =>
        Line(roots, Strings.Summary_SourceGivenUp_Drive, Strings.Summary_SourceGivenUp_Path,
            Strings.Summary_SourcesGivenUp);

    /// <summary>
    /// The command line's line, or empty when <paramref name="roots"/> is empty. That host
    /// shows no wait and has no button, so the line says the app stopped waiting, and it
    /// ends by saying to run the command again.
    /// </summary>
    internal static string CommandLine(IReadOnlyList<SourceRootGivenUp> roots) =>
        Line(roots, Strings.Cli_SourceGivenUp_Drive, Strings.Cli_SourceGivenUp_Path,
            Strings.Cli_SourcesGivenUp);

    /// <summary>
    /// Every root in <paramref name="roots"/>, in the order given up, as the list inside
    /// the brackets names it: a drive as "drive D:", anything else as it is spelled. The
    /// command line's Application-log entries name the roots in this same form.
    /// </summary>
    internal static string ListOf(IReadOnlyList<SourceRootGivenUp> roots) =>
        string.Join(Strings.Display_ListSeparator, roots.Select(given =>
            DisplayHelpers.IsDriveRoot(given.Root)
                ? string.Format(Strings.Display_DriveName, DisplayHelpers.SourceRootName(given.Root))
                : given.Root));

    /// <summary>
    /// One host's line, from its three forms.
    ///
    /// ONE ROOT IS NAMED INSIDE THE SENTENCE, A DRIVE AND A PATH EACH BY A STRING OF ITS
    /// OWN, because several languages decline the noun after "without" or "for": the drive
    /// form carries its own word for "drive", as the waiting line does. TWO OR MORE ARE
    /// LISTED IN BRACKETS after a phrase that needs no case (<see cref="ListOf"/>), joined
    /// with <see cref="Strings.Display_ListSeparator"/> and no conjunction.
    /// </summary>
    private static string Line(
        IReadOnlyList<SourceRootGivenUp> roots, string drive, string path, string several)
    {
        if (roots.Count == 0) return string.Empty;

        if (roots.Count == 1)
        {
            var root = roots[0].Root;
            return string.Format(DisplayHelpers.IsDriveRoot(root) ? drive : path,
                DisplayHelpers.SourceRootName(root));
        }

        return string.Format(several, ListOf(roots));
    }
}
