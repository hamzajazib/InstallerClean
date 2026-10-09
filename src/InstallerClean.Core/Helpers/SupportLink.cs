namespace InstallerClean.Helpers;

/// <summary>
/// The donation addresses, one constant per address, because each one moves.
/// Written out twice, the second copy goes stale on the day the first one
/// changes, and a donate button that opens a page which is no longer there
/// raises no error, fails no check and produces no complaint, because the
/// person who meets it is not the person who would report it.
/// </summary>
public static class SupportLink
{
    /// <summary>
    /// The page the app's donate controls open: the two donate buttons on the
    /// card shown at the end of a run, the large Donate and the small Donate under
    /// Done, and the pill in the About window.
    /// </summary>
    public const string Url = "https://nofaff.netlify.app/support";

    /// <summary>
    /// The page the command line's donate line prints. A console shows the
    /// address it prints, so it prints the page the donation is made on rather
    /// than <see cref="Url"/>.
    /// </summary>
    public const string KoFiUrl = "https://ko-fi.com/nofaff";
}
