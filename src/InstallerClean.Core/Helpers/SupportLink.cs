namespace InstallerClean.Helpers;

/// <summary>
/// The page the app's donate controls open: the two donate buttons on the card
/// shown at the end of a run, "Donate $5" and the small Donate under Done, and
/// the pill in the About window.
///
/// One constant, read by all three, because the address moves. Written out
/// twice, the second copy goes stale on the day the first one changes, and a
/// donate button that opens a page which is no longer there raises no error,
/// fails no check and produces no complaint, because the person who meets it
/// is not the person who would report it.
/// </summary>
public static class SupportLink
{
    /// <summary>The address itself.</summary>
    public const string Url = "https://nofaff.netlify.app/support";
}
