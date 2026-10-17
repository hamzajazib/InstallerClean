using InstallerClean.ViewModels;

namespace InstallerClean.Services;

/// <summary>
/// Opens the secondary windows (the two Details windows, About and the report
/// window), opens a link in the browser, puts text on the clipboard, relaunches the
/// app, hands over to a newer version and closes the main window. The interface keeps MainViewModel
/// free of a direct dependency on Application.Current.MainWindow and on
/// Window constructors, both of which NRE under xUnit.
/// </summary>
public interface IWindowService
{
    void ShowOrphanedDetails(OrphanedFilesViewModel viewModel);

    void ShowRegisteredDetails(RegisteredFilesViewModel viewModel);

    void ShowAbout();

    /// <summary>
    /// Shows the report window: <paramref name="report"/> is the text of
    /// <c>last-run.json</c> as it was read, shown as it stands, or null where the
    /// file could not be read, which the window says in place of the report.
    /// </summary>
    void ShowReport(string? report);

    void CloseMainWindow();

    void OpenUrl(string url);

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard. Answers false where the clipboard
    /// refused it, as it does while another program holds it open.
    /// </summary>
    bool CopyToClipboard(string text);

    /// <summary>
    /// Relaunches the app so a newly chosen display-language preference
    /// takes effect. The resx strings resolve once when each window is
    /// built, so a culture swapped at runtime does not reach text already
    /// painted; a fresh process is the reliable way to apply it.
    /// </summary>
    void RelaunchForLanguageChange();

    /// <summary>
    /// Hands over to the newer version <paramref name="prepared"/> holds and closes the
    /// app: runs the newer setup, or puts the newer version written beside the app at its
    /// own path and starts it. Answers false where it could not be started, with the app
    /// still running as it was from its own path.
    /// </summary>
    Task<bool> StartUpdateAndCloseAsync(PreparedUpdate prepared);
}
