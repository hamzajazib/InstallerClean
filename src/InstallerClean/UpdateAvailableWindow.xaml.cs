using System.Windows;
using InstallerClean.Helpers;
using InstallerClean.Resources;

namespace InstallerClean;

public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow(string currentVersion, string latestVersion)
    {
        InitializeComponent();
        VersionInfo.Text = string.Format(
            Strings.UpdateCheck_UpdateAvailable_Body,
            currentVersion, latestVersion);
        // The window title is what a screen reader announces when a dialog
        // opens, and ShowInTaskbar is false under custom chrome, so it serves
        // the announcement and nothing else. Heading then body, the order the
        // card reads, as the sibling modals compose theirs: on open only the
        // title and the focused button are spoken, so a heading left on the
        // card alone would go unheard and the versions under it would arrive
        // with nothing saying what they are about.
        //
        // Joined with the stop the resx carries, as MessageWindow joins its own,
        // because this heading ends in none in any language and the mark that ends
        // a sentence differs between them. The Confirm dialogs join with a bare
        // space because their headings ask a question and carry the question
        // mark their language uses.
        Title = Strings.UpdateCheck_UpdateAvailable_Title
            + Strings.Display_SentenceSeparator + VersionInfo.Text;

        // Sized to content; the clamp stops a very large text scale
        // pushing the card past the work area, at which point the
        // version row scrolls and the action buttons stay visible.
        MaxHeight = DetailWindowSizing.WorkAreaHeightLimit(Application.Current?.MainWindow);

        this.EnableAltSpaceSystemMenu();
        this.SuppressFocusVisualOnDeactivation();
        // Open with focus on Cancel (IsCancel, the conservative default) so a
        // keyboard user gets a visible focus ring at once rather than focus on
        // the window itself. Deferred to Loaded so the visual tree exists when
        // Focus runs.
        Loaded += (_, _) => CancelButton.Focus();
    }

    private void OnOpen(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
